"""Opt-in live import check. Downloads only the public song explicitly supplied."""
import json
import sys
from pathlib import Path
import soundfile as sf

root = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(root/"engine"))
import engine

if len(sys.argv) != 2:
    raise SystemExit("Usage: python scripts/smoke_suno.py https://suno.com/song/<id>")
directory = root/"artifacts"/"suno-live"
result = engine.import_audio({"url": sys.argv[1], "directory": str(directory)})
(directory/"Imported Suno.stemforge.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
info = sf.info(result["source"])
assert info.duration > 0 and info.channels == 2
assert Path(result["preview"]).is_file()
summary = {key: result[key] for key in ("name", "sourceUrl", "downloadKind", "duration", "sampleRate")}
summary["status"] = "PASS: public media downloaded, audio decoded, float master and PCM preview created"
(directory/"verification.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(summary, ensure_ascii=True))
