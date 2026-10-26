using System.IO;
using System.Text.Json;

namespace StemForge;

public partial class MainWindow
{
    LiveAudioSession? liveSession;
    bool liveActive;
    int liveLaunchVersion;

    object LiveRequest(string stage)
    {
        CaptureProject();
        if(stage=="original") return new {source=project.Source,processingMode="legacy"};
        if(stage=="drumsep" && SelectedDrumParts().Length==0)
            return new {source=project.Source,silence=true};
        Stem? single=stage.StartsWith("stem:") ? project.Stems.FirstOrDefault(s=>s.Name==stage[5..]) : null;
        if(stage.StartsWith("stem:") && single is null)throw new InvalidOperationException(L.T("Không tìm thấy stem để nghe."));
        return PreviewRequest.Create(project,stage,"",single,stage=="drumsep"?SelectedDrumParts():null);
    }
    void ValidateLive(string stage)
    {
        if((stage=="drums" || stage=="mix" && Selected(ProcessingModeBox,true)=="legacy") && project.DrumParts.Count>0 && !HasValidDrumParts())
            throw new InvalidOperationException(L.T("Bộ DrumSep đã thay đổi hoặc thiếu file. Tách lại bộ trống trước khi dựng bản remix."));
    }
    async Task PlayLive(string stage,string label,bool resume=true)
    {
        if(!HasAudio())return;
        int launch=liveLaunchVersion;
        try
        {
            ValidateLive(stage); var request=LiveRequest(stage);
            if(liveActive && liveSession is not null)
            {
                liveSession.Update(request);activeChannel=stage;playbackMode=label+" · Live";
                if(resume)liveSession.Resume();else liveSession.Pause();
                playing=resume; UpdateTransport(); return;
            }
            double position=cursorSeconds>=project.Duration-.02?0:cursorSeconds;
            Stop(false);launch=++liveLaunchVersion;
            if(liveSession is null)
            {
                var session=new LiveAudioSession(settings);liveSession=session;
                session.Failed+=error=>Dispatcher.BeginInvoke(()=>
                {
                    if(!ReferenceEquals(liveSession,session))return;
                    Stop(false);DisposeLive();AppendLog(L.T("Live preview lỗi: ")+error.Message);
                });
                session.Ended+=()=>Dispatcher.BeginInvoke(()=>
                {
                    if(!liveActive || !ReferenceEquals(liveSession,session))return;
                    playing=false;UpdatePosition(project.Duration);
                });
            }
            liveSession.Volume=settings.PlaybackVolume/100;liveSession.Repeat=settings.Repeat;
            liveActive=true;activePath="live://"+project.Source;activeChannel=stage;
            playbackMode=label+" · Live";playing=resume;UpdateTransport();
            AppendLog(L.T("Đang nạp bộ đệm nghe trực tiếp…"));
            await liveSession.StartAsync(request,position,resume);
            if(launch!=liveLaunchVersion || !liveActive)return;
            mediaReady=true;playing=liveSession.IsPlaying;
            AppendLog(L.T("Live RAM đã sẵn sàng · chỉnh DSP để nghe ngay. LUFS và định dạng file áp dụng khi xuất."));
            UpdateTransport();
        }
        catch(OperationCanceledException) { }
        catch(Exception error)
        {
            if(launch!=liveLaunchVersion)return;
            Stop(false);DisposeLive();AppendLog(L.T("Live preview lỗi: ")+error.Message);
        }
    }
    void UpdateLiveParameters()
    {
        if(!liveActive || liveSession is null)return;
        try { ValidateLive(activeChannel);liveSession.Update(LiveRequest(activeChannel)); }
        catch(Exception error){Stop(false);AppendLog(L.T("Live preview lỗi: ")+error.Message);}
    }
    void StopLive()
    {
        liveLaunchVersion++;liveActive=false;liveSession?.Stop();
    }
    void DisposeLive()
    {
        StopLive();var session=liveSession;liveSession=null;session?.Dispose();
    }
    async Task PreviewWaveformInMemory(string stage,Stem? single)
    {
        await WorkCore(async()=>
        {
            ValidateLive(stage);
            CaptureProject();
            var request=JsonSerializer.SerializeToElement(PreviewRequest.Create(project,stage,"",single,stage=="drumsep"?SelectedDrumParts():null),Settings.JsonOptions);
            var values=JsonSerializer.Deserialize<Dictionary<string,object>>(request.GetRawText())!;
            values["action"]="preview_waveform";
            var result=await Run(values);
            var waveform=JsonSerializer.Deserialize<double[]>(result.GetProperty("waveform").GetRawText())!;
            if(stage=="mix")SetComparisonWaveform(waveform,L.T("DSP trước mastering (RAM)"));
            if(stage=="drums")SetDrumComparison(waveform);
        },keepPlayback:true);
    }
}
