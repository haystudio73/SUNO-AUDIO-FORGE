using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace StemForge;

public partial class HistoryWindow : Window
{
    readonly List<SessionEntry> sessions;
    readonly List<HistoryEntry> events;
    public Project? SelectedProject { get; private set; }
    public HistoryWindow(AppStore store)
    {
        sessions = store.Sessions(); events = store.History(); InitializeComponent(); Refresh();
    }
    void Filter(object sender, TextChangedEventArgs e) { if (IsInitialized) Refresh(); }
    void Refresh()
    {
        string term = Search.Text.Trim();
        SessionsList.ItemsSource = sessions.Where(s => (s.Name+" "+s.Source).Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        EventsList.ItemsSource = events.Where(h => (h.Name+" "+h.Kind+" "+h.Output).Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        Count.Text = L.F($"{sessions.Count} phiên · {events.Count} thao tác gần nhất");
    }
    void OpenSelected(object sender, RoutedEventArgs e)
    {
        if (SessionsList.SelectedItem is not SessionEntry entry) return;
        try { SelectedProject = JsonSerializer.Deserialize<Project>(entry.Json, Settings.JsonOptions); DialogResult = true; }
        catch (Exception error) { MessageBox.Show(this, error.Message, L.T("Không đọc được phiên")); }
    }
    void OpenOutputFolder(object sender, RoutedEventArgs e)
    {
        if (EventsList.SelectedItem is not HistoryEntry entry || string.IsNullOrEmpty(entry.Output)) { MessageBox.Show(this, L.T("Chọn thao tác có file/thư mục xuất.")); return; }
        string? folder = Directory.Exists(entry.Output) ? entry.Output : File.Exists(entry.Output) ? Path.GetDirectoryName(entry.Output) : null;
        if (folder is null) { MessageBox.Show(this, L.T("File xuất đã bị di chuyển hoặc không còn trên máy.")); return; }
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }
}
