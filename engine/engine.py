"""Local audio engine. One JSON request on stdin, JSON events on stdout.

Refix filter topology follows SUNO-AI-refix-audio (Apache-2.0 headers).
Demucs normalization/inference follows StemDeck (Apache-2.0). See notices.
"""
from __future__ import annotations
from i18n import tr

import json
import math
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import uuid
import suno_download
import stereo_dsp
import lyrics
import model_pipeline
import drum_midi
import audio_analysis

import numpy as np
import soundfile as sf
from scipy import signal


def emit(kind, **data):
    print(json.dumps({"type": kind, **data}, ensure_ascii=True), flush=True)


def progress(message):
    emit("progress", message=message)


def ffmpeg():
    path = os.environ.get("STEMFORGE_FFMPEG") or shutil.which("ffmpeg")
    if not path or not Path(path).is_file():
        raise ValueError("Không tìm thấy FFmpeg. Chọn ffmpeg.exe trong Settings.")
    return path


def run_ffmpeg(args):
    result = subprocess.run([ffmpeg(), "-hide_banner", "-nostdin", "-y", *args],
                            capture_output=True, text=True, encoding="utf-8", errors="replace",
                            creationflags=0x08000000 if os.name == "nt" else 0)
    if result.returncode:
        raise ValueError("FFmpeg: " + result.stderr[-2500:])
    return result.stderr


def read_audio(path, sr=None):
    path = Path(path)
    if not path.is_file():
        raise ValueError(f"Không tìm thấy audio: {path}")
    # FFmpeg also handles compressed input consistently across soundfile builds.
    with tempfile.TemporaryDirectory(prefix="stemforge-decode-") as tmp:
        wav = Path(tmp) / "decoded.wav"
        run_ffmpeg(["-i", str(path), "-vn", "-ac", "2", *(["-ar", str(sr)] if sr else []),
                    "-c:a", "pcm_f32le", str(wav)])
        info = sf.info(wav)
        if info.duration > 1800:
            raise ValueError("Bản hiện tại hỗ trợ tối đa 30 phút mỗi bài. Hãy cắt đoạn trước khi nhập.")
        data, rate = sf.read(wav, dtype="float32", always_2d=True)
    if not len(data) or not np.isfinite(data).all():
        raise ValueError("Audio rỗng hoặc chứa giá trị không hợp lệ.")
    return data, rate


def write_float(path, data, sr):
    sf.write(path, data, sr, subtype="FLOAT")


def waveform(data, bins=700):
    mono = np.max(np.abs(data), axis=1)
    return [float(np.max(chunk)) for chunk in np.array_split(mono, min(bins, len(mono)))]


def file_waveform(path, bins=700):
    """Peak envelope of the delivered PCM, after mastering/resampling/encoding.

    Stream equal time bins to avoid another full-song allocation. Bin boundaries
    match waveform(), including short files and non-divisible frame counts.
    """
    with sf.SoundFile(path) as audio:
        count = min(bins, len(audio))
        if count <= 0:
            return []
        size, extra = divmod(len(audio), count)
        return [float(np.max(np.abs(audio.read(size + (i < extra), dtype="float32", always_2d=True))))
                for i in range(count)]


def download_suno(url, directory):
    return suno_download.download(url, directory, progress, validate_suno_media)["path"]


def validate_suno_media(path):
    # Check an audio stream actually decodes before accepting a downloaded video.
    run_ffmpeg(["-i", str(path), "-map", "0:a:0", "-t", "0.1", "-f", "null", "-"])


def import_audio(req):
    directory = Path(req["directory"])
    directory.mkdir(parents=True, exist_ok=True)
    downloaded = None
    source = req.get("source")
    if not source:
        downloaded = suno_download.download(req["url"], directory, progress, validate_suno_media)
        source = str(downloaded["path"])
    progress(tr("Đọc audio và tạo waveform…"))
    data, sr = read_audio(source)
    path = directory / "original.wav"
    write_float(path, data, sr)
    preview = directory / "original-preview.wav"
    # Media Foundation playback uses PCM; keep float master untouched.
    run_ffmpeg(["-i", str(path), "-c:a", "pcm_s16le", str(preview)])
    return {"source": str(path), "preview": str(preview), "name": downloaded["name"] if downloaded else Path(source).stem,
            "sourceUrl": downloaded["sourceUrl"] if downloaded else "", "downloadKind": downloaded["downloadKind"] if downloaded else "",
            "sampleRate": sr, "duration": len(data) / sr, "waveform": waveform(data)}


