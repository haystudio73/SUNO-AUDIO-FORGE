"""Persistent, pull-driven PCM preview. Paged float32 RAM cache; never writes audio.

Shared pre-master DSP is evaluated with warm-up/look-ahead context. This is an
audition, not offline loudness mastering or bit-identical full-track inference.
"""
import base64
from collections import OrderedDict
import contextlib
import json
import math
from pathlib import Path
import sys
import time
import numpy as np
import soundfile as sf
from scipy import signal
import engine as dsp

RATE = 48000
PAGE_SECONDS = 8


class PcmCache:
    def __init__(self, limit=256 * 1024 * 1024):
        self.limit, self.bytes, self.reads = limit, 0, 0
        self.pages = OrderedDict()

    def read(self, path, start, end, rate=RATE):
        path = Path(path).resolve()
        stamp = path.stat()
        info = sf.info(path)
        if info.channels not in (1, 2) or info.duration > 1800:
            raise ValueError("Live preview requires mono/stereo audio up to 30 minutes.")
        length = round(info.frames * rate / info.samplerate)
        start, end = max(0, start), min(end, length)
        if end <= start:
            return np.empty((0, 2), np.float32)
        page_frames = PAGE_SECONDS * rate
        chunks = []
        for index in range(start // page_frames, (end - 1) // page_frames + 1):
            key = (str(path), stamp.st_size, stamp.st_mtime_ns, rate, index)
            if key not in self.pages:
                # Page boundaries are whole seconds, hence aligned at both rates.
                native_start = index * PAGE_SECONDS * info.samplerate
                guard = 0 if rate == info.samplerate else info.samplerate // math.gcd(rate, info.samplerate) * 64
                left = max(0, native_start - guard)
                right = min(info.frames, native_start + PAGE_SECONDS * info.samplerate + guard)
                with sf.SoundFile(path) as stream:
                    stream.seek(left)
                    audio = stream.read(right-left, dtype="float32", always_2d=True)
                self.reads += 1
                if audio.shape[1] == 1:
                    audio = np.repeat(audio, 2, axis=1)
                if rate != info.samplerate:
                    gcd = math.gcd(rate, info.samplerate)
                    audio = signal.resample_poly(audio, rate//gcd, info.samplerate//gcd, axis=0)
                offset = round((native_start-left)*rate/info.samplerate)
                audio = np.ascontiguousarray(audio[offset:offset+min(page_frames, length-index*page_frames)], dtype=np.float32)
                if not np.isfinite(audio).all():
                    raise ValueError("Live input contains invalid samples.")
                while self.pages and self.bytes + audio.nbytes > self.limit:
                    _, old = self.pages.popitem(last=False); self.bytes -= old.nbytes
                self.pages[key] = audio; self.bytes += audio.nbytes
            audio = self.pages.pop(key); self.pages[key] = audio
            chunks.append(audio[max(0,start-index*page_frames):min(len(audio),end-index*page_frames)])
        return np.concatenate(chunks, axis=0)


class LiveMixer:
    def __init__(self):
        self.cache = PcmCache()
        self.previous = None
        self.last_end = -1

    def process(self, mix, start, count):
        info = sf.info(mix['source'])
        total = round(info.frames * RATE / info.samplerate)
        end = min(total, start+count)
        if start >= total:
            return np.empty((0,2), np.float32), total
        # Anchor STFT frame phase to the track timeline, not to each callback.
        # Structural EQ needs 149 ms of STFT/median context at 48 kHz.
        # Avoid repeatedly processing a full second for that stateless branch;
        # retain longer warm-up for dynamics and sub/trigger tails.
        structural = mix.get('processingMode') == 'structural'
        warmup = RATE//4 if structural else RATE
        lookahead = RATE//4 if structural else RATE//2
        left = max(0, (start-warmup)//512*512)
        right = min(total, end+lookahead)
        tracks = {str(Path(p).resolve()) for p in [mix['source'], *[s['path'] for s in mix.get('stems',[])],
                  *[s['path'] for s in mix.get('drums',{}).get('parts',[])]]}

        def reader(path, sr=None):
            sr = sr or RATE
            if str(Path(path).resolve()) in tracks:
                return self.cache.read(path, round(left*sr/RATE), round(right*sr/RATE), sr), sr
            # User samples are bounded by the existing ten-second sample validator.
            sample_info = sf.info(path)
            if sample_info.duration > 10:
                raise ValueError("Live trigger samples must be at most 10 seconds.")
            return self.cache.read(path, 0, round(sample_info.duration*sr), sr), sr

        if mix.get('silence'):
            return np.zeros((end-start,2),np.float32), total
        saved = dsp.read_audio
        try:
            dsp.read_audio = reader
            # No stdout diagnostics may enter the PCM protocol.
            with contextlib.redirect_stdout(sys.stderr):
                data, _, _, _ = dsp.mix_audio(mix)
            return data[start-left:end-left].copy(), total
        finally:
            dsp.read_audio = saved

    def pull(self, req):
        started = time.perf_counter()
        mix = req['mix']; start = int(req.get('frame',0)); count = int(req.get('frames',9600))
        if start < 0 or not 1 <= count <= RATE:
            raise ValueError("Invalid live PCM range.")
        audio, total = self.process(mix,start,count)
        signature = json.dumps(mix, sort_keys=True)
        if self.previous is not None and self.previous[0] != signature and start == self.last_end and len(audio):
            # Crossfade two DSP settings at the same musical position (20 ms).
            old, _ = self.process(self.previous[1],start,min(960,len(audio)))
            n = min(len(old),len(audio),960)
            fade = np.linspace(0,1,n,dtype=np.float32)[:,None]
            audio[:n] = old[:n]*(1-fade)+audio[:n]*fade
        self.previous = (signature,mix); self.last_end = start+len(audio)
        if not np.isfinite(audio).all():
            raise ValueError("Live DSP produced invalid samples.")
        peak = float(np.max(np.abs(audio),initial=0))
        return {'type':'pcm','frame':start,'frames':len(audio),'totalFrames':total,'sampleRate':RATE,
                'pcm':base64.b64encode(np.asarray(audio,dtype='<f4').tobytes()).decode('ascii'),
                'peak':peak,'cacheBytes':self.cache.bytes,'diskReads':self.cache.reads,
                'computeMs':(time.perf_counter()-started)*1000}


def main():
    dsp.progress = lambda _: None
    mixer = LiveMixer()
    for line in sys.stdin:
        try:
            if len(line) > 4_000_000:
                raise ValueError('Live request too large.')
            req = json.loads(line)
            if req.get('action') == 'close': break
            result = mixer.pull(req)
        except Exception as error:
            result = {'type':'error','message':str(error)}
        print(json.dumps(result,ensure_ascii=True,separators=(',',':')),flush=True)


if __name__ == '__main__':
    main()
