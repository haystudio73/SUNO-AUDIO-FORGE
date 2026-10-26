using System.Windows;

namespace StemForge;

public partial class MainWindow
{
    StepBox[] StepBoxes => [SourceStep, ProcessingStep, StemsStep, RefixStep, LegacyDrumLab, ExportStep];
    void InitializeSteps()
    {
        foreach (var box in StepBoxes) box.IsExpanded = !settings.CollapsedSteps.Contains(box.StepId);
    }
    void StepExpansionChanged(object sender, RoutedEventArgs e)
    {
        if (!ready || e.OriginalSource is not StepBox box) return;
        if (box.IsExpanded) settings.CollapsedSteps.Remove(box.StepId);
        else if (!settings.CollapsedSteps.Contains(box.StepId)) settings.CollapsedSteps.Add(box.StepId);
        SaveSettings();
    }
}
