using System.Windows;
namespace StemForge;
public partial class App : Application
{
    readonly bool startWindow;
    public App() : this(true) { }
    public App(bool startWindow) { this.startWindow = startWindow; }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!startWindow) return;
        try { MainWindow = new MainWindow(); MainWindow.Show(); }
        catch (Exception error) { MessageBox.Show(error.Message, L.T("Suno Audio Forge — không thể khởi động"), MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
    }
}