def biquad(data, sr, frequency, gain, q=1.0, shelf=False):
    if abs(gain) < 1e-8:
        return data
    w = 2 * np.pi * min(frequency, sr * .45) / sr
    a = 10 ** (gain / 40)
    c, s = np.cos(w), np.sin(w)
    alpha = s / (2 * q)
    if shelf:
        alpha = s / 2 * np.sqrt(2)
        t = 2 * np.sqrt(a) * alpha
        b = a * np.array([(a+1)+(a-1)*c+t, -2*((a-1)+(a+1)*c), (a+1)+(a-1)*c-t])
        den = np.array([(a+1)-(a-1)*c+t, 2*((a-1)-(a+1)*c), (a+1)-(a-1)*c-t])
    else:
        b = np.array([1+alpha*a, -2*c, 1-alpha*a])
        den = np.array([1+alpha/a, -2*c, 1-alpha/a])
    return signal.lfilter(b / den[0], den / den[0], data, axis=0).astype(np.float32)


def envelope(data, sr, ms):
    coeff = np.exp(-1 / (sr * ms / 1000))
    return signal.lfilter([1-coeff], [1, -coeff], np.max(np.abs(data), axis=1))


def compress(data, sr, threshold=-20, ratio=3, ms=12):
    env = envelope(data, sr, ms)
    db = 20*np.log10(np.maximum(env, 1e-8))
    reduction = np.maximum(db-threshold, 0) * (1-1/ratio)
    coeff = np.exp(-1 / (sr * .04))
    gain = signal.lfilter([1-coeff], [1, -coeff], 10**(-reduction/20), zi=[coeff])[0]
    return (data * gain[:, None]).astype(np.float32)


def refix(data, sr, params):
    hp = float(params.get("highPass", 32))
    data = signal.sosfilt(signal.butter(2, hp, "highpass", fs=sr, output="sos"), data, axis=0).astype(np.float32)
    # Frequencies and Q mirror the referenced Web Audio signal chain.
    for freq, gain, q in [(80, -1.5, 1), (180, params.get("mud", -3), 1.2),
                           (320, params.get("mud", -3)*.5, 1.4),
                           (3200, params.get("presence", 2), 1.1)]:
        data = biquad(data, sr, freq, float(gain), q)
    data = biquad(data, sr, 8500, float(params.get("air", 1.5)), shelf=True)
    mid = data.mean(axis=1)
    side = (data[:, 0]-data[:, 1])*.5*float(params.get("width", 1.0))
    data = np.stack([mid+side, mid-side], axis=1)
    return compress(data, sr, -18, 2, 25)


def split_bands(data, sr):
    # Complementary bands reconstruct the input exactly, with no crossover gain bump.
    low = signal.sosfilt(signal.butter(2, 180, fs=sr, output="sos"), data, axis=0).astype(np.float32)
    below_high = signal.sosfilt(signal.butter(2, min(4000, sr*.4), fs=sr, output="sos"), data, axis=0).astype(np.float32)
    return [low, below_high-low, data-below_high]


def drum_fx(data, sr, params):
    transient = float(params.get("transient", 0)) / 100
    if transient:
        fast, slow = envelope(data, sr, 2), envelope(data, sr, 35)
        attack = np.clip((fast-slow) / np.maximum(slow, .005), 0, 2)
        data = data * np.maximum(0, 1 + transient*attack[:, None])
    amount = float(params.get("multiband", 0)) / 100
    if amount:
        wet = sum(compress(b, sr, -24, 3, 10) for b in split_bands(data, sr))
        data = data*(1-amount) + wet*amount
    parallel = float(params.get("parallel", 0)) / 100
    if parallel:
        wet = compress(data, sr, -28, 8, 3) * 10**(6/20)
        data = data*(1-parallel) + wet*parallel
    return data.astype(np.float32)


