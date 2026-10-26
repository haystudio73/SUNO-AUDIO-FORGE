"""Create a known C-major / 120 BPM audio fixture and exercise the real engine."""
import json
import os
from pathlib import Path
import sys

import numpy as np
import soundfile as sf

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT/"engine"))
import engine

sr = 22050
t = np.arange(3*sr)/sr
chords = []
for notes in ((60, 64, 67), (65, 69, 72), (67, 71, 74), (60, 64, 67)):
    y = sum(np.sin(2*np.pi*440*2**((note-69)/12)*t) for note in notes)*.13
    y[:1000] *= np.linspace(0, 1, 1000)
    y[-1000:] *= np.linspace(1, 0, 1000)
    chords.append(y)
data = np.concatenate(chords).astype(np.float32)
tick_t = np.arange(int(sr*.04))/sr
click = .35*np.sin(2*np.pi*1000*tick_t)*np.exp(-tick_t*100)
for beat in np.arange(.5, 11.9, .5):
    pos = int(beat*sr)
    data[pos:pos+len(click)] += click
audio = np.column_stack((data, data))
path = ROOT/"artifacts"/"analysis-fixture.wav"
sf.write(path, audio, sr, subtype="PCM_16")
project = {"source": str(path), "preview": str(path), "name": "Kiểm thử C trưởng · 120 BPM",
           "duration": 12, "sampleRate": sr, "waveform": engine.waveform(audio)}
(path.with_suffix(".json")).write_text(json.dumps(project, ensure_ascii=False), encoding="utf-8")
result = engine.audio_analysis.analyze({"source": str(path)}, engine.run_ffmpeg, engine.progress)
assert result["bpm"] is not None and abs(result["bpm"]-120) < 3, result
assert result["key"] == "C trưởng", result
if len(sys.argv) > 1:
    real = engine.audio_analysis.analyze({"source": sys.argv[1]}, engine.run_ffmpeg, engine.progress)
    result = {"synthetic": result, "realAudioEstimate": real, "realSource": sys.argv[1]}
(ROOT/"artifacts"/"analysis-smoke-result.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(result, ensure_ascii=True))
