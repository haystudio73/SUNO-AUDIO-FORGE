"""Render PA1/PA2 from the already-downloaded user song; no network or Demucs."""
import json
import sys
import time
from pathlib import Path

root = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(root/"engine"))
import engine

project = json.loads((root/"artifacts/suno-live/Imported Suno.stemforge.json").read_text(encoding="utf-8"))
folder = root/"artifacts/stereo-real"
folder.mkdir(parents=True, exist_ok=True)
reports = []
for mode, extension in (("structural", "wav"), ("impact", "flac")):
    started = time.monotonic()
    result = engine.render({"source": project["source"], "stems": [], "processingMode": mode,
        "stereo": {}, "output": str(folder/(mode+"."+extension)), "sampleRate": 48000,
        "bits": "24", "normalize": True, "lufs": -14})
    reports.append({key: result[key] for key in ("output", "sampleRate", "bits", "stereo", "integratedLufs", "truePeakDbtp")})
    reports[-1]["elapsedSeconds"] = round(time.monotonic()-started, 3)
    assert result["truePeakDbtp"] <= -1
    assert Path(result["output"]).is_file()
    print(json.dumps(reports[-1], ensure_ascii=True))
(folder/"verification.json").write_text(json.dumps(reports, ensure_ascii=False, indent=2), encoding="utf-8")
