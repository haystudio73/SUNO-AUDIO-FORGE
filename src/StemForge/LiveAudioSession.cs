using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace StemForge;

/// <summary>Persistent DSP worker + bounded PCM queue. Export never reads this queue.</summary>
public sealed class LiveAudioSession : IDisposable
{
    const int Rate = 48000, ChunkFrames = 9600;
    readonly object gate = new();
    readonly Process worker;
    readonly CancellationTokenSource lifetime = new();
    readonly MonitorProvider provider = new();
    readonly WasapiOut output;
    readonly Task producer;
    string mix = "{}";
    long startFrame, nextFrame, totalFrames, loopsQueued;
    int generation, revision;
    bool active, wantPlay, outputStarted, ended, disposed;
    TaskCompletionSource? prepared;
    public event Action<Exception>? Failed;
    public event Action? Ended;
    public bool Repeat { get; set; }
    public double ComputeMilliseconds { get; private set; }
    public long CacheBytes { get; private set; }
    public int AppliedRevision { get; private set; }
    public int Revision { get { lock (gate) return revision; } }
    public long ProcessedBlocks { get; private set; }
    public int WorkerId => worker.Id;
    public bool IsPlaying { get { lock(gate) return active && wantPlay && outputStarted && !ended; } }
    public double BufferedSeconds => provider.BufferedSeconds;
    public double Volume { get => provider.Volume; set => provider.Volume = (float)Math.Clamp(value, 0, 1); }
    public double Position
    {
        get { lock(gate) {
            if(ended) return totalFrames/(double)Rate;
            long played = Math.Max(0, provider.FramesRead - (outputStarted ? Rate*60/1000 : 0));
            long frame = startFrame + played;
            if(loopsQueued > 0 && totalFrames > 0 && frame >= totalFrames) frame %= totalFrames;
            return frame/(double)Rate;
        } }
    }

