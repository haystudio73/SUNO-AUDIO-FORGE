import json
from pathlib import Path
import sys
import tempfile
import unittest

import numpy as np
import soundfile as sf

sys.path.insert(0, str(Path(__file__).resolve().parents[1]/"engine"))
import engine as e


class LyricsTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.source = self.root/"giọng hát.wav"
        sf.write(self.source, np.zeros((48000, 2), dtype=np.float32), 48000)
        self.request = {"source": str(self.source), "preview": str(self.root/"nghe.wav"), "model": "base", "language": "vi", "device": "cpu"}

    def tearDown(self):
        self.tmp.cleanup()

    def test_silent_stem_does_not_hallucinate(self):
        result = e.lyrics.transcribe(self.request, e.run_ffmpeg, lambda message: None)
        self.assertEqual(result["segments"], [])
        self.assertEqual(result["language"], "vi")
        self.assertEqual(result["duration"], 1)
        self.assertEqual(sf.info(result["preview"]).subtype, "PCM_16")

    def test_preview_preserves_origin_duration_and_channels(self):
        data = np.zeros((48000, 2), dtype=np.float32)
        data[24000, 0] = .5
        sf.write(self.source, data, 48000, subtype="FLOAT")
        result = e.lyrics.prepare_preview(self.request, e.run_ffmpeg)
        output, rate = sf.read(result["preview"])
        self.assertEqual(output.shape, data.shape)
        self.assertEqual(rate, 48000)
        self.assertEqual(np.argmax(output[:, 0]), 24000)

    def test_original_is_not_overwritten(self):
        before = self.source.read_bytes()
        req = {**self.request, "preview": str(self.source)}
        with self.assertRaises(ValueError):
            e.lyrics.prepare_preview(req, e.run_ffmpeg)
        with self.assertRaises(ValueError):
            e.lyrics.transcribe(req, e.run_ffmpeg, lambda message: None)
        self.assertEqual(before, self.source.read_bytes())

    def test_validates_model_language_device(self):
        for key, value in [("model", "../model.pt"), ("language", "not-a-language"), ("device", "random")]:
            with self.assertRaises(ValueError):
                e.lyrics.transcribe({**self.request, key: value}, e.run_ffmpeg, lambda message: None)

    def test_language_info_includes_vietnamese(self):
        result = e.lyrics.info({})
        self.assertIn("vi", result["languages"])


if __name__ == "__main__":
    unittest.main()
