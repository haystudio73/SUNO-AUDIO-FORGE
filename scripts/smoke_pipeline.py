"""Generate a clearly labelled synthetic test clip; optionally run real Demucs."""
import argparse
import json
from pathlib import Path
import sys

import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parents[1]/"engine"))
import engine as e

parser = argparse.ArgumentParser()
parser.add_argument("--demucs", action="store_true")
parser.add_argument("--model", default="htdemucs")
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]/"artifacts"/"smoke"
root.mkdir(parents=True, exist_ok=True)
sr = 44100
t = np.arange(sr*8)/sr
audio = np.repeat((.07*np.sin(2*np.pi*110*t)+.04*np.sin(2*np.pi*440*t))[:, None], 2, axis=1)
for name, offsets in [("kick", np.arange(.2, 7, .5)), ("snare", np.arange(.45, 7, 1)), ("hat", np.arange(.2, 7, .25))]:
    sample = e.synthesized_sample(name, sr, "Studio")
    for at in offsets:
        pos = int(at*sr)
        audio[pos:pos+len(sample)] += sample*.2
source = root/"Synthetic drum test.wav"
e.write_float(source, audio, sr)
project = e.import_audio({"source": str(source), "directory": str(root/"session")})
if args.demucs:
    result = e.separate({"source": project["source"], "directory": str(root), "model": args.model, "device": "auto"})
    project.update(result)
    project["drums"] = {"transient": 30, "parallel": 25, "multiband": 20, "replacement": 35, "sensitivity": 50, "kit": "Studio"}
    project["refix"] = {"enabled": True, "highPass": 32, "mud": -3, "presence": 2, "air": 1.5, "width": 1.0}
    for ext in ["wav", "flac"]:
        result = e.render({**project, "output": str(root/f"full-pipeline.{ext}"), "bits": "24", "sampleRate": 48000, "normalize": True})
        print(json.dumps({k: v for k, v in result.items() if k != "waveform"}))
(root/"Synthetic test.stemforge.json").write_text(json.dumps(project, ensure_ascii=False, indent=2), encoding="utf-8")
print("Smoke artifacts:", root)
