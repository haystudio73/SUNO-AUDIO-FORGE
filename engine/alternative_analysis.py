"""Bounded adapter for libraz/bpm-detector (MIT). Uncertain results are absent."""
from collections import defaultdict
import contextlib
import io
import math

import numpy as np
import soundfile as sf
from scipy import signal


def empty_result(reason="No reliable estimate."):
    return dict(bpm=None, key=None, alternateKey=None, tempoStrength=0.0,
                keyCorrelation=0.0, analyzedSeconds=0.0,
                method="libraz/bpm-detector · harmonic clustering / enhanced key profiles",
                notes=reason)


def analyze_wav(path, progress=lambda _: None):
    result = empty_result()
    try:
        import librosa
        from vendor.libraz_bpm.tempo import BPMDetector
        from vendor.libraz_bpm.key_detector import KeyDetector
    except Exception:
        result['notes'] = 'Analysis backend unavailable. Skipped; audio is unchanged.'
        return result
    tempos, keys = [], []
    with sf.SoundFile(path) as audio:
        sr = audio.samplerate
        duration = len(audio)/sr
        if duration < 6:
            result['notes'] = 'Audio is too short for reliable analysis. Skipped.'
            return result
        # Whole short tracks; three spread-out windows for long tracks. <= 90 seconds.
        starts = [0] if duration <= 30 else sorted(set(np.linspace(0, max(0, duration-30), min(3, math.ceil(duration/30)))))
        for index, start in enumerate(starts):
            audio.seek(int(start*sr))
            block = audio.read(int(sr*min(30, duration-start)), dtype='float32', always_2d=True)
            y = block[:, int(np.argmax(np.mean(block**2, axis=0)))]
            result['analyzedSeconds'] += len(y)/sr
            progress(f'BPM / key: {index+1}/{len(starts)}')
            if len(y) < sr*6 or not np.all(np.isfinite(y)) or float(np.sqrt(np.mean(y*y))) < 1e-5:
                continue
            try:
                # The upstream modules can print; never contaminate the JSON event stream.
                with contextlib.redirect_stdout(io.StringIO()):
                    spectrum = np.abs(librosa.stft(y, n_fft=4096, hop_length=256))
                    onset = librosa.onset.onset_strength(S=librosa.power_to_db(spectrum**2), sr=sr, hop_length=256)
                    onset[:4] = onset[-4:] = 0
                    peaks, _ = signal.find_peaks(onset, height=max(.1, float(onset.max())*.2), distance=3)
                    if len(peaks) >= 6:
                        bpm, confidence, _, _ = BPMDetector(sr, 128).detect(y, sr)
                        centered = onset-np.mean(onset)
                        corr = signal.correlate(centered, centered, mode='full', method='fft')[len(onset)-1:]
                        if np.isfinite(bpm) and 40 <= bpm <= 300 and confidence >= 40:
                            lag = int(round(60*sr/(bpm*256)))
                            if 2 <= lag < len(corr)-2:
                                peak = lag-1+int(np.argmax(corr[lag-1:lag+2]))
                                strength = float(corr[peak]/max(corr[0], 1e-12))
                                if strength >= .2:
                                    left, center, right = corr[peak-1:peak+2]
                                    curve = left-2*center+right
                                    offset = float(np.clip(.5*(left-right)/curve, -.5, .5)) if curve < -1e-12 else 0
                                    tempos.append((60*sr/(256*(peak+offset)), strength))
                    chroma = librosa.feature.chroma_stft(S=spectrum**2, sr=sr, n_fft=4096, hop_length=256, norm=None).sum(axis=1)
                    p = chroma/max(float(chroma.sum()), 1e-12)
                    entropy = -float(np.sum(p*np.log(np.maximum(p, 1e-15))))/np.log(12)
                    # Reject noise and isolated tones before the upstream key detector.
                    if entropy < .98 and np.count_nonzero(p > p.max()*.2) >= 3 and p.max() < .65:
                        key = KeyDetector(hop_length=512).detect_key(y, sr)
                        score = float(key.get('confidence', 0))/100
                        strength = float(key.get('key_strength', 0))
                        if (np.isfinite(score) and np.isfinite(strength) and score >= .5 and strength >= .5
                                and key.get('mode') in ('Major', 'Minor')
                                and not key.get('key', '').startswith('Unknown')
                                and 'fallback' not in key.get('analysis_notes', '').lower()):
                            keys.append((f"{key['key']} {key['mode']}", min(1., score)))
            except Exception:
                # One failed window must not prevent mixing/exporting or other windows.
                continue
    if tempos:
        center = max(tempos, key=lambda pair: sum(w for t,w in tempos if abs(t-pair[0])/pair[0]<.04))[0]
        group = [(t,w) for t,w in tempos if abs(t-center)/center<.04]
        if len(group) >= len(tempos)*.65:
            result['bpm'] = round(sum(t*w for t,w in group)/sum(w for _,w in group), 1)
            result['tempoStrength'] = round(sum(w for _,w in group)/len(group), 3)
    if keys:
        votes = defaultdict(float)
        for key, weight in keys: votes[key] += weight
        ordered = sorted(votes, key=votes.get, reverse=True)
        if votes[ordered[0]] >= sum(votes.values())*.65:
            result['key'] = ordered[0]
            result['keyCorrelation'] = round(votes[ordered[0]]/sum(k==ordered[0] for k,_ in keys), 3)
        if len(ordered)>1: result['alternateKey'] = ordered[1]
    result['analyzedSeconds'] = round(result['analyzedSeconds'], 2)
    result['notes'] = ('Estimates from up to 90 seconds. Tempo may be half/double; key may vary between sections. '
                       'Uncertain BPM/key values are skipped. Scores are not probabilities.')
    return result