def synthesized_sample(kind, sr, kit):
    rng = np.random.default_rng(42)
    duration = {"kick": .6, "snare": .32, "hat": .13}[kind]
    t = np.arange(int(sr*duration))/sr
    if kind == "kick":
        f0 = 46 if kit == "Electronic" else 58
        phase = 2*np.pi*(f0*t + 100*.025*(1-np.exp(-t/.025)))
        sound = np.sin(phase)*np.exp(-t/ .14) + rng.normal(0, .09, len(t))*np.exp(-t/.006)
    elif kind == "snare":
        noise = rng.normal(0, 1, len(t))
        noise = signal.sosfilt(signal.butter(2, 900, "highpass", fs=sr, output="sos"), noise)
        sound = .5*noise*np.exp(-t/.065)+.35*np.sin(2*np.pi*185*t)*np.exp(-t/.045)
    else:
        noise = rng.normal(0, 1, len(t))
        sound = signal.sosfilt(signal.butter(2, min(6500, sr*.4), "highpass", fs=sr, output="sos"), noise)*np.exp(-t/.025)
    sound /= max(np.max(np.abs(sound)), 1e-8)
    sound[:min(48, len(sound))] *= np.linspace(0, 1, min(48, len(sound)))
    return np.repeat((sound*.8)[:, None], 2, axis=1).astype(np.float32)


def trigger_parameter(params, key, default, low, high):
    value = float(params.get(key, default))
    if not np.isfinite(value) or not low <= value <= high:
        raise ValueError(f"Thông số trigger {key} phải trong khoảng {low}–{high}.")
    return value


def detect_drum_hits(bands, sr, params, names=("kick", "snare", "hat")):
    """Offline onset gate; independent DSP, not the proprietary Trigger 2 algorithm.

    Absolute envelope gate rejects background noise. Relative onset threshold finds
    attacks, refractory time avoids double triggers, and cross-band rejection can
    reduce spill. Cross-band rejection is heuristic and may reject simultaneous hits.
    """
    sensitivity = trigger_parameter(params, "sensitivity", 50, 0, 100)/100
    threshold = 10**(trigger_parameter(params, "thresholdDb", -48, -80, 0)/20)
    retrigger = trigger_parameter(params, "retriggerMs", 80, 20, 250)/1000
    suppression = trigger_parameter(params, "bleed", 0, 0, 100)/100
    dynamics = trigger_parameter(params, "dynamics", 100, 0, 100)/100
    envelopes = [envelope(band, sr, 2) for band in bands]
    lag = max(1, int(sr*.012))
    onsets = []
    for env in envelopes:
        previous = np.zeros_like(env)
        previous[lag:] = env[:-lag]
        onsets.append(np.maximum(env-previous, 0))
    hits = {}
    for index, name in enumerate(names):
        onset, env = onsets[index], envelopes[index]
        maximum = float(onset.max(initial=0))
        if maximum < 1e-8:
            hits[name] = []
            continue
        # Padding permits a transient at either file boundary to be detected.
        candidates, _ = signal.find_peaks(np.pad(onset, (1, 1)),
            height=max(1e-8, maximum*(.45-.43*sensitivity)),
            prominence=max(1e-8, maximum*(.07-.05*sensitivity)))
        candidates = candidates-1
        accepted = []
        for pos in candidates:
            if env[pos] < threshold:
                continue
            other = max((onsets[i][pos] for i in range(len(bands)) if i != index), default=0)
            if onset[pos] < other*suppression:
                continue
            # Follow the leading edge back, rather than placing the sample at the
            # envelope peak. No circular roll: audio at the end cannot affect start.
            left = max(0, pos-lag)
            below = np.flatnonzero(onset[left:pos] <= onset[pos]*.1)
            start = left+int(below[-1]) if len(below) else left
            if left == 0 and np.any(bands[index][0]):
                start = 0
            accepted.append((start, float(env[pos])))
        # Keep the strongest attack within the refractory interval, then restore
        # chronological order for deterministic round-robin sample selection.
        spacing = max(1, int(sr*retrigger))
        selected = []
        occupied = np.zeros(len(onset), dtype=bool)
        for start, strength in sorted(accepted, key=lambda hit: -hit[1]):
            if not occupied[start]:
                selected.append((start, strength))
                occupied[max(0, start-spacing+1):start+spacing] = True
        reference = max((strength for _, strength in selected), default=1)
        hits[name] = [(start, float((1-dynamics)+dynamics*np.clip(strength/reference, .02, 1)))
                      for start, strength in sorted(selected)]
    return hits


