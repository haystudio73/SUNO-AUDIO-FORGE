"""Local Whisper transcription. Decode via the application's configured FFmpeg."""
from i18n import tr
from pathlib import Path
import importlib.util
import math
import subprocess
import sys
import tempfile

import numpy as np
import soundfile as sf

MODELS = ("tiny", "base", "small", "medium", "turbo", "large-v3")


def prepare_preview(req, run_ffmpeg):
    source, output = Path(req["source"]), Path(req["preview"])
    if source.resolve() == output.resolve():
        raise ValueError("File nghe thử phải khác stem gốc.")
    output.parent.mkdir(parents=True, exist_ok=True)
    run_ffmpeg(["-i", str(source), "-vn", "-ac", "2", "-ar", "48000", "-c:a", "pcm_s16le", str(output)])
    return {"preview": str(output), "duration": sf.info(output).duration}


def info(req):
    spec = importlib.util.find_spec("whisper")
    if spec is None:
        return {"installed": False, "languages": {"vi": "Vietnamese", "en": "English"}}
    # Read the installed package's language table as data. Opening the lyrics
    # editor must not import Whisper/PyTorch just to offer public Suno lyrics.
    import ast
    languages = {"vi": "Vietnamese", "en": "English"}
    try:
        tree = ast.parse((Path(spec.origin).parent / 'tokenizer.py').read_text(encoding='utf-8'))
        for node in tree.body:
            if isinstance(node, ast.Assign) and any(isinstance(t, ast.Name) and t.id == 'LANGUAGES' for t in node.targets):
                value = ast.literal_eval(node.value)
                if isinstance(value, dict) and all(isinstance(k, str) and isinstance(v, str) for k, v in value.items()):
                    languages = value
                break
    except (OSError, ValueError, TypeError, SyntaxError):
        pass
    return {"installed": True, "languages": languages}


def install(req, progress):
    progress(tr("Đang cài openai-whisper vào Python đã chọn…"))
    command = [sys.executable, "-m", "pip", "install", "--disable-pip-version-check", "openai-whisper==20250625"]
    with subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                          text=True, encoding="utf-8", errors="replace",
                          creationflags=0x08000000 if sys.platform == "win32" else 0) as process:
        for line in process.stdout:
            progress(line.strip())
        if process.wait():
            raise ValueError("Không cài được Whisper. Kiểm tra kết nối, pip và quyền ghi của Python đã chọn.")
    return {"installed": True}


def transcribe(req, run_ffmpeg, progress):
    try:
        import whisper
        import torch
    except ImportError as exc:
        raise ValueError("Chưa có Whisper trong Python đã chọn. Bấm Cài Whisper trong cửa sổ lyrics.") from exc
    model_name = req.get("model", "small")
    language = req.get("language") or None
    if language == "auto":
        language = None
    if model_name not in MODELS:
        raise ValueError("Model Whisper không được hỗ trợ.")
    if language and language not in whisper.tokenizer.LANGUAGES:
        raise ValueError("Mã ngôn ngữ không được hỗ trợ.")
    device = req.get("device", "auto")
    if device not in ("auto", "cpu", "cuda"):
        raise ValueError("Thiết bị Whisper không hợp lệ.")
    if device == "auto":
        device = "cuda" if torch.cuda.is_available() else "cpu"
    if device == "cuda" and not torch.cuda.is_available():
        raise ValueError("CUDA không khả dụng. Chọn CPU hoặc Auto.")
    source = Path(req["source"])
    if not source.is_file():
        raise ValueError("Không tìm thấy stem Vocals.")
    preview = Path(req["preview"])
    if preview.resolve() == source.resolve():
        raise ValueError("File nghe thử phải khác stem gốc.")
    preview.parent.mkdir(parents=True, exist_ok=True)
    progress(tr("Chuẩn bị stem Vocals cho Whisper…"))
    with tempfile.TemporaryDirectory(prefix="stemforge-lyrics-") as tmp:
        decoded = Path(tmp)/"mono.wav"
        run_ffmpeg(["-i", str(source), "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_f32le", str(decoded)])
        if sf.info(decoded).duration > 1800:
            raise ValueError("Lyrics hỗ trợ tối đa 30 phút mỗi bài.")
        audio, _ = sf.read(decoded, dtype="float32")
    if not len(audio) or not np.isfinite(audio).all():
        raise ValueError("Stem Vocals rỗng hoặc không hợp lệ.")
    duration = len(audio)/16000
    # PCM preview keeps the same zero/time origin as recognition and stem playback.
    run_ffmpeg(["-i", str(source), "-vn", "-ac", "2", "-ar", "48000", "-c:a", "pcm_s16le", str(preview)])
    if np.max(np.abs(audio)) < 1e-5:
        return {"segments": [], "language": language or "", "duration": duration, "preview": str(preview), "model": model_name}
    torch.set_num_threads(min(4, torch.get_num_threads()))
    progress(tr("Đang nạp Whisper {0} ({1}); lần đầu có thể cần tải model…", model_name, device))
    model = whisper.load_model(model_name, device=device)
    progress(tr("Đang nhận dạng lời và thời gian từng câu…"))
    result = model.transcribe(audio, language=language, task="transcribe", fp16=device == "cuda",
                              verbose=False, condition_on_previous_text=False)
    segments = []
    for segment in result["segments"]:
        start, end = float(segment["start"]), float(segment["end"])
        if not math.isfinite(start) or not math.isfinite(end):
            continue
        start, end = max(0, start), min(duration, end)
        text = segment["text"].strip()
        if text and end > start:
            segments.append({"start": start, "end": end, "text": text})
    return {"segments": segments, "language": result.get("language", language or ""),
            "duration": duration, "preview": str(preview), "model": model_name}
