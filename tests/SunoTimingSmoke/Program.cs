using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using StemForge;

internal static class Program
{
    static void Assert(bool value, string text) { if (!value) throw new Exception(text); Console.WriteLine("PASS " + text); }
    [STAThread]
    static void Main(string[] args)
    {
        var app = new App(false) { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.InitializeComponent(); L.Initialize("vi");
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        var task = Verify(Path.GetFullPath(args[0])); var frame = new DispatcherFrame(); var until = DateTime.UtcNow.AddSeconds(120);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) => { if (task.IsCompleted || DateTime.UtcNow > until) { timer.Stop(); frame.Continue = false; } };
        timer.Start(); Dispatcher.PushFrame(frame);
        try { Assert(task.IsCompleted, "Browser test completed before timeout"); task.GetAwaiter().GetResult(); }
        finally { app.Shutdown(); }
    }
    static async Task Wait(Func<Task<bool>> ready)
    { for (int i=0;i<150;i++) { if (await ready()) return; await Task.Delay(100); } throw new Exception("Timed out waiting for browser result"); }
    static async Task Verify(string root)
    {
        const string id="52e94026-fa85-4779-b54e-da4d8df58d62";
        const string url="https://suno.com/song/"+id;
        Assert(SunoAlignedLyrics.SongId(url+"?source=test")==id, "Exact public URL maps to correct song");
        Assert(SunoAlignedLyrics.SongId("https://suno.ai/song/"+id)==id && SunoAlignedLyrics.SongId("https://cdn1.suno.com/"+id+".mp3")==id,"Legacy and direct-download URLs keep the song ID");
        foreach(var bad in new[]{"http://suno.com/song/"+id,"https://suno.com.attacker.test/song/"+id,"https://u:p@suno.com/song/"+id,"https://suno.com:8443/song/"+id})
        { try { SunoAlignedLyrics.SongId(bad); throw new Exception("Invalid URL accepted"); } catch(InvalidOperationException) {} }
        Assert(!SunoAlignedLyrics.IsSunoOrigin("https://example.com") && !SunoAlignedLyrics.IsSunoOrigin("https://suno.com:444"),"Foreign bridge origins rejected");
        const string json="""{"data":{"aligned_lyrics":[{"text":"Lời tiếng Việt","words":[{"start_s":1.25,"end_s":2.0,"text":"Lời"},{"start_s":2.1,"end_s":3.5,"text":"tiếng Việt"}]},{"text":"Câu hai","start_s":4,"end_s":6},{"text":"Chưa có timing","start_s":-1,"end_s":80}]}}""";
        using(var data=JsonDocument.Parse(json))
        {
            var lines=SunoAlignedLyrics.Parse(data.RootElement,20);
            Assert(lines.Count==3 && lines[0].Start=="00:00:01.250" && lines[0].End=="00:00:03.500" && lines[2].Start=="","Word timing preserved; invalid timing left blank");
            Assert(LyricsFormat.Export(lines.Take(2),20,"srt").Contains("00:00:01,250 --> 00:00:03,500"),"Real timing exported to Vietnamese SRT");
            Assert(LyricsFormat.Export(lines.Take(2),20,"lrc").Contains("[00:01.25]Lời tiếng Việt"),"Real timing exported to Vietnamese LRC");
        }
        var folder=Path.Combine(root,"artifacts","timing-v0141"); Directory.CreateDirectory(folder);
        var window=new SunoLyricsBrowser(url,20,Path.Combine(folder,"profile-"+Guid.NewGuid().ToString("N")))
            {Left=-20000,Top=-20000,WindowStartupLocation=WindowStartupLocation.Manual,ShowInTaskbar=false,ShowActivated=false};
        int calls=0,mode=0; bool authenticated=false; bool received=false;
        window.LyricsReceived+=()=>received=true;
        var init=window.InitializeAsync((environment,web)=>
        {
            web.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);
            web.WebResourceRequested+=(_,e)=>
            {
                string text="",type="text/html"; int status=200;
                if(e.Request.Uri.StartsWith("https://studio-api.prod.suno.com/api/gen/"))
                {
                    type="application/json";
                    if(e.Request.Method!="OPTIONS")
                    {
                        calls++; authenticated=e.Request.Headers.GetHeader("Authorization")=="Bearer fake-session-for-local-test";
                        Assert(e.Request.Uri=="https://studio-api.prod.suno.com/api/gen/"+id+"/aligned_lyrics/v2/","Browser requests only selected song alignment");
                        text=mode==0?json:mode==1?"{}":mode==2?"{\"aligned_lyrics\":[]}":new string('x',1_048_577);
                        if(mode==1)status=401;
                    }
                }
                else if(e.Request.Uri.StartsWith("https://suno.com/song/")) text="<html lang='vi'><body><h2>Suno mock — offline integration test</h2></body></html>";
                else status=404;
                e.Response=environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(text)),status,"Test",
                    "Content-Type: "+type+"; charset=utf-8\r\nAccess-Control-Allow-Origin: https://suno.com\r\nAccess-Control-Allow-Headers: Authorization\r\nAccess-Control-Allow-Methods: GET, OPTIONS");
            };
        });
        window.Show(); await init;
        var web=window.Browser.CoreWebView2;
        Assert(web is not null,"Embedded WebView2 initialized with isolated test profile");
        await Wait(async()=>await web!.ExecuteScriptAsync("document.readyState === 'complete' && location.pathname.startsWith('/song/')")=="true");
        await window.FetchAsync(); await Wait(()=>Task.FromResult(window.StatusText.Contains("Cần đăng nhập")));
        Assert(calls==0 && !received,"Signed-out state does not fetch or overwrite lyrics");
        await web!.ExecuteScriptAsync("window.Clerk={session:{getToken:async()=> 'fake-session-for-local-test'}}");
        await window.FetchAsync(); await Wait(()=>Task.FromResult(received));
        Assert(authenticated && window.Result![0].Start=="00:00:01.250","Authenticated browser fetch returns actual timestamps to native editor");
        var saved=window.Result;
        mode=1; received=false; await window.FetchAsync(); await Wait(()=>Task.FromResult(window.StatusText.Contains("Cần đăng nhập")));
        Assert(!received && ReferenceEquals(saved,window.Result),"401 preserves previous result");
        mode=2; await window.FetchAsync(); await Wait(()=>Task.FromResult(window.StatusText.Contains("chưa có mốc")));
        Assert(!received && ReferenceEquals(saved,window.Result),"Missing alignment preserves prior result and invents no timestamps");
        mode=3; await window.FetchAsync(); await Wait(()=>Task.FromResult(window.StatusText.Contains("Suno chưa trả")));
        Assert(!received,"Oversized response rejected");
        mode=0; await web.ExecuteScriptAsync("window.Clerk=undefined;document.cookie='__session=fake-session-for-local-test; Secure; SameSite=Lax; path=/'");
        await window.FetchAsync(); await Wait(()=>Task.FromResult(received));
        Assert(authenticated,"Suno page session-cookie fallback works without exporting credentials");
        await web.ExecuteScriptAsync("document.cookie='__session=; Max-Age=0; path=/'");
        var script=SunoLyricsBrowser.FetchScript(id,"test-nonce");
        Assert(!script.Contains("__SONG__") && !script.Contains("__REQUEST__") && script.Contains("redirect: 'error'"),"Script parameters and redirect guard");
        ThemeManager.Apply("Light"); window.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var output=File.Create(Path.Combine(folder,"SunoTimingWindow.png")))encoder.Save(output);
        window.Close();
    }
}
