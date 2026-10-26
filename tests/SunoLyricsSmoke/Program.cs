using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StemForge;

static class Program
{
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Assert(bool value,string message) { if(!value) throw new Exception(message); Console.WriteLine("PASS "+message); }
    static void Click(Window window,string name) => ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static IEnumerable<DependencyObject> Visuals(DependencyObject node)
    {
        yield return node;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++) foreach(var child in Visuals(VisualTreeHelper.GetChild(node,i))) yield return child;
    }
    static void Save(Window window,string path,int width,int height)
    {
        var body=(FrameworkElement)window.Content;
        body.Measure(new Size(width,height));body.Arrange(new Rect(0,0,width,height));body.UpdateLayout();
        Application.Current.Dispatcher.Invoke(()=>{},DispatcherPriority.ContextIdle);
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
        var bg=new DrawingVisual();using(var dc=bg.RenderOpen())dc.DrawRectangle((Brush)Application.Current.Resources["AppBackground"],null,new Rect(0,0,width,height));
        bitmap.Render(bg);bitmap.Render(body);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))png.Save(file);
    }
    [STAThread]
    static void Main(string[] args)
    {
        var root=Path.GetFullPath(args[0]);var folder=Path.Combine(root,"artifacts","features-v014");Directory.CreateDirectory(folder);
        var app=new App(false){ShutdownMode=ShutdownMode.OnExplicitShutdown};app.InitializeComponent();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        var store=new AppStore(Path.Combine(folder,Guid.NewGuid()+".db"));
        var settings=store.LoadSettings();settings.Python="missing-python-for-offline-ui-test.exe";settings.RestoreLastSession=false;store.SaveSettings(settings);
        var main=new MainWindow(store);
        var project=new Project { Source=Path.Combine(root,"artifacts","analysis-fixture.wav"),Preview=Path.Combine(root,"artifacts","analysis-fixture.wav"),SourceUrl="https://suno.com/song/52e94026-fa85-4779-b54e-da4d8df58d62",Name="Bài thử tiếng Việt — lyrics trực tiếp",Duration=20 };
        main.LoadProjectSnapshot(project);
        var lyrics=new LyricsWindow(project,new Stem{Name="original",Path=project.Source},settings,store);
        Assert(lyrics.FindName("SunoLyricsButton") is null,"Removed public-lyrics button");
        Assert(((Button)lyrics.FindName("SunoTimingButton")).IsEnabled && ((Button)lyrics.FindName("SunoTimingButton")).Content.ToString()=="Lấy lyrics từ Suno (cần Đăng nhập Suno)","Single correctly labeled Suno action");
        var lines=new System.Collections.ObjectModel.ObservableCollection<LyricLine>{new(){Text="Lời thử tiếng Việt",Start="",End=""},new(){Text="Giữ nguyên dấu và dòng",Start="",End=""}};
        typeof(LyricsWindow).GetMethod("ApplySunoLines",Flags)!.Invoke(lyrics,new object[]{lines,"untimed"});
        Assert(project.Lyrics?.Provider=="suno" && project.Lyrics.Lines.Count==2 && project.Lyrics.Lines[0].Start=="","Direct lyrics stored with no invented timestamps");
        Assert(LyricsFormat.Export(project.Lyrics!.Lines,20,"txt").Contains("tiếng Việt"),"Plain Vietnamese TXT export");
        try{LyricsFormat.Export(project.Lyrics.Lines,20,"srt");throw new Exception("Untimed lyrics accepted");}catch(InvalidOperationException){}
        var document=(LyricsDocument)typeof(LyricsWindow).GetField("document",Flags)!.GetValue(lyrics)!;
        document.Lines[0].Start="00:00:01.000";document.Lines[0].End="00:00:03.000";
        document.Lines[1].Start="00:00:04.000";document.Lines[1].End="00:00:06.000";document.Lines[1].Text="Đã sửa câu — giữ tiếng Việt";
        typeof(LyricsWindow).GetField("dirty",Flags)!.SetValue(lyrics,true);
        typeof(LyricsWindow).GetMethod("SaveDraft",Flags)!.Invoke(lyrics,null);
        Assert(LyricsFormat.Export(project.Lyrics.Lines,20,"lrc").Contains("[00:04.00]Đã sửa câu"),"Manually timed Suno lyrics export as LRC");
        Assert(store.LoadSession(project.Source)!.Lyrics!.SourceUrl==project.SourceUrl,"Suno provenance persists in SQLite");
        var reopened=new LyricsWindow(project,new Stem{Name="vocals",Path=project.Preview+".different-stem"},settings,store);
        Assert(((System.Collections.IEnumerable)((DataGrid)reopened.FindName("LinesGrid")).ItemsSource).Cast<LyricLine>().Count()==2,"Direct Suno lyrics survive switching from original audio to separated vocals");reopened.Close();
        var button=(Button)main.FindName("LastExportButton");
        var remember=typeof(MainWindow).GetMethod("RememberExport",Flags)!;
        Assert(button.Visibility==Visibility.Collapsed,"No export-folder button before completed export");
        remember.Invoke(main,new object[]{Path.Combine(folder,"missing.wav")});Assert(button.Visibility==Visibility.Collapsed,"Missing/incomplete output does not expose folder action");
        var export=Path.Combine(folder,"lời xuất.txt");LyricsFormat.Write(export,project.Lyrics.Lines,20,"txt");remember.Invoke(main,new object[]{export});
        Assert(button.Visibility==Visibility.Visible && button.ToolTip.ToString()==folder,"Completed file reveals correct output folder");
        remember.Invoke(main,new object[]{folder});Assert(button.ToolTip.ToString()==folder,"Completed stems directory resolves directly");
        var menu=(WrapPanel)main.FindName("ProjectActions");var labels=menu.Children.OfType<Button>().Select(b=>b.Content?.ToString()).ToArray();
        Assert(Array.IndexOf(labels,L.T("Lưu project"))+1==Array.IndexOf(labels,L.T("Lịch sử")),"History immediately follows Save Project");
        Assert(!menu.Children.OfType<Button>().Any(b=>b.ToolTip?.ToString()==L.T("Cài đặt")),"Settings removed from project menu row");
        var settingsWindow=new SettingsWindow(settings,store.DatabasePath);var history=new HistoryWindow(store);
        foreach(var theme in new[]{"Light","Dark"})
        {
            ThemeManager.Apply(theme);
            Save(main,Path.Combine(folder,"Main-"+theme+".png"),1384,940);
            Save(main,Path.Combine(folder,"Main-Narrow-"+theme+".png"),1024,760);
            Save(lyrics,Path.Combine(folder,"Lyrics-"+theme+".png"),1100,780);
            Save(settingsWindow,Path.Combine(folder,"Settings-"+theme+".png"),720,760);
            Save(history,Path.Combine(folder,"History-"+theme+".png"),1000,720);
        }
        foreach(var window in new Window[]{lyrics,settingsWindow,history})
            Assert(Visuals((DependencyObject)window.Content).OfType<Button>().Where(b=>b.Content is string).All(b=>b.ContentTemplate is not null),window.GetType().Name+" text buttons have icon templates");
        lyrics.Close();settingsWindow.Close();history.Close();main.Close();app.Shutdown();
        Console.WriteLine("PASS direct Suno lyrics, edit/export persistence, menu placement, icons and export folder.");
    }
}
