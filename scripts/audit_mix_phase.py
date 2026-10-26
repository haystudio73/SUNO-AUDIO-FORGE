"""Read-only DSP audit: cached Demucs residual and steady-state multiband phase.

Not a perceptual score; no model inference or changes to user projects.
"""
import json
import sys
from pathlib import Path
import numpy as np

root = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(root/"engine"))
import engine as e

fixture = json.loads((root/"artifacts/smoke/Synthetic test.stemforge.json").read_text(encoding="utf-8"))
x, sr = e.read_audio(fixture["source"], 44100)
summed = np.zeros_like(x)
for stem in fixture["stems"]:
    audio, _ = e.read_audio(stem["path"], sr)
    n = min(len(x), len(audio))
    summed[:n] += audio[:n]
rms = lambda a: float(np.sqrt(np.mean(a.astype(np.float64)**2)))
residual = summed-x
report = {
    "fixture": "Previously generated synthetic 8-second Demucs fixture; not the user's Suno song",
    "stemCount": len(fixture["stems"]), "sampleRate": sr,
    "residualRms": rms(residual), "residualPeak": float(np.max(np.abs(residual))),
    "residualRelativeDb": float(20*np.log10(max(rms(residual), 1e-12)/rms(x))),
    "toneTest": "0.3 peak cosine, stereo, 3 seconds at 44100 Hz. Measure fundamental in last 2 seconds; only multiband active.",
    "multiband": []
}
t = np.arange(sr*3)/sr
for frequency in (40, 60, 100, 180, 300, 1000, 4000):
    dry = np.repeat((.3*np.cos(2*np.pi*frequency*t))[:, None], 2, axis=1).astype(np.float32)
    carrier = np.exp(-2j*np.pi*frequency*t[sr:])
    for amount in (0, 50, 100):
        out = e.drum_fx(dry, sr, {"multiband": amount})
        transfer = np.dot(out[sr:, 0], carrier)/np.dot(dry[sr:, 0], carrier)
        report["multiband"].append({"frequencyHz": frequency, "amountPercent": amount,
            "fundamentalGainDb": float(20*np.log10(abs(transfer))),
            "fundamentalPhaseDegrees": float(np.angle(transfer, deg=True))})
destination = root/"artifacts/mix-phase-audit.json"
destination.write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report, indent=2))