def load_trigger_samples(name, sr, params):
    paths = params.get(name+"Samples", [])
    if not isinstance(paths, list) or len(paths) > 8 or any(not isinstance(p, str) or not p for p in paths):
        raise ValueError(f"{name}: chọn tối đa 8 file sample.")
    # Keep projects from v0.1/v0.2 readable.
    if not paths and params.get(name):
        paths = [params[name]]
    if not paths:
        return [synthesized_sample(name, sr, params.get("kit", "Studio"))] if name in ("kick", "snare", "hat") else []
    samples = []
    for path in paths:
        sample, _ = read_audio(path, sr)
        if not 0 < len(sample) <= sr*10:
            raise ValueError(f"Sample {name} phải dài từ 1 frame đến 10 giây.")
        samples.append(sample)
    return samples


def read_drum_parts(data, sr, params, source):
    parts = params.get("parts", [])
    if not source or not params.get("partsSource") or Path(source).resolve() != Path(params["partsSource"]).resolve():
        raise ValueError("Bộ trống không thuộc stem drums hiện tại. Hãy tách lại DrumSep.")
    names = [p["name"] for p in parts]
    if len(names) != 6 or set(names) != set(drum_midi.GM):
        raise ValueError("DrumSep cần đủ Kick, Snare, Toms, Hi-hat, Ride và Crash.")
    result = {}
    for part in parts:
        audio, _ = read_audio(part["path"], sr)
        if abs(len(audio)-len(data)) > 2 or not np.isfinite(audio).all():
            raise ValueError("Rãnh DrumSep không khớp độ dài của stem drums.")
        result[part["name"]] = audio[:len(data)] if len(audio) >= len(data) else np.pad(audio, ((0,len(data)-len(audio)),(0,0)))
    return result


def trigger_events(data, sr, params, source=None, require_parts=False):
    parts = params.get("parts", [])
    if not parts:
        if require_parts:
            raise ValueError("Tách bộ trống bằng DrumSep trước khi xuất MIDI nhận diện kit.")
        return detect_drum_hits(split_bands(data, sr), sr, params)
    analysis_sr = 44100  # Model's native rate, shared by MIDI and every export rate.
    expected_length = round(len(data)*analysis_sr/sr)
    lanes = read_drum_parts(np.empty((expected_length, 2), np.float32), analysis_sr, params, source)
    # Independent isolated lanes must retain simultaneous kick/snare/cymbal hits.
    hits = detect_drum_hits(list(lanes.values()), analysis_sr, {**params, "bleed": 0}, list(lanes))
    muted = {p["name"] for p in parts if params.get("mixParts") and p.get("mute", False)}
    return {name: [(min(len(data)-1, round(pos*sr/analysis_sr)), velocity) for pos, velocity in lane]
            for name, lane in hits.items() if name not in muted}


def replace_drums(data, sr, params, source=None, hits=None, sample_banks=None):
    blend = trigger_parameter(params, "replacement", 0, 0, 100)/100
    if blend == 0:
        return data, {}
    mode = params.get("sampleMode", "roundRobin")
    if mode not in ("roundRobin", "velocity"):
        raise ValueError("Chế độ sample trigger không hợp lệ.")
    blend_mode = params.get("blendMode", "replace")
    if blend_mode not in ("layer", "replace"):
        raise ValueError("Chế độ chồng/thay sample không hợp lệ.")
    if hits is None:
        hits = trigger_events(data, sr, params, source)
    # A fully wet mix never leaks the original band when a gate finds no hits.
    result = data.copy() if blend_mode == "layer" else data*(1-blend)
    counts = {}
    for name, events in hits.items():
        samples = sample_banks[name] if sample_banks is not None else load_trigger_samples(name, sr, params)
        if not samples:
            continue
        for number, (start, velocity) in enumerate(events):
            index = number % len(samples) if mode == "roundRobin" else min(len(samples)-1, int(velocity*len(samples)))
            sample = samples[index]
            end = min(len(data), start+len(sample))
            result[start:end] += sample[:end-start]*velocity*blend
        counts[name] = len(events)
    return result.astype(np.float32), counts


