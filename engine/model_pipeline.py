"""Pinned local SW/DrumSep inference; no per-stem normalization or cloud audio upload."""
from i18n import tr
from pathlib import Path
import hashlib
import importlib.metadata
import logging
import os
import subprocess
import sys
import urllib.request
import uuid

import numpy as np

ASSETS = {
    "sw": ("https://huggingface.co/enerjazzer/BS-ROFO-SW-Fixed/resolve/a443a2985534b3bc815ef54a5d446c6a0390f974/", [
        ("BS-Rofo-SW-Fixed.ckpt", "sw.ckpt", "24e7d35ee9c64415673d3fd33e06a67cac2c103c5df6267ba1576459c775916e"),
        ("BS-Rofo-SW-Fixed.yaml", "sw.yaml", "f9fada9f94e5ba2d2e4600196299459294bc5f532b314c209cc156ac63e4329b")]),
    "drumsep": ("https://huggingface.co/Politrees/UVR_resources/resolve/929e057b81aa49bc2e6490bef8671f47b2c120f6/models/MDX23C/", [
        ("MDX23C-DrumSep-aufr33-jarredou.ckpt", "drumsep.ckpt", "d2a4aa53eb584d21eead358a4e66d1882ad182911be018f052b5da73be9096d0"),
        ("config_drumsep_mdx23c.yaml", "drumsep.yaml", "17d1649a227f841165bdb4c11a42082898192a1ea3ceab7e7e0b9293d6589dd6")])}
NAMES = {"sw": ["bass", "drums", "other", "vocals", "guitar", "piano"],
         "drumsep": ["kick", "snare", "toms", "hh", "ride", "crash"]}


def digest(path):
    with open(path, "rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def assets(kind, directory, progress):
    directory = Path(directory)
    directory.mkdir(parents=True, exist_ok=True)
    base, items = ASSETS[kind]
    for remote, filename, expected in items:
        path = directory/filename
        if path.exists() and digest(path) == expected:
            continue
        partial = directory/(filename+"."+uuid.uuid4().hex+".part")
        try:
            progress(tr("Tải {0}; audio vẫn ở trên máy…", filename))
            with urllib.request.urlopen(base+remote, timeout=60) as source, open(partial, "wb") as target:
                size = int(source.headers.get("Content-Length", 0)); count = 0; last = -1
                while chunk := source.read(1024*1024):
                    target.write(chunk); count += len(chunk)
                    if count > 1100*1024*1024:
                        raise ValueError("Model vượt giới hạn dung lượng dự kiến.")
                    percent = count*100//max(1, size)
                    if percent//10 != last:
                        last = percent//10; progress(tr("Tải {0}: {1} MB", filename, count//1048576) + (f" / {size//1048576} MB" if size else ""))
            if digest(partial) != expected:
                raise ValueError(f"Checksum không khớp: {filename}. Hãy tải lại.")
            os.replace(partial, path)
        finally:
            partial.unlink(missing_ok=True)
    return directory/(kind+".ckpt"), directory/(kind+".yaml")


def install(req, progress):
    command = [sys.executable, "-m", "pip", "install", "--disable-pip-version-check", "audio-separator==0.47.0"]
    with subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace",
                          creationflags=0x08000000 if os.name == "nt" else 0) as process:
        for line in process.stdout:
            progress(line.strip())
        if process.wait():
            raise ValueError("Không cài được engine RoFormer/DrumSep trong Python đã chọn.")
    return {"installed": True}


def infer(kind, source, cache, device, read_audio, progress):
    import torch
    import yaml
    try:
        if importlib.metadata.version("audio-separator") != "0.47.0":
            raise ImportError()
        from audio_separator.separator.architectures.mdxc_separator import MDXCSeparator
    except (ImportError, importlib.metadata.PackageNotFoundError) as exc:
        raise ValueError("Bấm Cài engine RoFormer/DrumSep trước (audio-separator 0.47.0).") from exc
    if device not in ("auto", "cpu", "cuda"):
        raise ValueError("Thiết bị phải là Auto, CPU hoặc CUDA.")
    device = ("cuda" if torch.cuda.is_available() else "cpu") if device == "auto" else device
    if device == "cuda" and not torch.cuda.is_available():
        raise ValueError("CUDA chưa sẵn sàng. Chọn CPU hoặc Auto.")
    checkpoint, config_path = assets(kind, cache, progress)
    class ConfigLoader(yaml.SafeLoader):
        pass
    ConfigLoader.add_constructor("tag:yaml.org,2002:python/tuple", lambda loader, node: tuple(loader.construct_sequence(node)))
    config = yaml.load(config_path.read_text(encoding="utf-8"), Loader=ConfigLoader)
    if config["training"]["instruments"] != NAMES[kind]:
        raise ValueError("Thứ tự đầu ra model không đúng cấu hình đã xác minh.")
    if kind == "sw":
        config["is_roformer"] = True
    data, sr = read_audio(source, 44100)
    progress(tr("{0} · {1} · đang tách…", "BS-RoFormer SW" if kind == "sw" else "DrumSep MDX23C", device.upper()))
    logger = logging.getLogger("stemforge.separation")
    logger.setLevel(logging.WARNING)
    torch.set_num_threads(min(4, torch.get_num_threads()))
    common = dict(logger=logger, log_level=logging.WARNING, torch_device=torch.device(device), torch_device_cpu=torch.device("cpu"),
                  model_name="BS-Roformer-SW" if kind == "sw" else "MDX23C-DrumSep", model_path=str(checkpoint), model_data=config,
                  sample_rate=sr, output_format="WAV", normalization_threshold=1.0, amplification_threshold=0.0,
                  use_soundfile=True, use_autocast=False)
    separator = MDXCSeparator(common, {"batch_size": 1, "overlap": config["inference"]["num_overlap"], "pitch_shift": 0})
    try:
        # demix returns raw model estimates. separate() would peak-normalize each
        # stem independently, destroying the relative gains needed for remixing.
        with torch.inference_mode():
            output = separator.demix(data.T.copy())
        result = {}
        for name in NAMES[kind]:
            audio = np.asarray(output[name], dtype=np.float32).T
            if audio.shape != data.shape or not np.isfinite(audio).all():
                raise ValueError(f"Model trả về stem {name} sai độ dài/kênh hoặc không hữu hạn.")
            result["hat" if name == "hh" else name] = audio
        return result, sr, device
    finally:
        del separator
        if device == "cuda":
            torch.cuda.empty_cache()


def separate(req, read_audio, write_float, progress):
    model = req.get("model", "bs_roformer_sw")
    if model not in ("bs_roformer_sw", "drumsep_mdx23c"):
        raise ValueError("Model tách không được hỗ trợ.")
    kind = "sw" if model == "bs_roformer_sw" else "drumsep"
    output, sr, device = infer(kind, req["source"], req["modelDirectory"], req.get("device", "auto"), read_audio, progress)
    directory = Path(req["directory"])/("stems-"+uuid.uuid4().hex)
    directory.mkdir(parents=True)
    stems = []
    for name, audio in output.items():
        path = directory/(name+".wav")
        write_float(path, audio, sr)
        stems.append({"name": name, "path": str(path), "gain": 0, "mute": False, "solo": False})
    return {"stems": stems, "sampleRate": sr, "device": device, "model": model}
