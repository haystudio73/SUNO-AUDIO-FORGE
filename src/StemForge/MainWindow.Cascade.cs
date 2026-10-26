using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace StemForge;

public partial class MainWindow
{
    static string ModelDirectory => Path.Combine(Settings.DirectoryPath, "Models");
    Dictionary<string, TextBox> MidiBoxes() => new()
    { ["kick"] = MidiKick, ["snare"] = MidiSnare, ["toms"] = MidiToms, ["hat"] = MidiHat, ["ride"] = MidiRide, ["crash"] = MidiCrash };

    void ResetDrumMapping()
    {
        MidiBpm.Text = "120";
        foreach (var (key, note) in new Dictionary<string, int> { ["kick"] = 36, ["snare"] = 38, ["toms"] = 45, ["hat"] = 42, ["ride"] = 51, ["crash"] = 49 })
            MidiBoxes()[key].Text = note.ToString(CultureInfo.InvariantCulture);
    }

    void RestoreDrumMapping()
    {
        MidiBpm.Text = project.Drums.GetValueOrDefault("midiBpm")?.ToString() ?? "120";
        if (project.Drums.TryGetValue("midiNotes", out var value))
        {
            var mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(JsonSerializer.Serialize(value, Settings.JsonOptions));
            foreach (var (key, box) in MidiBoxes()) if (mapping?.TryGetValue(key, out var note) == true) box.Text = note;
        }
    }

    bool HasValidDrumParts()
    {
        var parent = project.Stems.FirstOrDefault(s => s.Name == "drums");
        return parent is not null && project.DrumSeparation?.Matches("drumsep_mdx23c", parent.Path, project.DrumParts) == true &&
            project.DrumSeparation.Source.Matches(project.DrumPartsSource);
    }

    void RefreshDrumParts()
    {
        bool valid = HasValidDrumParts();
        BuildDrumPartWaves(valid);
        ExportPartsButton.IsEnabled = ExportMidiButton.IsEnabled = valid;
        RefreshPartWavesButton.IsEnabled = valid;
        RefreshSeparationButtons();
        DrumPartsHint.Text = project.DrumParts.Count == 0 ? L.T("Tách stem Drums thành Kick / Snare / Toms / Hi-hat / Ride / Crash.") :
            valid ? L.T("Chỉ mix các loại trống được chọn, theo gain riêng. Nhóm này thay thế stem Drums cũ trong bản remix.") :
            L.T("Bộ trống thiếu file hoặc không khớp stem Drums. Bấm Tách bộ trống để tạo lại.");
    }

    async void InstallSeparator(object sender, RoutedEventArgs e) => await Work(async () =>
    {
        await Run(new { action = "install_separator" });
        AppendLog(L.T("Đã cài engine RoFormer / DrumSep. Model tải lần tách đầu tiên (tổng khoảng 1,1 GB). Audio xử lý trên máy."));
        Record(L.T("Cài engine RoFormer / DrumSep"));
    });

    async Task SeparatePartsCore()
    {
        if (HasValidDrumParts())
        {
            RefreshSeparationButtons();
            AppendLog(L.T("Đã có đủ 6 rãnh DrumSep hợp lệ. Dùng lại kết quả, không tách lại."));
            return;
        }
        var drum = project.Stems.FirstOrDefault(s => s.Name == "drums") ?? throw new InvalidOperationException(L.T("Tách stem Drums trước."));
        var input = AudioFileStamp.Read(drum.Path) ?? throw new InvalidOperationException(L.T("Không tìm thấy stem Drums hợp lệ."));
        var result = await RunSeparation(new { action = "separate", source = drum.Path, directory = Path.GetDirectoryName(project.Source),
            model = "drumsep_mdx23c", device = Selected(DeviceBox, true), modelDirectory = ModelDirectory });
        var parts = JsonSerializer.Deserialize<ObservableCollection<Stem>>(result.GetProperty("stems").GetRawText(), Settings.JsonOptions)!;
        var stamp = SeparationResult.Capture("drumsep_mdx23c", drum.Path, parts) ?? throw new InvalidOperationException(L.T("Kết quả DrumSep thiếu rãnh hoặc file không hợp lệ."));
        if (!input.Matches(drum.Path)) throw new InvalidOperationException(L.T("Stem Drums đã đổi trong lúc tách. Hãy thử lại."));
        project.DrumParts = parts; project.DrumSeparation = stamp;
        project.DrumPartsSource = drum.Path;
        RefreshProject();
        Persist(L.T("Tách bộ trống"), detail: L.T("DrumSep MDX23C · 6 rãnh"));
        AppendLog(L.T("Đã tách 6 rãnh trống. Có thể chọn sample riêng, dùng Layer 45% hoặc xuất MIDI."));
    }

