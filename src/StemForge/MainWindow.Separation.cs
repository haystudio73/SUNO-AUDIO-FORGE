using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace StemForge;

public partial class MainWindow
{
    Task<JsonElement> RunSeparation(object request) => separationRunner?.Invoke(request) ?? Run(request);
    bool HasSeparatedStems(string model) => project.StemSeparation?.Matches(model, project.Source, project.Stems) == true;

    void RefreshSeparationButtons()
    {
        if (SeparateStemsButton is null || SeparatePartsButton is null || CascadeButton is null) return;
        bool stems = HasSeparatedStems(Selected(ModelBox, true));
        bool parts = HasValidDrumParts();
        SeparateStemsButton.Content = stems ? L.T("Đã tách stems") : L.T("Tách stems");
        SeparateStemsButton.ToolTip = stems ? L.T("Kết quả còn đầy đủ và khớp audio/model. Bấm sẽ dùng lại, không chạy model.") : L.T("Tách bằng model đang chọn; kết quả hợp lệ sẽ được dùng lại.");
        SeparatePartsButton.Content = parts ? L.T("Đã tách bộ trống") : L.T("Tách bộ trống");
        SeparatePartsButton.ToolTip = parts ? L.T("Đã có 6 rãnh DrumSep hợp lệ. Không cần tách lại.") : L.T("Tách stem Drums thành 6 rãnh.");
        bool sw = HasSeparatedStems("bs_roformer_sw");
        CascadeButton.Content = sw && parts ? L.T("Đã tách đủ hai bước") : sw ? L.T("Tiếp tục bước 2 · DrumSep") : L.T("Tách hai bước SW → DrumSep");
        CascadeButton.ToolTip = L.T("Tự bỏ qua từng bước đã có kết quả hợp lệ. Đổi CPU/CUDA không yêu cầu tách lại.");
    }

    void SeparationModelChanged(object sender, SelectionChangedEventArgs e)
    {
        Dirty(sender, e);
        if (ready) RefreshSeparationButtons();
    }

    async Task SeparateStemsCore(string model)
    {
        if (HasSeparatedStems(model))
        {
            RefreshSeparationButtons();
            AppendLog(L.T("Đã có stems hợp lệ của ") + model + L.T(". Dùng lại kết quả, không tách lại."));
            return;
        }
        var input = AudioFileStamp.Read(project.Source) ?? throw new InvalidOperationException(L.T("Không tìm thấy audio nguồn hợp lệ."));
        var result = await RunSeparation(new { action = "separate", source = project.Source, directory = Path.GetDirectoryName(project.Source),
            model, device = Selected(DeviceBox, true), modelDirectory = ModelDirectory });
        var stems = JsonSerializer.Deserialize<ObservableCollection<Stem>>(result.GetProperty("stems").GetRawText(), Settings.JsonOptions)!;
        var stamp = SeparationResult.Capture(model, project.Source, stems) ?? throw new InvalidOperationException(L.T("Kết quả tách thiếu rãnh hoặc file audio không hợp lệ."));
        if (!input.Matches(project.Source)) throw new InvalidOperationException(L.T("Audio nguồn đã đổi trong lúc tách. Hãy thử lại."));
        project.Stems = stems; project.StemSeparation = stamp; project.SeparationTrackingVersion = 1;
        project.DrumParts = []; project.DrumPartsSource = ""; project.DrumSeparation = null;
        RefreshProject(); Persist(L.T("Tách stems"), detail: $"{stems.Count} stems · {model}");
        AppendLog(L.F($"Đã tách {stems.Count} stems. Drum Lab đã sẵn sàng."));
    }

    async Task SeparateCascadeCore()
    {
        Select(ModelBox, "bs_roformer_sw", true);
        await SeparateStemsCore("bs_roformer_sw");
        // Stage one persists independently, so cancellation/failure in stage two can resume.
        await SeparatePartsCore();
    }
}
