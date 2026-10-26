using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace StemForge;

public partial class MainWindow : Window
{
    readonly EngineClient engine = new();
    readonly Func<object, Task<JsonElement>>? separationRunner;
    MediaPlayer player = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    readonly AppStore store;
    Settings settings;
    readonly DispatcherTimer autosave = new() { Interval = TimeSpan.FromMilliseconds(750) };
    Project project = new();
    CancellationTokenSource? cancellation;
    readonly Dictionary<string, string[]> samples = new() { ["kick"] = [], ["snare"] = [], ["hat"] = [], ["toms"] = [], ["ride"] = [], ["crash"] = [] };
    readonly Dictionary<string, (string Path, double[] Waveform)> previews = [];
    string activePath = "", activeChannel = "";
    bool mediaReady, updatingPosition;
    double cursorSeconds;
    string playbackMode = "";
    bool ready, playing, changed;

    public MainWindow() : this(AppStore.Default) { }
    public MainWindow(AppStore database, Func<object, Task<JsonElement>>? separationRunner = null)
    {
        this.separationRunner = separationRunner;
        store = database;
        settings = store.LoadSettings();
        L.Initialize(settings.Language);
        InitializeComponent();
        InitializeSteps();
        InitializePlaybackVolume();
        InitializeDrumPresets();
        ThemeManager.Apply(settings.Theme);
        UpdateProcessingMode();
        MinWidth = Math.Min(MinWidth, SystemParameters.WorkArea.Width - 32);
        MinHeight = Math.Min(MinHeight, SystemParameters.WorkArea.Height - 32);
        Width = Math.Min(Width, SystemParameters.WorkArea.Width - 32);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 32);
        OriginalRepeat.IsChecked = settings.Repeat;
        ShowOriginalWave.IsChecked = Wave.ShowOriginal = settings.ShowOriginalWaveform;
        ShowProcessedWave.IsChecked = Wave.ShowProcessed = settings.ShowProcessedWaveform;
        ShowDrumWave.IsChecked = DrumWave.ShowOriginal = settings.ShowDrumWaveform;
        ShowProcessedDrumWave.IsChecked = DrumWave.ShowProcessed = settings.ShowProcessedDrumWaveform;
        AttachPlayer(player);
        timer.Tick += (_, _) => { if (playing && mediaReady && !Wave.IsMouseCaptured && !DrumWave.IsMouseCaptured && !DrumPartsWave.IsMouseCaptured && !SeekBar.IsMouseCaptureWithin) UpdatePosition(liveActive && liveSession is not null ? liveSession.Position : player.Position.TotalSeconds); };
        Closed += (_, _) => DisposeLive();
        Wave.SeekRequested += ratio => Seek(ratio*project.Duration);
        DrumWave.SeekRequested += ratio => Seek(ratio*project.Duration);
        DrumPartsWave.SeekRequested += ratio => Seek(ratio*project.Duration);
        autosave.Tick += (_, _) => { autosave.Stop(); Persist(); };
        timer.Start();
        ready = true;
        Loaded += async (_, _) =>
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && File.Exists(args[1])) await Import(args[1]);
            else if (settings.RestoreLastSession && !string.IsNullOrEmpty(settings.LastSessionSource))
            {
                try { if (store.LoadSession(settings.LastSessionSource) is { } last) LoadProjectSnapshot(last); }
                catch (Exception error) { AppendLog(L.T("Không mở lại phiên gần nhất: ") + error.Message); }
            }
        };
    }

    static string Selected(ComboBox box, bool tag = false) => ((ComboBoxItem)box.SelectedItem)?.GetValue(tag ? FrameworkElement.TagProperty : ContentControl.ContentProperty)?.ToString() ?? "";
    static void Select(ComboBox box, string value, bool tag = false)
    {
        foreach (ComboBoxItem item in box.Items)
            if ((tag ? item.Tag : item.Content)?.ToString() == value) { box.SelectedItem = item; return; }
    }

    void AppendLog(string text)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => AppendLog(text)); return; }
        Status.Text = text;
        Log.AppendText($"{DateTime.Now:HH:mm:ss}  {text}\n");
        if (Log.Text.Length > 18000) Log.Text = Log.Text[^12000..];
        Log.ScrollToEnd();
    }

    Task Work(Func<Task> action) => WorkCore(action, keepPlayback: false);

    async Task WorkCore(Func<Task> action, bool keepPlayback)
    {
        if (cancellation is not null) return;
        cancellation = new();
        autosave.Stop();
        Workspace.IsEnabled = ProjectActions.IsEnabled = false;
        PlaybackActions.IsEnabled = keepPlayback;
        CancelButton.IsEnabled = true;
        WorkProgress.IsIndeterminate = true;
        if (!keepPlayback) Stop(false);
        try { await action(); }
        catch (OperationCanceledException) { AppendLog(L.T("Đã hủy. Những file xuất hoàn tất trước khi hủy vẫn được giữ.")); Record(L.T("Hủy tác vụ")); }
        catch (Exception ex)
        {
            AppendLog(L.T("Lỗi: ") + ex.Message);
            Record(L.T("Lỗi tác vụ"), detail: ex.Message);
            if (!keepPlayback) MessageBox.Show(this, ex.Message, "Suno Audio Forge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            cancellation.Dispose(); cancellation = null;
            Workspace.IsEnabled = ProjectActions.IsEnabled = PlaybackActions.IsEnabled = true;
            CancelButton.IsEnabled = false; WorkProgress.IsIndeterminate = false;
            if (changed) autosave.Start();
        }
    }

    Task<JsonElement> Run(object request) => engine.RunAsync(request, settings, AppendLog, cancellation!.Token);

    bool HasAudio()
    {
        if (File.Exists(project.Source)) return true;
        AppendLog(L.T("Hãy mở audio trước.")); return false;
    }

    void Dirty(object? sender, RoutedEventArgs e)
    {
        if (!ready) return;
        InvalidateMix();
    }

    void InvalidateMix()
    {
        if (!ready) return;
        changed = true; previews.Clear();
        Wave.SetProcessedSamples([]);
        DrumWave.SetProcessedSamples([]);
        DrumWaveLabel.Text = L.T("Đã đổi thông số · Bấm Cập nhật waveform trống để dựng lại đường tím.");
        WaveLabel.Text = L.T("Chung thời gian / thang biên độ · Bấm Cập nhật so sánh để dựng mix mới.");
        if(liveActive)UpdateLiveParameters();
        else if (activeChannel != "original" && !string.IsNullOrEmpty(activePath)) Stop(false);
        autosave.Stop(); autosave.Start();
    }

    void RefreshProject()
    {
        TrackName.Text = string.IsNullOrEmpty(project.Source) ? L.T("Bắt đầu với bản nhạc của bạn") : project.Name;
        TrackInfo.Text = $"{TimeSpan.FromSeconds(project.Duration):mm\\:ss}  /  {project.SampleRate:N0} Hz  /  Stereo  /  {project.Stems.Count} stems";
        if (project.DownloadKind == "video") TrackInfo.Text += L.T("  /  Audio từ video Suno");
        SunoUrl.Text = project.SourceUrl;
        SourceLyricsButton.IsEnabled = File.Exists(project.Source);
        Wave.SetSamples(project.Waveform);
        Wave.SetProcessedSamples([]);
        RefreshAnalysis();
        updatingPosition = true; SeekBar.Maximum = Math.Max(.001, project.Duration); updatingPosition = false;
        UpdatePosition(0);
        WaveLabel.Text = L.T("Chung thời gian / thang biên độ · Chưa có waveform sau xử lý.");
        StemList.ItemsSource = project.Stems;
        RefreshDrumParts();
        StemEmpty.Visibility = project.Stems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DrumPanel.IsEnabled = project.Stems.Any(s => s.Name == "drums");
        DrumHint.Text = DrumPanel.IsEnabled ? L.T("Xử lý trên stem drums; các stem còn lại giữ riêng trong mixer.") : L.T("Tách stems để mở bộ xử lý riêng cho trống.");
        foreach (var stem in project.Stems) { stem.PropertyChanged -= StemChanged; stem.PropertyChanged += StemChanged; }
        InvalidateMix();
        RefreshDrumWaveform();
    }

    void StemChanged(object? sender, PropertyChangedEventArgs e) => InvalidateMix();

    static OpenFileDialog AudioDialog() => new() { Filter = "Audio|*.wav;*.flac;*.mp3;*.m4a;*.ogg;*.opus;*.aiff;*.aif;*.wma;*.mp4|All files|*.*" };

    bool CanReplaceProject() => !changed || Persist();

    async void ImportFile(object sender, RoutedEventArgs e)
    {
        var dialog = AudioDialog();
        if (dialog.ShowDialog(this) == true && CanReplaceProject()) await Import(dialog.FileName);
    }

    async void ImportUrl(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SunoUrl.Text)) { AppendLog(L.T("Dán URL bài hát Suno trước.")); return; }
        if (CanReplaceProject()) await Import(null, SunoUrl.Text.Trim());
    }

    void OpenSunoPage(object sender, RoutedEventArgs e)
    {
        var text = SunoUrl.Text.Trim();
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort ||
            uri.Host is not ("suno.com" or "www.suno.com" or "suno.ai" or "www.suno.ai"))
        { AppendLog(L.T("Dán URL https://suno.com/song/… để mở bài trên Suno.")); return; }
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { AppendLog(L.T("Không mở được trình duyệt: ")+ex.Message); }
    }

    async void FileDrop(object sender, DragEventArgs e)
    {
        if (cancellation is not null) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0 && CanReplaceProject()) await Import(paths[0]);
    }

    Task Import(string? path, string? url = null) => Work(async () =>
    {
        var directory = Path.Combine(Settings.DirectoryPath, "Sessions", DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]);
        var result = await Run(new { action = "import", source = path, url, directory });
        DisposeLive();
        project = JsonSerializer.Deserialize<Project>(result.GetRawText(), Settings.JsonOptions)!;
        ResetControls();
        ApplyNewDrumDefaults();
        Select(ProcessingModeBox, "structural", true); UpdateProcessingMode();
        RefreshProject();
        Persist(L.T("Nhập audio"), detail: project.DownloadKind == "video" ? L.T("Audio từ video Suno công khai · ")+project.SourceUrl : project.SourceUrl);
        AppendLog(project.DownloadKind == "video" ? L.T("Đã nhập audio từ video Suno công khai (không phải WAV gốc). Có thể nghe hoặc tách stems.") : L.T("Đã nhập audio. Có thể nghe bản gốc, refix hoặc tách stems."));
    });

    async void Separate(object sender, RoutedEventArgs e)
    {
        if (!HasAudio()) return;
        string model = Selected(ModelBox, true);
        if (HasSeparatedStems(model)) { await SeparateStemsCore(model); return; }
        await Work(() => SeparateStemsCore(model));
    }

    Dictionary<string, object> RefixParams() => new()
    {
        ["enabled"] = RefixEnabled.IsChecked == true, ["highPass"] = HighPass.Value,
        ["mud"] = Mud.Value, ["presence"] = Presence.Value, ["air"] = Air.Value, ["width"] = WidthControl.Value/100
    };
    Dictionary<string, object> StereoParams() => new()
    {
        ["enabled"] = StereoEnabled.IsChecked == true,
        ["attack80"] = Attack80.Value, ["attack3200"] = Attack3200.Value,
        ["mud180"] = Tonal180.Value, ["mud320"] = Tonal320.Value, ["harsh"] = TonalHarsh.Value,
        ["sideAmount"] = SideAmount.Value, ["width"] = StereoWidth.Value,
        ["impactDb"] = ImpactGain.Value, ["subAmount"] = SubAmount.Value, ["subHz"] = SubFrequency.Value
    };
    void ProcessingModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        UpdateProcessingMode(); InvalidateMix();
    }
    void UpdateProcessingMode()
    {
        var mode = Selected(ProcessingModeBox, true);
        StructuralControls.Visibility = mode == "structural" ? Visibility.Visible : Visibility.Collapsed;
        ImpactControls.Visibility = mode == "impact" ? Visibility.Visible : Visibility.Collapsed;
        StereoCommon.Visibility = mode != "legacy" ? Visibility.Visible : Visibility.Collapsed;
        LegacyWorkspace.IsEnabled = LegacyDrumLab.IsEnabled = mode == "legacy";
        LegacyWorkspace.Visibility = LegacyDrumLab.Visibility = mode == "legacy" ? Visibility.Visible : Visibility.Collapsed;
        ExportStemsButton.IsEnabled = mode == "legacy";
        RoutingHint.Text = mode == "legacy" ? L.T("DrumSep đã tách: chỉ mix các loại trống được chọn, áp gain riêng và Drum Lab, rồi ghép với stems còn lại. Không cộng thêm Drums cũ.") :
            L.T("Xử lý stereo gốc trực tiếp. Mixer stems, Drum Lab và Refix cũ không tham gia bản cuối.");
    }
    Dictionary<string, object> DrumParams() => new()
    {
        ["genrePreset"] = GenrePresetBox.SelectedValue?.ToString() ?? "neutral",
        ["transient"] = Transient.Value, ["multiband"] = Multiband.Value, ["parallel"] = Parallel.Value,
        ["replacement"] = Replacement.Value, ["sensitivity"] = Sensitivity.Value, ["kit"] = Selected(KitBox),
        ["thresholdDb"] = TriggerThreshold.Value, ["retriggerMs"] = Retrigger.Value,
        ["bleed"] = Bleed.Value, ["dynamics"] = Dynamics.Value, ["sampleMode"] = Selected(SampleMode, true),
        ["kickSamples"] = samples["kick"], ["snareSamples"] = samples["snare"], ["hatSamples"] = samples["hat"],
        ["tomsSamples"] = samples["toms"], ["rideSamples"] = samples["ride"], ["crashSamples"] = samples["crash"],
        ["mixParts"] = project.DrumParts.Count > 0,
        ["blendMode"] = Selected(BlendMode, true), ["parts"] = project.DrumParts, ["partsSource"] = project.DrumPartsSource,
        ["midiBpm"] = MidiBpm.Text, ["midiNotes"] = MidiBoxes().ToDictionary(p => p.Key, p => p.Value.Text)
    };

    object RenderRequest(string output, bool preview = false) => new
    {
        action = "render", source = project.Source, stems = project.Stems, refix = RefixParams(), drums = DrumParams(), output,
        processingMode = Selected(ProcessingModeBox, true), stereo = StereoParams(),
        sampleRate = preview ? 48000 : int.Parse(Selected(RateBox, true)), bits = preview ? "16" : Selected(BitsBox, true),
        normalize = Normalize.IsChecked == true, lufs = double.Parse(Selected(LufsBox, true), CultureInfo.InvariantCulture)
    };

    void Play(string path, string mode, string channel)
    {
        if (activePath == path && mediaReady)
        {
            if (cursorSeconds >= project.Duration-.05) Seek(0);
            player.Play(); playing = true; UpdateTransport(); return;
        }
        Stop(false); activePath = path; activeChannel = channel; playbackMode = mode; playOnOpen = true;
        player.Open(new Uri(path, UriKind.Absolute));
        PlaybackInfo.Text = mode + L.T(" · đang mở…");
    }
    void Stop(bool resetPosition = true)
    {
        if(liveActive && liveSession is not null && !resetPosition)UpdatePosition(liveSession.Position);
        StopLive();
        CancelPendingSwitch(); playOnOpen = false;
        player.Stop(); player.Close(); playing = false; mediaReady = false; activePath = "";
        if (resetPosition) UpdatePosition(0);
        UpdateTransport();
    }
    bool MatchesChannel(string channel) => activeChannel == channel || channel == "stems" && activeChannel.StartsWith("stem:");
    void StopPlayback(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && MatchesChannel(button.Tag?.ToString() ?? "mix")) Stop();
    }
    void PausePlayback(object sender, RoutedEventArgs e)
    {
        if(liveActive){PauseGlobal(sender,e);return;}
        if (sender is Button button && MatchesChannel(button.Tag?.ToString() ?? "mix") && mediaReady)
        { player.Pause(); playing = false; UpdatePosition(player.Position.TotalSeconds); UpdateTransport(); }
    }
    void Seek(double seconds)
    {
        UpdatePosition(seconds);
        if(liveActive)liveSession?.Seek(cursorSeconds);
        else if (mediaReady) player.Position = TimeSpan.FromSeconds(cursorSeconds);
    }
    void SeekBarChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if (ready && !updatingPosition) Seek(e.NewValue); }
    void UpdatePosition(double seconds)
    {
        cursorSeconds = Math.Clamp(double.IsFinite(seconds) ? seconds : 0, 0, Math.Max(0, project.Duration));
        if (SeekBar is null) return;
        updatingPosition = true; SeekBar.Value = cursorSeconds; updatingPosition = false;
        Wave.PositionRatio = project.Duration > 0 ? cursorSeconds/project.Duration : 0;
        DrumWave.PositionRatio = Wave.PositionRatio;
        DrumPartsWave.PositionRatio = Wave.PositionRatio;
        UpdateTransport();
    }
    void UpdateTransport()
    {
        if (PlaybackInfo is null) return;
        string time = $"{TimeSpan.FromSeconds(cursorSeconds):mm\\:ss} / {TimeSpan.FromSeconds(project.Duration):mm\\:ss}";
        string state = playing ? L.T("Đang phát") : mediaReady ? L.T("Tạm dừng") : L.T("Đã dừng");
        PlaybackInfo.Text = $"{(string.IsNullOrEmpty(playbackMode) ? L.T("Vị trí phát") : playbackMode)} · {state} · {time}";
    }
    void RepeatChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        settings.Repeat = OriginalRepeat.IsChecked == true; SaveSettings();
        if(liveSession is not null)liveSession.Repeat=settings.Repeat;
    }
    async void PlayOriginal(object sender, RoutedEventArgs e)
    {
        if(settings.LivePreview){await PlayLive("original",L.T("Bản gốc"));return;}
        if (!HasAudio()) return;
        if (!File.Exists(project.Preview)) { AppendLog(L.T("File nghe bản gốc bị thiếu. Hãy nhập lại audio.")); return; }
        Play(project.Preview, L.T("Bản gốc"), "original");
    }
    void WaveformVisibilityChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        settings.ShowOriginalWaveform = Wave.ShowOriginal = ShowOriginalWave.IsChecked == true;
        settings.ShowProcessedWaveform = Wave.ShowProcessed = ShowProcessedWave.IsChecked == true;
        SaveSettings();
    }
    void SetComparisonWaveform(double[] waveform, string label)
    {
        Wave.SetProcessedSamples(waveform);
        WaveLabel.Text = L.T("Chung thời gian / thang biên độ · Cam: ") + label + L.T(" · Kéo để tua.");
    }
    async void RefreshComparison(object sender, RoutedEventArgs e) => await PlayPreview("mix", L.T("Bản mix cuối"), autoplay: false);
    async void PlayMix(object sender, RoutedEventArgs e) => await PlayPreview("mix", L.T("Bản mix cuối"));
    async void PlayStems(object sender, RoutedEventArgs e) => await PlayPreview("stems", L.T("Stems trước hiệu ứng"));
    async void PlayDrums(object sender, RoutedEventArgs e) => await PlayPreview("drums", L.T("Trống sau hiệu ứng"));
    async void PlaySingleStem(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is Stem stem) await PlayPreview("stem:"+stem.Name, "Stem "+stem.Name, stem);
    }
    void ExtractLyrics(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not Stem stem || stem.Name != "vocals") return;
        if (!File.Exists(stem.Path)) { AppendLog(L.T("Không tìm thấy stem Vocals. Hãy tách lại stems.")); return; }
        OpenLyrics(stem);
    }
    async Task PlayPreview(string stage, string label, Stem? single = null, bool autoplay = true)
    {
        if (!HasAudio()) return;
        if(settings.LivePreview)
        {
            if(autoplay)await PlayLive(stage,label);
            else await PreviewWaveformInMemory(stage,single);
            return;
        }
        if ((stage == "drums" || stage == "mix" && Selected(ProcessingModeBox, true) == "legacy") &&
            project.DrumParts.Count > 0 && !HasValidDrumParts())
        { AppendLog(L.T("Bộ DrumSep đã thay đổi hoặc thiếu file. Tách lại bộ trống trước khi dựng bản remix.")); return; }
        if (stage != "mix" && project.Stems.Count == 0) { AppendLog(L.T("Tách stems trước khi nghe riêng.")); return; }
        if (stage == "drums" && !project.Stems.Any(s => s.Name == "drums")) { AppendLog(L.T("Tách stem Drums trước.")); return; }
        if (previews.TryGetValue(stage, out var cached) && File.Exists(cached.Path))
        {
            if (stage == "mix") SetComparisonWaveform(cached.Waveform, L.T("mix nghe thử"));
            if (stage == "drums") SetDrumComparison(cached.Waveform);
            if (autoplay) Play(cached.Path, label, stage);
            return;
        }
        await Work(async () =>
        {
            if (stage == "drums") await EnsureDrumWaveform();
            CaptureProject();
            var output = Path.Combine(Path.GetDirectoryName(project.Source)!, SafeName(stage)+"-preview.wav");
            var result = await Run(PreviewRequest.Create(project, stage, output, single, stage == "drumsep" ? SelectedDrumParts() : null));
            var waveform = JsonSerializer.Deserialize<double[]>(result.GetProperty("waveform").GetRawText())!;
            previews[stage] = (output, waveform);
            if (stage == "mix") SetComparisonWaveform(waveform, L.T("mix nghe thử"));
            if (stage == "drums") SetDrumComparison(waveform);
            Persist(autoplay ? "Nghe: "+label : L.T("Cập nhật waveform so sánh"));
            AppendLog(L.T("Đã dựng ") + label + ". " + Hits(result));
            if (autoplay) Play(output, label, stage);
        });
    }

    static string Hits(JsonElement result)
    {
        if (!result.TryGetProperty("hits", out var hits)) return "";
        return string.Join(" · ", hits.EnumerateObject().Select(p => $"{p.Name}: {p.Value} hits"));
    }

    async void ExportMix(object sender, RoutedEventArgs e)
    {
        if (!HasAudio()) return;
        string extension = Selected(FormatBox).ToLowerInvariant();
        var dialog = new SaveFileDialog { Filter = $"{extension.ToUpperInvariant()} audio|*.{extension}", FileName = SafeName(project.Name)+"-mix."+extension, AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        if (IsInputPath(dialog.FileName)) { AppendLog(L.T("Chọn tên xuất khác để giữ nguyên source/stems.")); return; }
        await Work(async () =>
        {
            ValidateDrumMix();
            var result = await Run(RenderRequest(dialog.FileName));
            RememberExport(dialog.FileName);
            SetComparisonWaveform(JsonSerializer.Deserialize<double[]>(result.GetProperty("waveform").GetRawText())!, L.T("file mix vừa xuất"));
            var loudness = result.GetProperty("integratedLufs");
            var peak = result.GetProperty("truePeakDbtp");
            Persist(L.T("Xuất bản mix"), dialog.FileName, $"{loudness} LUFS / {peak} dBTP");
            AppendLog(L.F($"Đã xuất: {dialog.FileName}  |  {loudness} LUFS / {peak} dBTP  ") + Hits(result));
        });
    }

    bool IsInputPath(string path) => new[] { project.Source, project.Preview }.Concat(project.Stems.Concat(project.DrumParts).Select(s => s.Path)).Any(p => string.Equals(Path.GetFullPath(p), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
    static string SafeName(string name) => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    void ValidateDrumMix()
    {
        if (Selected(ProcessingModeBox, true) == "legacy" && project.DrumParts.Count > 0 && !HasValidDrumParts())
            throw new InvalidOperationException(L.T("Bộ DrumSep đã thay đổi hoặc thiếu file. Tách lại bộ trống trước khi xuất."));
    }

    async void ExportStems(object sender, RoutedEventArgs e)
    {
        if (project.Stems.Count == 0) { AppendLog(L.T("Tách stems trước khi xuất riêng từng stem.")); return; }
        var dialog = new OpenFolderDialog { Title = L.T("Chọn thư mục xuất stems") };
        if (dialog.ShowDialog(this) != true) return;
        await Work(async () =>
        {
            // New subdirectory avoids silently overwriting any previous export.
            ValidateDrumMix();
            var folder = Path.Combine(dialog.FolderName, SafeName(project.Name)+"-stems-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..4]);
            foreach (var stem in project.Stems)
            {
                cancellation!.Token.ThrowIfCancellationRequested();
                var output = Path.Combine(folder, stem.Name+"."+Selected(FormatBox).ToLowerInvariant());
                await Run(new { action = "render", source = project.Source, stems = new[] { new Stem { Name = stem.Name, Path = stem.Path, Gain = stem.Gain } },
                    refix = new { enabled = false }, drums = DrumParams(), output,
                    sampleRate = int.Parse(Selected(RateBox, true)), bits = Selected(BitsBox, true), normalize = false });
            }
            Persist(L.T("Xuất stems"), folder);
            RememberExport(folder);
            AppendLog(L.T("Đã xuất từng stem (gain/Drum Lab, không master refix/loudness): ") + folder);
        });
    }

    void FormatChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        ((ComboBoxItem)BitsBox.Items[2]).IsEnabled = Selected(FormatBox) == "WAV";
        if (Selected(FormatBox) == "FLAC" && Selected(BitsBox, true) == "32") BitsBox.SelectedIndex = 1;
        InvalidateMix();
    }

    void ChooseSample(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender; var key = (string)button.Tag;
        var dialog = AudioDialog();
        dialog.Multiselect = true;
        dialog.Title = L.T("Chọn 1–8 sample; đặt tên 01, 02… từ nhẹ đến mạnh nếu dùng theo lực");
        if (dialog.ShowDialog(this) != true) return;
        if (dialog.FileNames.Length > 8) { AppendLog(L.T("Mỗi tiếng trống hỗ trợ tối đa 8 sample. Hãy chọn lại.")); return; }
        samples[key] = dialog.FileNames.OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase).ToArray();
        UpdateSampleButtons(); InvalidateMix();
    }
    void UpdateSampleButtons()
    {
        foreach (var button in new[] { KickButton, SnareButton, HatButton, TomsButton, RideButton, CrashButton })
        {
            var key = (string)button.Tag;
            button.Content = key == "hat" ? "Hi-hat…" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(key)+"…";
            if (samples[key].Length > 0) button.Content = button.Content.ToString()!.TrimEnd('…')+" · "+samples[key].Length;
            button.ToolTip = samples[key].Length == 0 ? (key is "toms" or "ride" or "crash" ? L.T("Chưa có sample. Bấm để chọn tối đa 8 sample riêng.") : L.T("Sample tổng hợp trong kit. Bấm để chọn tối đa 8 sample riêng.")) : string.Join("\n", samples[key].Select((path, index) => $"{index+1}. {path}"));
        }
    }
    void ClearSamples(object sender, RoutedEventArgs e) { foreach (var key in samples.Keys.ToArray()) samples[key] = []; UpdateSampleButtons(); InvalidateMix(); }
    void ResetDrums(object sender, RoutedEventArgs e)
    {
        Transient.Value = Multiband.Value = Parallel.Value = Replacement.Value = 0; Sensitivity.Value = 50;
        TriggerThreshold.Value = -48; Retrigger.Value = 80; Bleed.Value = 0; Dynamics.Value = 100; SampleMode.SelectedIndex = 0;
    }
    void PunchPreset(object sender, RoutedEventArgs e) { Transient.Value = 35; Multiband.Value = 20; Parallel.Value = 30; }
    void TightPreset(object sender, RoutedEventArgs e) { Transient.Value = 15; Multiband.Value = 45; Parallel.Value = 15; }
    void ResetControls()
    {
        ResetStereo(this, new());
        RefixEnabled.IsChecked = true; HighPass.Value = 32; Mud.Value = -3; Presence.Value = 2; Air.Value = 1.5; WidthControl.Value = 100;
        ResetDrums(this, new()); ClearSamples(this, new()); ResetDrumMapping(); BlendMode.SelectedIndex = 0;
        GenrePresetBox.SelectedIndex = 0;
    }
    void ResetStereo(object sender, RoutedEventArgs e)
    {
        StereoEnabled.IsChecked = true; Attack80.Value = Attack3200.Value = 2;
        Tonal180.Value = TonalHarsh.Value = -2; Tonal320.Value = -1.5;
        SideAmount.Value = 50; StereoWidth.Value = 100;
        ImpactGain.Value = 6; SubAmount.Value = 20; SubFrequency.Value = 45;
    }

    void SaveProject(object sender, RoutedEventArgs e)
    {
        if (!HasAudio()) return;
        var dialog = new SaveFileDialog { Filter = "Suno Audio Forge project|*.stemforge.json", FileName = SafeName(project.Name)+".stemforge.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            CaptureProject();
            var temporary = dialog.FileName+".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(project, Settings.JsonOptions));
            File.Move(temporary, dialog.FileName, true);
            Persist(L.T("Lưu project JSON"), dialog.FileName); changed = false; AppendLog(L.T("Đã lưu project: ") + dialog.FileName);
        }
        catch (Exception ex) { AppendLog(L.T("Không lưu được: ") + ex.Message); }
    }

    void OpenProject(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Suno Audio Forge project|*.stemforge.json" };
        if (dialog.ShowDialog(this) != true || !CanReplaceProject()) return;
        try
        {
            var loaded = JsonSerializer.Deserialize<Project>(File.ReadAllText(dialog.FileName), Settings.JsonOptions) ?? throw new Exception(L.T("Project không hợp lệ."));
            LoadProjectSnapshot(loaded);
        }
        catch (Exception ex) { AppendLog(L.T("Không mở được project: ") + ex.Message); }
    }
    public void LoadProjectSnapshot(Project loaded)
    {
        try
        {
            if (loaded.Version != 1 || !File.Exists(loaded.Source) || !File.Exists(loaded.Preview))
                throw new Exception(L.T("Source/stem đã bị di chuyển, thiếu preview hoặc phiên bản project không hỗ trợ. Project lưu đường dẫn audio trên máy, không nhúng audio."));
            autosave.Stop(); ready = false; DisposeLive();
            Stop(); project = loaded; SeparationResult.AdoptLegacy(project); previews.Clear(); ResetControls();
            if (project.ProcessingMode is not ("legacy" or "structural" or "impact")) throw new Exception(L.T("Chế độ xử lý project không được hỗ trợ."));
            Select(ProcessingModeBox, project.ProcessingMode, true);
            StereoEnabled.IsChecked = !project.Stereo.TryGetValue("enabled", out var stereoEnabled) || stereoEnabled.ToString()!.Equals("true", StringComparison.OrdinalIgnoreCase);
            Attack80.Value = Number(project.Stereo, "attack80", 2); Attack3200.Value = Number(project.Stereo, "attack3200", 2);
            Tonal180.Value = Number(project.Stereo, "mud180", -2); Tonal320.Value = Number(project.Stereo, "mud320", -1.5); TonalHarsh.Value = Number(project.Stereo, "harsh", -2);
            SideAmount.Value = Number(project.Stereo, "sideAmount", 50); StereoWidth.Value = Number(project.Stereo, "width", 100);
            ImpactGain.Value = Number(project.Stereo, "impactDb", 6); SubAmount.Value = Number(project.Stereo, "subAmount", 20); SubFrequency.Value = Number(project.Stereo, "subHz", 45);
            UpdateProcessingMode();
            HighPass.Value = Number(project.Refix, "highPass", 32); Mud.Value = Number(project.Refix, "mud", -3);
            Presence.Value = Number(project.Refix, "presence", 2); Air.Value = Number(project.Refix, "air", 1.5); WidthControl.Value = Number(project.Refix, "width", 1)*100;
            RefixEnabled.IsChecked = !project.Refix.TryGetValue("enabled", out var enabled) || enabled.ToString()!.Equals("true", StringComparison.OrdinalIgnoreCase);
            Transient.Value = Number(project.Drums, "transient", 0); Multiband.Value = Number(project.Drums, "multiband", 0);
            Parallel.Value = Number(project.Drums, "parallel", 0); Replacement.Value = Number(project.Drums, "replacement", 0); Sensitivity.Value = Number(project.Drums, "sensitivity", 50);
            TriggerThreshold.Value = Number(project.Drums, "thresholdDb", -48); Retrigger.Value = Number(project.Drums, "retriggerMs", 80);
            Bleed.Value = Number(project.Drums, "bleed", 0); Dynamics.Value = Number(project.Drums, "dynamics", 100);
            Select(SampleMode, project.Drums.GetValueOrDefault("sampleMode")?.ToString() ?? "roundRobin", true);
            Select(BlendMode, project.Drums.GetValueOrDefault("blendMode")?.ToString() ?? "replace", true);
            RestoreDrumMapping();
            GenrePresetBox.SelectedValue = project.Drums.GetValueOrDefault("genrePreset")?.ToString() ?? "neutral";
            if (GenrePresetBox.SelectedIndex < 0) GenrePresetBox.SelectedIndex = 0;
            foreach (var key in samples.Keys.ToArray()) samples[key] = ReadSamples(project.Drums, key);
            Select(KitBox, project.Drums.GetValueOrDefault("kit")?.ToString() ?? "Studio"); UpdateSampleButtons();
            Select(ModelBox, project.Model, true); Select(DeviceBox, project.Device, true);
            Select(FormatBox, project.ExportFormat); Select(RateBox, project.ExportRate.ToString(), true); Select(BitsBox, project.ExportBits, true);
            Select(LufsBox, project.Lufs.ToString(CultureInfo.InvariantCulture), true); Normalize.IsChecked = project.Normalize;
            RefreshProject(); ready = true; changed = false; Persist(L.T("Mở phiên")); AppendLog(L.T("Đã mở phiên từ lịch sử/project."));
        }
        finally { ready = true; }
    }
    static double Number(Dictionary<string, object> dictionary, string key, double fallback) => dictionary.TryGetValue(key, out var value) && double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ? number : fallback;

    static string[] ReadSamples(Dictionary<string, object> dictionary, string key)
    {
        if (dictionary.TryGetValue(key+"Samples", out var bank))
        {
            var values = JsonSerializer.Deserialize<string[]>(JsonSerializer.Serialize(bank, Settings.JsonOptions)) ?? [];
            if (values.Length > 8 || values.Any(string.IsNullOrWhiteSpace)) throw new Exception(L.T("Danh sách sample không hợp lệ: ")+key);
            return values;
        }
        var legacy = dictionary.GetValueOrDefault(key)?.ToString();
        return string.IsNullOrWhiteSpace(legacy) ? [] : [legacy];
    }

    async void OpenSettings(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(settings, store.DatabasePath) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        Stop(false); DisposeLive(); settings.LivePreview=dialog.UseLivePreview;
        settings.Language = dialog.SelectedLanguage;
        settings.Python = dialog.Python; settings.Ffmpeg = dialog.Ffmpeg; settings.RestoreLastSession = dialog.RestoreSession;
        if (!SaveSettings()) return;
        await Work(async () => { var result = await Run(new { action = "diagnostics" }); AppendLog("Engine: " + result.GetRawText()); });
    }
    void ToggleTheme(object sender, RoutedEventArgs e)
    {
        settings.Theme = settings.Theme == "Light" ? "Dark" : "Light";
        ThemeManager.Apply(settings.Theme); SaveSettings();
    }
    bool SaveSettings()
    {
        try { store.SaveSettings(settings); return true; }
        catch (Exception error) { AppendLog(L.T("Không lưu được settings SQLite: ") + error.Message); return false; }
    }
    void OpenHistory(object sender, RoutedEventArgs e)
    {
        if (!CanReplaceProject()) return;
        try
        {
            var dialog = new HistoryWindow(store) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.SelectedProject is { } selected) LoadProjectSnapshot(selected);
        }
        catch (Exception error) { AppendLog(L.T("Không mở được lịch sử: ") + error.Message); }
    }
    void CaptureProject()
    {
        project.Refix = RefixParams(); project.Drums = DrumParams(); project.Model = Selected(ModelBox, true); project.Device = Selected(DeviceBox, true);
        project.ProcessingMode = Selected(ProcessingModeBox, true); project.Stereo = StereoParams();
        project.ExportRate = int.Parse(Selected(RateBox, true)); project.ExportBits = Selected(BitsBox, true); project.ExportFormat = Selected(FormatBox);
        project.Lufs = double.Parse(Selected(LufsBox, true), CultureInfo.InvariantCulture); project.Normalize = Normalize.IsChecked == true;
    }
    bool Persist(string? kind = null, string output = "", string detail = "")
    {
        autosave.Stop();
        if (!ready || string.IsNullOrEmpty(project.Source)) return true;
        try
        {
            CaptureProject(); store.SaveSession(project, kind, output, detail);
            settings.LastSessionSource = project.Source; store.SaveSettings(settings);
            changed = false; return true;
        }
        catch (Exception error) { AppendLog(L.T("Không lưu được phiên SQLite: ") + error.Message); return false; }
    }
    void Record(string kind, string output = "", string detail = "")
    {
        try { store.Record(kind, project, output, detail); }
        catch (Exception error) { AppendLog(L.T("Không ghi được lịch sử SQLite: ") + error.Message); }
    }
    void OpenFolder(object sender, RoutedEventArgs e)
    {
        var path = HasAudio() ? Path.GetDirectoryName(project.Source)! : Settings.DirectoryPath;
        Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
    void CancelWork(object sender, RoutedEventArgs e) => cancellation?.Cancel();
    void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (cancellation is not null && MessageBox.Show(this, L.T("Đang xử lý audio. Hủy tác vụ và đóng?"), "Suno Audio Forge", MessageBoxButton.YesNo) != MessageBoxResult.Yes) { e.Cancel = true; return; }
        if (!Persist() && MessageBox.Show(this, L.T("Không lưu được phiên SQLite. Vẫn đóng ứng dụng?"), "Suno Audio Forge", MessageBoxButton.YesNo) != MessageBoxResult.Yes) { e.Cancel = true; return; }
        cancellation?.Cancel(); autosave.Stop(); Stop(); timer.Stop();
    }
}
