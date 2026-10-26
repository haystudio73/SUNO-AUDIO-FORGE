import json
from pathlib import Path
import sys
import tempfile
import unittest

import numpy as np
import soundfile as sf

sys.path.insert(0, str(Path(__file__).resolve().parents[1]/"engine"))
import audio_analysis as analysis


class AudioAnalysisTests(unittest.TestCase):
    sr = 22050

    def analyze(self, data):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder)/"nhạc thử.wav"
            sf.write(path, data, self.sr, subtype="FLOAT")
            result = analysis.analyze_wav(path)
        json.dumps(result, allow_nan=False)
        return result

    def clicks(self, bpm, duration=16):
        data = np.zeros(int(self.sr*duration), dtype=np.float32)
        t = np.arange(int(self.sr*.04))/self.sr
        click = np.sin(2*np.pi*1000*t)*np.exp(-t*100)
        for start in np.arange(.5, duration-.1, 60/bpm):
            p = int(start*self.sr)
            data[p:p+len(click)] += click
        return data

    def harmony(self, tonic=60, minor=False):
        data = []
        third = 3 if minor else 4
        chords = ((0, third, 7), (5, 8 if minor else 9, 12), (7, 11, 14), (0, third, 7))
        t = np.arange(3*self.sr)/self.sr
        for chord in chords:
            y = sum(np.sin(2*np.pi*440*2**((tonic+n-69)/12)*t) for n in chord)*.13
            y[:1000] *= np.linspace(0, 1, 1000)
            y[-1000:] *= np.linspace(1, 0, 1000)
            data.append(y)
        return np.concatenate(data).astype(np.float32)

    def test_known_tempos(self):
        for bpm in (90, 120, 174):
            with self.subTest(bpm=bpm):
                result = self.analyze(self.clicks(bpm))
                self.assertIsNotNone(result["bpm"])
                self.assertAlmostEqual(result["bpm"], bpm, delta=1)

    def test_major_minor_and_transposition(self):
        for tonic, minor, expected in ((60, False, "C trưởng"), (57, True, "A thứ"), (63, False, "D♯/E♭ trưởng")):
            with self.subTest(expected=expected):
                result = self.analyze(self.harmony(tonic, minor))
                self.assertEqual(result["key"], expected)

    def test_silence_short_audio_and_single_tone_abstain(self):
        for data in (np.zeros(self.sr*8), np.ones(100)*.1, np.sin(2*np.pi*440*np.arange(self.sr*8)/self.sr)*.2):
            result = self.analyze(data)
            self.assertIsNone(result["bpm"])
            self.assertIsNone(result["key"])

    def test_opposite_polarity_stereo_retains_tempo(self):
        y = self.clicks(120)
        result = self.analyze(np.column_stack([y, -y]))
        self.assertAlmostEqual(result["bpm"], 120, delta=3)

    def test_unpitched_noise_does_not_invent_tempo_or_key(self):
        noise = np.random.default_rng(42).normal(0, .1, self.sr*12).astype(np.float32)
        result = self.analyze(noise)
        self.assertIsNone(result["bpm"])
        self.assertIsNone(result["key"])

    def test_multiple_windows_and_partial_tail(self):
        data = self.clicks(120, duration=34)
        result = self.analyze(data)
        self.assertAlmostEqual(result["bpm"], 120, delta=3)
        self.assertEqual(result["analyzedSeconds"], 34)


if __name__ == "__main__":
    unittest.main()
