"""Detector contracts with known hit positions, amplitudes and real sample files."""
import sys
import tempfile
import unittest
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]/"engine"))
import engine as e


class TriggerTests(unittest.TestCase):
    sr = 44100

    def band(self, events):
        data = np.zeros((self.sr, 2), dtype=np.float32)
        for time, strength in events:
            start = int(time*self.sr)
            data[start:min(len(data), start+int(.005*self.sr))] = strength
        return data

    def detect(self, events, **params):
        band = self.band(events)
        return e.detect_drum_hits([band, np.zeros_like(band), np.zeros_like(band)], self.sr,
            {"sensitivity": 100, "thresholdDb": -60, **params})["kick"]

    def test_gate_rejects_noise_but_keeps_quiet_hits_and_alignment(self):
        hits = self.detect([(.2, .002), (.4, .05), (.7, .5)], thresholdDb=-40)
        self.assertEqual(len(hits), 2)
        np.testing.assert_allclose([position/self.sr for position, _ in hits], [.4, .7], atol=.001)
        self.assertAlmostEqual(hits[0][1]/hits[1][1], .1, delta=.01)

    def test_retrigger_rejects_double_hits_and_can_keep_fast_rolls(self):
        events = [(.2, .5), (.24, .4), (.6, .5)]
        self.assertEqual(len(self.detect(events, retriggerMs=80)), 2)
        self.assertEqual(len(self.detect(events, retriggerMs=20)), 3)

    def test_dynamics_and_file_boundaries(self):
        hits = self.detect([(0, .2), (.5, .5), (.997, .2)], dynamics=0)
        self.assertEqual(len(hits), 3)
        self.assertEqual(hits[0][0], 0)
        self.assertTrue(all(velocity == 1 for _, velocity in hits))

    def test_reference_presets_can_reject_fast_rolls_and_ghost_notes(self):
        # Two 1/32 notes at 120 BPM: 62.5 ms apart, same instrument.
        roll = [(.2, .5), (.2625, .5)]
        for interval in (70, 120):
            self.assertEqual(len(self.detect(roll, retriggerMs=interval)), 1)
        self.assertEqual(len(self.detect(roll, retriggerMs=40)), 2)
        # Isolate the absolute gate from the separate relative sensitivity gate.
        quiet = [(.2, .012), (.6, .5)]
        self.assertEqual(len(self.detect(quiet, thresholdDb=-36)), 1)
        self.assertEqual(len(self.detect(quiet, thresholdDb=-56)), 2)
        # Jazz reference sensitivity can still reject a ghost note above its gate.
        accents = [(.2, .1), (.6, .5)]
        self.assertEqual(len(self.detect(accents, thresholdDb=-36, sensitivity=35)), 1)
        self.assertEqual(len(self.detect(accents, thresholdDb=-56, sensitivity=75)), 2)

    def test_cross_band_suppression_preserves_dominant_hit(self):
        strong, spill = self.band([(.2, .5)]), self.band([(.2, .05)])
        bands = [strong, spill, np.zeros_like(strong)]
        off = e.detect_drum_hits(bands, self.sr, {"bleed": 0})
        on = e.detect_drum_hits(bands, self.sr, {"bleed": 100})
        self.assertEqual([len(off[n]) for n in ("kick", "snare")], [1, 1])
        self.assertEqual([len(on[n]) for n in ("kick", "snare")], [1, 0])

    def test_fully_wet_gate_never_leaks_original(self):
        data = self.band([(.2, .001)])
        out, counts = e.replace_drums(data, self.sr, {"replacement": 100, "thresholdDb": 0})
        self.assertTrue(all(count == 0 for count in counts.values()))
        np.testing.assert_array_equal(out, np.zeros_like(data))
        mixed, _ = e.replace_drums(data, self.sr, {"replacement": 50, "thresholdDb": 0})
        np.testing.assert_array_equal(mixed, data*.5)
        bypass, _ = e.replace_drums(data, self.sr, {"replacement": 0})
        np.testing.assert_array_equal(bypass, data)

    def test_sample_banks_round_robin_velocity_and_legacy(self):
        with tempfile.TemporaryDirectory() as folder:
            paths = []
            for index, amplitude in enumerate((.1, .3)):
                path = str(Path(folder)/f"{index+1:02d}-âm trống.wav")
                e.write_float(path, np.full((88, 2), amplitude, dtype=np.float32), self.sr)
                paths.append(path)
            params = {"replacement": 100, "dynamics": 0, "sensitivity": 100,
                      **{name+"Samples": paths for name in ("kick", "snare", "hat")}}
            data = self.band([(.2, .5), (.6, .5)])
            first, counts = e.replace_drums(data, self.sr, params)
            self.assertTrue(all(count == 2 for count in counts.values()), counts)
            peak1, peak2 = [np.max(np.abs(first[int(t*self.sr):int((t+.08)*self.sr)])) for t in (.19, .59)]
            self.assertAlmostEqual(peak2/peak1, 3, places=4)
            repeat, _ = e.replace_drums(data, self.sr, params)
            np.testing.assert_array_equal(first, repeat)
            params.update(sampleMode="velocity", dynamics=100)
            layered, _ = e.replace_drums(self.band([(.2, .125), (.6, .5)]), self.sr, params)
            soft, loud = [np.max(np.abs(layered[int(t*self.sr):int((t+.08)*self.sr)])) for t in (.19, .59)]
            self.assertAlmostEqual(loud/soft, 12, delta=.05)
            legacy = e.load_trigger_samples("kick", self.sr, {"kick": paths[0]})
            self.assertEqual(len(legacy), 1)
            self.assertAlmostEqual(float(legacy[0].max()), .1, places=5)
            with self.assertRaises(ValueError):
                e.load_trigger_samples("kick", self.sr, {"kickSamples": paths*5})
            with self.assertRaises(Exception):
                e.load_trigger_samples("kick", self.sr, {"kickSamples": [str(Path(folder)/"missing.wav")]})

    def test_invalid_parameters_fail_clearly(self):
        for key, value in (("thresholdDb", float("nan")), ("retriggerMs", 0), ("bleed", 101), ("dynamics", -1)):
            with self.subTest(key=key), self.assertRaises(ValueError):
                self.detect([(.2, .5)], **{key: value})


if __name__ == "__main__":
    unittest.main(verbosity=2)
