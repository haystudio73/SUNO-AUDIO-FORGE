using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace StemForge;

public partial class AboutWindow : Window
{
    public static string AppVersion => typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? L.T("Không rõ");
    public sealed record Source(string Name, string Url, string Description);
    public AboutWindow()
    {
        InitializeComponent();
        Width = Math.Min(Width, SystemParameters.WorkArea.Width-32); Height = Math.Min(Height, SystemParameters.WorkArea.Height-32);
        VersionInfo.Text = L.T("Phiên bản ") + AppVersion + " · Windows native / C# / WPF";
        SourceLinks.ItemsSource = new Source[] {
            new("StemDeck", "https://github.com/stemdeckapp/stemdeck", L.T("Tham khảo quy trình tách stems và chuẩn hóa Demucs · Apache-2.0.")),
            new("SUNO-AI-refix-audio", "https://github.com/haystudio73/SUNO-AI-refix-audio", L.T("Tham khảo chuỗi xử lý Refix · Apache-2.0.")),
            new("Meta Demucs", "https://github.com/facebookresearch/demucs", L.T("Mô hình tách nhạc cụ · MIT.")),
            new("audio-separator / MSST", "https://github.com/nomadkaraoke/python-audio-separator", L.T("Engine RoFormer / MDX23C; nguồn checkpoint xem phần thông tin bên thứ ba.")),
            new("OpenAI Whisper", "https://github.com/openai/whisper", L.T("Nhận dạng lời hát cục bộ · MIT.")),
            new("librosa", "https://github.com/librosa/librosa", L.T("Phân tích BPM và hòa âm khi người dùng yêu cầu · ISC.")),
            new("libraz/bpm-detector", "https://github.com/libraz/bpm-detector", L.T("Dò BPM/tông thay thế · MIT. Bỏ qua kết quả thiếu tin cậy.")),
            new(".NET / WPF", "https://github.com/dotnet/wpf", L.T("Giao diện Windows native · MIT. SQLite lưu settings và lịch sử; WebView2 hiển thị hướng dẫn.")) };
        LicenseText.Text = ReadNotice("StemForge.License.txt");
        NoticesText.Text = ReadNotice("StemForge.Notices.txt");
    }
    static string ReadNotice(string name)
    {
        using var stream = typeof(App).Assembly.GetManifestResourceStream(name);
        if (stream is null) return L.T("Không tìm thấy thông tin giấy phép trong bản chạy.");
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    void OpenSource(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        if (e.Uri.Scheme != "https") return;
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, L.T("Không mở được liên kết"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    void CloseAbout(object sender, RoutedEventArgs e) => Close();
}
