using System.Globalization;
using System.Text.Json;
using System.Windows;

namespace StemForge;

public partial class MainWindow
{
    async void AnalyzeAudio(object sender, RoutedEventArgs e)
    {
        if (HasAudio()) await Work(AnalyzeAudioCore);
    }

    async Task AnalyzeAudioCore()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation!.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            var result = await engine.RunAsync(new { action = "analyze", source = project.Source }, settings, AppendLog, timeout.Token);
            project.Analysis = JsonSerializer.Deserialize<MusicAnalysis>(result.GetRawText(), Settings.JsonOptions);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            // Analysis is optional. Preserve an earlier result and all audio/mixer state.
            AppendLog(L.T("Bỏ qua dò BPM/tông; vẫn có thể chỉnh và xuất audio.") + " " +
                (timeout.IsCancellationRequested ? L.T("Hết thời gian phân tích.") : error.Message));
            return;
        }
        RefreshAnalysis();
        Persist(L.T("Dò BPM và tông"), detail: MusicInfo.Text);
        AppendLog(project.Analysis?.Bpm is null && project.Analysis?.Key is null
            ? L.T("Không đủ dữ liệu để dò BPM/tông. Đã bỏ qua; audio giữ nguyên.")
            : L.T("Đã phân tích audio. ") + MusicInfo.Text + L.T(" · Di chuột vào kết quả để xem lưu ý."));
    }

    void RefreshAnalysis()
    {
        var a = project.Analysis;
        bool validBpm = a?.Bpm is double bpm && double.IsFinite(bpm) && bpm is >= 30 and <= 300;
        UseDetectedBpmButton.IsEnabled = validBpm;
        MusicInfo.Text = a is null ? L.T("BPM / Tông: chưa phân tích") :
            L.F($"BPM ≈ {(validBpm ? a.Bpm!.Value.ToString("0.#", CultureInfo.CurrentCulture) : L.T("chưa rõ"))}   ·   Tông: {DisplayKey(a.Key)} (ước lượng)");
        MusicInfo.ToolTip = a is null ? L.T("Chỉ phân tích khi bấm Dò BPM / tông. Không tự chạy khi nhập hoặc mở bài.") :
            L.F($"Đã phân tích {a.AnalyzedSeconds:0} giây. {L.T(a.Notes)}") +
            (a.AlternateKey is null ? "" : L.F($" Tông khả dĩ khác: {DisplayKey(a.AlternateKey)}.")) +
            L.F($"\n{a.Method}\nĐiểm chu kỳ nhịp: {a.TempoStrength:0.00}; tương quan tông: {a.KeyCorrelation:0.00}. Đây không phải xác suất đúng.");
    }

    static string DisplayKey(string? key)
    {
        if (key is null) return L.T("chưa rõ");
        return L.Language == "vi" ? key.Replace(" Major", " trưởng").Replace(" Minor", " thứ")
            : key.Replace(" trưởng", " Major").Replace(" thứ", " Minor");
    }

    void UseDetectedBpm(object sender, RoutedEventArgs e)
    {
        if (project.Analysis?.Bpm is not double bpm || !double.IsFinite(bpm) || bpm is < 30 or > 300) return;
        MidiBpm.Text = bpm.ToString("0.#", CultureInfo.InvariantCulture);
        Persist(L.T("Dùng BPM dò cho MIDI"), detail: MidiBpm.Text);
        AppendLog(L.T("Đã dùng BPM dò cho MIDI. Không thay tốc độ audio hoặc lượng tử hóa hit."));
    }

    void DrumWaveformVisibilityChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        settings.ShowDrumWaveform = DrumWave.ShowOriginal = ShowDrumWave.IsChecked == true;
        settings.ShowProcessedDrumWaveform = DrumWave.ShowProcessed = ShowProcessedDrumWave.IsChecked == true;
        SaveSettings();
    }

    void RefreshDrumWaveform()
    {
        var drum = project.Stems.FirstOrDefault(s => s.Name == "drums");
        DrumWave.SetSamples(drum?.Waveform ?? []);
        DrumWave.SetProcessedSamples([]);
        DrumWaveLabel.Text = drum is null ? L.T("Tách stem Drums để xem waveform trống.") :
            drum.Waveform.Length == 0 ? L.T("Bấm Cập nhật waveform trống để đọc stem đã lưu.") :
            L.T("Lam: stem Drums gốc · Bấm cập nhật hoặc Phát trống để dựng đường tím sau hiệu ứng.");
    }

    async Task EnsureDrumWaveform()
    {
        var drum = project.Stems.FirstOrDefault(s => s.Name == "drums") ?? throw new InvalidOperationException(L.T("Tách stem Drums trước."));
        if (drum.Waveform.Length == 0)
        {
            var result = await Run(new { action = "waveform", source = drum.Path });
            drum.Waveform = JsonSerializer.Deserialize<double[]>(result.GetProperty("waveform").GetRawText())!;
        }
        DrumWave.SetSamples(drum.Waveform);
    }

    void SetDrumComparison(double[] samples)
    {
        DrumWave.SetProcessedSamples(samples);
        DrumWaveLabel.Text = L.T("Lam: Drums gốc · Tím: trống sau hiệu ứng và gain, trước master Refix · Chung thời gian / thang biên độ · Kéo để tua.");
    }

    async void RefreshDrumComparison(object sender, RoutedEventArgs e) => await PlayPreview("drums", L.T("Trống sau hiệu ứng"), autoplay: false);
}
