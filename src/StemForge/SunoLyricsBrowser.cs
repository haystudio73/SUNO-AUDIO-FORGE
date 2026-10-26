using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace StemForge;

public sealed class SunoLyricsBrowser : Window
{
    readonly string songId, profileDirectory;
    readonly double duration;
    readonly WebView2 browser = new();
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    readonly TextBlock address = new() { TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(0, 6, 0, 0) };
    readonly Button fetchButton;
    readonly DispatcherTimer deadline = new() { Interval = TimeSpan.FromSeconds(55) };
    string? pending;
    bool closed;
    Task? initializing;
    public ObservableCollection<LyricLine>? Result { get; private set; }
    public WebView2 Browser => browser;
    public string StatusText => status.Text;
    public event Action? LyricsReceived;

    public SunoLyricsBrowser(string url, double duration, string? profileDirectory = null)
    {
        songId = SunoAlignedLyrics.SongId(url); this.duration = duration;
        this.profileDirectory = profileDirectory ?? Path.Combine(Settings.DirectoryPath, "SunoBrowser");
        Title = L.T("Kết nối Suno · Lời và mốc thời gian");
        Style = (Style)Application.Current.Resources["AppWindow"];
        Width = Math.Min(1120, SystemParameters.WorkArea.Width - 32); Height = Math.Min(850, SystemParameters.WorkArea.Height - 32);
        MinWidth = 700; MinHeight = 480; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new DockPanel(); var toolbar = new StackPanel { Margin = new(18, 12, 18, 12) };
        toolbar.Children.Add(new TextBlock { Text = L.T("Đăng nhập Suno trong cửa sổ này, rồi bấm Lấy mốc thời gian. Phiên được lưu riêng trên máy; mật khẩu/token không lưu vào project hoặc SQLite."), TextWrapping = TextWrapping.Wrap });
        var actions = new WrapPanel { Margin = new(0, 10, 0, 0) };
        fetchButton = Button(L.T("Lấy mốc thời gian"), "DownloadIcon", async (_, _) => await FetchAsync()); fetchButton.IsEnabled = false;
        actions.Children.Add(fetchButton);
        actions.Children.Add(Button(L.T("Về bài Suno"), "OpenProjectIcon", (_, _) => browser.CoreWebView2?.Navigate(SongUrl)));
        actions.Children.Add(Button(L.T("Xóa phiên đăng nhập"), "ResetIcon", async (_, _) => await ClearSession()));
        actions.Children.Add(Button(L.T("Đóng"), "CancelIcon", (_, _) => Close()));
        toolbar.Children.Add(actions); toolbar.Children.Add(address); toolbar.Children.Add(status);
        DockPanel.SetDock(toolbar, Dock.Top); layout.Children.Add(toolbar); layout.Children.Add(browser); Content = layout;
        deadline.Tick += (_, _) => Fail(L.T("Suno chưa trả mốc thời gian. Có thể thử lại; bản lời đang sửa được giữ nguyên."));
        Closed += (_, _) => { closed = true; pending = null; deadline.Stop(); browser.Dispose(); };
        Loaded += async (_, _) => await InitializeAsync();
    }
    string SongUrl => "https://suno.com/song/" + songId;
    static Button Button(string text, string icon, RoutedEventHandler click)
    {
        var button = new Button { Content = text, Style = (Style)Application.Current.Resources["IconLabelButton"], Tag = Application.Current.Resources[icon] };
        button.Click += click; return button;
    }
    public Task InitializeAsync(Action<CoreWebView2Environment, CoreWebView2>? configure = null) => initializing ??= InitializeCore(configure);
    async Task InitializeCore(Action<CoreWebView2Environment, CoreWebView2>? configure)
    {
        if (browser.CoreWebView2 is not null || closed) return;
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profileDirectory);
            if (closed) return;
            await browser.EnsureCoreWebView2Async(environment);
            if (closed) return;
            var web = browser.CoreWebView2 ?? throw new InvalidOperationException("WebView2 initialization failed");
            web.Settings.AreHostObjectsAllowed = false; web.Settings.AreDevToolsEnabled = false;
            web.Settings.IsPasswordAutosaveEnabled = false; web.Settings.IsGeneralAutofillEnabled = false;
            web.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            web.DownloadStarting += (_, e) => e.Cancel = true;
            web.NavigationStarting += (_, e) =>
            {
                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || uri.Scheme != "https") e.Cancel = true;
                // Discard pending results after navigation, even when returning to Suno.
                if (pending is not null) Fail(L.T("Trang đã đổi. Trở về bài Suno rồi lấy lại mốc thời gian."));
            };
            web.SourceChanged += (_, _) => { if (!closed) address.Text = web.Source; };
            web.NewWindowRequested += (_, e) => { e.Handled = true; if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.Scheme == "https") web.Navigate(e.Uri); };
            web.WebMessageReceived += Receive;
            configure?.Invoke(environment, web);
            fetchButton.IsEnabled = true; web.Navigate(SongUrl);
        }
        catch { if (!closed) status.Text = L.T("Không mở được Suno. Kiểm tra Microsoft Edge WebView2 Runtime và kết nối mạng."); }
    }
    public static string FetchScript(string song, string request)
    {
        using var stream = typeof(SunoLyricsBrowser).Assembly.GetManifestResourceStream("StemForge.SunoAlignedLyrics.js")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("__SONG__", JsonSerializer.Serialize(song)).Replace("__REQUEST__", JsonSerializer.Serialize(request));
    }
    public async Task FetchAsync()
    {
        if (closed || pending is not null || browser.CoreWebView2 is not { } web) return;
        if (!SunoAlignedLyrics.IsSunoOrigin(web.Source)) { status.Text = L.T("Trở về trang suno.com và đăng nhập trước khi lấy mốc thời gian."); return; }
        pending = Guid.NewGuid().ToString("N"); fetchButton.IsEnabled = false;
        status.Text = L.T("Đang lấy lời và mốc thời gian bằng phiên Suno…"); deadline.Start();
        try { await web.ExecuteScriptAsync(FetchScript(songId, pending)); }
        catch { if (!closed) Fail(L.T("Không gửi được yêu cầu Suno. Hãy thử lại.")); }
    }
    void Receive(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (closed || pending is null || !SunoAlignedLyrics.IsSunoOrigin(e.Source)) return;
        try
        {
            var json = e.WebMessageAsJson;
            if (json.Length > 1_048_576) { Fail(L.T("Dữ liệu lyrics Suno vượt giới hạn 1 MB.")); return; }
            using var doc = JsonDocument.Parse(json); var value = doc.RootElement;
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("request", out var request) || request.GetString() != pending
                || !value.TryGetProperty("song", out var song) || song.GetString() != songId) return;
            if (value.TryGetProperty("error", out var error))
            {
                Fail(error.GetString() == "login" ? L.T("Cần đăng nhập Suno hoặc phiên đã hết hạn. Đăng nhập rồi bấm lấy lại.")
                    : L.T("Suno chưa trả mốc thời gian. Có thể thử lại; bản lời đang sửa được giữ nguyên.")); return;
            }
            var lines = SunoAlignedLyrics.Parse(value.GetProperty("data"), duration);
            if (!lines.Any(l => l.Start.Length > 0)) { Fail(L.T("Bài này chưa có mốc thời gian hợp lệ trên Suno. Không tự tạo timing; có thể dùng lời công khai hoặc Whisper.")); return; }
            pending = null; deadline.Stop(); fetchButton.IsEnabled = true; Result = lines;
            status.Text = L.T("Đã lấy mốc thời gian từ Suno.");
            LyricsReceived?.Invoke();
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException)
        { Fail(L.T("Suno trả dữ liệu lyrics không hợp lệ.")); }
    }
    void Fail(string message) { pending = null; deadline.Stop(); fetchButton.IsEnabled = true; status.Text = message; }
    async Task ClearSession()
    {
        if (browser.CoreWebView2 is not { } web) return;
        Fail(L.T("Đang xóa phiên Suno trên máy…")); fetchButton.IsEnabled = false;
        try { await web.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile); if (!closed) web.Navigate(SongUrl); }
        catch { if (!closed) status.Text = L.T("Không xóa được phiên Suno. Hãy thử lại."); }
        finally { if (!closed) fetchButton.IsEnabled = true; }
    }
}
