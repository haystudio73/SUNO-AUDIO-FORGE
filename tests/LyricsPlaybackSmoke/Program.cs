using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StemForge;

static class Program
{
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static LyricsWindow window=null!;
    static object? Field(string name)=>typeof(LyricsWindow).GetField(name,Flags)!.GetValue(window);
    static object? Call(string name,params object[] values)=>typeof(LyricsWindow).GetMethod(name,Flags)!.Invoke(window,values);
    static void Assert(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
    static void Until(Func<bool> done,int ms=12000)
    {
        var deadline=DateTime.UtcNow.AddMilliseconds(ms);var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(30)};
        timer.Tick+=(_,_)=>{if(done() || DateTime.UtcNow>=deadline){timer.Stop();frame.Continue=false;}};
        timer.Start();Dispatcher.PushFrame(frame);Assert(done(),"Playback wait completed");
    }
    static IEnumerable<DependencyObject> Visuals(DependencyObject node)
    {yield return node;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in Visuals(VisualTreeHelper.GetChild(node,i)))yield return child;}
    static void WriteFixture(string path)
    {
        const int rate=48000, frames=rate*20, size=frames*4;
        using var writer=new BinaryWriter(File.Create(path));
        void Tag(string value)=>writer.Write(System.Text.Encoding.ASCII.GetBytes(value));
        Tag("RIFF");writer.Write(36+size);Tag("WAVE");Tag("fmt ");writer.Write(16);writer.Write((short)1);writer.Write((short)2);
        writer.Write(rate);writer.Write(rate*4);writer.Write((short)4);writer.Write((short)16);Tag("data");writer.Write(size);
        for(int i=0;i<frames;i++){short sample=(short)(300*Math.Sin(2*Math.PI*220*i/rate));writer.Write(sample);writer.Write(sample);}
    }
    [STAThread]
    static void Main(string[] args)
    {
        var root=Path.GetFullPath(args[0]);var folder=Path.Combine(root,"artifacts","lyrics-v0142");Directory.CreateDirectory(folder);
        var app=new App(false){ShutdownMode=ShutdownMode.OnExplicitShutdown};app.InitializeComponent();L.Initialize("vi");
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        var source=Path.Combine(folder,"timeline-fixture.wav");WriteFixture(source);
        Assert(File.Exists(source),"PCM audio fixture available");
        var store=new AppStore(Path.Combine(folder,Guid.NewGuid()+".db"));var settings=store.LoadSettings();settings.PlaybackVolume=0;settings.Python="missing-python-for-ui-fixture.exe";
        var project=new Project{Source=source,Name="Kiểm thử lyrics theo timeline",Duration=20,SourceUrl="https://suno.com/song/52e94026-fa85-4779-b54e-da4d8df58d62"};
        window=new LyricsWindow(project,new Stem{Name="vocals",Path=source},settings,store);
        window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-20000;window.Top=-20000;window.ShowInTaskbar=false;window.ShowActivated=false;window.Show();
        Until(()=>Field("job") is null);
        var a=new LyricLine{Text="Dòng đầu — tiếng Việt",Start="00:00:01.000",End="00:00:03.000"};
        var b=new LyricLine{Text="Dòng hai — theo vị trí phát",Start="00:00:04.000",End="00:00:06.000"};
        var lines=new ObservableCollection<LyricLine>{b,new(){Text="[Instrument]",Start=a.Start,End="00:00:01.100"},a,new(){Text="Chưa có thời gian",Start="",End=""},new(){Text="Sai thời gian",Start="bad",End="00:00:10.000"}};
        Call("ApplySunoLines",lines,"mixed");
        var grid=(DataGrid)window.FindName("LinesGrid");var slider=(Slider)window.FindName("PositionSlider");
        Call("SetPosition",1.05);Assert(ReferenceEquals(grid.SelectedItem,a),"Overlapping tag does not hide longer lyric; unsorted input supported");
        Call("SetPosition",3.5);Assert(grid.SelectedItem is null,"Gap between lyrics clears highlight");
        slider.Value=4.2;Assert(ReferenceEquals(grid.SelectedItem,b),"Seek selects matching lyric");
        typeof(LyricsWindow).GetField("editingLine",Flags)!.SetValue(window,true);Call("SetPosition",1.5);
        Assert(ReferenceEquals(grid.SelectedItem,b),"Follow does not steal the row during editing");
        typeof(LyricsWindow).GetField("editingLine",Flags)!.SetValue(window,false);Call("SetPosition",1.5);
        Assert(ReferenceEquals(grid.SelectedItem,a),"Follow resumes after editing");
        var preview=(string)typeof(LyricsWindow).GetProperty("PreviewPath",Flags)!.GetValue(window)!;
        Directory.CreateDirectory(Path.GetDirectoryName(preview)!);File.Copy(source,preview);
        var player=(MediaPlayer)Field("player")!;
        Call("StartPlayback",1.1,20.0,false);Until(()=>(bool)Field("playing")! && (bool)Field("playerReady")!);
        player.Position=TimeSpan.FromSeconds(4.2);Until(()=>ReferenceEquals(grid.SelectedItem,b));
        Assert((bool)Field("playing")!,"Timer follows real MediaPlayer playback");
        Call("Pause",window,new RoutedEventArgs());slider.Value=1.2;
        Assert(ReferenceEquals(grid.SelectedItem,a) && !(bool)Field("playing")!,"Paused seek updates row without resuming playback");
        ((CheckBox)window.FindName("Repeat")).IsChecked=true;Call("PlayLine",window,new RoutedEventArgs());
        player.Position=TimeSpan.FromSeconds(2.99);Until(()=>player.Position.TotalSeconds<2 && ReferenceEquals(grid.SelectedItem,a));
        Assert((bool)Field("playing")!,"Repeat selected line restarts with correct lyric");
        Call("Stop",window,new RoutedEventArgs());Assert(grid.SelectedItem is null,"Stop resets timeline highlight");
        for(int i=7;i<19;i++)lines.Add(new(){Text="Dòng lyrics "+i,Start=LyricsFormat.Time(i),End=LyricsFormat.Time(i+.8)});
        foreach(var theme in new[]{"Light","Dark"})
        {
            ThemeManager.Apply(theme);var body=(FrameworkElement)window.Content;body.Measure(new Size(1100,780));body.Arrange(new Rect(0,0,1100,780));body.UpdateLayout();
            Call("SetPosition",0.0); Call("SetPosition",18.2);body.UpdateLayout();app.Dispatcher.Invoke(()=>{},DispatcherPriority.Render);
            Until(()=> { body.UpdateLayout(); return ReferenceEquals(grid.SelectedItem,lines.Last()) && grid.ItemContainerGenerator.ContainerFromItem(lines.Last()) is DataGridRow row && row.TranslatePoint(new Point(0,0),grid).Y>=0 && row.TranslatePoint(new Point(0,row.ActualHeight),grid).Y<=grid.ActualHeight; });
            Assert(ReferenceEquals(grid.SelectedItem,lines.Last()),"Follow scrolls a later row into view");
            var selected=Visuals(body).OfType<DataGridCell>().First(c=>c.IsSelected);
            Assert(selected.Background is SolidColorBrush brush && brush.Color==Color.FromRgb(21,101,192),"Blue selected row in "+theme);
            var bitmap=new RenderTargetBitmap(1100,780,96,96,PixelFormats.Pbgra32);var background=new DrawingVisual();using(var dc=background.RenderOpen())dc.DrawRectangle((Brush)app.Resources["AppBackground"],null,new Rect(0,0,1100,780));bitmap.Render(background);bitmap.Render(body);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
            using var file=File.Create(Path.Combine(folder,"Lyrics-"+theme+".png"));png.Save(file);
        }
        Call("StartPlayback",1.1,20.0,false);Until(()=>(bool)Field("playing")!);
        ((Button)window.FindName("DeleteAllLyricsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(lines.Count==0 && grid.SelectedItem is null && !(bool)Field("playing")!,"Delete all clears every row and stops playback");
        Call("SaveDraft");Assert(store.LoadSession(source)!.Lyrics!.Lines.Count==0 && File.Exists(source),"Empty lyrics persist in SQLite; audio retained");
        window.Close();var reopen=new LyricsWindow(store.LoadSession(source)!,new Stem{Name="vocals",Path=source},settings,store);
        Assert(((DataGrid)reopen.FindName("LinesGrid")).Items.Count==0,"Reopening does not resurrect deleted lyrics");reopen.Close();app.Shutdown();
    }
}
