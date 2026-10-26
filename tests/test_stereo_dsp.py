"""Timing, stereo image, reconstruction and new stereo render routing contracts."""
import sys
import tempfile
import unittest
from pathlib import Path
import numpy as np
import soundfile as sf

sys.path.insert(0, str(Path(__file__).resolve().parents[1]/"engine"))
import stereo_dsp as s
import engine as e


class StereoTests(unittest.TestCase):
    sr = 44100
    neutral = {"attack80": 0, "attack3200": 0, "mud180": 0, "mud320": 0, "harsh": 0,
               "sideAmount": 100, "width": 100, "impactDb": 0, "subAmount": 0}

    def tone(self, frequency=3200, seconds=2, level=.05):
        t = np.arange(int(self.sr*seconds))/self.sr
        return np.repeat((level*np.sin(2*np.pi*frequency*t))[:, None], 2, axis=1).astype(np.float32)

    def test_unity_reconstructs_stereo_and_short_files_without_delay(self):
        rng = np.random.default_rng(42)
        for length in (1, 100, 4097, self.sr):
            data = rng.normal(0, .03, (length, 2)).astype(np.float32)
            for mode in ("structural", "impact"):
                out, _ = s.process(data, self.sr, self.neutral, mode)
                self.assertEqual(out.shape, data.shape)
                np.testing.assert_allclose(out, data, atol=1e-7)
        impulse = np.zeros((self.sr, 2), dtype=np.float32)
        impulse[12345] = [.2, -.1]
        out, _ = s.process(impulse, self.sr, {}, "structural")
        self.assertEqual(np.argmax(np.abs(out[:, 0])), 12345)
        self.assertEqual(np.argmax(np.abs(out[:, 1])), 12345)

    def test_block_boundaries_do_not_change_audio(self):
        data = np.random.default_rng(12).normal(0, .02, (self.sr*3+27, 2)).astype(np.float32)
        one, _ = s.process(data, self.sr, {}, "structural", block_seconds=1)
        four, _ = s.process(data, self.sr, {}, "structural", block_seconds=4)
        np.testing.assert_allclose(one, four, atol=2e-6)

    def test_supported_sample_rates_keep_impulse_timing_and_unity(self):
        for sr in (44100, 48000, 88200, 96000, 176400, 192000):
            data = np.zeros((sr//2, 2), dtype=np.float32)
            position = sr//4
            data[position] = [.15, -.08]
            with self.subTest(rate=sr):
                unity, _ = s.process(data, sr, self.neutral, "structural")
                np.testing.assert_allclose(unity, data, atol=1e-7)
                processed, _ = s.process(data, sr, {}, "structural")
                self.assertEqual(np.argmax(np.abs(processed[:, 0])), position)

    def test_mid_only_does_not_process_side_and_width_zero_is_mono(self):
        mid = self.tone(180)[:, 0]
        side = self.tone(3300)[:, 0]
        data = np.stack([mid+side, mid-side], axis=1)
        out, _ = s.process(data, self.sr, {"sideAmount": 0}, "structural")
        np.testing.assert_allclose((out[:, 0]-out[:, 1])*.5, side, atol=1e-7)
        mono, _ = s.process(data, self.sr, {"width": 0}, "structural")
        np.testing.assert_array_equal(mono[:, 0], mono[:, 1])

    def test_tonal_cut_reduces_sustained_tone_without_shifting_it(self):
        data = self.tone(3200)
        params = {**self.neutral, "harsh": -6}
        out, _ = s.process(data, self.sr, params, "structural")
        carrier = np.exp(-2j*np.pi*3200*np.arange(self.sr)/self.sr)
        transfer = np.dot(out[self.sr:, 0], carrier)/np.dot(data[self.sr:, 0], carrier)
        self.assertLess(abs(transfer), .8)
        self.assertLess(abs(np.angle(transfer, deg=True)), .5)

    def test_transient_boost_and_impact_controls_are_effective(self):
        data = np.zeros((self.sr, 2), dtype=np.float32)
        data[self.sr//2] = .1
        boosted, _ = s.process(data, self.sr, {**self.neutral, "attack3200": 6}, "structural")
        self.assertGreater(float(np.max(np.abs(boosted))), .1)
        down, _ = s.process(data, self.sr, {**self.neutral, "impactDb": -18}, "impact")
        up, _ = s.process(data, self.sr, {**self.neutral, "impactDb": 18}, "impact")
        self.assertLess(np.max(np.abs(down)), np.max(np.abs(data)))
        self.assertGreater(np.max(np.abs(up)), np.max(np.abs(data))*2)

    def test_sub_adds_low_frequency_mono_content_without_changing_duration(self):
        data = np.zeros((self.sr*2, 2), dtype=np.float32)
        t = np.arange(int(self.sr*.25))/self.sr
        kick = .15*np.sin(2*np.pi*95*t)*np.exp(-t/.04)
        for at in (.2, .7, 1.2):
            start = int(at*self.sr)
            data[start:start+len(kick)] += kick[:, None]
        dry, _ = s.process(data, self.sr, self.neutral, "impact")
        wet, info = s.process(data, self.sr, {**self.neutral, "subAmount": 100, "subHz": 40}, "impact")
        self.assertEqual(wet.shape, data.shape)
        self.assertGreaterEqual(info["subHits"], 3)
        delta = wet-dry
        np.testing.assert_array_equal(delta[:, 0], delta[:, 1])
        frequency = np.fft.rfftfreq(len(delta), 1/self.sr)
        power = np.abs(np.fft.rfft(delta[:, 0]))**2
        self.assertGreater(power[(frequency >= 20) & (frequency <= 60)].sum()/power.sum(), .8)

    def test_silence_bypass_and_invalid_parameters(self):
        data = np.zeros((self.sr, 2), dtype=np.float32)
        for mode in ("structural", "impact"):
            out, info = s.process(data, self.sr, {}, mode)
            np.testing.assert_array_equal(out, data)
            self.assertEqual(info["subHits"], 0)
        tone = self.tone()
        out, _ = s.process(tone, self.sr, {"enabled": False}, "structural")
        np.testing.assert_array_equal(out, tone)
        for params in ({"impactDb": 19}, {"subHz": 0}, {"width": float("nan")}):
            with self.assertRaises(ValueError):
                s.process(data, self.sr, params, "impact")

    def test_stereo_render_ignores_stems_and_legacy_effects_and_exports_safely(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            source, stem = root/"source.wav", root/"drums.wav"
            data = self.tone(80)
            e.write_float(source, data, self.sr)
            e.write_float(stem, -data*3, self.sr)
            request = {"source": str(source), "processingMode": "structural", "stereo": self.neutral,
                       "output": str(root/"out.wav"), "normalize": False, "bits": "32",
                       "stems": [{"name": "drums", "path": str(stem), "gain": 12}],
                       "drums": {"replacement": 100}, "refix": {"enabled": True}}
            e.render(request)
            out, _ = sf.read(request["output"], dtype="float32", always_2d=True)
            np.testing.assert_allclose(out, data, atol=1e-7)
            for mode, extension in (("structural", "wav"), ("impact", "flac")):
                result = e.render({**request, "processingMode": mode, "stereo": {"impactDb": 18, "subAmount": 100},
                    "stems": [], "bits": "24", "normalize": True, "output": str(root/(mode+"."+extension))})
                self.assertEqual(result["stereo"]["mode"], mode)
                self.assertLessEqual(result["truePeakDbtp"], -1)


if __name__ == "__main__":
    unittest.main(verbosity=2)
