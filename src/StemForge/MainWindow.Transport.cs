using System.Windows;
using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace StemForge;

public partial class MainWindow
{
    MediaPlayer? pendingPlayer;
    Task? remixPreparation;
    int versionSwitchRevision;
    bool playOnOpen;

    void InitializePlaybackVolume()
    {
        settings.PlaybackVolume = double.IsFinite(settings.PlaybackVolume) ? Math.Clamp(settings.PlaybackVolume, 0, 100) : 50;
        PlaybackVolumeSlider.Value = settings.PlaybackVolume;
        player.Volume = settings.PlaybackVolume / 100;
    }

    void PlaybackVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        double volume = Math.Clamp(e.NewValue, 0, 100);
        player.Volume = volume / 100;
        // An A/B destination can already be loading while the old player is audible.
        if (pendingPlayer is not null) pendingPlayer.Volume = player.Volume;
        if(liveSession is not null)liveSession.Volume=volume/100;
        if (!ready) return;
        settings.PlaybackVolume = volume;
        SaveSettings();
    }

    void AttachPlayer(MediaPlayer target)
    {
        target.MediaEnded += (_, _) =>
        {
            if (!ReferenceEquals(target, player)) return;
            if (settings.Repeat) { target.Position = TimeSpan.Zero; UpdatePosition(0); target.Play(); playing = true; }
            else { playing = false; UpdatePosition(project.Duration); }
            UpdateTransport();
        };
        target.MediaFailed += (_, e) =>
        {
            if (!ReferenceEquals(target, player)) return;
            playing = false; mediaReady = false; activePath = ""; UpdateTransport();
            AppendLog(L.T("Không phát được: ") + e.ErrorException.Message);
        };
        target.MediaOpened += (_, _) =>
        {
            if (!ReferenceEquals(target, player)) return;
            mediaReady = true; target.Position = TimeSpan.FromSeconds(cursorSeconds);
            if (playOnOpen) target.Play();
            playing = playOnOpen; UpdateTransport();
        };
    }

    void CancelPendingSwitch()
    {
        versionSwitchRevision++;
        pendingPlayer?.Close(); pendingPlayer = null;
    }

    async void PlaySelected(object sender, RoutedEventArgs e)
    {
        if(settings.LivePreview){await PlayLive(ListenOriginal.IsChecked==true?"original":"mix",ListenOriginal.IsChecked==true?L.T("Bản gốc"):L.T("Bản đã ReMix"));return;}
        if (remixPreparation is { IsCompleted: false } task && !mediaReady && string.IsNullOrEmpty(activePath))
        {
            if (ListenOriginal.IsChecked == true) { PlayOriginal(sender, e); return; }
            int revision = ++versionSwitchRevision;
            await task;
            if (revision == versionSwitchRevision && previews.TryGetValue("mix", out var preview) && File.Exists(preview.Path))
                Play(preview.Path, L.T("Bản đã ReMix"), "mix");
            return;
        }
        if (remixPreparation is { IsCompleted: false } || pendingPlayer is not null)
        {
            playOnOpen = true;
            if (mediaReady) { player.Play(); playing = true; UpdateTransport(); }
            return;
        }
        if (ListenOriginal.IsChecked == true) PlayOriginal(sender, e);
        else await PlayPreview("mix", L.T("Bản đã ReMix"));
    }

    async void ListenVersionChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        await ChangeVersionAsync();
    }

    async Task ChangeVersionAsync()
    {
        if(settings.LivePreview)
        {
            string stage=ListenOriginal.IsChecked==true?"original":"mix";
            string label=stage=="original"?L.T("Bản gốc"):L.T("Bản đã ReMix");
            if(liveActive || mediaReady)await PlayLive(stage,label,playing);
            else {playbackMode=label;UpdateTransport();}
            return;
        }
        CancelPendingSwitch();
        int revision = versionSwitchRevision;
        bool original = ListenOriginal.IsChecked == true;
        string mode = original ? L.T("Bản gốc") : L.T("Bản đã ReMix");
        // Selecting a version while stopped does not start playback or render work.
        if (!mediaReady && string.IsNullOrEmpty(activePath)) { playbackMode = mode; UpdateTransport(); return; }
        try
        {
            string path = project.Preview;
            if (!original)
            {
                if (Selected(ProcessingModeBox, true) == "legacy" && project.DrumParts.Count > 0 && !HasValidDrumParts())
                    throw new InvalidOperationException(L.T("Bộ DrumSep đã thay đổi hoặc thiếu file. Tách lại bộ trống trước khi dựng bản remix."));
                if (!previews.TryGetValue("mix", out var cached) || !File.Exists(cached.Path))
                {
                    AppendLog(L.T("Đang chuẩn bị bản remix; tiếp tục nghe bản hiện tại…"));
                    // Share a single render across rapid A/B changes. The current player keeps running.
                    remixPreparation ??= WorkCore(async () =>
                    {
                        CaptureProject();
                        var output = Path.Combine(Path.GetDirectoryName(project.Source)!, "mix-preview.wav");
                        var result = await Run(PreviewRequest.Create(project, "mix", output));
                        var waveform = JsonSerializer.Deserialize<double[]>(result.GetProperty("waveform").GetRawText())!;
                        previews["mix"] = (output, waveform);
                        SetComparisonWaveform(waveform, L.T("mix nghe thử"));
                        Persist(L.T("Cập nhật waveform so sánh"));
                    }, keepPlayback: true);
                    var preparation = remixPreparation;
                    try { await preparation; }
                    finally { if (ReferenceEquals(remixPreparation, preparation)) remixPreparation = null; }
                    if (revision != versionSwitchRevision) return;
                    if (!previews.TryGetValue("mix", out cached) || !File.Exists(cached.Path)) { RestoreListenChoice(); return; }
                }
                path = cached.Path;
            }
            if (revision != versionSwitchRevision) return;
            if (!File.Exists(path)) throw new FileNotFoundException(L.T("File nghe bản gốc bị thiếu. Hãy nhập lại audio."), path);
            QueueVersionSwitch(path, mode, original ? "original" : "mix", revision);
        }
        catch (Exception error)
        {
            if (revision != versionSwitchRevision) return;
            AppendLog(L.T("Không chuyển được bản nghe: ") + error.Message); RestoreListenChoice();
        }
    }

    void RestoreListenChoice()
    {
        bool previous = ready; ready = false;
        try { if (activeChannel == "mix") ListenRemix.IsChecked = true; else ListenOriginal.IsChecked = true; }
        finally { ready = previous; }
    }

    void QueueVersionSwitch(string path, string mode, string channel, int revision)
    {
        if (activePath == path && mediaReady) { playbackMode = mode; activeChannel = channel; UpdateTransport(); return; }
        var next = new MediaPlayer { Volume = player.Volume, IsMuted = player.IsMuted };
        pendingPlayer = next;
        AttachPlayer(next);
        next.MediaOpened += (_, _) =>
        {
            if (revision != versionSwitchRevision || !ReferenceEquals(pendingPlayer, next)) { if (!ReferenceEquals(player, next)) next.Close(); return; }
            bool resume = mediaReady ? playing : playOnOpen;
            double position = mediaReady ? player.Position.TotalSeconds : cursorSeconds;
            var previous = player;
            // Preload first, then transfer the live position and state. Never reset to the start.
            next.Position = TimeSpan.FromSeconds(Math.Clamp(position, 0, project.Duration));
            previous.Stop(); previous.Close();
            player = next; pendingPlayer = null; activePath = path; activeChannel = channel;
            playbackMode = mode; mediaReady = true; playOnOpen = resume; playing = resume;
            if (resume) next.Play();
            UpdatePosition(position);
        };
        next.MediaFailed += (_, e) =>
        {
            if (!ReferenceEquals(pendingPlayer, next)) return;
            pendingPlayer = null; next.Close(); RestoreListenChoice();
            AppendLog(L.T("Không chuyển được bản nghe: ") + e.ErrorException.Message);
        };
        next.Open(new Uri(path, UriKind.Absolute));
    }

    void PauseGlobal(object sender, RoutedEventArgs e)
    {
        if(liveActive && liveSession is not null){liveSession.Pause();playing=false;UpdatePosition(liveSession.Position);UpdateTransport();return;}
        playOnOpen = false;
        if (!mediaReady) return;
        player.Pause(); playing = false; UpdatePosition(player.Position.TotalSeconds); UpdateTransport();
    }
    void StopGlobal(object sender, RoutedEventArgs e) => Stop();

    void NewProject(object sender, RoutedEventArgs e)
    {
        if (cancellation is not null || !CanReplaceProject()) return;
        Stop(); DisposeLive(); autosave.Stop(); ready = false;
        try
        {
            project = new() { ProcessingMode = "structural", SeparationTrackingVersion = 1 };
            previews.Clear(); activeChannel = playbackMode = "";
            ResetControls(); KitBox.SelectedIndex = 0; ApplyNewDrumDefaults();
            ModelBox.SelectedIndex = DeviceBox.SelectedIndex = 0;
            FormatBox.SelectedIndex = 0; RateBox.SelectedIndex = BitsBox.SelectedIndex = 1;
            LufsBox.SelectedIndex = 0; Normalize.IsChecked = true;
            Select(ProcessingModeBox, "structural", true); UpdateProcessingMode();
            ListenOriginal.IsChecked = true;
            RefreshProject();
            TrackInfo.Text = L.T("Kéo thả audio hoặc mở file WAV, FLAC, MP3, M4A…");
            settings.LastSessionSource = ""; SaveSettings(); changed = false;
            AppendLog(L.T("Project mới đã sẵn sàng. Phiên trước vẫn được lưu trong Lịch sử."));
        }
        finally { ready = true; }
    }

    void ResetMidiNotes(object sender, RoutedEventArgs e)
    {
        string bpm = MidiBpm.Text;
        ResetDrumMapping(); MidiBpm.Text = bpm;
        Persist(L.T("Reset ánh xạ trống"));
        AppendLog(L.T("Đã đặt Kick 36 / Snare 38 / Toms 45 / Hi-hat 42 / Ride 51 / Crash 49. Giữ nguyên BPM."));
    }
}