def process_drums(data, sr, params, source):
    if not params.get("mixParts", False):
        audio, counts = replace_drums(data, sr, params, source)
        return drum_fx(audio, sr, params), counts
    lanes = read_drum_parts(data, sr, params, source)
    selected = [p for p in params["parts"] if not p.get("mute", False)]
    # Reconstruct from isolated lanes only. Never add the parent drums or original.
    mixed = np.zeros_like(data)
    hits = trigger_events(data, sr, params, source) if selected and float(params.get("replacement", 0)) != 0 else {}
    counts = {}
    for part in selected:
        gain = float(part.get("gain", 0))
        if not math.isfinite(gain) or not -48 <= gain <= 12:
            raise ValueError("Gain từng loại trống phải từ -48 đến +12 dB.")
        name = part["name"]
        bank = load_trigger_samples(name, sr, params) if float(params.get("replacement", 0)) != 0 else []
        if bank:
            audio, lane_counts = replace_drums(lanes[name], sr, params, source,
                hits={name: hits.get(name, [])}, sample_banks={name: bank})
        else:
            # No replacement sample for this instrument: keep its selected dry lane.
            audio, lane_counts = lanes[name], {}
        mixed += audio * 10**(gain/20)
        counts.update(lane_counts)
    return drum_fx(mixed, sr, params), counts


def separate(req):
    if req.get("model") in ("bs_roformer_sw", "drumsep_mdx23c"):
        result = model_pipeline.separate(req, read_audio, write_float, progress)
        for stem in result["stems"]:
            if stem["name"] == "drums" or req.get("model") == "drumsep_mdx23c":
                stem["waveform"] = file_waveform(stem["path"])
        return result
    progress(tr("Đang nạp Demucs; lần đầu có thể tải model…"))
    import torch
    from demucs.pretrained import get_model
    from demucs.apply import apply_model
    model_name = req.get("model", "htdemucs")
    if model_name not in ("htdemucs", "htdemucs_6s"):
        raise ValueError("Model không được hỗ trợ.")
    device = req.get("device", "auto")
    device = ("cuda" if torch.cuda.is_available() else "cpu") if device == "auto" else device
    if device == "cuda" and not torch.cuda.is_available():
        raise ValueError("CUDA chưa sẵn sàng. Chọn CPU hoặc Auto.")
    model = get_model(model_name)
    model.eval()
    data, sr = read_audio(req["source"], model.samplerate)
    wav = torch.from_numpy(data.T.copy())
    ref = wav.mean(0)
    mean, std = ref.mean(), ref.std()
    progress(tr("Tách {0} stems trên {1}…", len(model.sources), device.upper()))
    if std < 1e-8:
        sources = torch.zeros((len(model.sources), 2, len(data)))
    else:
        with torch.no_grad():
            sources = apply_model(model, ((wav-mean)/std)[None], device=device,
                                  shifts=1, split=True, overlap=.25, progress=True,
                                  num_workers=0)[0].cpu()*std+mean
    directory = Path(req["directory"]) / ("stems-"+uuid.uuid4().hex[:8])
    directory.mkdir(parents=True, exist_ok=True)
    stems = []
    for name, audio in zip(model.sources, sources):
        path = directory / f"{name}.wav"
        write_float(path, audio.T.numpy(), sr)
        stems.append({"name": name, "path": str(path), "gain": 0, "mute": False, "solo": False})
        if name == "drums":
            stems[-1]["waveform"] = waveform(audio.T.numpy())
    return {"stems": stems, "device": device, "model": model_name}


def mix_audio(req):
    """Shared pre-master DSP for offline export and the RAM preview worker."""
    data, sr = read_audio(req["source"])
    stems = req.get("stems", [])
    counts = {}
    mode = req.get("processingMode", "legacy")
    stereo_info = {}
    if mode in ("structural", "impact"):
        progress("PA1: transient / tonal + Mid/Side…" if mode == "structural" else "PA2: impact + sub resynthesis…")
        data, stereo_info = stereo_dsp.process(data, sr, req.get("stereo", {}), mode, progress)
    elif mode != "legacy":
        raise ValueError("Chế độ xử lý không được hỗ trợ.")
    elif stems:
        data = np.zeros_like(data)
        any_solo = any(s.get("solo", False) for s in stems)
        for stem in stems:
            if stem.get("mute") or (any_solo and not stem.get("solo")):
                continue
            audio, _ = read_audio(stem["path"], sr)
            if stem["name"] == "drums":
                audio, counts = process_drums(audio, sr, req.get("drums", {}), stem["path"])
            n = min(len(data), len(audio))
            data[:n] += audio[:n]*10**(float(stem.get("gain", 0))/20)
    elif any(float(req.get("drums", {}).get(k, 0)) != 0 for k in ("transient", "multiband", "parallel", "replacement")):
        raise ValueError("Tách stems trước khi xử lý riêng stem trống.")
    if mode == "legacy" and req.get("refix", {}).get("enabled", False):
        progress(tr("Refix: lọc sub, gọt low-mid, presence, air và stereo…"))
        data = refix(data, sr, req["refix"])
    if not np.isfinite(data).all():
        raise ValueError("DSP tạo dữ liệu không hợp lệ; hãy giảm thông số xử lý.")
    return data, sr, counts, stereo_info


