using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StemForge;

static class Program
{
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static MainWindow window = null!;
    static object? Field(string name) => typeof(MainWindow).GetField(name, Flags)!.GetValue(window);
    static object? Call(string name, params object?[] args) => typeof(MainWindow).GetMethod(name, Flags)!.Invoke(window, args);
    static MediaPlayer Player => (MediaPlayer)Field("player")!;
    static bool Playing => (bool)Field("playing")!;
    static string Channel => (string)Field("activeChannel")!;
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
    static void PumpUntil(Func<bool> done, int timeout = 20000)
    {
        if (done()) return;
        var frame = new DispatcherFrame(); var end = DateTime.UtcNow.AddMilliseconds(timeout);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (_, _) => { if (done() || DateTime.UtcNow > end) { timer.Stop(); frame.Continue = false; } };
        timer.Start(); Dispatcher.PushFrame(frame); Assert(done(), "Async operation completed");
    }
    static void Delay(int ms) { var end = DateTime.UtcNow.AddMilliseconds(ms); PumpUntil(() => DateTime.UtcNow >= end); }
    static void Wait(Task task) { PumpUntil(() => task.IsCompleted, 60000); task.GetAwaiter().GetResult(); }
    static void Choose(bool original) => ((RadioButton)window.FindName(original ? "ListenOriginal" : "ListenRemix")).IsChecked = true;
    static void Click(string name) => ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static void Stop() => Call("Stop", true);
    static IEnumerable<DependencyObject> Visuals(DependencyObject node)
    {
        yield return node;
        for (int i=0; i<VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var item in Visuals(VisualTreeHelper.GetChild(node,i))) yield return item;
    }
    [STAThread]
    static void Main(string[] args)
    {
        string root=Path.GetFullPath(args[0]), output=Path.Combine(root,"artifacts","transport-v0132"); Directory.CreateDirectory(output);
        var app=new App(false) { ShutdownMode=ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        var store=new AppStore(Path.Combine(output,Guid.NewGuid()+".db"));
        var settings=store.LoadSettings(); settings.RestoreLastSession=false; settings.LivePreview=false; settings.Python=Environment.GetEnvironmentVariable("STEMFORGE_TEST_PYTHON") ?? "python"; settings.Ffmpeg=Environment.GetEnvironmentVariable("STEMFORGE_FFMPEG") ?? ""; settings.Repeat=true; store.SaveSettings(settings);
        window=new MainWindow(store);
        var snapshot=JsonSerializer.Deserialize<Project>(File.ReadAllText(Path.Combine(root,"artifacts","smoke","Synthetic test.stemforge.json")),Settings.JsonOptions)!;
        window.LoadProjectSnapshot(snapshot); Player.IsMuted=true;
        var content=(FrameworkElement)window.Content;
        content.Measure(new Size(1384,1020)); content.Arrange(new Rect(0,0,1384,1020)); content.UpdateLayout();
        var stemButtons=Visuals((ItemsControl)window.FindName("StemList")).OfType<Button>().Where(b=>ReferenceEquals(b.Content,app.Resources["PlayIcon"])).ToArray();
        Assert(stemButtons.Length==snapshot.Stems.Count,"One Play icon per separated instrument stem");
        stemButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpUntil(()=>Field("cancellation") is null && (bool)Field("mediaReady")!,60000);
        Assert(Playing && Channel=="stem:"+((Stem)stemButtons[0].DataContext).Name,"Stem button plays only its stem through shared player");
        var volume=(Slider)window.FindName("PlaybackVolumeSlider");
        var originalGain=((Stem)stemButtons[0].DataContext).Gain;
        volume.Value=23;
        Assert(Playing && Math.Abs(Player.Volume-.23)<.001 && ((Stem)stemButtons[0].DataContext).Gain==originalGain,"Volume changes live on stem audition without stopping or changing mix gain");
        volume.Value=0; Assert(Player.Volume==0 && Playing,"Zero volume silences playback without stopping");
        volume.Value=100; Assert(Player.Volume==1,"100 percent maps to full player volume");
        volume.Value=23;
        Stop();
        Wait((Task)Call("PlayPreview","mix","Mix",null,false)!);
        Choose(true); Click("GlobalPlayButton"); PumpUntil(()=>Playing);
        Call("Seek",2.0); Delay(150);
        var old=Player; var before=old.Position.TotalSeconds;
        Choose(false); Assert(Playing,"Cached A/B does not stop current playback while opening next file");
        volume.Value=67;
        Assert(Field("pendingPlayer") is MediaPlayer loading && Math.Abs(loading.Volume-.67)<.001,"Volume change reaches A/B player while it is preloading");
        PumpUntil(()=>Channel=="mix" && Field("pendingPlayer") is null);
        Assert(Math.Abs(Player.Volume-.67)<.001 && store.LoadSettings().PlaybackVolume==67,"A/B keeps latest volume and persists it in SQLite");
        Assert(Playing && !ReferenceEquals(old,Player) && Player.Position.TotalSeconds>=before-.15,"Cached remix starts at live original position");
        var position=Player.Position.TotalSeconds; Delay(200); Assert(Player.Position.TotalSeconds>position+.08,"Playback keeps advancing after A/B switch");
        Call("PauseGlobal",window,new RoutedEventArgs()); position=Player.Position.TotalSeconds;
        Choose(true); PumpUntil(()=>Channel=="original" && Field("pendingPlayer") is null);
        Assert(!Playing && Math.Abs(Player.Position.TotalSeconds-position)<.15,"Switch while paused preserves pause and position");
        Click("GlobalPlayButton"); PumpUntil(()=>Playing); Call("Seek",1.0);
        Call("InvalidateMix"); Choose(false);
        Assert(Playing && Channel=="original" && Field("cancellation") is not null,"Uncached remix renders while original continues playing");
        Choose(true); Choose(false); // Latest selection wins and shares one render.
        PumpUntil(()=>Field("cancellation") is null && Field("pendingPlayer") is null && Channel=="mix",60000);
        Assert(Playing,"Rapid A/B during preparation finishes on latest requested version");
        Choose(true); PumpUntil(()=>Channel=="original");
        Call("InvalidateMix"); Choose(false); Assert(Playing,"Second uncached render keeps playback active"); Stop();
        PumpUntil(()=>Field("cancellation") is null,60000); Delay(250);
        Assert(!Playing && (string)Field("activePath")! == "" && (double)Field("cursorSeconds")! == 0,"Stop during render prevents stale auto-resume");
        Choose(true); Click("GlobalPlayButton"); PumpUntil(()=>Playing);
        Choose(false); Stop(); Delay(250);
        Assert(!Playing && Field("pendingPlayer") is null,"Stop during preload cancels pending switch");
        var gain=((Project)Field("project")!).Stems[0].Gain;
        foreach(var name in new[]{"SourceStep","ProcessingStep","StemsStep","RefixStep","LegacyDrumLab","ExportStep"})
        {
            var step=(StepBox)window.FindName(name); step.ApplyTemplate();
            var body=(ContentPresenter)step.Template.FindName("StepBody",step);
            var toggle=(ToggleButton)step.Template.FindName("StepToggle",step);
            toggle.IsChecked=false;
            Assert(!step.IsExpanded && body.Visibility==Visibility.Collapsed && store.LoadSettings().CollapsedSteps.Contains(step.StepId),name+" footer hides body and saves preference");
            toggle.IsChecked=true; Assert(body.Visibility==Visibility.Visible,name+" footer restores body");
        }
        Assert(((Project)Field("project")!).Stems[0].Gain==gain,"Collapsing steps preserves mixer values");
        ((StepBox)window.FindName("StemsStep")).IsExpanded=false;
        var restored=new MainWindow(store); Assert(!((StepBox)restored.FindName("StemsStep")).IsExpanded,"Collapsed state restored from SQLite");
        Assert(((Slider)restored.FindName("PlaybackVolumeSlider")).Value==67 && Math.Abs(((MediaPlayer)typeof(MainWindow).GetField("player",Flags)!.GetValue(restored)!).Volume-.67)<.001,"Volume restored to slider and player after reopening app"); restored.Close();
        ((StepBox)window.FindName("StemsStep")).IsExpanded=true;
        ((ScrollViewer)window.FindName("MainScroll")).ScrollToVerticalOffset(650);
        content.Measure(new Size(1384,1020)); content.Arrange(new Rect(0,0,1384,1020)); content.UpdateLayout();
        var bmp=new RenderTargetBitmap(1384,1020,96,96,PixelFormats.Pbgra32); bmp.Render(content);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bmp)); using(var file=File.Create(Path.Combine(output,"Steps-and-stem-play.png"))) encoder.Save(file);
        window.Close(); app.Shutdown(); Console.WriteLine("PASS Transport and collapsible steps smoke");
    }
}
