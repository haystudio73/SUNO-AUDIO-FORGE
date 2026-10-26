using System.Windows;

namespace StemForge;

public partial class SaveDrumPresetWindow : Window
{
    readonly Func<string, bool, string> save;
    public SaveDrumPresetWindow(Func<string, bool, string> save)
    {
        this.save = save;
        InitializeComponent();
        Loaded += (_, _) => PresetName.Focus();
    }
    void Save(object sender, RoutedEventArgs e)
    {
        try { save(PresetName.Text, UseAsDefault.IsChecked == true); DialogResult = true; }
        catch (Exception error) { ErrorText.Text = error.Message; PresetName.Focus(); }
    }
}
