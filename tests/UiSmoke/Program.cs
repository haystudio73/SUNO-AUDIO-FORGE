using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StemForge;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        string root = Path.GetFullPath(args[0]);
        string folder = Path.Combine(root, "artifacts", "ui-v013");
        Directory.CreateDirectory(folder);
        var app = new App(startWindow: false); app.InitializeComponent();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        var store = new AppStore(Path.Combine(folder, "test-"+Guid.NewGuid().ToString("N")+".db"));
        var window = new MainWindow(store);
        Assert(((ComboBox)window.FindName("ProcessingModeBox")).SelectedIndex == 0, "New window defaults to PA1");
        var snapshot = JsonSerializer.Deserialize<Project>(File.ReadAllText(Path.Combine(root, "artifacts", "smoke", "Synthetic test.stemforge.json")), Settings.JsonOptions)!;
        window.LoadProjectSnapshot(snapshot);
        Assert(((ComboBox)window.FindName("ProcessingModeBox")).SelectedIndex == 2, "Legacy project preserves old routing");
        Assert(((TextBlock)window.FindName("MusicInfo")).Text.Contains("chưa phân tích"), "Old projects don't invent tempo or key");
        window.Width = 1440; window.Height = 1100;
        // Render the app's own visual tree offscreen; no desktop input or user session is involved.
        var content = (FrameworkElement)window.Content;
        foreach (string theme in new[] { "Dark", "Light" })
        {
            ThemeManager.Apply(theme);
            ((ScrollViewer)window.FindName("MainScroll")).ScrollToTop();
            content.Measure(new Size(1384, 1020)); content.Arrange(new Rect(0, 0, 1384, 1020)); content.UpdateLayout();
            app.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
            var bitmap = new RenderTargetBitmap(1384, 1020, 96, 96, PixelFormats.Pbgra32);
            var surface = new DrawingVisual();
            using (var drawing = surface.RenderOpen()) drawing.DrawRectangle((Brush)app.Resources["AppBackground"], null, new Rect(0, 0, 1384, 1020));
            bitmap.Render(surface); bitmap.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(Path.Combine(folder, theme+".png"))) encoder.Save(output);
            Assert(((SolidColorBrush)window.Background).Color == ((SolidColorBrush)app.Resources["AppBackground"]).Color, "Window background theme");
            var trackName = (TextBlock)window.FindName("TrackName");
            Assert(((SolidColorBrush)trackName.Foreground).Color == ((SolidColorBrush)app.Resources["Text"]).Color, "Text contrast theme");
            ((ScrollViewer)window.FindName("MainScroll")).ScrollToEnd();
            content.UpdateLayout();
            SaveVisual(content, folder, theme+"-drums", 1384, 1020);
        }
        var settingsWindow = new SettingsWindow(store.LoadSettings(), store.DatabasePath);
        SaveVisual((FrameworkElement)settingsWindow.Content, folder, "Settings", 700, 650);
        var historyWindow = new HistoryWindow(store);
        SaveVisual((FrameworkElement)historyWindow.Content, folder, "History", 980, 640);
        var wave = (WaveformView)window.FindName("Wave");
        var previewMethod = typeof(MainWindow).GetMethod("PlayPreview", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        WaitFor((Task)previewMethod.Invoke(window, new object?[] { "mix", "Bản mix cuối", null, false })!);
        Assert(wave.HasProcessedSamples, "Compare action renders a real final mix without autoplay");
        Assert(!((TextBlock)window.FindName("PlaybackInfo")).Text.Contains("Đang phát"), "Compare does not start playback");
        var previewPath = Path.Combine(Path.GetDirectoryName(snapshot.Source)!, "mix-preview.wav");
        var previewTime = File.GetLastWriteTimeUtc(previewPath);
        WaitFor((Task)previewMethod.Invoke(window, new object?[] { "mix", "Bản mix cuối", null, false })!);
        Assert(File.GetLastWriteTimeUtc(previewPath) == previewTime && wave.HasProcessedSamples, "Cached comparison reuses finished preview");
        var drumWave = (WaveformView)window.FindName("DrumWave");
        WaitFor((Task)previewMethod.Invoke(window, new object?[] { "drums", "Trống sau hiệu ứng", null, false })!);
        Assert(drumWave.HasProcessedSamples, "Drum preview produces actual processed waveform");
        Assert(wave.HasProcessedSamples, "Drum waveform doesn't replace final mix comparison");
        Assert(store.LoadSession(snapshot.Source)!.Stems.First(s => s.Name == "drums").Waveform.Length > 0, "Raw drum waveform cached in SQLite for legacy project");
        var drumPath = Path.Combine(Path.GetDirectoryName(snapshot.Source)!, "drums-preview.wav");
        var drumTime = File.GetLastWriteTimeUtc(drumPath);
        WaitFor((Task)previewMethod.Invoke(window, new object?[] { "drums", "Trống sau hiệu ứng", null, false })!);
        Assert(File.GetLastWriteTimeUtc(drumPath) == drumTime, "Drum waveform reuses cached render");
        var drumScale = drumWave.AmplitudeMaximum;
        ((CheckBox)window.FindName("ShowDrumWave")).IsChecked = false;
        ((CheckBox)window.FindName("ShowProcessedDrumWave")).IsChecked = false;
        Assert(!drumWave.ShowOriginal && !drumWave.ShowProcessed && drumWave.HasProcessedSamples && drumScale == drumWave.AmplitudeMaximum, "Drum visibility preserves data and common amplitude scale");
        var drumSettings = new AppStore(store.DatabasePath).LoadSettings();
        Assert(!drumSettings.ShowDrumWaveform && !drumSettings.ShowProcessedDrumWaveform, "Drum toggles persist in SQLite");
        var drumRestore = new MainWindow(store);
        Assert(!((WaveformView)drumRestore.FindName("DrumWave")).ShowOriginal && !((WaveformView)drumRestore.FindName("DrumWave")).ShowProcessed, "Drum toggles restore on restart");
        drumRestore.Close();
        ((CheckBox)window.FindName("ShowDrumWave")).IsChecked = true;
        ((CheckBox)window.FindName("ShowProcessedDrumWave")).IsChecked = true;
        foreach (var theme in new[] { "Dark", "Light" })
        {
            ThemeManager.Apply(theme);
            var scroll = (ScrollViewer)window.FindName("MainScroll");
            var lab = (FrameworkElement)window.FindName("LegacyDrumLab");
            scroll.ScrollToVerticalOffset(lab.TransformToAncestor((Visual)scroll.Content).Transform(new Point()).Y);
            SaveVisual(content, folder, "DrumWave-"+theme, 1384, 1020);
            SaveVisual(content, folder, "DrumWave-Narrow-"+theme, 1024, 800);
        }
        wave.SetProcessedSamples([.2, .5, .1]);
        var scale = wave.AmplitudeMaximum;
        var showOriginal = (CheckBox)window.FindName("ShowOriginalWave");
        var showProcessed = (CheckBox)window.FindName("ShowProcessedWave");
        showOriginal.IsChecked = false; showProcessed.IsChecked = false;
        Assert(!wave.ShowOriginal && !wave.ShowProcessed, "Both waveforms can be hidden independently of playback");
        Assert(wave.HasProcessedSamples && wave.AmplitudeMaximum == scale, "Visibility retains comparison data and scale");
        var savedWaveSettings = new AppStore(store.DatabasePath).LoadSettings();
        Assert(!savedWaveSettings.ShowOriginalWaveform && !savedWaveSettings.ShowProcessedWaveform, "Waveform visibility persists in SQLite");
        var secondWindow = new MainWindow(store);
        Assert(!((WaveformView)secondWindow.FindName("Wave")).ShowProcessed, "Waveform visibility restored on restart");
        secondWindow.Close();
        showOriginal.IsChecked = true; showProcessed.IsChecked = true;
        wave.PositionRatio = 2; Assert(wave.PositionRatio == 1, "Seek upper bound");
        wave.PositionRatio = -1; Assert(wave.PositionRatio == 0, "Seek lower bound");
        var seek = (Slider)window.FindName("SeekBar");
        seek.Value = snapshot.Duration*.5;
        Assert(Math.Abs(wave.PositionRatio-.5) < .001, "Seek slider updates waveform playhead");
        Assert(Math.Abs(drumWave.PositionRatio-.5) < .001, "Drum playhead shares main timeline");
        var repeat = (CheckBox)window.FindName("OriginalRepeat"); repeat.IsChecked = true;
        Assert(((Border)repeat.Template.FindName("Shape", repeat)).CornerRadius.TopLeft == 10, "Repeat uses a rounded icon toggle");
        Assert(store.LoadSettings().Repeat, "Repeat persists");
        Assert(store.LoadSession(snapshot.Source)?.Stems.Count == snapshot.Stems.Count, "History restores stems");
        ((Slider)window.FindName("Transient")).Value = 47;
        Assert(!wave.HasProcessedSamples, "DSP change removes stale comparison");
        Assert(!drumWave.HasProcessedSamples, "DSP change removes stale processed drum waveform");
        ((Slider)window.FindName("TriggerThreshold")).Value = -37;
        ((Slider)window.FindName("Retrigger")).Value = 63;
        ((Slider)window.FindName("Bleed")).Value = 28;
        ((Slider)window.FindName("Dynamics")).Value = 72;
        ((ComboBox)window.FindName("SampleMode")).SelectedIndex = 1;
        var frame = new DispatcherFrame();
        var delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        delay.Tick += (_, _) => { frame.Continue = false; delay.Stop(); };
        delay.Start(); Dispatcher.PushFrame(frame);
        Assert(store.LoadSession(snapshot.Source)?.Drums["transient"].ToString() == "47", "DSP autosave after changing slider");
        var saved = store.LoadSession(snapshot.Source)!;
        foreach (var (key, value) in new[] { ("thresholdDb", "-37"), ("retriggerMs", "63"), ("bleed", "28"), ("dynamics", "72"), ("sampleMode", "velocity") })
            Assert(saved.Drums[key].ToString() == value, "Trigger SQLite persistence: "+key);
        saved.Drums["kickSamples"] = new[] { snapshot.Source, snapshot.Source };
        window.LoadProjectSnapshot(saved);
        wave.SetProcessedSamples([.3]);
        window.LoadProjectSnapshot(saved);
        Assert(!wave.HasProcessedSamples, "Project switch clears previous comparison even while controls are loading");
        Assert(((Slider)window.FindName("TriggerThreshold")).Value == -37, "Trigger threshold restored");
        Assert(((ComboBox)window.FindName("SampleMode")).SelectedIndex == 1, "Sample mode restored");
        Assert(((Button)window.FindName("KickButton")).Content.ToString()!.Contains("2"), "Sample bank restored");
        var reopened = new AppStore(store.DatabasePath).LoadSession(snapshot.Source)!;
        Assert(((JsonElement)reopened.Drums["kickSamples"]).GetArrayLength() == 2, "Sample bank survives database reopen");
        reopened.Drums.Remove("kickSamples"); reopened.Drums["kick"] = snapshot.Source;
        window.LoadProjectSnapshot(reopened);
        Assert(((Button)window.FindName("KickButton")).Content.ToString()!.Contains("1"), "Legacy single sample migrated");
        var flagsPreset = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        foreach (var preset in DrumPresets.All) Assert(preset.Setup.IsValid, "Preset ranges: "+preset.Name);
        var priorTransient = ((Slider)window.FindName("Transient")).Value;
        foreach (var (id, transient, threshold, interval) in new[] { ("rap-reference", 55d, -42d, 70d), ("jazz-reference", 25d, -36d, 120d) })
        {
            ((ComboBox)window.FindName("GenrePresetBox")).SelectedValue = id;
            Assert(((Slider)window.FindName("Transient")).Value == priorTransient, "Reference preview does not apply: "+id);
            Assert(((ItemsControl)window.FindName("GenreDetails")).Items.Count == 12, "All twelve controls explained");
            ((Button)window.FindName("ApplyGenreButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(((Slider)window.FindName("Transient")).Value == transient && ((Slider)window.FindName("TriggerThreshold")).Value == threshold && ((Slider)window.FindName("Retrigger")).Value == interval, "Reference settings apply: "+id);
            Assert(store.LoadSession(snapshot.Source)!.Drums["genrePreset"].ToString() == id, "Reference preset SQLite persistence");
            priorTransient = transient;
        }
        foreach (var (name, tag) in new[] { ("KickButton", "kick"), ("SnareButton", "snare"), ("HatButton", "hat"), ("TomsButton", "toms"), ("RideButton", "ride"), ("CrashButton", "crash") })
        {
            var button = (Button)window.FindName(name);
            Assert(button.Tag is string actual && actual == tag, "Icon preserves instrument identity: "+tag);
            Assert(button.Style == app.Resources["SampleIconButton"], "Sample has icon template: "+tag);
        }
        ((ComboBox)window.FindName("GenrePresetBox")).SelectedValue = "rock";
        Assert(((Slider)window.FindName("Transient")).Value == priorTransient, "Selecting genre only previews values");
        wave.SetProcessedSamples([.4]);
        ((Button)window.FindName("ApplyGenreButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(((Slider)window.FindName("Transient")).Value == 35 && ((Slider)window.FindName("Replacement")).Value == 45, "Apply Rock configures effects and layer amount");
        Assert(!wave.HasProcessedSamples, "Genre apply invalidates rendered preview");
        Assert(((Button)window.FindName("KickButton")).Content.ToString()!.Contains("1"), "Genre apply preserves custom sample");
        Assert(store.LoadSession(snapshot.Source)!.Drums["genrePreset"].ToString() == "rock", "Genre selection persists in project");
        ((Slider)window.FindName("Transient")).Value = 31;
        var saveNamed = typeof(MainWindow).GetMethod("SaveNamedDrumPreset", flagsPreset)!;
        var savedId = (string)saveNamed.Invoke(window, new object[] { "Trống Việt – bản mềm", true })!;
        var defaultSettings = new AppStore(store.DatabasePath).LoadSettings();
        Assert(defaultSettings.DrumDefaults?.Transient == 31 && defaultSettings.DrumDefaults.Replacement == 45, "SQLite saves current edited values, not unedited suggestion");
        Assert(defaultSettings.SavedDrumPresets.Single().Name == "Trống Việt – bản mềm", "Unicode named preset persists");
        var savedCount = defaultSettings.SavedDrumPresets.Count;
        foreach (string invalid in new[] { "   ", "Trống Việt – bản mềm", "Rock / Live" })
        {
            try { saveNamed.Invoke(window, new object[] { invalid, false }); throw new Exception("Invalid name accepted"); }
            catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is ArgumentException) { }
        }
        Assert(new AppStore(store.DatabasePath).LoadSettings().SavedDrumPresets.Count == savedCount, "Invalid names don't mutate stored presets");
        foreach (var theme in new[] { "Dark", "Light" })
        {
            ThemeManager.Apply(theme);
            var saveDialog = new SaveDrumPresetWindow((_, _) => "test");
            ((TextBox)saveDialog.FindName("PresetName")).Text = "Trống Việt – bản mềm";
            SaveVisual((FrameworkElement)saveDialog.Content, folder, "SavePreset-"+theme, 560, 350);
            saveDialog.Close();
            var scroll = (ScrollViewer)window.FindName("MainScroll");
            scroll.ScrollToTop();
            SaveVisual(content, folder, "Icons-"+theme, 1384, 1020);
            var lab = (FrameworkElement)window.FindName("LegacyDrumLab");
            scroll.ScrollToVerticalOffset(lab.TransformToAncestor((Visual)scroll.Content).Transform(new Point()).Y);
            SaveVisual(content, folder, "Presets-"+theme, 1384, 1020);
            ((ComboBox)window.FindName("GenrePresetBox")).SelectedValue = "jazz-reference";
            ((Expander)window.FindName("GenreDetailsExpander")).IsExpanded = true;
            SaveVisual(content, folder, "Analysis-"+theme, 1384, 1020);
            SaveVisual(content, folder, "Analysis-Narrow-"+theme, 1024, 800);
            ((Expander)window.FindName("GenreDetailsExpander")).IsExpanded = false;
            ((ComboBox)window.FindName("GenrePresetBox")).SelectedValue = "rock";
            Assert(((DrumPreset)((ComboBox)window.FindName("GenrePresetBox")).SelectedItem).ToString() == "Rock / Live", "Preset fallback text is a user-facing name");
            SaveVisual(content, folder, "Presets-Narrow-"+theme, 1024, 800);
            SaveVisual(content, folder, "Presets-"+theme, 1384, 1020);
        }
        var defaultWindow = new MainWindow(new AppStore(store.DatabasePath));
        ((ComboBox)defaultWindow.FindName("GenrePresetBox")).SelectedValue = savedId;
        Assert(((ComboBox)defaultWindow.FindName("GenrePresetBox")).SelectedItem is DrumPreset { Name: "Trống Việt – bản mềm" }, "Saved preset is available after restart");
        ((Button)defaultWindow.FindName("ApplyGenreButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(((Slider)defaultWindow.FindName("Transient")).Value == 31, "Named preset restores edited controls");
        typeof(MainWindow).GetMethod("ResetControls", flagsPreset)!.Invoke(defaultWindow, null);
        typeof(MainWindow).GetMethod("ApplyNewDrumDefaults", flagsPreset)!.Invoke(defaultWindow, null);
        Assert(((Slider)defaultWindow.FindName("Transient")).Value == 31, "New import initialization uses persisted default");
        var oldProject = JsonSerializer.Deserialize<Project>(File.ReadAllText(Path.Combine(root, "artifacts", "smoke", "Synthetic test.stemforge.json")), Settings.JsonOptions)!;
        var oldTransient = oldProject.Drums.TryGetValue("transient", out var oldValue) ? double.Parse(oldValue.ToString()!, System.Globalization.CultureInfo.InvariantCulture) : 0;
        defaultWindow.LoadProjectSnapshot(oldProject);
        Assert(((Slider)defaultWindow.FindName("Transient")).Value == oldTransient, "Saved defaults never overwrite an existing project");
        ((Button)defaultWindow.FindName("ClearDrumDefaultButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(new AppStore(store.DatabasePath).LoadSettings().DrumDefaults is null, "Clear default persists to SQLite");
        Assert(((Slider)defaultWindow.FindName("Transient")).Value == oldTransient, "Clear default preserves current project");
        defaultWindow.Close();
        var musicFixture = Path.Combine(root, "artifacts", "analysis-fixture.wav");
        Assert(File.Exists(musicFixture), "Known music fixture exists");
        var musicProject = JsonSerializer.Deserialize<Project>(File.ReadAllText(Path.ChangeExtension(musicFixture, ".json")), Settings.JsonOptions)!;
        window.LoadProjectSnapshot(musicProject);
        ((TextBox)window.FindName("MidiBpm")).Text = "137.3";
        var analyzeMethod = typeof(MainWindow).GetMethod("AnalyzeAudioCore", flagsPreset)!;
        var workMethod = typeof(MainWindow).GetMethod("Work", flagsPreset)!;
        WaitFor((Task)workMethod.Invoke(window, new object[] { new Func<Task>(() => (Task)analyzeMethod.Invoke(window, null)!) })!);
        Assert(musicProject.Analysis?.Bpm is double detected && Math.Abs(detected-120) < 3 && musicProject.Analysis.Key == "C Major", "Native app analyzes real fixture via Python bridge");
        Assert(((TextBox)window.FindName("MidiBpm")).Text == "137.3", "Detecting BPM doesn't overwrite manual MIDI tempo");
        ((Button)window.FindName("UseDetectedBpmButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(double.Parse(((TextBox)window.FindName("MidiBpm")).Text, System.Globalization.CultureInfo.InvariantCulture) == musicProject.Analysis!.Bpm, "BPM explicitly applied to MIDI");
        var storedAnalysis = new AppStore(store.DatabasePath).LoadSession(musicFixture)!;
        Assert(storedAnalysis.Analysis!.Key == "C Major", "Analysis survives SQLite reopen");
        window.LoadProjectSnapshot(storedAnalysis);
        Assert(((TextBlock)window.FindName("MusicInfo")).Text.Contains("C trưởng"), "Analysis restored in UI");
        foreach (var theme in new[] { "Dark", "Light" })
        {
            ThemeManager.Apply(theme);
            ((ScrollViewer)window.FindName("MainScroll")).ScrollToTop();
            SaveVisual(content, folder, "MusicAnalysis-"+theme, 1384, 1020);
            SaveVisual(content, folder, "MusicAnalysis-Narrow-"+theme, 1024, 800);
        }
        window.LoadProjectSnapshot(snapshot);
        Assert(((TextBlock)window.FindName("MusicInfo")).Text.Contains("chưa phân tích") && !drumWave.HasProcessedSamples, "Project change clears previous analysis and processed drum view");
        var cascadeFile = Path.Combine(root, "artifacts", "cascade-real", "Cascade.stemforge.json");
        if (File.Exists(cascadeFile))
        {
            var cascade = JsonSerializer.Deserialize<Project>(File.ReadAllText(cascadeFile), Settings.JsonOptions)!;
            window.LoadProjectSnapshot(cascade);
            Assert(((WrapPanel)window.FindName("DrumPartList")).Children.Count == 6, "DrumSep lanes restored");
            Assert(((Button)window.FindName("ExportMidiButton")).IsEnabled, "MIDI enabled for valid isolated lanes");
            var refreshParts = typeof(MainWindow).GetMethod("RefreshPartWaveformsCore", flagsPreset)!;
            WaitFor((Task)workMethod.Invoke(window, new object[] { new Func<Task>(() => (Task)refreshParts.Invoke(window, null)!) })!);
            Assert(cascade.DrumParts.All(s => s.Waveform.Length > 0), "All six real drum part waveforms loaded");
            var overlay = (WaveformView)window.FindName("DrumPartsWave");
            Assert(overlay.Layers.Count == 6 && overlay.Layers.Select(l => l.BrushKey).Distinct().Count() == 6, "Six colored layers share one waveform");
            var toggles = ((WrapPanel)window.FindName("DrumPartList")).Children.OfType<Grid>().Select(g => g.Children.OfType<CheckBox>().Single()).ToArray();
            var kickToggle = toggles[0];
            kickToggle.IsChecked = false;
            Assert(overlay.Layers.Count == 5 && overlay.Layers.All(l => l.Name != "kick"), "Deselected kick is removed from overlay");
            var selectedMethod = typeof(MainWindow).GetMethod("SelectedDrumParts", flagsPreset)!;
            var selected = (Stem[])selectedMethod.Invoke(window, null)!;
            Assert(selected.Length == 5 && selected.All(p => p.Name != "kick"), "Audition selects exactly the visible lanes");
            var req = JsonSerializer.SerializeToElement(PreviewRequest.Create(cascade, "drumsep", "test.wav", selectedParts: selected), Settings.JsonOptions);
            Assert(req.GetProperty("stems").GetArrayLength() == 5 && !req.GetProperty("refix").GetProperty("enabled").GetBoolean() && req.GetProperty("drums").EnumerateObject().Count() == 0, "Audition contains selected lanes only, no effects or original overlay");
            var persistParts = typeof(MainWindow).GetMethod("Persist", flagsPreset)!;
            ((Slider)((Grid)((WrapPanel)window.FindName("DrumPartList")).Children[0]).Children[2]).Value = -7;
            Assert(cascade.DrumParts.First(p => p.Name == "kick").Gain == -7, "Part gain slider changes project value");
            Assert((bool)persistParts.Invoke(window, new object?[] { null, "", "" })!, "Save lane mix");
            var storedParts = new AppStore(store.DatabasePath).LoadSession(cascade.Source)!;
            Assert(storedParts.DrumParts.First(p => p.Name == "kick") is { Mute: true, Gain: -7 }, "Selection and gain persist per project in SQLite");
            var partRestore = new MainWindow(new AppStore(store.DatabasePath)); partRestore.LoadProjectSnapshot(storedParts);
            Assert(((Grid)((WrapPanel)partRestore.FindName("DrumPartList")).Children[0]).Children.OfType<CheckBox>().Single().IsChecked == false, "Selection restored across windows");
            partRestore.Close();
            foreach (var toggle in toggles) toggle.IsChecked = false;
            Assert(overlay.Layers.Count == 0 && ((Button)window.FindName("GlobalPlayButton")).IsEnabled, "No drums selected still allows whole-song playback");
            try { PreviewRequest.Create(cascade, "drumsep", "empty.wav", selectedParts: []); throw new Exception("Empty request accepted"); }
            catch (InvalidOperationException) { }
            toggles[0].IsChecked = true; toggles[1].IsChecked = true;
            ((Slider)window.FindName("Replacement")).Value = 0;
            WaitFor((Task)previewMethod.Invoke(window, new object?[] { "mix", "Remix selected drums", null, false })!);
            var cacheField = typeof(MainWindow).GetField("previews", flagsPreset)!;
            var cache = (Dictionary<string, (string Path, double[] Waveform)>)cacheField.GetValue(window)!;
            Assert(cache.ContainsKey("mix"), "Final mix renders selected drum lanes");
            File.Copy(cache["mix"].Path, Path.Combine(folder, "selected-drums-final.wav"), true);
            toggles[0].IsChecked = false;
            Assert(!cache.ContainsKey("mix") && !wave.HasProcessedSamples, "Changing selection invalidates final mix and comparison waveform");
            foreach (var toggle in toggles) toggle.IsChecked = true;
            ((Slider)window.FindName("SeekBar")).Value = cascade.Duration*.25;
            Assert(Math.Abs(overlay.PositionRatio-.25) < .001, "Overlay playhead follows shared seek");

            Assert(((ComboBoxItem)((ComboBox)window.FindName("BlendMode")).SelectedItem).Tag.ToString() == "layer", "Layer mode restored");
            ((TextBox)window.FindName("MidiBpm")).Text = "137.3";
            ((TextBox)window.FindName("MidiKick")).Text = "35";
            var capture = typeof(MainWindow).GetMethod("Persist", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            Assert((bool)capture.Invoke(window, new object?[] { null, "", "" })!, "Cascade saves to SQLite");
            var cascadeSaved = new AppStore(store.DatabasePath).LoadSession(cascade.Source)!;
            Assert(cascadeSaved.DrumParts.Count == 6 && cascadeSaved.DrumPartsSource == cascade.DrumPartsSource, "Parent association survives SQLite reopen");
            Assert(cascadeSaved.DrumParts.All(p => p.Waveform.Length > 0), "Part waveform data survives SQLite reopen");
            window.LoadProjectSnapshot(cascadeSaved);
            Assert(((TextBox)window.FindName("MidiBpm")).Text == "137.3" && ((TextBox)window.FindName("MidiKick")).Text == "35", "MIDI mapping survives restart");
            ((Button)window.FindName("ResetMidiButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(((TextBox)window.FindName("MidiKick")).Text == "36" && ((TextBox)window.FindName("MidiBpm")).Text == "137.3", "Reset mapping restores notes while retaining BPM");
            ((Expander)window.FindName("MidiMappingExpander")).IsExpanded = true;
            foreach (var theme in new[] { "Dark", "Light" })
            {
                ThemeManager.Apply(theme);
                ((ScrollViewer)window.FindName("MainScroll")).ScrollToEnd();
                SaveVisual(content, folder, "Cascade-"+theme, 1384, 1020);
                var lanes = (WrapPanel)window.FindName("DrumPartList");
                Assert(lanes.ActualHeight >= 20 && lanes.Children.Count == 6, "Six DrumSep lane controls are laid out");
                var scroll = (ScrollViewer)window.FindName("MainScroll");
                scroll.ScrollToVerticalOffset(Math.Max(0, lanes.TransformToAncestor((Visual)scroll.Content).Transform(new Point()).Y-100));
                SaveVisual(content, folder, "SixParts-"+theme, 1384, 1020);
                SaveVisual(content, folder, "SixParts-Narrow-"+theme, 1024, 800);

            }
            var part = cascade.DrumParts.First(s => s.Name == "kick");
            WaitFor((Task)previewMethod.Invoke(window, new object?[] { "stem:kick", "Kick", part, false })!);
            Assert(File.Exists(Path.Combine(Path.GetDirectoryName(cascade.Source)!, "stem_kick-preview.wav")), "Real DrumSep lane preview renders");
            cascadeSaved.DrumPartsSource = cascade.Source;
            window.LoadProjectSnapshot(cascadeSaved);
            Assert(!((Button)window.FindName("ExportMidiButton")).IsEnabled, "Stale parent disables MIDI export");
        }
        var sunoFixture = Path.Combine(root, "artifacts", "suno-live", "Imported Suno.stemforge.json");
        if (File.Exists(sunoFixture))
        {
            var suno = JsonSerializer.Deserialize<Project>(File.ReadAllText(sunoFixture), Settings.JsonOptions)!;
            window.LoadProjectSnapshot(suno);
            Assert(((TextBox)window.FindName("SunoUrl")).Text == suno.SourceUrl, "Suno URL restored");
            Assert(((TextBlock)window.FindName("TrackInfo")).Text.Contains("Audio từ video Suno"), "Video fallback source visible");
            foreach (var theme in new[] { "Dark", "Light" })
            {
                ThemeManager.Apply(theme);
                ((ScrollViewer)window.FindName("MainScroll")).ScrollToTop();
                SaveVisual(content, folder, "Suno-"+theme, 1384, 1020);
            }
        }
        var modeBox = (ComboBox)window.FindName("ProcessingModeBox");
        modeBox.SelectedIndex = 0;
        Assert(((Grid)window.FindName("LegacyWorkspace")).Visibility == Visibility.Collapsed, "PA1 hides inactive stem routing");
        ((Slider)window.FindName("Attack80")).Value = 3.5;
        ((Slider)window.FindName("SideAmount")).Value = 25;
        var stereoFrame = new DispatcherFrame();
        var stereoDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        stereoDelay.Tick += (_, _) => { stereoFrame.Continue = false; stereoDelay.Stop(); };
        stereoDelay.Start(); Dispatcher.PushFrame(stereoFrame);
        var stereoSaved = new AppStore(store.DatabasePath).LoadSession(store.LoadSettings().LastSessionSource)!;
        Assert(stereoSaved.ProcessingMode == "structural", "PA1 mode autosaves to SQLite");
        Assert(stereoSaved.Stereo["attack80"].ToString() == "3.5", "Stereo DSP autosaves");
        window.LoadProjectSnapshot(stereoSaved);
        Assert(((Slider)window.FindName("SideAmount")).Value == 25, "Mid/Side setting restored");
        foreach (var theme in new[] { "Dark", "Light" })
        {
            ThemeManager.Apply(theme); modeBox.SelectedIndex = 0;
            ((ScrollViewer)window.FindName("MainScroll")).ScrollToTop();
            SaveVisual(content, folder, "PA1-"+theme, 1384, 1020);
            modeBox.SelectedIndex = 1;
            Assert(((Grid)window.FindName("ImpactControls")).Visibility == Visibility.Visible, "PA2 controls visible");
            SaveVisual(content, folder, "PA2-"+theme, 1384, 1020);
        }
        var comparisonFixture = Path.Combine(root, "artifacts", "waveform-comparison.json");
        if (File.Exists(comparisonFixture))
        {
            var comparison = JsonSerializer.Deserialize<Dictionary<string, double[]>>(File.ReadAllText(comparisonFixture))!;
            modeBox.SelectedIndex = 0;
            wave.SetSamples(comparison["original"]);
            wave.SetProcessedSamples(comparison["processed"]);
            ((TextBlock)window.FindName("WaveLabel")).Text = "Chung thời gian / thang biên độ · Cam: mix sau xử lý · Kéo để tua.";
            foreach (var theme in new[] { "Dark", "Light" })
            {
                ThemeManager.Apply(theme);
                ((ScrollViewer)window.FindName("MainScroll")).ScrollToTop();
                SaveVisual(content, folder, "Comparison-"+theme, 1384, 1020);
                showOriginal.IsChecked = false;
                SaveVisual(content, folder, "ProcessedOnly-"+theme, 1384, 1020);
                showOriginal.IsChecked = true; showProcessed.IsChecked = false;
                SaveVisual(content, folder, "OriginalOnly-"+theme, 1384, 1020);
                showProcessed.IsChecked = true;
            }
        }
        var lyricsSource = Path.Combine(root, "artifacts", "lyrics-real", "vocals.wav");
        if (File.Exists(lyricsSource))
        {
            var lyricProject = new Project { Source = lyricsSource, Name = "Vocals kiểm thử · đoạn 30 giây", Duration = 30 };
            var lyricStem = new Stem { Name = "vocals", Path = lyricsSource };
            var lyricSettings = store.LoadSettings(); lyricSettings.WhisperModel = "base"; lyricSettings.WhisperLanguage = "vi";
            var lyricsWindow = new LyricsWindow(lyricProject, lyricStem, lyricSettings, store);
            // Execute the real native dialog -> Python bridge without desktop input or audio playback.
            ((Button)lyricsWindow.FindName("TranscribeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var jobField = typeof(LyricsWindow).GetField("job", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var lyricsFrame = new DispatcherFrame();
            var limit = DateTime.UtcNow.AddSeconds(90);
            var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            poll.Tick += (_, _) => { if (jobField.GetValue(lyricsWindow) is null || DateTime.UtcNow > limit) { poll.Stop(); lyricsFrame.Continue = false; } };
            poll.Start(); Dispatcher.PushFrame(lyricsFrame);
            Assert(lyricProject.Lyrics?.Lines.Count > 0, "Native lyrics window transcribes real Vocals and saves to SQLite");
            foreach (var theme in new[] { "Dark", "Light" })
            {
                ThemeManager.Apply(theme);
                SaveVisual((FrameworkElement)lyricsWindow.Content, folder, "Lyrics-"+theme, 1080, 750);
                SaveVisual((FrameworkElement)settingsWindow.Content, folder, "Settings-"+theme, 700, 650);
            }
            var lyricsGrid = (DataGrid)lyricsWindow.FindName("LinesGrid");
            var lyricLines = (System.Collections.ObjectModel.ObservableCollection<LyricLine>)lyricsGrid.ItemsSource;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var lyricsPlayer = (MediaPlayer)typeof(LyricsWindow).GetField("player", flags)!.GetValue(lyricsWindow)!;
            lyricsPlayer.Volume = 0; // Exercise Media Foundation silently.
            ((CheckBox)lyricsWindow.FindName("Repeat")).IsChecked = true;
            typeof(LyricsWindow).GetMethod("StartPlayback", flags)!.Invoke(lyricsWindow, new object[] { 1.0, 1.4, true });
            WaitFor(Task.Delay(1800));
            Assert((bool)typeof(LyricsWindow).GetField("playerReady", flags)!.GetValue(lyricsWindow)!, "Lyrics PCM opens in MediaPlayer");
            Assert(lyricsPlayer.Position.TotalSeconds >= .9 && lyricsPlayer.Position.TotalSeconds < 1.6, "Selected lyric loop stays within its interval");
            typeof(LyricsWindow).GetMethod("Pause", flags)!.Invoke(lyricsWindow, new object[] { lyricsWindow, new RoutedEventArgs() });
            var pausedAt = lyricsPlayer.Position.TotalSeconds;
            WaitFor(Task.Delay(250));
            Assert(Math.Abs(lyricsPlayer.Position.TotalSeconds-pausedAt) < .05, "Lyrics pause holds position");
            ((Slider)lyricsWindow.FindName("PositionSlider")).Value = 5;
            Assert(Math.Abs(lyricsPlayer.Position.TotalSeconds-5) < .05, "Lyrics seek updates MediaPlayer");
            typeof(LyricsWindow).GetMethod("Stop", flags)!.Invoke(lyricsWindow, new object[] { lyricsWindow, new RoutedEventArgs() });
            Assert(((Slider)lyricsWindow.FindName("PositionSlider")).Value == 0, "Lyrics stop resets position");
            lyricLines[0].Text = "Đã sửa lời — tiếng Việt đầy đủ dấu";
            typeof(LyricsWindow).GetField("dirty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(lyricsWindow, true);
            lyricsWindow.Close();
            Assert(new AppStore(store.DatabasePath).LoadSession(lyricsSource)!.Lyrics!.Lines[0].Text == "Đã sửa lời — tiếng Việt đầy đủ dấu", "Closing lyrics editor retains Vietnamese edits");
        }
        window.LoadProjectSnapshot(snapshot);
        var mainPlayer = (System.Windows.Media.MediaPlayer)typeof(MainWindow).GetField("player", flagsPreset)!.GetValue(window)!;
        mainPlayer.Volume = 0;
        ((RadioButton)window.FindName("ListenOriginal")).IsChecked = true;
        ((Button)window.FindName("GlobalPlayButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WaitFor(Task.Delay(700));
        Assert((string)typeof(MainWindow).GetField("activeChannel", flagsPreset)!.GetValue(window)! == "original", "Global Play uses original selection");
        ((RadioButton)window.FindName("ListenRemix")).IsChecked = true;
        Assert(((RadioButton)window.FindName("ListenOriginal")).IsChecked == false && (bool)typeof(MainWindow).GetField("playing", flagsPreset)!.GetValue(window)!, "A/B selection is exclusive and keeps old playback while preparing");
        if (typeof(MainWindow).GetField("remixPreparation", flagsPreset)!.GetValue(window) is Task preparing) WaitFor(preparing);
        WaitFor(Task.Delay(500));
        typeof(MainWindow).GetMethod("Stop", flagsPreset)!.Invoke(window, new object[] { true });
        Assert(AllVisuals((ItemsControl)window.FindName("StemList")).OfType<Button>().Count(b => ReferenceEquals(b.Content, app.Resources["PlayIcon"])) == snapshot.Stems.Count, "One audition button per stem plus shared bottom transport");
        ((Button)window.FindName("NewProjectButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var fresh = (Project)typeof(MainWindow).GetField("project", flagsPreset)!.GetValue(window)!;
        Assert(fresh.Source == "" && fresh.Stems.Count == 0 && fresh.DrumParts.Count == 0 && fresh.Analysis is null && fresh.Lyrics is null, "New Project clears audio and derived state");
        Assert(store.LoadSession(snapshot.Source) is not null && store.LoadSettings().LastSessionSource == "", "New Project preserves old history and clears startup restore");
        Assert(((RadioButton)window.FindName("ListenOriginal")).IsChecked == true && ((Slider)window.FindName("SeekBar")).Value == 0, "New Project resets transport");
        SaveVisual(content, folder, "NewProject", 1024, 800);
        Console.WriteLine("PASS: native WPF themes, rounded transport, waveform, SQLite, genre presets / saved defaults, cascade lanes / MIDI mapping, real Whisper dialog, edited lyrics persistence.");
        window.Close();
        app.Shutdown();
    }
    static IEnumerable<DependencyObject> AllVisuals(DependencyObject node)
    {
        yield return node;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in AllVisuals(VisualTreeHelper.GetChild(node, i))) yield return child;
    }
    static void SaveVisual(FrameworkElement content, string folder, string name, int width, int height)
    {
        RemeasureTree(content);
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        RemeasureTree(content);
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var surface = new DrawingVisual();
        using (var drawing = surface.RenderOpen()) drawing.DrawRectangle((Brush)Application.Current.Resources["AppBackground"], null, new Rect(0, 0, width, height));
        bitmap.Render(surface); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(folder, name+".png")); encoder.Save(output);
    }
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void RemeasureTree(DependencyObject node)
    {
        // Detached offscreen trees have no presentation source to schedule a
        // full ancestor layout pass after dynamically inserting lane controls.
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) RemeasureTree(VisualTreeHelper.GetChild(node, i));
        if (node is UIElement element) { element.InvalidateMeasure(); element.InvalidateArrange(); }
    }
    static void WaitFor(Task task)
    {
        var frame = new DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(100);
        var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        poll.Tick += (_, _) => { if (task.IsCompleted || DateTime.UtcNow > deadline) { frame.Continue = false; poll.Stop(); } };
        poll.Start(); Dispatcher.PushFrame(frame);
        Assert(task.IsCompleted, "Preview completed within timeout");
        task.GetAwaiter().GetResult();
    }
}
