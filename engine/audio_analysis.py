"""Bounded-memory, local tempo/key estimates. No audio or models are uploaded.

Tempo: librosa dynamic-programming beat tracker per 30-second window.
Key: harmonic chroma correlated with Krumhansl-Kessler major/minor profiles.
Scores describe signal evidence, not calibrated probabilities of correctness.
"""
from i18n import tr
from pathlib import Path
import tempfile

import numpy as np
import soundfile as sf
from scipy import signal

MAJOR = np.array([6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88])
MINOR = np.array([6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17])
PITCHES = ("C", "C♯/D♭", "D", "D♯/E♭", "E", "F", "F♯/G♭", "G", "G♯/A♭", "A", "A♯/B♭", "B")


def key_from_chroma(chroma):
    chroma = np.asarray(chroma, dtype=float)
    if chroma.sum() <= 1e-10 or np.std(chroma) < 1e-10:
        return None, None, 0.0
    p = chroma / chroma.sum()
    # Flat spectra (noise) and isolated notes are not enough to infer a key.
    entropy = -np.sum(p*np.log(np.maximum(p, 1e-15))) / np.log(12)
    if entropy > .98 or np.count_nonzero(p > p.max()*.2) < 3 or p.max() > .65:
        return None, None, 0.0
    candidates = []
    for mode, profile in (("trưởng", MAJOR), ("thứ", MINOR)):
        for tonic in range(12):
            score = float(np.corrcoef(chroma, np.roll(profile, tonic))[0, 1])
            candidates.append((score, f"{PITCHES[tonic]} {mode}"))
    candidates.sort(reverse=True)
    first, second = candidates[:2]
    if first[0] < .5:
        return None, None, max(0.0, first[0])
    # Retain an alternative instead of claiming an unambiguous tonality.
    return first[1], second[1], first[0]


def analyze_wav(path, progress=lambda _: None):
    try:
        import librosa
    except ImportError as error:
        raise RuntimeError("Thiếu librosa cho dò BPM/tông. Chạy lại scripts/setup-engine.ps1 để cập nhật engine.") from error
    hop = 512
    chroma_sum = np.zeros(12)
    tempos = []
    analyzed = 0.0
    active = 0.0
    with sf.SoundFile(path) as audio:
        sr = audio.samplerate
        duration = len(audio)/sr
        if duration > 1800:
            raise ValueError("Phân tích hỗ trợ audio tối đa 30 phút.")
        while audio.tell() < len(audio):
            block = audio.read(int(sr*30), dtype="float32", always_2d=True)
            # Avoid opposite-polarity stereo cancelling the analysis signal.
            energy = np.mean(block**2, axis=0)
            y = block[:, int(np.argmax(energy))]
            seconds = len(y)/sr
            analyzed += seconds
            progress(f"Dò BPM / tông: {min(100, round(analyzed/max(duration, .001)*100))}%")
            if seconds < 2 or np.sqrt(np.mean(y*y)) < 1e-5:
                continue
            active += seconds
            spectrum = np.abs(librosa.stft(y, n_fft=4096, hop_length=hop))
            harmonic, _ = librosa.decompose.hpss(spectrum, margin=2)
            chroma = librosa.feature.chroma_stft(S=harmonic**2, sr=sr, n_fft=4096,
                                                 hop_length=hop, norm=None, tuning=None)
            chroma_sum += chroma.sum(axis=1)
            if seconds < 6:
                continue
            onset = librosa.onset.onset_strength(S=librosa.power_to_db(spectrum**2), sr=sr, hop_length=hop)
            # Discard window boundary onsets; don't interpret a steady tone as a beat.
            onset[:4] = 0
            onset[-4:] = 0
            peaks, _ = signal.find_peaks(onset, height=max(1e-5, float(onset.max())*.2), distance=3)
            if len(peaks) < 6 or float(np.max(onset)) < .1:
                continue
            tempo, beats = librosa.beat.beat_track(onset_envelope=onset, sr=sr, hop_length=hop, trim=False)
            bpm = float(np.asarray(tempo).reshape(-1)[0])
            if not 30 <= bpm <= 300 or len(beats) < 4:
                continue
            # Remove the positive baseline: stationary noise is not periodic evidence.
            centered = onset - np.mean(onset)
            correlation = signal.correlate(centered, centered, mode="full", method="fft")[len(onset)-1:]
            lag = int(round(60*sr/(bpm*hop)))
            start = max(1, lag-1)
            peak = start + int(np.argmax(correlation[start:lag+2]))
            strength = float(correlation[peak]/max(correlation[0], 1e-12))
            if strength >= .15:
                # Refine the frame-quantized lag (e.g. 117.5 for a 120 BPM click).
                left, center, right = correlation[peak-1:peak+2]
                curvature = left - 2*center + right
                offset = float(np.clip(.5*(left-right)/curvature, -.5, .5)) if curvature < -1e-12 else 0.0
                bpm = 60*sr/(hop*(peak+offset))
                if not 30 <= bpm <= 300:
                    continue
                tempos.append((bpm, strength, seconds))
    key, alternate, key_score = key_from_chroma(chroma_sum)
    bpm, tempo_score = None, 0.0
    notes = ["BPM có thể bằng nửa/gấp đôi nhịp bạn đếm. Tông là ước lượng toàn bài; có thể đổi tông hoặc nhầm trưởng/thứ tương đối."]
    if tempos:
        # Dominant tempo cluster: don't average unrelated sections into a false tempo.
        clusters = [(sum(strength*seconds for other, strength, seconds in tempos if abs(other-value)/value < .04), value)
                    for value, _, _ in tempos]
        _, center = max(clusters)
        group = [(v, w*t) for v, w, t in tempos if abs(v-center)/center < .04]
        bpm = round(sum(v*w for v, w in group)/sum(w for _, w in group), 1)
        tempo_score = sum(w for _, w in group)/max(active, 1)
        if sum(t for v, _, t in tempos if abs(v-center)/center < .04) < active*.65:
            notes.append("Nhịp giữa các đoạn chưa thống nhất; đang hiển thị cụm BPM nổi trội.")
    if bpm is None:
        notes.append("Chưa đủ xung nhịp lặp để xác định BPM.")
    if key is None:
        notes.append("Chưa đủ bằng chứng hòa âm để xác định tông trưởng/thứ.")
    return {"bpm": bpm, "key": key, "alternateKey": alternate, "tempoStrength": round(tempo_score, 3),
            "keyCorrelation": round(key_score, 3), "analyzedSeconds": round(analyzed, 2),
            "method": "librosa 0.11 · beat track / harmonic chroma + Krumhansl-Kessler", "notes": " ".join(notes)}


def analyze(req, run_ffmpeg, progress):
    # Decode at a fixed rate to disk, then analyze windows instead of loading 30 min of STFT.
    with tempfile.TemporaryDirectory(prefix="stemforge-analysis-") as folder:
        target = Path(folder)/"analysis.wav"
        progress(tr("Đọc audio để dò BPM và tông…"))
        run_ffmpeg(["-i", req["source"], "-vn", "-ac", "2", "-ar", "22050", "-c:a", "pcm_f32le", str(target)])
        from alternative_analysis import analyze_wav as alternative
        return alternative(target, progress)
