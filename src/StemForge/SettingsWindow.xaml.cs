using Microsoft.Win32;
using System.Windows;

namespace StemForge;

public partial class SettingsWindow : Window
{
    CancellationTokenSource? installation;
    bool closed;
    public string SelectedLanguage => AppLanguageBox.SelectedValue as string ?? "vi";
    public string Python => PythonPath.Text.Trim();
    public string Ffmpeg => FfmpegPath.Text.Trim();
    public bool RestoreSession => RestoreLast.IsChecked == true;
    public bool UseLivePreview => LivePreviewBox.IsChecked == true;
    public SettingsWindow(Settings settings, string databasePath)
    {
        InitializeComponent();
        MaxHeight = Math.Max(320, SystemParameters.WorkArea.Height - 40);
        AppLanguageBox.ItemsSource = L.AvailableLanguages(); AppLanguageBox.SelectedValue = settings.Language;
        if (AppLanguageBox.SelectedIndex < 0) AppLanguageBox.SelectedValue = "vi";
        PythonPath.Text = settings.Python; FfmpegPath.Text = settings.Ffmpeg;
        RestoreLast.IsChecked = settings.RestoreLastSession; DatabasePath.Text = databasePath;
        LivePreviewBox.IsChecked = settings.LivePreview;
        Closed += (_, _) => { closed = true; installation?.Cancel(); };
    }
    void BrowsePython(object sender, RoutedEventArgs e) { var d = new OpenFileDialog { Filter = "Python executable|python.exe|Executable|*.exe" }; if (d.ShowDialog(this) == true) PythonPath.Text = d.FileName; }
    void BrowseFfmpeg(object sender, RoutedEventArgs e) { var d = new OpenFileDialog { Filter = "FFmpeg executable|ffmpeg.exe|Executable|*.exe" }; if (d.ShowDialog(this) == true) FfmpegPath.Text = d.FileName; }
    void Save(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Python)) { MessageBox.Show(this, L.T("Chọn Python executable hoặc nhập python.")); return; }
        DialogResult = true;
    }
    void CancelInstall(object sender, RoutedEventArgs e) => installation?.Cancel();
    async void InstallFfmpeg(object sender, RoutedEventArgs e)
    {
        if (installation is not null) return;
        installation = new();
        InstallFfmpegButton.IsEnabled = SaveButton.IsEnabled = false; CancelInstallButton.IsEnabled = true;
        try
        {
            var path = await FfmpegInstaller.InstallAsync(System.IO.Path.Combine(Settings.DirectoryPath, "Tools"),
                new Progress<string>(text => { if (!closed) InstallStatus.Text = text; }), installation.Token);
            if (!closed) { FfmpegPath.Text = path; InstallStatus.Text = L.T("Đã cài và kiểm tra FFmpeg. Bấm Lưu để sử dụng."); }
        }
        catch (OperationCanceledException) { if (!closed) InstallStatus.Text = L.T("Đã hủy tải. Cấu hình FFmpeg trước đó được giữ nguyên."); }
        catch (Exception ex) { if (!closed) InstallStatus.Text = L.T("Không cài được FFmpeg: ")+ex.Message; }
        finally { installation.Dispose(); installation = null; if (!closed) { InstallFfmpegButton.IsEnabled = SaveButton.IsEnabled = true; CancelInstallButton.IsEnabled = false; } }
    }
}
