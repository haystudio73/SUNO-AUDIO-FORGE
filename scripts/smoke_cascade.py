"""Opt-in real SW -> MDX inference, aligned parts, MIDI and final stems-only render."""
import argparse
import json
import os
import time
from pathlib import Path
import sys
import numpy as np
import soundfile as sf

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/"engine"))
import engine as e

p=argparse.ArgumentParser()
p.add_argument("source")
p.add_argument("--seconds",type=float,default=36)
p.add_argument("--device", choices=("auto", "cpu", "cuda"), default="auto")
p.add_argument("--output", default="artifacts/cascade-real")
p.add_argument("--replacement", type=float, default=45)
args=p.parse_args()
if args.device == "cpu":
    os.environ["CUDA_VISIBLE_DEVICES"] = "-1"
folder=ROOT/args.output;folder.mkdir(parents=True,exist_ok=True)
source=folder/"original.wav"
e.run_ffmpeg(["-i",args.source,"-t",str(args.seconds),"-c:a","pcm_f32le",str(source)])
base={"directory":str(folder),"modelDirectory":str(ROOT/"artifacts"/"model-cache"),"device":args.device}
started=time.perf_counter()
sw=e.separate({**base,"source":str(source),"model":"bs_roformer_sw"})
sw_seconds=time.perf_counter()-started
if args.device != "auto": assert sw["device"] == args.device
print(f"SW FINISHED: {sw_seconds:.2f}s, device={sw['device']}",flush=True)
drum=next(s for s in sw["stems"] if s["name"]=="drums")
started=time.perf_counter()
parts=e.separate({**base,"source":drum["path"],"model":"drumsep_mdx23c"})
drumsep_seconds=time.perf_counter()-started
if args.device != "auto": assert parts["device"] == args.device
assert {s["name"] for s in sw["stems"]}=={"bass","drums","other","vocals","guitar","piano"}
assert {s["name"] for s in parts["stems"]}=={"kick","snare","toms","hat","ride","crash"}
print(f"DRUMSEP FINISHED: {drumsep_seconds:.2f}s, device={parts['device']}",flush=True)
audio,sr=e.read_audio(source,44100)
for stem in sw["stems"]+parts["stems"]:
    data,rate=sf.read(stem["path"],always_2d=True)
    assert rate==44100 and data.shape==audio.shape and np.isfinite(data).all()
params={"parts":parts["stems"],"partsSource":drum["path"],"replacement":args.replacement,"blendMode":"layer","midiBpm":120}
mid=e.drum_midi.export({"source":drum["path"],"drums":params,"output":str(folder/"drums.mid")},e.read_audio,e.trigger_events)
out=e.render({"source":str(source),"stems":sw["stems"],"drums":params,"output":str(folder/("mix.flac" if args.replacement == 0 else f"layer{args.replacement:g}.flac")),"sampleRate":48000,"bits":"24","normalize":False})
project={"version":1,"name":"Suno · SW → DrumSep","source":str(source),"preview":str(folder/"preview.wav"),"duration":len(audio)/sr,
         "sampleRate":48000,"device":args.device,"processingMode":"legacy","model":"bs_roformer_sw","stems":sw["stems"],"drumParts":parts["stems"],"drumPartsSource":drum["path"],"drums":params,"waveform":e.file_waveform(source)}
e.run_ffmpeg(["-i",str(source),"-c:a","pcm_s16le",project["preview"]])
(folder/"Cascade.stemforge.json").write_text(json.dumps(project,ensure_ascii=False,indent=2),encoding="utf-8")
(folder/"verification.json").write_text(json.dumps({"seconds":len(audio)/sr,"requestedDevice":args.device,"cudaVisibleDevices":os.environ.get("CUDA_VISIBLE_DEVICES"),"swSeconds":sw_seconds,"drumsepSeconds":drumsep_seconds,"cpu":os.environ.get("PROCESSOR_IDENTIFIER"),"sw":sw,"drumsep":parts,"midi":mid,"render":{k:v for k,v in out.items() if k!="waveform"}},ensure_ascii=False,indent=2),encoding="utf-8")
print(f"CASCADE PASS: 6 SW stems, 6 aligned DrumSep lanes, percussion MIDI, FLAC; requested={args.device}",flush=True)
