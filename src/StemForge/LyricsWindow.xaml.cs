using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace StemForge;

public partial class LyricsWindow : Window
{
    public record LanguageOption(string Code, string Name) { public override string ToString() => Name; }
    readonly Project project;
    readonly Stem stem;
    readonly Settings settings;
    readonly AppStore store;
    readonly Action<string>? exported;
    readonly EngineClient engine = new();
    readonly MediaPlayer player = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    readonly string tempDirectory = Path.Combine(Path.GetTempPath(), "StemForge-lyrics-"+Guid.NewGuid().ToString("N"));
    string PreviewPath => Path.Combine(tempDirectory, "vocals-preview.wav");
    LyricsDocument document;
    CancellationTokenSource? job;
    bool closed, dirty, playerReady, playing, updatingSeek;
    double duration, playStart, playEnd;
    bool lineMode, editingLine;
    LyricLine? auditionLine;

    public LyricsWindow(Project project, Stem stem, Settings settings, AppStore store, Action<string>? exported = null)
    {
        this.project = project; this.stem = stem; this.settings = settings; this.store = store;
        this.exported = exported;
        duration = project.Duration;
        var sameStem = project.Lyrics is { } saved &&
            (saved.Provider == "suno" && !string.IsNullOrEmpty(saved.SourceUrl) && saved.SourceUrl == project.SourceUrl ||
             saved.StemPath == stem.Path && saved.StemModifiedTicks == File.GetLastWriteTimeUtc(stem.Path).Ticks);
        document = sameStem ? JsonSerializer.Deserialize<LyricsDocument>(JsonSerializer.Serialize(project.Lyrics, Settings.JsonOptions), Settings.JsonOptions)! : new() { StemPath = stem.Path, StemModifiedTicks = File.GetLastWriteTimeUtc(stem.Path).Ticks };
        InitializeComponent(); SongTitle.Text = project.Name;
        SunoTimingButton.IsEnabled = !string.IsNullOrWhiteSpace(project.SourceUrl);
        SunoSource.Text = project.SourceUrl;
        player.Volume = Math.Clamp(settings.PlaybackVolume, 0, 100) / 100;
        Width = Math.Min(Width, SystemParameters.WorkArea.Width-32); Height = Math.Min(Height, SystemParameters.WorkArea.Height-32);
        ModelBox.SelectedValue = settings.WhisperModel; if (ModelBox.SelectedIndex < 0) ModelBox.SelectedValue = "small";
        DeviceBox.SelectedValue = settings.WhisperDevice; if (DeviceBox.SelectedIndex < 0) DeviceBox.SelectedValue = "auto";
        SetLanguages(new() { ["vi"] = "Vietnamese", ["en"] = "English" });
        LinesGrid.ItemsSource = document.Lines; PositionSlider.Maximum = Math.Max(.001, duration);
        player.MediaOpened += (_, _) => { if (closed) return; playerReady = true; player.Position = TimeSpan.FromSeconds(playStart); player.Play(); playing = true; };
        player.MediaFailed += (_, e) => { playing = false; playerReady = false; Status.Text = L.T("Không nghe được Vocals: ")+e.ErrorException.Message; };
        player.MediaEnded += (_, _) => EndPlayback();
        timer.Tick += (_, _) =>
        {
            if (!playing || !playerReady) return;
            var position = player.Position.TotalSeconds;
            if (position >= playEnd) { EndPlayback(); return; }
            SetPosition(position); PlayStatus.Text = (lineMode ? L.T("Nghe câu · ") : "Vocals · ")+LyricsFormat.Time(position);
        };
        timer.Start();
        Loaded += async (_, _) => await Work(async token =>
        {
            var info = await engine.RunAsync(new { action = "lyrics_info" }, settings, Progress, token);
            if (closed) return;
            SetLanguages(info.GetProperty("languages").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!));
            var prepared = await engine.RunAsync(new { action = "lyrics_preview", source = stem.Path, preview = PreviewPath }, settings, Progress, token);
            if (closed) return;
            duration = prepared.GetProperty("duration").GetDouble(); PositionSlider.Maximum = duration;
            Status.Text = sameStem ? DocumentStatus() : !string.IsNullOrWhiteSpace(project.SourceUrl)
                ? L.T("Bấm Lấy lyrics từ Suno (cần Đăng nhập Suno) để lấy lời và mốc thời gian, không chạy Whisper.")
                : L.T("Chọn ngôn ngữ hoặc để tự nhận diện. Model tải ở lần dùng đầu tiên.");
        });
    }
    void SetLanguages(Dictionary<string, string> languages)
    {
        LanguageBox.ItemsSource = new[] { new LanguageOption("auto", L.T("Tự nhận diện")) }.Concat(languages.OrderBy(p => p.Value).Select(p => new LanguageOption(p.Key, p.Key == "vi" ? L.T("Tiếng Việt · vi") : p.Value+" · "+p.Key))).ToList();
        LanguageBox.SelectedValue = settings.WhisperLanguage; if (LanguageBox.SelectedIndex < 0) LanguageBox.SelectedValue = "auto";
    }
    void Progress(string text)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => Progress(text)); return; }
        if (!closed) Status.Text = text.Length > 500 ? text[^500..] : text;
    }
    async Task Work(Func<CancellationToken, Task> action)
    {
        if (job is not null || closed) return;
        job = new(); StopAudio();
        RecognitionOptions.IsEnabled = ListenControls.IsEnabled = EditActions.IsEnabled = LinesGrid.IsEnabled = false;
        CancelButton.IsEnabled = true;
        try { await action(job.Token); }
        catch (OperationCanceledException) { Progress(L.T("Đã hủy tác vụ. Bản lời hiện tại vẫn được giữ.")); }
        catch (Exception ex) { Progress(L.T("Lỗi: ")+ex.Message); }
        finally
        {
            job.Dispose(); job = null;
            if (!closed) { RecognitionOptions.IsEnabled = ListenControls.IsEnabled = EditActions.IsEnabled = LinesGrid.IsEnabled = true; CancelButton.IsEnabled = false; }
            else Cleanup();
        }
    }
    async void InstallWhisper(object sender, RoutedEventArgs e) => await Work(async token =>
    {
        await engine.RunAsync(new { action = "install_whisper" }, settings, Progress, token);
        var info = await engine.RunAsync(new { action = "lyrics_info" }, settings, Progress, token);
        if (!closed) { SetLanguages(info.GetProperty("languages").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!)); Status.Text = L.T("Whisper đã sẵn sàng. Bấm Trích lyrics để bắt đầu."); }
    });
    string DocumentStatus() => document.Provider == "suno"
        ? document.Lines.Any(line => string.IsNullOrWhiteSpace(line.Start) || string.IsNullOrWhiteSpace(line.End))
            ? L.T("Đã lấy lời Suno. Một số hoặc toàn bộ câu chưa có thời gian: nghe audio để căn Bắt đầu/Kết thúc, hoặc xuất TXT ngay.")
            : L.T("Đã lấy lời và mốc thời gian từ Suno. Nghe và kiểm tra lại trước khi xuất LRC/SRT.")
        : L.F($"Đã mở {document.Lines.Count} câu đã lưu ({document.Language}).");

    void FetchSunoTiming(object sender, RoutedEventArgs e)
    {
        if (job is not null || string.IsNullOrWhiteSpace(project.SourceUrl) || !SaveDraft()) return;
        StopAudio();
        try
        {
            var window = new SunoLyricsBrowser(project.SourceUrl, duration) { Owner = this };
            window.LyricsReceived += () => window.DialogResult = true;
            if (window.ShowDialog() == true && window.Result is { } lines)
                ApplySunoLines(lines, lines.All(l => l.Start.Length > 0 && l.End.Length > 0) ? "suno" : "mixed");
        }
        catch (Exception error) { Status.Text = L.T("Lỗi: ") + error.Message; }
    }

    void ApplySunoLines(ObservableCollection<LyricLine> lines, string timing)
    {
        document = new() { StemPath = stem.Path, StemModifiedTicks = File.GetLastWriteTimeUtc(stem.Path).Ticks,
            Provider = "suno", Model = "", SourceUrl = project.SourceUrl, Timing = timing, Lines = lines };
        LinesGrid.ItemsSource = document.Lines; dirty = true;
        if (SaveDraft()) Status.Text = DocumentStatus();
    }

    async void Transcribe(object sender, RoutedEventArgs e)
    {
        if (!SaveDraft()) return;
        settings.WhisperLanguage = LanguageBox.SelectedValue?.ToString() ?? "auto";
        settings.WhisperModel = ModelBox.SelectedValue?.ToString() ?? "small";
        settings.WhisperDevice = DeviceBox.SelectedValue?.ToString() ?? "auto";
        await Work(async token =>
        {
            store.SaveSettings(settings);
            var result = await engine.RunAsync(new { action = "lyrics", source = stem.Path, preview = PreviewPath,
                model = settings.WhisperModel, language = settings.WhisperLanguage, device = settings.WhisperDevice }, settings, Progress, token);
            if (closed) return;
            var lines = new ObservableCollection<LyricLine>(result.GetProperty("segments").EnumerateArray().Select(segment => new LyricLine
            { Start = LyricsFormat.Time(segment.GetProperty("start").GetDouble()), End = LyricsFormat.Time(segment.GetProperty("end").GetDouble()), Text = segment.GetProperty("text").GetString()! }));
            document = new() { StemPath = stem.Path, StemModifiedTicks = File.GetLastWriteTimeUtc(stem.Path).Ticks,
                Language = result.GetProperty("language").GetString()!, Model = settings.WhisperModel, Lines = lines };
            LinesGrid.ItemsSource = document.Lines; duration = result.GetProperty("duration").GetDouble(); PositionSlider.Maximum = duration;
            dirty = true;
            if (SaveDraft()) Status.Text = lines.Count == 0 ? L.T("Không tìm thấy lời rõ ràng. Có thể chọn ngôn ngữ hoặc model khác rồi thử lại.") : L.F($"Đã lưu {lines.Count} câu · Ngôn ngữ: {document.Language}. Chọn câu để nghe và sửa.");
        });
    }
    void CancelJob(object sender, RoutedEventArgs e) => job?.Cancel();
    void BeginLyricsEdit(object? sender, DataGridBeginningEditEventArgs e) => editingLine = true;
    void Edited(object? sender, DataGridCellEditEndingEventArgs e) { editingLine = false; if (e.EditAction == DataGridEditAction.Commit) dirty = true; }
    bool CommitEdits() => LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true) && LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);
    bool SaveDraft()
    {
        try
        {
            if (!CommitEdits()) return false;
            if (!dirty) return true;
            var old = project.Lyrics;
            project.Lyrics = JsonSerializer.Deserialize<LyricsDocument>(JsonSerializer.Serialize(document, Settings.JsonOptions), Settings.JsonOptions);
            try { store.SaveSession(project, L.T("Lưu lyrics"), detail: L.F($"{document.Lines.Count} câu · {document.Language}")); }
            catch { project.Lyrics = old; throw; }
            dirty = false; return true;
        }
        catch (Exception ex) { Progress(L.T("Không lưu được bản nháp: ")+ex.Message); return false; }
    }
    void SaveDraftClick(object sender, RoutedEventArgs e) { if (SaveDraft()) Status.Text = L.T("Đã lưu bản nháp lyrics trong SQLite."); }
    void AddLine(object sender, RoutedEventArgs e)
    {
        if (!CommitEdits()) return;
        var start = Math.Min(PositionSlider.Value, Math.Max(0, duration-.001));
        var line = new LyricLine { Start = LyricsFormat.Time(start), End = LyricsFormat.Time(Math.Min(start+3, duration)) };
        document.Lines.Add(line); LinesGrid.SelectedItem = line; LinesGrid.ScrollIntoView(line); dirty = true;
    }
    void DeleteLine(object sender, RoutedEventArgs e) { if (CommitEdits() && LinesGrid.SelectedItem is LyricLine line) { document.Lines.Remove(line); dirty = true; } }
    void DeleteAllLines(object sender, RoutedEventArgs e)
    {
        if (!CommitEdits()) return;
        StopAudio(); auditionLine = null; document.Lines.Clear(); LinesGrid.SelectedItem = null; dirty = true;
        PlayStatus.Text = L.T("Đã dừng");
        Status.Text = L.T("Đã xóa tất cả dòng lyrics. Bấm Lưu bản nháp để lưu thay đổi.");
    }
    void Export(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!SaveDraft()) return;
            var format = ((Button)sender).Tag.ToString()!;
            LyricsFormat.Export(document.Lines, duration, format); // Validate before asking for a destination.
            var safeName = string.Concat(project.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var dialog = new SaveFileDialog { Filter = $"{format.ToUpperInvariant()}|*.{format}", FileName = safeName+"."+format, DefaultExt = format, AddExtension = true };
            if (dialog.ShowDialog(this) != true) return;
            if (new[] { project.Source, project.Preview }.Concat(project.Stems.Select(s => s.Path)).Where(p => !string.IsNullOrWhiteSpace(p)).Any(p => string.Equals(Path.GetFullPath(p), Path.GetFullPath(dialog.FileName), StringComparison.OrdinalIgnoreCase))) throw new IOException(L.T("Chọn tên khác để giữ audio gốc."));
            LyricsFormat.Write(dialog.FileName, document.Lines, duration, format);
            exported?.Invoke(dialog.FileName);
            Status.Text = L.T("Đã xuất UTF-8: ")+dialog.FileName;
            store.Record(L.T("Xuất lyrics"), project, dialog.FileName, format.ToUpperInvariant());
        }
        catch (Exception ex) { Progress(L.T("Không xuất được: ")+ex.Message); }
    }
    void PlayAll(object sender, RoutedEventArgs e) => StartPlayback(PositionSlider.Value >= duration-.02 ? 0 : PositionSlider.Value, duration, false);
    void PlayLine(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!CommitEdits() || LinesGrid.SelectedItem is not LyricLine line) { Status.Text = L.T("Chọn một câu để nghe."); return; }
            var timing = LyricsFormat.Validate([line], duration);
            if (timing.Count == 0) { Status.Text = L.T("Nhập lời cho câu đang chọn."); return; }
            StartPlayback(timing[0].Start, timing[0].End, true);
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    void StartPlayback(double start, double end, bool selectedLine)
    {
        if (!CommitEdits()) return;
        if (!File.Exists(PreviewPath)) { Status.Text = L.T("Chưa có file nghe thử. Đóng và mở lại Lyrics để chuẩn bị audio."); return; }
        lineMode = selectedLine; auditionLine = selectedLine ? LinesGrid.SelectedItem as LyricLine : null;
        playStart = start; playEnd = end; SetPosition(start);
        if (playerReady) { player.Position = TimeSpan.FromSeconds(start); player.Play(); playing = true; }
        else player.Open(new Uri(PreviewPath));
    }
    void EndPlayback()
    {
        if (Repeat.IsChecked == true) { var restart = lineMode ? playStart : 0; player.Position = TimeSpan.FromSeconds(restart); SetPosition(restart); player.Play(); }
        else { player.Pause(); playing = false; SetPosition(playEnd); PlayStatus.Text = L.T("Đã phát xong"); }
    }
    void Pause(object sender, RoutedEventArgs e) { player.Pause(); playing = false; PlayStatus.Text = L.T("Tạm dừng · ")+LyricsFormat.Time(PositionSlider.Value); }
    void StopAudio() { player.Close(); playing = playerReady = false; }
    void Stop(object sender, RoutedEventArgs e) { StopAudio(); SetPosition(0); PlayStatus.Text = L.T("Đã dừng"); }
    void SetPosition(double value) { updatingSeek = true; PositionSlider.Value = Math.Clamp(value, 0, duration); updatingSeek = false; FollowLyrics(PositionSlider.Value); }
    void FollowLyrics(double position)
    {
        // Never move the editor to another row during a cell edit.
        if (editingLine) return;
        LyricLine? active = null; double latest = -1, latestEnd = -1;
        foreach (var line in document.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Text)) continue;
            double start, end;
            try { start = LyricsFormat.Seconds(line.Start); end = LyricsFormat.Seconds(line.End); }
            catch (InvalidOperationException) { continue; }
            if (end <= start || end > duration + .05 || position < start || position >= end) continue;
            if (lineMode && ReferenceEquals(line, auditionLine)) { active = line; break; }
            // Latest start wins overlaps; at an identical start prefer the longer lyric
            // over a short section/instrument tag. Input order need not be sorted.
            if (start > latest || start == latest && end > latestEnd) { active = line; latest = start; latestEnd = end; }
        }
        if (ReferenceEquals(LinesGrid.SelectedItem, active)) return;
        LinesGrid.SelectedItem = active;
        if (active is not null) LinesGrid.ScrollIntoView(active);
    }
    void SeekChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (updatingSeek || PositionSlider is null) return;
        lineMode = false; playStart = e.NewValue; playEnd = duration;
        if (playerReady) player.Position = TimeSpan.FromSeconds(e.NewValue);
        FollowLyrics(e.NewValue);
    }
    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!SaveDraft()) { e.Cancel = true; return; }
        closed = true; job?.Cancel(); timer.Stop(); StopAudio();
        if (job is null) Cleanup();
    }
    void Cleanup() { try { if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
}
