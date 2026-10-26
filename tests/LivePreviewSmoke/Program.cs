using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using StemForge;

static class Program
{
    static MainWindow window=null!;
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static object? Field(string name)=>typeof(MainWindow).GetField(name,Flags)!.GetValue(window);
    static object? Call(string name,params object?[] args)=>typeof(MainWindow).GetMethod(name,Flags)!.Invoke(window,args);
    static LiveAudioSession Session=>(LiveAudioSession)Field("liveSession")!;
    static void Assert(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
    static void Until(Func<bool> done,int timeout=30000)
    {
        if(done())return;
        var frame=new DispatcherFrame();var end=DateTime.UtcNow.AddMilliseconds(timeout);
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};
        timer.Tick+=(_,_)=>{if(done()||DateTime.UtcNow>end){timer.Stop();frame.Continue=false;}};
        timer.Start();Dispatcher.PushFrame(frame);Assert(done(),"Async completion");
    }
    static void Wait(Task task){Until(()=>task.IsCompleted,120000);task.GetAwaiter().GetResult();}
    static Task Play(string channel)=>(Task)Call("PlayLive",channel,channel,true)!;
    static void Choose(bool original)=>((RadioButton)window.FindName(original?"ListenOriginal":"ListenRemix")).IsChecked=true;
    static void Slider(string name,double value)=>((Slider)window.FindName(name)).Value=value;
    [STAThread]
    static void Main(string[] args)
    {
        var root=Path.GetFullPath(args[0]);var folder=Path.Combine(root,"artifacts","live-v015");Directory.CreateDirectory(folder);
        var app=new App(false){ShutdownMode=ShutdownMode.OnExplicitShutdown};app.InitializeComponent();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        var store=new AppStore(Path.Combine(folder,Guid.NewGuid()+".db"));var settings=store.LoadSettings();
        Assert(settings.LivePreview,"Live preview defaults on");settings.RestoreLastSession=false;settings.PlaybackVolume=0;
        settings.Python=@"C:\Program Files\Python313\python.exe";settings.Ffmpeg=@"C:\ffmpeg\bin\ffmpeg.exe";store.SaveSettings(settings);
        var project=JsonSerializer.Deserialize<Project>(File.ReadAllText(Path.Combine(root,"artifacts","cascade-cpu-v0142","CPU test.stemforge.json")),Settings.JsonOptions)!;
        window=new MainWindow(store);window.LoadProjectSnapshot(project);
        var initial=Directory.GetFiles(Path.GetDirectoryName(project.Source)!,"*",SearchOption.AllDirectories).Order().ToArray();
        var watch=Stopwatch.StartNew();Wait(Play("original"));double startupMs=watch.Elapsed.TotalMilliseconds;
        Assert(Session.IsPlaying,"WASAPI is playing RAM PCM");int worker=Session.WorkerId;
        Until(()=>Session.Position>.5);double position=Session.Position;
        Choose(false);Until(()=>Session.AppliedRevision==Session.Revision);
        Assert(Session.IsPlaying&&Session.Position>=position-.1&&Session.WorkerId==worker,"A/B preserves playback position and worker");
        watch.Restart();Slider("Presence",5);Until(()=>Session.AppliedRevision==Session.Revision);double parameterQueuedMs=watch.Elapsed.TotalMilliseconds;
        Assert(Session.IsPlaying,"EQ changes while audio keeps playing");
        Wait((Task)Call("PlayPreview","mix","mix",null,false)!);
        Assert(Session.IsPlaying,"Waveform comparison runs in RAM without stopping live playback");
        Slider("Transient",45);Slider("Multiband",30);Slider("Parallel",25);
        project.Stems.First(s=>s.Name=="bass").Gain=-4;project.DrumParts.First(s=>s.Name=="kick").Gain=-3;project.DrumParts.First(s=>s.Name=="snare").Mute=true;
        Until(()=>Session.AppliedRevision==Session.Revision);
        Assert(Session.CacheBytes<=256L*1024*1024,"PCM cache is bounded to 256 MiB");
        Call("PauseGlobal",window,new RoutedEventArgs());position=Session.Position;Slider("Presence",1);
        Until(()=>Session.AppliedRevision==Session.Revision);
        Assert(!Session.IsPlaying&&Math.Abs(Session.Position-position)<.02,"Editing paused DSP retains pause and position");
        Call("Seek",8.0);Wait(Play("mix"));Until(()=>Session.Position>8.2);Assert(Session.Position<10,"Seek resumes at requested position");
        Choose(true);Until(()=>Session.AppliedRevision==Session.Revision);Assert(Session.IsPlaying,"Remix to original continues playing");
        Wait(Play("stem:vocals"));Until(()=>Session.AppliedRevision==Session.Revision);Assert((string)Field("activeChannel")! == "stem:vocals","Individual stem uses live playback");
        Session.Repeat=true;Call("Seek",project.Duration-.3);Until(()=>Session.Position<1.5);Assert(Session.IsPlaying,"Repeat wraps without stopping");
        Call("Stop",true);Assert(!Session.IsPlaying,"Stop clears live playback");
        Assert(initial.SequenceEqual(Directory.GetFiles(Path.GetDirectoryName(project.Source)!,"*",SearchOption.AllDirectories).Order()),"Live DSP writes no preview audio files");
        var pending=Play("mix");Call("Stop",true);Wait(pending);Assert(!(bool)Field("playing")!,"Stop during preparation prevents stale autoplay");
        Wait(Play("mix"));Call("PauseGlobal",window,new RoutedEventArgs());
        string output=Path.Combine(folder,"export-latest.flac");var request=Call("RenderRequest",output,false)!;
        var rendered=JsonSerializer.SerializeToElement(request,Settings.JsonOptions);
        Assert(rendered.GetProperty("refix").GetProperty("presence").GetDouble()==1 && rendered.GetProperty("drums").GetProperty("transient").GetDouble()==45,"Export captures latest live DSP settings");
        var task=new EngineClient().RunAsync(request,settings,Console.WriteLine,CancellationToken.None);Wait(task);
        Assert(File.Exists(output),"Export renders the real file using current settings");
        var evidence=new{startupMs,parameterQueuedMs,estimatedAdditionalBufferMs=Session.BufferedSeconds*1000+60,Session.ComputeMilliseconds,Session.CacheBytes,Session.ProcessedBlocks,worker,request};
        File.WriteAllText(Path.Combine(folder,"native-verification.json"),JsonSerializer.Serialize(evidence,Settings.JsonOptions));
        window.Close();Assert(Field("liveSession") is null,"Closing app disposes live worker");app.Shutdown();
    }
}