    public LiveAudioSession(Settings settings)
    {
        output = new WasapiOut(AudioClientShareMode.Shared, true, 60);
        try { output.Init(provider); }
        catch { output.Dispose(); lifetime.Dispose(); throw; }
        output.PlaybackStopped += (_, e) => { if(e.Exception is not null && !disposed) Failed?.Invoke(e.Exception); };
        var info = new ProcessStartInfo(settings.Python) { UseShellExecute=false, CreateNoWindow=true,
            RedirectStandardInput=true, RedirectStandardOutput=true, RedirectStandardError=true,
            StandardInputEncoding=new UTF8Encoding(false), StandardOutputEncoding=Encoding.UTF8, StandardErrorEncoding=Encoding.UTF8 };
        info.ArgumentList.Add("-u"); info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory,"engine","live_preview.py"));
        info.Environment["PYTHONIOENCODING"]="utf-8";
        info.Environment["OMP_NUM_THREADS"]="2"; info.Environment["OPENBLAS_NUM_THREADS"]="2";
        worker = new Process { StartInfo=info };
        try { worker.Start(); } catch { output.Dispose(); worker.Dispose(); lifetime.Dispose(); throw; }
        // Drain diagnostics continuously; do not block a worker on a full stderr pipe.
        _ = Task.Run(async () => { try { while(await worker.StandardError.ReadLineAsync() is not null) { } } catch(ObjectDisposedException) { } catch(IOException) { } });
        producer = Task.Run(ProduceAsync);
    }

    public Task StartAsync(object request, double position, bool play)
    {
        lock(gate) {
            ObjectDisposedException.ThrowIf(disposed,this);
            Reset(position); mix=JsonSerializer.Serialize(request,Settings.JsonOptions); revision++;
            active=true; wantPlay=play; prepared=new(TaskCreationOptions.RunContinuationsAsynchronously);
            return prepared.Task;
        }
    }
    public void Update(object request)
    {
        string json=JsonSerializer.Serialize(request,Settings.JsonOptions);
        lock(gate) {
            if(disposed || json==mix) return;
            if(active && !wantPlay) Reset(Position);
            mix=json; revision++;
        }
    }
    void Reset(double position)
    {
        generation++; output.Stop(); provider.Clear(); outputStarted=false; ended=false; loopsQueued=0;
        startFrame=nextFrame=Math.Max(0,(long)(position*Rate));
    }
    public void Seek(double position) { lock(gate) { if(!disposed) Reset(position); } }
    public void Pause() { lock(gate) { if(disposed)return; wantPlay=false; output.Pause(); } }
    public void Resume()
    {
        lock(gate) { if(disposed)return; if(ended)Reset(0); active=true; wantPlay=true;
            if(provider.BufferedSeconds>0) { output.Play(); outputStarted=true; } }
    }
    public void Stop()
    {
        lock(gate) { if(disposed)return; active=false; wantPlay=false; prepared?.TrySetCanceled(); Reset(0); }
    }
    async Task ProduceAsync()
    {
        try
        {
            while(!lifetime.IsCancellationRequested)
            {
                int gen, rev; long frame; string snapshot;
                bool wait=false, finish=false;
                lock(gate)
                {
                    gen=generation; rev=revision; frame=nextFrame; snapshot=mix;
                    wait=!active || ended || provider.BufferedSeconds>=.22;
                    if(!wait && totalFrames>0 && frame>=totalFrames)
                    {
                        if(Repeat) { frame=nextFrame=0; loopsQueued++; }
                        else { finish=provider.BufferedSeconds==0; wait=true; }
                    }
                }
                if(finish)
                {
                    await Task.Delay(80,lifetime.Token);
                    bool notify=false;
                    lock(gate) { if(gen==generation && active && !ended && provider.BufferedSeconds==0) { ended=true; wantPlay=false; output.Pause(); prepared?.TrySetResult(); notify=true; } }
                    if(notify) Ended?.Invoke();
                }
                if(wait) { await Task.Delay(12,lifetime.Token); continue; }
                using var body=JsonDocument.Parse(snapshot);
                var command=JsonSerializer.Serialize(new {mix=body.RootElement,frame,frames=ChunkFrames});
                await worker.StandardInput.WriteLineAsync(command.AsMemory(),lifetime.Token);
                await worker.StandardInput.FlushAsync(lifetime.Token);
                string response=await worker.StandardOutput.ReadLineAsync(lifetime.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(45),lifetime.Token)
                    ?? throw new IOException(L.T("Live DSP đã dừng. Kiểm tra Python và các thư viện engine."));
                if(response.Length>2_000_000)throw new IOException("Invalid live PCM response size.");
                using var message=JsonDocument.Parse(response); var value=message.RootElement;
                if(value.GetProperty("type").GetString()=="error")throw new InvalidOperationException(value.GetProperty("message").GetString());
                var pcm=Convert.FromBase64String(value.GetProperty("pcm").GetString()!);
                int frames=value.GetProperty("frames").GetInt32();
                if(frames<0 || frames>ChunkFrames || pcm.Length!=frames*8 || value.GetProperty("frame").GetInt64()!=frame || value.GetProperty("sampleRate").GetInt32()!=Rate)
                    throw new IOException("Invalid live PCM frame.");
                lock(gate)
                {
                    if(gen!=generation || !active || disposed)continue;
                    totalFrames=value.GetProperty("totalFrames").GetInt64();
                    ComputeMilliseconds=value.GetProperty("computeMs").GetDouble(); CacheBytes=value.GetProperty("cacheBytes").GetInt64();
                    provider.Add(pcm); nextFrame=frame+frames; AppliedRevision=rev; ProcessedBlocks++;
                    if(wantPlay && !outputStarted && pcm.Length>0) { output.Play(); outputStarted=true; }
                    prepared?.TrySetResult();
                }
            }
        }
        catch(OperationCanceledException) when(lifetime.IsCancellationRequested) { }
        catch(Exception error)
        {
            lock(gate) { active=false; wantPlay=false; if(!disposed)output.Stop(); prepared?.TrySetException(error); }
            if(!disposed)Failed?.Invoke(error);
        }
    }
    public void Dispose()
    {
        lock(gate) { if(disposed)return; active=false; disposed=true; generation++; lifetime.Cancel(); prepared?.TrySetCanceled(); output.Stop(); }
        try { if(!worker.HasExited)worker.Kill(true); } catch(InvalidOperationException) { }
        // Do not dispose pipe objects until the producer exits its pending read.
        _ = producer.ContinueWith(_ => { worker.Dispose(); lifetime.Dispose(); },TaskScheduler.Default);
        output.Dispose();
    }

    sealed class MonitorProvider : IWaveProvider
    {
        readonly BufferedWaveProvider buffer=new(WaveFormat.CreateIeeeFloatWaveFormat(Rate,2))
            {BufferDuration=TimeSpan.FromSeconds(1),ReadFully=false,DiscardOnBufferOverflow=false};
        long framesRead;
        public float Volume=0.5f;
        public WaveFormat WaveFormat=>buffer.WaveFormat;
        public long FramesRead=>Interlocked.Read(ref framesRead);
        public double BufferedSeconds=>buffer.BufferedDuration.TotalSeconds;
        public void Add(byte[] pcm) { if(pcm.Length>0)buffer.AddSamples(pcm,0,pcm.Length); }
        public void Clear() { buffer.ClearBuffer(); Interlocked.Exchange(ref framesRead,0); }
        public int Read(byte[] destination,int offset,int count)
        {
            int read=buffer.Read(destination,offset,count); Interlocked.Add(ref framesRead,read/8);
            var samples=MemoryMarshal.Cast<byte,float>(destination.AsSpan(offset,read));float volume=Volatile.Read(ref Volume);
            for(int i=0;i<samples.Length;i++)samples[i]=Math.Clamp(samples[i]*volume,-1,1);
            if(read<count)Array.Clear(destination,offset+read,count-read);
            return count; // Silence during an underrun; never skip musical frames.
        }
    }
}
