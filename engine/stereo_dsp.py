"""Independent stereo transient/tonal processing; no proprietary plugin DSP.

Shared real spectral masks preserve interchannel bin phase. Centered STFT/ISTFT
and equal-context blocks compensate analysis latency offline. Time-varying masks
can still smear transients: this is not a guarantee of artefact-free separation.
"""
import numpy as np
from scipy import signal, ndimage


def number(params, key, default, low, high):
    value = float(params.get(key, default))
    if not np.isfinite(value) or not low <= value <= high:
        raise ValueError(f"Stereo DSP: {key} phải trong khoảng {low}–{high}.")
    return value


def bell(frequencies, center, octaves=.65):
    return np.exp(-.5*(np.log2(np.maximum(frequencies, 1)/center)/octaves)**2)


def transient_mask(spectrum):
    # One mask shared by Mid and Side: no independent L/R classification drift.
    magnitude = np.sqrt(np.mean(np.abs(spectrum)**2, axis=0))
    harmonic = ndimage.median_filter(magnitude, size=(1, 17), mode="nearest")
    percussive = ndimage.median_filter(magnitude, size=(9, 1), mode="nearest")
    mask = percussive**2/(percussive**2+harmonic**2+1e-16)
    return np.clip(mask, 0, 1).astype(np.float32)


def spectral_block(data, sr, params, mode, nfft):
    mid_side = np.stack([data.mean(axis=1), (data[:, 0]-data[:, 1])*.5])
    hop = nfft//4
    frequencies, _, spectrum = signal.stft(mid_side, fs=sr, nperseg=nfft,
        noverlap=nfft-hop, boundary="zeros", padded=True)
    mask = transient_mask(spectrum)
    if mode == "structural":
        transient_db = (params["attack80"]*bell(frequencies, 80)
                        + params["attack3200"]*bell(frequencies, 3200, .5))
        tonal_db = (params["mud180"]*bell(frequencies, 180, .45)
                    + params["mud320"]*bell(frequencies, 320, .45)
                    + params["harsh"]*bell(frequencies, np.sqrt(2000*4000), .5))
        gain = mask*10**(transient_db[:, None]/20)+(1-mask)*10**(tonal_db[:, None]/20)
    else:
        # Emphasize the percussive estimate, not a claimed isolated drum stem.
        gain = 1+mask**1.5*(10**(params["impactDb"]/20)-1)
    spectrum[0] *= gain
    spectrum[1] *= (1+(gain-1)*params["sideAmount"]/100)*params["width"]/100
    _, restored = signal.istft(spectrum, fs=sr, nperseg=nfft, noverlap=nfft-hop, boundary=True)
    restored = restored[:, :len(data)]
    return np.stack([restored[0]+restored[1], restored[0]-restored[1]], axis=1).astype(np.float32)


def parameters(params):
    specs = {
        "attack80": (2, 0, 9), "attack3200": (2, 0, 9),
        "mud180": (-2, -9, 0), "mud320": (-1.5, -9, 0), "harsh": (-2, -9, 0),
        "sideAmount": (50, 0, 100), "width": (100, 0, 160),
        "impactDb": (6, -18, 18), "subAmount": (20, 0, 100), "subHz": (45, 20, 60)
    }
    return {key: number(params, key, *spec) for key, spec in specs.items()}


def sub_resynthesis(data, sr, amount, frequency):
    if amount == 0:
        return data, 0
    mid = data.mean(axis=1)
    # IIR is used only for detection, never as a parallel filtered audio branch.
    detector = signal.sosfilt(signal.butter(2, [45, 180], btype="bandpass", fs=sr, output="sos"), mid)
    def envelope(ms):
        coefficient = np.exp(-1/(sr*ms/1000))
        return signal.lfilter([1-coefficient], [1, -coefficient], np.abs(detector))
    fast, slow = envelope(4), envelope(60)
    novelty = np.maximum(fast-slow, 0)
    maximum = float(novelty.max(initial=0))
    if maximum < .002:
        return data, 0
    peaks, _ = signal.find_peaks(novelty, height=max(.002, maximum*.22),
        prominence=max(.001, maximum*.12), distance=max(1, int(sr*.09)))
    out = data.copy()
    time = np.arange(int(sr*.32))/sr
    shape = (1-np.exp(-time/.006))*np.exp(-time/.09)
    sine, cosine = np.sin(2*np.pi*frequency*time), np.cos(2*np.pi*frequency*time)
    for peak in peaks:
        start = max(0, int(peak)-int(sr*.006))
        end = min(len(data), start+len(time))
        count = end-start
        # Fit phase against local Mid content; silence/absence of a useful
        # projection uses a deterministic zero crossing. This cannot guarantee
        # phase identity to an unknown original kick or recover missing content.
        reference = mid[start:end]*shape[:count]
        phase = np.arctan2(np.dot(reference, cosine[:count]), np.dot(reference, sine[:count]))
        amplitude = min(.35, float(fast[peak])*1.5)*amount/100
        sub = amplitude*(sine[:count]*np.cos(phase)+cosine[:count]*np.sin(phase))*shape[:count]
        out[start:end] += sub[:, None]
    return out.astype(np.float32), len(peaks)


def process(data, sr, params, mode, progress=lambda message: None, block_seconds=4):
    if mode not in ("structural", "impact"):
        raise ValueError("Chế độ Stereo DSP không hợp lệ.")
    values = parameters(params)
    if params.get("enabled", True) is False:
        return data.copy(), {"mode": mode, "bypassed": True, "subHits": 0}
    # Fixed time resolution across supported sample rates; overlapping context
    # makes results independent of block boundaries and bounds FFT memory.
    nfft = 2**int(np.ceil(np.log2(sr*.04)))
    hop = nfft//4
    context = nfft+hop*10
    block = max(nfft, int(sr*block_seconds)//hop*hop)
    out = np.empty_like(data)
    for start in range(0, len(data), block):
        end = min(len(data), start+block)
        left, right = max(0, start-context), min(len(data), end+context)
        segment = data[left:right]
        # scipy reduces nperseg for short clips unless explicitly padded.
        length = len(segment)
        if length < nfft:
            segment = np.pad(segment, ((0, nfft-length), (0, 0)))
        processed = spectral_block(segment, sr, values, mode, nfft)[:length]
        out[start:end] = processed[start-left:end-left]
        if start == 0 or end == len(data) or start % (block*8) == 0:
            progress(f"Stereo DSP: {end/len(data):.0%}")
    hits = 0
    if mode == "impact":
        out, hits = sub_resynthesis(out, sr, values["subAmount"], values["subHz"])
    if not np.isfinite(out).all():
        raise ValueError("Stereo DSP tạo dữ liệu không hợp lệ.")
    return out, {"mode": mode, "bypassed": False, "subHits": hits, "latencyCompensated": True}
