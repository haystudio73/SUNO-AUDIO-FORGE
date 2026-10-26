using System.Windows;
using System.Windows.Controls;

namespace StemForge;

public partial class MainWindow
{
    void InitializeDrumPresets()
    {
        RefreshDrumPresetList();
        GenrePresetBox.SelectedIndex = 0;
        var controls = new Control[] { Transient, Multiband, Parallel, Replacement, Sensitivity,
            TriggerThreshold, Retrigger, Bleed, Dynamics, SampleMode, BlendMode, KitBox };
        var help = DrumSettingInfo.For(new());
        for (int i = 0; i < controls.Length; i++) controls[i].ToolTip = help[i].Explanation;
        UpdateDrumDefaultLabel();
    }

    void RefreshDrumPresetList(string? selectedId = null)
    {
        GenrePresetBox.ItemsSource = DrumPresets.All.Concat(settings.SavedDrumPresets.Where(p => p.Setup.IsValid)).ToArray();
        if (selectedId is not null) GenrePresetBox.SelectedValue = selectedId;
    }

    string SaveNamedDrumPreset(string name, bool useAsDefault)
    {
        name = name.Trim().Normalize();
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl))
            throw new ArgumentException(L.T("Nhập tên từ 1 đến 80 ký tự, không có ký tự xuống dòng."));
        if (DrumPresets.All.Concat(settings.SavedDrumPresets).Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException(L.T("Tên này đã có trong Gợi ý. Hãy đặt tên khác."));
        var setup = CurrentDrumSetup();
        if (!setup.IsValid) throw new ArgumentException(L.T("Thông số Drum Lab không hợp lệ."));
        var preset = new DrumPreset("user-" + Guid.NewGuid().ToString("N"), name, L.T("Bộ thông số đã lưu của bạn. Bấm Áp dụng để dùng cho bài đang mở."), setup);
        var previousDefaults = settings.DrumDefaults;
        settings.SavedDrumPresets.Add(preset);
        if (useAsDefault) settings.DrumDefaults = setup;
        if (!SaveSettings())
        {
            settings.SavedDrumPresets.Remove(preset); settings.DrumDefaults = previousDefaults;
            throw new InvalidOperationException(L.T("Chưa lưu được vào cơ sở dữ liệu. Hãy thử lại."));
        }
        RefreshDrumPresetList(preset.Id); UpdateDrumDefaultLabel();
        Persist(L.T("Lưu bộ thông số Drum Lab"), detail: name);
        AppendLog(L.T("Đã lưu “") + name + L.T("” trong Gợi ý.") + (useAsDefault ? L.T(" Đã đặt làm mặc định cho bài mới.") : ""));
        return preset.Id;
    }

    void SaveNamedDrumPresetDialog(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveDrumPresetWindow((name, useDefault) => SaveNamedDrumPreset(name, useDefault)) { Owner = this };
        dialog.ShowDialog();
    }

    DrumSetup CurrentDrumSetup() => new(Transient.Value, Multiband.Value, Parallel.Value, Replacement.Value,
        Sensitivity.Value, TriggerThreshold.Value, Retrigger.Value, Bleed.Value, Dynamics.Value,
        Selected(KitBox), Selected(SampleMode, true), Selected(BlendMode, true));

    void SetDrumSetup(DrumSetup setup)
    {
        if (!setup.IsValid) throw new InvalidOperationException(L.T("Thông số mặc định Drum Lab không hợp lệ."));
        // Apply once so changing twelve controls does not repeatedly stop playback/save.
        bool wasReady = ready;
        ready = false;
        try
        {
            Transient.Value = setup.Transient; Multiband.Value = setup.Multiband; Parallel.Value = setup.Parallel;
            Replacement.Value = setup.Replacement; Sensitivity.Value = setup.Sensitivity; TriggerThreshold.Value = setup.ThresholdDb;
            Retrigger.Value = setup.RetriggerMs; Bleed.Value = setup.Bleed; Dynamics.Value = setup.Dynamics;
            Select(KitBox, setup.Kit); Select(SampleMode, setup.SampleMode, true); Select(BlendMode, setup.BlendMode, true);
        }
        finally { ready = wasReady; }
        if (ready) InvalidateMix();
    }

    void GenrePresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GenrePresetBox.SelectedItem is not DrumPreset preset || GenreDescription is null) return;
        GenreDescription.Text = preset.Description;
        var s = preset.Setup;
        GenreValues.Text = $"Transient {s.Transient:0}% · Multiband {s.Multiband:0}% · Parallel {s.Parallel:0}%\n"+
            L.F($"Sample {s.Replacement:0}% · Ngưỡng {s.ThresholdDb:0} dBFS · Chống lặp {s.RetriggerMs:0} ms");
        GenreDetails.ItemsSource = DrumSettingInfo.For(s);
        if (ready) { changed = true; autosave.Stop(); autosave.Start(); }
    }

    void ApplyGenrePreset(object sender, RoutedEventArgs e)
    {
        if (GenrePresetBox.SelectedItem is not DrumPreset preset) return;
        SetDrumSetup(preset.Setup);
        Persist(L.T("Áp dụng gợi ý Drum Lab"), detail: preset.Name);
        AppendLog(L.T("Đã áp dụng ")+preset.Name+L.T(". Nghe trống và bản mix cuối để tinh chỉnh; sample riêng đang chọn được giữ."));
    }

    void ClearDrumDefault(object sender, RoutedEventArgs e)
    {
        var previous = settings.DrumDefaults;
        settings.DrumDefaults = null;
        if (!SaveSettings()) { settings.DrumDefaults = previous; return; }
        UpdateDrumDefaultLabel();
        AppendLog(L.T("Đã bỏ mặc định riêng. Bài nhập mới dùng Drum Lab trung tính; bài đang mở giữ nguyên."));
    }

    void UpdateDrumDefaultLabel()
    {
        DrumDefaultLabel.Text = settings.DrumDefaults is { IsValid: true } s
            ? L.F($"Mặc định bài mới: Transient {s.Transient:0}% / Multiband {s.Multiband:0}% / Parallel {s.Parallel:0}% / Sample {s.Replacement:0}%.")
            : L.T("Mặc định bài mới: trung tính, hiệu ứng và sample ở 0%.");
        ClearDrumDefaultButton.IsEnabled = settings.DrumDefaults is not null;
    }

    void ApplyNewDrumDefaults()
    {
        if (settings.DrumDefaults is { IsValid: true } setup) SetDrumSetup(setup);
    }
}
