using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using StemForge;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var app = new App(false) { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent(); L.Initialize("vi");
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        var task = Verify(Path.GetFullPath(args[0]));
        var frame = new DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(90);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) => { if (task.IsCompleted || DateTime.UtcNow > deadline) { timer.Stop(); frame.Continue = false; } };
        timer.Start(); Dispatcher.PushFrame(frame);
        try { Assert(task.IsCompleted, "Guide test timeout"); task.GetAwaiter().GetResult(); }
        finally { app.Shutdown(); }
    }

    static async Task Verify(string root)
    {
        var folder = Path.Combine(root, "artifacts", "guide-v013"); Directory.CreateDirectory(folder);
        string html = GuideWindow.ReadGuide();
        Assert(html.Contains("Phiên bản 0.15.0") && html.Contains("Waveform riêng cho trống") && html.Contains("BPM và tông chính"), "Embedded Vietnamese guide is current");
        Assert(html.Contains("Nghe riêng stem và thu gọn các bước") && html.Contains("Live RAM cập nhật DSP trong luồng phát"), "Guide covers stem audition, collapse and live A/B");
        Assert(GuideWindow.IsGuideAddress(GuideWindow.GuideUrl+"#drums") && !GuideWindow.IsGuideAddress("https://example.com"), "Only guide navigation allowed");
        var window = new GuideWindow(Path.Combine(folder, "profile"))
        { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowInTaskbar = false, ShowActivated = false };
        // Test the app's own embedded reader without desktop input or external browser tabs.
        window.Show();
        try
        {
            await window.InitializeBrowserAsync();
            var view = (WebView2)window.FindName("GuideBrowser");
            Assert(view.CoreWebView2 is not null, ((TextBlock)window.FindName("GuideStatus")).Text);
            var web = view.CoreWebView2!;
            for (int i = 0; i < 100 && await web.ExecuteScriptAsync("document.readyState === 'complete' && !!document.querySelector('#updates')") != "true"; i++) await Task.Delay(100);
            Assert(await web.ExecuteScriptAsync("document.querySelector('html').lang === 'vi' && document.querySelectorAll('[data-step]').length === 7") == "true", "HTML / JS loaded in WebView2");
            Assert(await web.ExecuteScriptAsync("[...document.images].every(i => i.src.startsWith('data:image/'))") == "true", "Guide images self-contained");
            await web.ExecuteScriptAsync("document.querySelector('#reset').click(); document.querySelector('[data-step=\"1\"]').click();");
            Assert((await web.ExecuteScriptAsync("document.querySelector('#progressText').textContent")).Contains("1 / 7"), "Interactive checklist works");
            web.Navigate(GuideWindow.GuideUrl);
            for (int i = 0; i < 100; i++)
            {
                await Task.Delay(100);
                if (await web.ExecuteScriptAsync("document.readyState === 'complete' && !!document.querySelector('[data-step=\"1\"]')?.checked") == "true") break;
            }
            Assert(await web.ExecuteScriptAsync("document.querySelector('[data-step=\"1\"]').checked") == "true", "Checklist survives reload on local origin");
            await web.ExecuteScriptAsync("document.querySelector('a[href=\"#drums\"]').click()");
            Assert(await web.ExecuteScriptAsync("location.hash === '#drums'") == "true", "Table of contents navigation works");
            await web.ExecuteScriptAsync("document.documentElement.style.scrollBehavior='auto'; window.scrollTo(0,0)");
            await Task.Delay(300);
            using (var file = File.Create(Path.Combine(folder, "Guide.png"))) await web.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
            await web.ExecuteScriptAsync("document.querySelector('#drum-types').scrollIntoView()");
            Assert(await web.ExecuteScriptAsync("document.querySelectorAll('svg[data-drum]').length === 6") == "true", "Six original drum illustrations embedded");
            await Task.Delay(250);
            using (var file = File.Create(Path.Combine(folder, "DrumTypes.png"))) await web.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
            await web.ExecuteScriptAsync("window.scrollBy(0, 500)"); await Task.Delay(250);
            using (var file = File.Create(Path.Combine(folder, "DrumTypes-Bottom.png"))) await web.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
            window.Width = 780; await Task.Delay(300);
            using (var file = File.Create(Path.Combine(folder, "Guide-Narrow.png"))) await web.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
        }
        finally { window.Close(); }
        L.Initialize("en");
        var englishGuide = new GuideWindow(Path.Combine(folder, "profile-en"))
        { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowInTaskbar = false, ShowActivated = false };
        englishGuide.Show();
        try
        {
            await englishGuide.InitializeBrowserAsync();
            var web = ((WebView2)englishGuide.FindName("GuideBrowser")).CoreWebView2;
            for (int i=0; i<100 && await web.ExecuteScriptAsync("document.readyState === 'complete' && document.documentElement.lang === 'en'")!="true"; i++) await Task.Delay(100);
            Assert(await web.ExecuteScriptAsync("document.documentElement.lang === 'en' && document.querySelectorAll('svg[data-drum]').length === 6") == "true", "English guide and all six drum illustrations");
            using(var file=File.Create(Path.Combine(folder,"Guide-English.png"))) await web.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);
            await web.ExecuteScriptAsync("document.querySelector('#drums').scrollIntoView()"); await Task.Delay(200);
            using(var file=File.Create(Path.Combine(folder,"Drums-English.png"))) await web.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);
        }
        finally { englishGuide.Close(); L.Initialize("vi"); }
        var main = new MainWindow(new AppStore(Path.Combine(folder, "menu-test.db")));
        ((WrapPanel)main.FindName("ProjectActions")).IsEnabled = false;
        Assert(((Button)main.FindName("GuideButton")).IsEnabled, "Guide remains available during audio jobs");
        ((WrapPanel)main.FindName("ProjectActions")).IsEnabled = true;
        var content = (FrameworkElement)main.Content;
        foreach (var theme in new[] { "Dark", "Light" })
        {
            ThemeManager.Apply(theme);
            content.Measure(new Size(1024, 800)); content.Arrange(new Rect(0, 0, 1024, 800)); content.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1024, 800, 96, 96, PixelFormats.Pbgra32);
            var surface = new DrawingVisual();
            using (var context = surface.RenderOpen()) context.DrawRectangle((Brush)Application.Current.Resources["AppBackground"], null, new Rect(0, 0, 1024, 800));
            bitmap.Render(surface); bitmap.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(folder, "Menu-"+theme+".png")); encoder.Save(file);
        }
        var about = new AboutWindow();
        Assert(((TextBlock)about.FindName("VersionInfo")).Text.Contains("0.15.0"), "About uses assembly version");
        Assert(((TextBox)about.FindName("LicenseText")).Text.Contains("GNU GENERAL PUBLIC LICENSE") && ((TextBox)about.FindName("NoticesText")).Text.Contains("StemDeck"), "About includes actual embedded GPL license and third-party notices");
        foreach (var theme in new[] { "Dark", "Light" })
        {
            ThemeManager.Apply(theme);
            var body = (FrameworkElement)about.Content;
            body.Measure(new Size(760, 670)); body.Arrange(new Rect(0, 0, 760, 670)); body.UpdateLayout();
            var bmp = new RenderTargetBitmap(760, 670, 96, 96, PixelFormats.Pbgra32);
            var bg = new DrawingVisual(); using(var drawing = bg.RenderOpen()) drawing.DrawRectangle((Brush)Application.Current.Resources["AppBackground"], null, new Rect(0,0,760,670));
            bmp.Render(bg); bmp.Render(body);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp));
            using var file = File.Create(Path.Combine(folder, "About-"+theme+".png")); png.Save(file);
        }
        about.Close();
        main.Close();
        Console.WriteLine("PASS: embedded HTML, Vietnamese, WebView2 load, offline images, checklist / local storage, anchors, menu available during jobs.");
    }

    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
}
