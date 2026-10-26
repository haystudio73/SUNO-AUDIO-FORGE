using System.Windows;
using System.Windows.Input;

namespace StemForge;

public partial class MainWindow
{
    GuideWindow? guideWindow;

    void OpenGuide(object sender, RoutedEventArgs e)
    {
        if (guideWindow is not null)
        {
            if (guideWindow.WindowState == WindowState.Minimized) guideWindow.WindowState = WindowState.Normal;
            guideWindow.Activate();
            return;
        }
        guideWindow = new GuideWindow { Owner = this };
        guideWindow.Closed += (_, _) => guideWindow = null;
        guideWindow.Show();
    }

    void OpenAbout(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();

    void HelpKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F1) return;
        e.Handled = true;
        OpenGuide(sender, e);
    }
}