def preview_waveform(req):
    # Use the same PCM reader as live playback: no decoded/preview WAV files,
    # and no allocation containing the entire song or all its stems.
    from live_preview import LiveMixer, RATE
    mixer = LiveMixer()
    info = sf.info(req['source'])
    total = round(info.frames * RATE / info.samplerate)
    if total <= 0:
        raise ValueError("Audio rỗng hoặc chứa giá trị không hợp lệ.")
    bins = min(700, total)
    size, extra = divmod(total, bins)
    lengths = np.full(bins, size, dtype=np.int64)
    lengths[:extra] += 1
    edges = np.concatenate(([0], np.cumsum(lengths)))
    peaks = np.zeros(bins, dtype=np.float32)
    for start in range(0, total, RATE*4):
        data, _ = mixer.process(req, start, min(RATE*4, total-start))
        mono = np.max(np.abs(data), axis=1)
        end = start+len(mono)
        first = max(0, int(np.searchsorted(edges, start, side='right'))-1)
        for index in range(first, bins):
            if edges[index] >= end:
                break
            left, right = max(start, edges[index])-start, min(end, edges[index+1])-start
            peaks[index] = max(peaks[index], np.max(mono[left:right], initial=0))
        progress(f"Waveform RAM: {end/total:.0%}")
    return {"waveform": peaks.tolist(), "sampleRate": RATE}


def render(req):
    output = Path(req["output"])
    inputs = [req["source"], *[s["path"] for s in req.get("stems", [])],
              *[s["path"] for s in req.get("drums", {}).get("parts", [])]]
    if any(os.path.normcase(str(output.resolve())) == os.path.normcase(str(Path(p).resolve())) for p in inputs):
        raise ValueError("File xuất phải khác source và stems gốc.")
    progress(tr("Đang dựng bản mix…"))
    data, sr, counts, stereo_info = mix_audio(req)
    output.parent.mkdir(parents=True, exist_ok=True)
    rate = int(req.get("sampleRate", sr))
    if rate not in (44100, 48000, 88200, 96000, 176400, 192000):
        raise ValueError("Sample rate không hợp lệ.")
    bits = str(req.get("bits", "24"))
    if output.suffix.lower() not in (".wav", ".flac") or bits not in ("16", "24", "32"):
        raise ValueError("Chọn WAV 16/24/32-bit float hoặc FLAC 16/24-bit.")
    if output.suffix.lower() == ".flac" and bits == "32":
        raise ValueError("FLAC hỗ trợ 16/24-bit trong ứng dụng này. Chọn WAV cho 32-bit float.")
    with tempfile.TemporaryDirectory(prefix="stemforge-render-", dir=output.parent) as tmp:
        raw = Path(tmp)/"mix.wav"
        write_float(raw, data, sr)
        af = []
        if req.get("normalize", True) and np.max(np.abs(data)) > 1e-8:
            target = float(req.get("lufs", -14))
            progress(tr("Đo loudness / true peak (lượt 1/2)…"))
            log = run_ffmpeg(["-i", str(raw), "-af", f"loudnorm=I={target}:TP=-1:LRA=11:print_format=json", "-f", "null", "-"])
            measurement = json.loads(log[log.rfind('{'):log.rfind('}')+1])
            if all(math.isfinite(float(measurement[k])) for k in ["input_i", "input_tp", "input_lra", "input_thresh", "target_offset"]):
                af.append(f"loudnorm=I={target}:TP=-1:LRA=11:measured_I={measurement['input_i']}:measured_TP={measurement['input_tp']}:measured_LRA={measurement['input_lra']}:measured_thresh={measurement['input_thresh']}:offset={measurement['target_offset']}:linear=true")
        # Oversampled peak check/attenuation also protects exports with loudnorm disabled.
        peak = oversampled_peak(data)
        if not af and peak > 10**(-1/20):
            af.append(f"volume={10**(-1/20)/peak:.10f}")
        progress(tr("Xuất lossless (lượt 2/2)…"))
        af.append(f"aresample={rate}:dither_method=triangular")
        if output.suffix.lower() == ".flac":
            codec = ["-c:a", "flac", "-sample_fmt", "s16" if bits == "16" else "s32", "-bits_per_raw_sample", bits, "-compression_level", "8"]
        else:
            codec = ["-c:a", {"16": "pcm_s16le", "24": "pcm_s24le", "32": "pcm_f32le"}[bits]]
        staged = Path(tmp)/("final"+output.suffix)
        run_ffmpeg(["-i", str(raw), "-af", ",".join(af), "-ar", str(rate), *codec, str(staged)])
        # Resampling/quantization can overshoot a limiter upstream. Measure the
        # actual deliverable, then attenuate and verify before replacing output.
        progress(tr("Xác minh loudness / true peak của file xuất…"))
        stats = measure_loudness(staged)
        if stats["truePeakDbtp"] is not None and stats["truePeakDbtp"] > -1.0:
            gain_db = -1.05-stats["truePeakDbtp"]
            guarded = Path(tmp)/("guarded"+output.suffix)
            run_ffmpeg(["-i", str(staged), "-af", f"volume={gain_db:.5f}dB,aresample={rate}:dither_method=triangular",
                        "-ar", str(rate), *codec, str(guarded)])
            stats = measure_loudness(guarded)
            if stats["truePeakDbtp"] is not None and stats["truePeakDbtp"] > -.99:
                raise ValueError("Kiểm tra true peak chưa đạt; file đích cũ được giữ nguyên.")
            staged = guarded
        final_waveform = file_waveform(staged)
        os.replace(staged, output)
    return {"output": str(output), "hits": counts, "stereo": stereo_info, "sampleRate": rate, "bits": bits,
            "waveform": final_waveform, **stats}


