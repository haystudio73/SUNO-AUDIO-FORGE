using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StemForge;

internal static class Program
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS " + message); }
    [STAThread]
    static void Main(string[] args)
    {
        var root=Path.GetFullPath(args[0]); var folder=Path.Combine(root,"artifacts","localization-v013"); Directory.CreateDirectory(folder);
        var app=new App(false) { ShutdownMode=ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        var store=new AppStore(Path.Combine(folder,Guid.NewGuid()+".db"));
        var resources=typeof(App).Assembly.GetManifestResourceNames();
        Assert(new[]{"StemForge.Languages.vi.json","StemForge.Languages.en.json","StemForge.Guide.en.html"}.All(resources.Contains),"Catalog and English guide fallbacks embedded in main assembly");
        Assert(store.LoadSettings().Language=="vi", "Existing/default settings use Vietnamese");
        var vi=new MainWindow(store);
        Assert(((Button)vi.FindName("NewProjectButton")).Content.ToString()=="Dự án mới", "Vietnamese default UI");
        Assert(vi.Title.StartsWith("Suno Audio Forge") && vi.Icon is not null, "Brand and window icon");
        var settings=store.LoadSettings();settings.Language="en";settings.RestoreLastSession=false;store.SaveSettings(settings);
        var en=new MainWindow(store);
        Assert(((Button)en.FindName("NewProjectButton")).Content.ToString()=="New Project", "English restored from SQLite");
        Assert(((TextBlock)en.FindName("MusicInfo")).Text=="BPM / Key: not analyzed", "Dynamic messages translated");
        Assert(((ComboBox)en.FindName("GenrePresetBox")).Items.Cast<DrumPreset>().First().Name.StartsWith("Neutral"),"Built-in presets translated");
        var settingsWindow=new SettingsWindow(settings,store.DatabasePath);
        Assert(settingsWindow.Title=="Settings — Suno Audio Forge" && settingsWindow.SelectedLanguage=="en", "Settings language selector");
        Assert(((ComboBox)settingsWindow.FindName("AppLanguageBox")).Items.Count>=2,"Bundled languages discovered");
        var about=new AboutWindow(); Assert(((TextBlock)about.FindName("VersionInfo")).Text.Contains("0.15.0"),"Version 0.15.0");
        Assert(GuideWindow.ReadGuide().Contains("<html lang=\"en\">"), "English guide selected");
        ThemeManager.Apply("Light"); Save(en,Path.Combine(folder,"English.png"),1384,940);
        Save(settingsWindow,Path.Combine(folder,"Settings-English.png"),700,760);
        // Optional analysis failure returns normally: never opens a warning dialog or changes audio state.
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var project=(Project)typeof(MainWindow).GetField("project",flags)!.GetValue(en)!;
        project.Name="Bài hát giữ nguyên";project.Source="missing-audio.wav";project.Analysis=new MusicAnalysis { Bpm=123,Key="C Major" };
        ((Settings)typeof(MainWindow).GetField("settings",flags)!.GetValue(en)!).Python=Path.Combine(folder,"missing-python.exe");
        typeof(MainWindow).GetField("cancellation",flags)!.SetValue(en,new CancellationTokenSource());
        ((Task)typeof(MainWindow).GetMethod("AnalyzeAudioCore",flags)!.Invoke(en,null)!).GetAwaiter().GetResult();
        Assert(project.Analysis.Bpm==123 && project.Name=="Bài hát giữ nguyên", "Analysis failure preserves audio/project and previous result");
        Assert(((TextBlock)en.FindName("Status")).Text.Contains("skipped"), "Analysis failure is non-blocking and localized");
        typeof(MainWindow).GetField("cancellation",flags)!.SetValue(en,null);
        // Catalog contract: translation placeholders must match the source format.
        var catalog=JsonSerializer.Deserialize<L.Catalog>(File.ReadAllText(Path.Combine(L.CatalogDirectory,"en.json")),Settings.JsonOptions)!;
        foreach(var entry in catalog.Strings.Where(e=>e.Key.Contains("{0")))
            Assert(CompositeFormat.Parse(entry.Key).MinimumArgumentCount==CompositeFormat.Parse(entry.Value).MinimumArgumentCount,"Format placeholders: "+entry.Key.Split('\n')[0]);
        string custom=Path.Combine(L.CatalogDirectory,"test-fr.json"), bad=Path.Combine(L.CatalogDirectory,"test-broken.json");
        try
        {
            File.WriteAllText(custom,JsonSerializer.Serialize(new L.Catalog { Code="test-fr",Name="Français test",Strings=new() { ["Cài đặt"]="Réglages",["Đã tách {0} stems. Drum Lab đã sẵn sàng."]="Malformed {8}" } },Settings.JsonOptions));
            File.WriteAllText(bad,"{bad-json");
            Assert(L.AvailableLanguages().Any(c=>c.Code=="test-fr"),"Custom JSON language discovered; malformed file ignored");
            L.Initialize("test-fr"); Assert(L.T("Cài đặt")=="Réglages" && L.T("Bản gốc")=="Bản gốc","Custom translation and missing-key fallback");
            Assert(L.F($"Đã tách {4} stems. Drum Lab đã sẵn sàng.").Contains("4"),"Malformed placeholder falls back safely");
            L.Initialize("unavailable");Assert(L.Language=="vi","Unknown saved language falls back to Vietnamese");
        }
        finally { File.Delete(custom);File.Delete(bad); }
        L.Initialize("vi"); Save(vi,Path.Combine(folder,"Vietnamese.png"),1384,940);
        Assert(GuideWindow.ReadGuide().Contains("lang=\"vi\""),"Vietnamese guide selected");
        app.Shutdown();
    }
    static void Save(Window window,string path,int width,int height)
    {
        var content=(FrameworkElement)window.Content;
        content.Measure(new Size(width,height));content.Arrange(new Rect(0,0,width,height));content.UpdateLayout();
        Application.Current.Dispatcher.Invoke(()=>{},DispatcherPriority.Render);
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
        var surface=new DrawingVisual();using(var dc=surface.RenderOpen())dc.DrawRectangle((Brush)Application.Current.Resources["AppBackground"],null,new Rect(0,0,width,height));
        bitmap.Render(surface);bitmap.Render(content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output=File.Create(path);encoder.Save(output);
    }
}
