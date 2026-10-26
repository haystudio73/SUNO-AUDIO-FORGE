using System.Diagnostics;
using System.IO;
using System.Windows;

namespace StemForge;

public partial class MainWindow
{
    string lastExportFolder = "";

    void RememberExport(string output)
    {
        string? folder = Directory.Exists(output) ? output : File.Exists(output) ? Path.GetDirectoryName(Path.GetFullPath(output)) : null;
        if (folder is null) return;
        lastExportFolder = folder;
        LastExportButton.ToolTip = folder;
        LastExportButton.Visibility = Visibility.Visible;
    }

    void OpenLastExport(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Directory.Exists(lastExportFolder)) { AppendLog(L.T("Thư mục xuất đã bị di chuyển hoặc không còn trên máy.")); return; }
            Process.Start(new ProcessStartInfo(lastExportFolder) { UseShellExecute = true });
        }
        catch (Exception error) { AppendLog(L.T("Không mở được thư mục xuất: ") + error.Message); }
    }

    void OpenSourceLyrics(object sender, RoutedEventArgs e)
    {
        if (!HasAudio()) return;
        var audio = project.Stems.FirstOrDefault(s => s.Name == "vocals" && File.Exists(s.Path))
            ?? new Stem { Name = "original", Path = project.Source };
        OpenLyrics(audio);
    }

    void OpenLyrics(Stem audio)
    {
        if (!Persist()) return;
        Stop(false);
        new LyricsWindow(project, audio, settings, store, RememberExport) { Owner = this }.ShowDialog();
    }
}