    async void SeparateDrumParts(object sender, RoutedEventArgs e)
    {
        if (!HasAudio()) return;
        if (HasValidDrumParts()) { await SeparatePartsCore(); return; }
        await Work(SeparatePartsCore);
    }

    async void SeparateCascade(object sender, RoutedEventArgs e)
    {
        if (!HasAudio()) return;
        if (HasSeparatedStems("bs_roformer_sw") && HasValidDrumParts())
        {
            AppendLog(L.T("Đã có đủ stems SW và 6 rãnh DrumSep hợp lệ. Không cần tách lại."));
            RefreshSeparationButtons(); return;
        }
        await Work(SeparateCascadeCore);
    }

    void LayerPreset(object sender, RoutedEventArgs e)
    {
        Select(BlendMode, "layer", true); Replacement.Value = 45;
    }

    async void ExportDrumMidi(object sender, RoutedEventArgs e)
    {
        var drum = project.Stems.FirstOrDefault(s => s.Name == "drums");
        if (drum is null || project.DrumParts.Count != 6) { AppendLog(L.T("Tách bộ trống trước khi xuất MIDI.")); return; }
        if (!double.TryParse(MidiBpm.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double bpm) || !double.IsFinite(bpm) || bpm < 20 || bpm > 300)
        { AppendLog(L.T("Tempo MIDI: nhập số từ 20 đến 300 BPM.")); return; }
        var mapping = new Dictionary<string, int>();
        foreach (var (key, box) in MidiBoxes())
        {
            if (!int.TryParse(box.Text, out int note) || note < 0 || note > 127 || mapping.ContainsValue(note))
            { AppendLog(L.T("MIDI note: mỗi rãnh dùng một số khác nhau từ 0 đến 127.")); return; }
            mapping[key] = note;
        }
        var dialog = new SaveFileDialog { Filter = "MIDI|*.mid", FileName = SafeName(project.Name)+"-drums.mid", AddExtension = true };
        if (dialog.ShowDialog(this) != true || IsInputPath(dialog.FileName)) return;
        await Work(async () =>
        {
            var parameters = DrumParams(); parameters["midiBpm"] = bpm; parameters["midiMapping"] = mapping;
            var result = await Run(new { action = "drum_midi", source = drum.Path, drums = parameters, output = dialog.FileName });
            RememberExport(dialog.FileName);
            Persist(L.T("Xuất MIDI trống"), dialog.FileName, Hits(result));
            AppendLog(L.T("Đã xuất MIDI kênh 10, giữ thời điểm hit · ")+Hits(result));
        });
    }

    async void ExportDrumParts(object sender, RoutedEventArgs e)
    {
        if (!HasValidDrumParts()) { AppendLog(L.T("Cần đủ bộ DrumSep hợp lệ trước khi xuất rãnh.")); return; }
        var selected = SelectedDrumParts();
        if (selected.Length == 0) { AppendLog(L.T("Chọn ít nhất một loại trống để xuất rãnh.")); return; }
        var dialog = new OpenFolderDialog { Title = L.T("Thư mục xuất các rãnh DrumSep") };
        if (dialog.ShowDialog(this) != true) return;
        await Work(async () =>
        {
            var folder = Path.Combine(dialog.FolderName, SafeName(project.Name)+"-drumsep-"+Guid.NewGuid().ToString("N")[..8]);
            foreach (var part in selected)
            {
                cancellation!.Token.ThrowIfCancellationRequested();
                await Run(new { action = "render", source = project.Source, stems = new[] { new Stem { Name = part.Name, Path = part.Path, Gain = part.Gain } }, processingMode = "legacy",
                    refix = new { enabled = false }, drums = new { }, normalize = false,
                    output = Path.Combine(folder, part.Name+"."+Selected(FormatBox).ToLowerInvariant()),
                    sampleRate = int.Parse(Selected(RateBox, true)), bits = Selected(BitsBox, true) });
            }
            RememberExport(folder);
            Persist(L.T("Xuất rãnh DrumSep"), folder); AppendLog(L.F($"Đã xuất {selected.Length} rãnh DrumSep đã chọn, theo gain riêng: ")+folder);
        });
    }
}
