using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace StemForge;

public partial class GuideWindow : Window
{
    public const string GuideUrl = "https://stemforge-guide.invalid/index.html";
    readonly string profileDirectory;
    bool closed, initialized;
    Task? initializing;

    public GuideWindow(string? profileDirectory = null)
    {
        this.profileDirectory = profileDirectory ?? Path.Combine(Settings.DirectoryPath, "GuideBrowser");
        InitializeComponent();
        Width = Math.Min(Width, SystemParameters.WorkArea.Width-32);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height-32);
        Loaded += async (_, _) => await InitializeBrowserAsync();
        Closed += (_, _) => { closed = true; GuideBrowser.Dispose(); };
    }

    public static string ReadGuide()
    {
        using var stream = typeof(GuideWindow).Assembly.GetManifestResourceStream(L.Language == "en" ? "StemForge.Guide.en.html" : "StemForge.Guide.html")
            ?? throw new InvalidOperationException(L.T("Thiếu hướng dẫn nhúng trong ứng dụng."));
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public Task InitializeBrowserAsync() => initializing ??= InitializeCore();

    async Task InitializeCore()
    {
        try
        {
            var html = Encoding.UTF8.GetBytes(ReadGuide());
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profileDirectory);
            if (closed) return;
            await GuideBrowser.EnsureCoreWebView2Async(environment);
            if (closed) return;
            var browser = GuideBrowser.CoreWebView2;
            browser.Settings.AreHostObjectsAllowed = false;
            browser.Settings.IsWebMessageEnabled = false;
            browser.Settings.AreDevToolsEnabled = false;
            browser.Settings.AreDefaultContextMenusEnabled = false;
            // Serve the assembly resource in memory. No web server, source-tree file,
            // or network request is needed; a stable origin retains checklist state.
            browser.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            browser.WebResourceRequested += (_, e) =>
            {
                bool guide = IsGuideAddress(e.Request.Uri);
                e.Response = environment.CreateWebResourceResponse(new MemoryStream(guide ? html : []),
                    guide ? 200 : 404, guide ? "OK" : "Not Found",
                    "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store");
            };
            browser.NavigationStarting += (_, e) => { if (!IsGuideAddress(e.Uri)) e.Cancel = true; };
            browser.NewWindowRequested += (_, e) => e.Handled = true;
            browser.NavigationCompleted += (_, e) =>
            {
                if (closed) return;
                if (e.IsSuccess) GuideStatusPanel.Visibility = Visibility.Collapsed;
                else ShowError(L.T("Không mở được nội dung hướng dẫn: ") + e.WebErrorStatus);
            };
            initialized = true;
            browser.Navigate(GuideUrl);
        }
        catch (Exception ex)
        {
            if (!closed) ShowError(L.T("Không mở được hướng dẫn trong app. Cần Microsoft Edge WebView2 Runtime. Bạn vẫn có thể bấm Lưu HTML để lấy hướng dẫn ngoại tuyến.\n\n") + ex.Message);
        }
    }

    public static bool IsGuideAddress(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri)
        && uri.GetLeftPart(UriPartial.Path) == GuideUrl;

    void ShowError(string message)
    {
        GuideStatus.Text = message;
        GuideStatusPanel.Visibility = Visibility.Visible;
        RetryGuideButton.Visibility = Visibility.Visible;
    }

    async void RetryGuide(object sender, RoutedEventArgs e)
    {
        RetryGuideButton.Visibility = Visibility.Collapsed;
        GuideStatus.Text = L.T("Đang mở hướng dẫn…");
        if (initialized) GuideBrowser.CoreWebView2.Navigate(GuideUrl);
        else { initializing = null; await InitializeBrowserAsync(); }
    }

    void GoHome(object sender, RoutedEventArgs e)
    {
        if (initialized) GuideBrowser.CoreWebView2.Navigate(GuideUrl);
    }

    void SaveHtml(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = L.T("Hướng dẫn HTML|*.html"), FileName = "Suno-Audio-Forge-Guide.html" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, ReadGuide(), new UTF8Encoding(false)); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, L.T("Không lưu được hướng dẫn"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    void CloseGuide(object sender, RoutedEventArgs e) => Close();
}
