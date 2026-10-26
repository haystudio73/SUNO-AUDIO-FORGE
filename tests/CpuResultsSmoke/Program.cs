using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using StemForge;

static class Program
{
    static void Assert(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("PASS "+message);}
    [STAThread]
    static void Main(string[] args)
    {
        var root=Path.GetFullPath(args[0]);var folder=Path.Combine(root,"artifacts","cascade-cpu-v0142");
        var evidence=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"verification.json"))).RootElement;
        Assert(evidence.GetProperty("requestedDevice").GetString()=="cpu" && evidence.GetProperty("sw").GetProperty("device").GetString()=="cpu" && evidence.GetProperty("drumsep").GetProperty("device").GetString()=="cpu","Both real inference stages used CPU");
        Environment.SetEnvironmentVariable("CUDA_VISIBLE_DEVICES","-1");
        var app=new App(false){ShutdownMode=ShutdownMode.OnExplicitShutdown};app.InitializeComponent();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        var store=new AppStore(Path.Combine(folder,"ui-"+Guid.NewGuid()+".db"));var settings=store.LoadSettings();settings.Python=@"C:\Program Files\Python313\python.exe";settings.Ffmpeg=@"C:\ffmpeg\bin\ffmpeg.exe";settings.RestoreLastSession=false;store.SaveSettings(settings);
        var project=JsonSerializer.Deserialize<Project>(File.ReadAllText(Path.Combine(folder,"Cascade.stemforge.json")),Settings.JsonOptions)!;project.Device="cpu";
        var window=new MainWindow(store);window.LoadProjectSnapshot(project);
        Assert(((ComboBoxItem)((ComboBox)window.FindName("DeviceBox")).SelectedItem).Tag.ToString()=="cpu","App shows CPU-only selection");
        Assert(project.Stems.Count==6 && project.DrumParts.Count==6 && project.StemSeparation!.Matches("bs_roformer_sw",project.Source,project.Stems) && project.DrumSeparation!.Matches("drumsep_mdx23c",project.DrumPartsSource,project.DrumParts),"App accepts all real SW and DrumSep files");
        var files=project.Stems.Concat(project.DrumParts).ToDictionary(s=>s.Path,s=>File.GetLastWriteTimeUtc(s.Path));
        foreach(var name in new[]{"SeparateStemsButton","SeparatePartsButton","CascadeButton"})((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(files.All(f=>File.GetLastWriteTimeUtc(f.Key)==f.Value),"Already separated audio is reused without rerunning inference");
        Assert(((Button)window.FindName("ExportPartsButton")).IsEnabled,"App enables export of completed CPU DrumSep results");
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var request=typeof(MainWindow).GetMethod("RenderRequest",flags)!.Invoke(window,new object[]{Path.Combine(folder,"ui-remix.flac"),false})!;
        var json=JsonSerializer.SerializeToElement(request,Settings.JsonOptions);
        Assert(json.GetProperty("processingMode").GetString()=="legacy" && json.GetProperty("stems").GetArrayLength()==6 && json.GetProperty("drums").GetProperty("parts").GetArrayLength()==6,"App remix request includes six stems and six drum parts");
        var task=new EngineClient().RunAsync(request,settings,Console.WriteLine,CancellationToken.None);
        var frame=new DispatcherFrame();var deadline=DateTime.UtcNow.AddSeconds(120);var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
        timer.Tick+=(_,_)=>{if(task.IsCompleted || DateTime.UtcNow>deadline){timer.Stop();frame.Continue=false;}};timer.Start();Dispatcher.PushFrame(frame);
        Assert(task.IsCompleted,"Native engine bridge completed render");var result=task.GetAwaiter().GetResult();
        Assert(File.Exists(Path.Combine(folder,"ui-remix.flac")),"App generated final FLAC from real CPU stems");
        typeof(MainWindow).GetMethod("CaptureProject",flags)!.Invoke(window,null);
        File.WriteAllText(Path.Combine(folder,"CPU test.stemforge.json"),JsonSerializer.Serialize(project,Settings.JsonOptions));
        File.WriteAllText(Path.Combine(folder,"ui-render-result.json"),result.GetRawText());
        window.Close();app.Shutdown();
    }
}