def measure_loudness(path):
    log = run_ffmpeg(["-i", str(path), "-af", "loudnorm=I=-14:TP=-1:print_format=json", "-f", "null", "-"])
    measured = json.loads(log[log.rfind('{'):log.rfind('}')+1])
    result = {}
    for source, target in [("input_i", "integratedLufs"), ("input_tp", "truePeakDbtp")]:
        value = float(measured[source])
        result[target] = value if math.isfinite(value) else None
    return result


def oversampled_peak(data):
    # Overlap avoids block-edge interpolation artifacts; bounded temporary memory.
    peak = 0.0
    block, margin = 262144, 64
    for start in range(0, len(data), block):
        first, last = max(0, start-margin), min(len(data), start+block+margin)
        up = signal.resample_poly(data[first:last], 4, 1, axis=0)
        begin = (start-first)*4
        end = (min(len(data), start+block)-first)*4
        peak = max(peak, float(np.max(np.abs(up[begin:end]))))
    return peak


def diagnostics(req):
    import importlib.metadata
    versions = {}
    for name in ("numpy", "scipy", "soundfile", "librosa", "torch", "demucs", "audio-separator"):
        try:
            versions[name] = importlib.metadata.version(name)
        except importlib.metadata.PackageNotFoundError:
            versions[name] = "MISSING"
    return {"python": sys.executable, "ffmpeg": ffmpeg(), "versions": versions}


def main():
    try:
        req = json.loads(sys.stdin.readline())
        import suno_lyrics
        handler = {"import": import_audio, "separate": separate, "render": render, "preview_waveform": preview_waveform, "diagnostics": diagnostics,
                   "suno_lyrics": lambda r: suno_lyrics.fetch(r, progress),
                   "analyze": lambda r: audio_analysis.analyze(r, run_ffmpeg, progress),
                   "waveform": lambda r: {"waveform": file_waveform(r["source"])},
                   "install_separator": lambda r: model_pipeline.install(r, progress),
                   "drum_midi": lambda r: drum_midi.export(r, read_audio, trigger_events),
                   "lyrics_info": lyrics.info, "install_whisper": lambda r: lyrics.install(r, progress),
                   "lyrics_preview": lambda r: lyrics.prepare_preview(r, run_ffmpeg),
                   "lyrics": lambda r: lyrics.transcribe(r, run_ffmpeg, progress)}[req["action"]]
        result = handler(req)
        emit("result", **result)
    except Exception as exc:
        emit("error", message=str(exc))
        sys.exit(1)


if __name__ == "__main__":
    main()
