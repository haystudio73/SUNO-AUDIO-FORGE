"""Audio contract tests, including actual FFmpeg encoding/decoding. No mocks."""
import json
from pathlib import Path
import sys
import tempfile
import unittest

import numpy as np
import soundfile as sf

sys.path.insert(0, str(Path(__file__).resolve().parents[1]/"engine"))
import engine as e


class AudioTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.sr = 44100
        t = np.arange(self.sr*4)/self.sr
        self.audio = np.column_stack([.12*np.sin(2*np.pi*220*t), .1*np.sin(2*np.pi*330*t)]).astype(np.float32)
        self.source = self.root/"âm thanh gốc.wav"
        e.write_float(self.source, self.audio, self.sr)

    def tearDown(self):
        self.tmp.cleanup()

    def render(self, **params):
        req = {"source": str(self.source), "output": str(self.root/"out.wav"), "sampleRate": self.sr, "bits": "32", "normalize": False}
        req.update(params)
        result = e.render(req)
        audio, sr = sf.read(result["output"], dtype="float32", always_2d=True)
        return result, audio, sr

    def test_bypass_preserves_samples_and_duration(self):
        _, out, sr = self.render()
        self.assertEqual(sr, self.sr)
        np.testing.assert_allclose(out, self.audio, atol=1e-6)

    def test_drumsep_final_routing_selection_gain_and_no_parent_doubling(self):
        parts = []
        for i, name in enumerate(e.drum_midi.GM):
            path = self.root/(name+".wav")
            e.write_float(path, self.audio*(i+1)*.1, self.sr)
            parts.append(dict(name=name, path=str(path), gain=-6 if name == "kick" else 0,
                              mute=name not in ("kick", "snare")))
        parent = self.root/"parent.wav"
        e.write_float(parent, self.audio*3, self.sr)  # Must never be added to child mix.
        params = dict(mixParts=True, parts=parts, partsSource=str(parent))
        stems = [dict(name="drums", path=str(parent), gain=-3),
                 dict(name="bass", path=str(self.source), gain=-12)]
        expected_drums = self.audio*(.1*10**(-6/20)+.2)*10**(-3/20)
        for suffix, bits, tolerance in [("wav", "32", 1e-6), ("flac", "24", 1e-6)]:
            _, out, _ = self.render(stems=stems, drums=params, output=str(self.root/("mix."+suffix)), bits=bits)
            np.testing.assert_allclose(out, expected_drums+self.audio*10**(-12/20), atol=tolerance)
        stems[0]["solo"] = True
        _, out, _ = self.render(stems=stems, drums=params)
        np.testing.assert_allclose(out, expected_drums, atol=1e-6)
        stems[0]["solo"] = False
        for part in parts: part["mute"] = True
        _, out, _ = self.render(stems=stems, drums=params)
        np.testing.assert_allclose(out, self.audio*10**(-12/20), atol=1e-6)

    def test_bands_reconstruct_and_drum_bypass_is_exact(self):
        np.testing.assert_allclose(sum(e.split_bands(self.audio, self.sr)), self.audio, atol=1e-7)
        np.testing.assert_array_equal(e.drum_fx(self.audio, self.sr, {}), self.audio)

    def test_pcm_and_flac_formats(self):
        for ext, bits, subtype in [("wav", "16", "PCM_16"), ("wav", "24", "PCM_24"), ("wav", "32", "FLOAT"), ("flac", "24", "PCM_24"), ("flac", "16", "PCM_16")]:
            path = self.root/f"out-{bits}.{ext}"
            self.render(output=str(path), bits=bits, sampleRate=48000)
            info = sf.info(path)
            self.assertEqual((info.samplerate, info.channels, info.subtype), (48000, 2, subtype))
            self.assertAlmostEqual(info.duration, 4, places=3)

    def test_solo_mute_and_gain(self):
        stems = [{"name": "vocals", "path": str(self.source), "gain": -6, "solo": True},
                 {"name": "bass", "path": str(self.source), "gain": 12}]
        _, out, _ = self.render(stems=stems)
        np.testing.assert_allclose(out, self.audio*10**(-6/20), atol=1e-6)
        stems[0]["mute"] = True
        _, out, _ = self.render(stems=stems)
        self.assertEqual(float(np.max(np.abs(out))), 0)

    def test_stem_mix_is_independent_of_original_audio_content(self):
        # A deliberately unrelated source must never leak into a 4/6-stem mix,
        # even with Drum Lab and the master refix active. Keep levels below the
        # export peak guard so it cannot obscure a routing error.
        t = np.arange(len(self.audio))/self.sr
        stems = []
        for index, name in enumerate(("drums", "bass", "vocals", "other", "guitar", "piano")):
            path = self.root/(name+".wav")
            tone = (.025*np.sin(2*np.pi*(60+index*173)*t)).astype(np.float32)
            e.write_float(path, np.column_stack([tone, tone*.8]), self.sr)
            stems.append({"name": name, "path": str(path), "gain": 0})
        for count in (4, 6):
            with self.subTest(stems=count):
                e.write_float(self.source, self.audio, self.sr)
                _, first, _ = self.render(stems=stems[:count], drums={"multiband": 50, "parallel": 30}, refix={"enabled": True})
                e.write_float(self.source, -self.audio*.5, self.sr)
                _, second, _ = self.render(stems=stems[:count], drums={"multiband": 50, "parallel": 30}, refix={"enabled": True})
                np.testing.assert_allclose(first, second, atol=1e-7)

    def test_muting_every_stem_does_not_fall_back_to_original(self):
        stems = [{"name": name, "path": str(self.source), "mute": True} for name in ("drums", "bass", "vocals", "other")]
        _, out, _ = self.render(stems=stems, drums={"multiband": 100, "parallel": 100, "replacement": 100}, refix={"enabled": True})
        np.testing.assert_array_equal(out, np.zeros_like(self.audio))

    def test_stems_sum_once_without_inserting_sample_delay(self):
        # Distinct impulses make duplication, channel swaps and time shifts visible.
        expected = np.zeros_like(self.audio)
        stems = []
        for index, name in enumerate(("drums", "bass", "vocals", "other")):
            audio = np.zeros_like(self.audio)
            audio[1000+index*2000, index % 2] = .05*(index+1)
            expected += audio
            path = self.root/(name+".wav")
            e.write_float(path, audio, self.sr)
            stems.append({"name": name, "path": str(path)})
        _, out, _ = self.render(stems=stems)
        np.testing.assert_allclose(out, expected, atol=1e-7)

    def test_silence_survives_normalization(self):
        e.write_float(self.source, np.zeros_like(self.audio), self.sr)
        _, out, _ = self.render(normalize=True, refix={"enabled": True}, bits="24")
        self.assertTrue(np.isfinite(out).all())
        self.assertLess(np.max(np.abs(out)), 1e-6)

    def test_truepeak_guard_without_normalization(self):
        e.write_float(self.source, self.audio*20, self.sr)
        _, out, _ = self.render()
        peak = np.max(np.abs(e.signal.resample_poly(out, 4, 1, axis=0)))
        self.assertLessEqual(peak, 10**(-.99/20))

    def test_loudness_and_peak_after_refix(self):
        result, out, _ = self.render(normalize=True, lufs=-14, bits="24", refix={"enabled": True}, sampleRate=48000)
        self.assertTrue(np.isfinite(out).all())
        log = e.run_ffmpeg(["-i", result["output"], "-af", "loudnorm=I=-14:TP=-1:print_format=json", "-f", "null", "-"])
        stats = json.loads(log[log.rfind('{'):log.rfind('}')+1])
        self.assertLess(abs(float(stats["input_i"])+14), .6)
        self.assertLessEqual(float(stats["input_tp"]), -.9)

    def test_waveform_matches_delivered_master_not_premaster(self):
        for extension in ("wav", "flac"):
            result, audio, _ = self.render(output=str(self.root/f"compare.{extension}"),
                                          normalize=True, lufs=-14, bits="24", sampleRate=48000)
            np.testing.assert_array_equal(result["waveform"], e.waveform(audio))
            self.assertGreater(np.max(np.abs(np.array(result["waveform"])-e.waveform(self.audio))), .01)

    def test_streamed_waveform_bin_boundaries_and_silence(self):
        for length in (1, 699, 700, 701, 1403):
            data = np.zeros((length, 2), dtype=np.float32)
            data[-1, 1] = -.7
            e.write_float(self.source, data, self.sr)
            np.testing.assert_array_equal(e.file_waveform(self.source), e.waveform(data))
        e.write_float(self.source, np.zeros((701, 2), dtype=np.float32), self.sr)
        self.assertEqual(e.file_waveform(self.source), [0.]*700)

    def test_replacement_and_fx_are_finite_and_find_hits(self):
        drums = np.zeros_like(self.audio)
        for name, positions in [("kick", [.3, 1.3, 2.3]), ("snare", [.8, 1.8, 2.8]), ("hat", [.55, 1.05, 1.55, 2.05])]:
            sample = e.synthesized_sample(name, self.sr, "Studio")
            for at in positions:
                pos = int(at*self.sr)
                drums[pos:pos+len(sample)] += sample*.5
        replaced, hits = e.replace_drums(drums, self.sr, {"replacement": 100, "sensitivity": 50})
        self.assertTrue(all(count > 0 for count in hits.values()), hits)
        self.assertGreater(np.max(np.abs(replaced)), .01)
        processed = e.drum_fx(replaced, self.sr, {"transient": 100, "multiband": 100, "parallel": 100})
        self.assertEqual(processed.shape, drums.shape)
        self.assertTrue(np.isfinite(processed).all())

    def test_invalid_flac_leaves_existing_file_untouched(self):
        target = self.root/"existing.flac"
        target.write_bytes(b"existing content")
        with self.assertRaises(ValueError):
            self.render(output=str(target), bits="32")
        self.assertEqual(target.read_bytes(), b"existing content")

    def test_source_is_never_overwritten(self):
        before = self.source.read_bytes()
        with self.assertRaises(ValueError):
            self.render(output=str(self.source))
        self.assertEqual(self.source.read_bytes(), before)

    def test_negative_transient_never_inverts_polarity(self):
        impulse = np.zeros((44100, 2), dtype=np.float32)
        impulse[100:300] = .8
        result = e.drum_fx(impulse, self.sr, {"transient": -100})
        self.assertGreaterEqual(result.min(), 0)

    def test_final_peak_after_sample_rate_conversion(self):
        drums = np.zeros((44100*4, 2), dtype=np.float32)
        sample = e.synthesized_sample("kick", 44100, "Electronic")
        for at in [.1, .6, 1.1, 1.6, 2.1, 2.6, 3.1]:
            start = int(at*44100)
            drums[start:start+len(sample)] += sample
        e.write_float(self.source, drums, 44100)
        for extension in ["wav", "flac"]:
            result, _, _ = self.render(output=str(self.root/f"drums.{extension}"), normalize=True, lufs=-11, bits="24", sampleRate=48000)
            self.assertLessEqual(result["truePeakDbtp"], -1.0)

    def test_import_unicode_path_and_url_validation(self):
        result = e.import_audio({"source": str(self.source), "directory": str(self.root/"session")})
        self.assertEqual(result["sampleRate"], self.sr)
        self.assertTrue(Path(result["preview"]).exists())
        self.assertEqual(len(result["waveform"]), 700)
        for url in ["http://suno.com/song/abc", "https://example.com/file.wav", "https://suno.com/s/abc"]:
            with self.assertRaises(ValueError):
                e.download_suno(url, self.root)


if __name__ == "__main__":
    unittest.main(verbosity=2)
