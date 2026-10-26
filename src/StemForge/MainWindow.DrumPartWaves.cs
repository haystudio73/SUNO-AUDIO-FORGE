using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace StemForge;

public partial class MainWindow
{
    static readonly (string Id, string Name)[] DrumLanes =
        [("kick", "Kick"), ("snare", "Snare"), ("toms", "Toms"), ("hat", "Hi-hat"), ("ride", "Ride"), ("crash", "Crash")];

    Stem[] SelectedDrumParts() => project.DrumParts.Where(p => !p.Mute).ToArray();

    void UpdateDrumPartOverlay(bool valid)
    {
        var scaled = project.DrumParts.ToDictionary(p => p.Name, p => p.Waveform.Select(v => v*Math.Pow(10, p.Gain/20)).ToArray());
        DrumPartsWave.ScaleMinimum = Math.Max(1, scaled.Values.SelectMany(v => v).DefaultIfEmpty().Max());
        var selected = valid ? SelectedDrumParts() : [];
        DrumPartsWave.SetLayers(selected.Select(p => new WaveformView.Layer(p.Name, scaled[p.Name], p.Name + "Wave")));
        DrumPartsWave.IsEnabled = valid;
        DrumPartsSelection.Text = !valid ? L.T("Tách bộ trống để chỉnh từng loại trống.") : selected.Length == 0
            ? L.T("Không chọn loại trống nào · Bản remix chỉ còn các stems khác.")
            : selected.Any(p => p.Waveform.Length == 0) ? L.T("Bấm Cập nhật waveform 6 rãnh để bổ sung đường sóng còn thiếu.")
            : L.F($"Mix {selected.Length}/6 loại trống · Waveform theo gain riêng, trước hiệu ứng Drum Lab · Kéo để tua.");
    }

    void DrumPartChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ready) return;
        UpdateDrumPartOverlay(HasValidDrumParts()); InvalidateMix();
    }

    void BuildDrumPartWaves(bool valid)
    {
        DrumPartList.Children.Clear();
        foreach (var (id, name) in DrumLanes)
        {
            var part = project.DrumParts.FirstOrDefault(p => p.Name == id);
            if (part is null) continue;
            part.PropertyChanged -= DrumPartChanged; part.PropertyChanged += DrumPartChanged;
            var row = new Grid { Width = 390, Margin = new Thickness(0, 0, 18, 8), IsEnabled = valid, DataContext = part };
            foreach (var width in new[] { 90, 30, 150, 30, 90 }) row.ColumnDefinitions.Add(new() { Width = new GridLength(width) });
            var toggle = new CheckBox { Content = name, IsChecked = !part.Mute, Tag = id,
                ToolTip = L.T("Đưa ") + name + L.T(" vào bản remix và waveform. Bỏ chọn sẽ loại cả tiếng nền và sample của loại này.") };
            toggle.SetResourceReference(ForegroundProperty, id + "Wave");
            System.Windows.Automation.AutomationProperties.SetName(toggle, "Mix " + name);
            toggle.Checked += (_, _) => { if (ready) part.Mute = false; };
            toggle.Unchecked += (_, _) => { if (ready) part.Mute = true; };
            var minus = new Button { Content = "−", Padding = new Thickness(3), Margin = new Thickness(0,4,2,4), ToolTip = L.T("Giảm ") + name + " 1 dB" };
            var plus = new Button { Content = "+", Padding = new Thickness(3), Margin = new Thickness(2,4,0,4), ToolTip = L.T("Tăng ") + name + " 1 dB" };
            minus.Click += (_, _) => part.Gain = Math.Max(-48, part.Gain-1);
            plus.Click += (_, _) => part.Gain = Math.Min(12, part.Gain+1);
            var gain = new Slider { Minimum = -48, Maximum = 12, ToolTip = name + " gain (dB)" };
            gain.SetBinding(Slider.ValueProperty, new Binding(nameof(Stem.Gain)) { Mode = BindingMode.TwoWay });
            System.Windows.Automation.AutomationProperties.SetName(gain, "Gain " + name);
            var value = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center };
            value.SetBinding(TextBlock.TextProperty, new Binding(nameof(Stem.Gain)) { StringFormat = "{0:F1} dB" });
            Grid.SetColumn(minus,1); Grid.SetColumn(gain,2); Grid.SetColumn(plus,3); Grid.SetColumn(value,4);
            row.Children.Add(toggle); row.Children.Add(minus); row.Children.Add(gain); row.Children.Add(plus); row.Children.Add(value);
            DrumPartList.Children.Add(row);
        }
        UpdateDrumPartOverlay(valid);
    }

    async Task RefreshPartWaveformsCore()
    {
        if (!HasValidDrumParts()) throw new InvalidOperationException(L.T("Tách bộ trống hợp lệ trước khi đọc waveform."));
        foreach (var part in project.DrumParts)
        {
            var result = await Run(new { action = "waveform", source = part.Path });
            part.Waveform = JsonSerializer.Deserialize<double[]>(result.GetProperty("waveform").GetRawText())!;
        }
        RefreshDrumParts(); Persist(L.T("Cập nhật waveform 6 rãnh"));
        AppendLog(L.T("Đã đọc waveform 6 rãnh. Chọn loại trống, chỉnh gain rồi nghe Bản đã ReMix ở thanh phát dưới."));
    }

    async void RefreshPartWaveforms(object sender, RoutedEventArgs e) => await Work(RefreshPartWaveformsCore);
}
