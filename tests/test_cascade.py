import struct
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]/"engine"))
import engine as e
import drum_midi as midi
import model_pipeline as models


class CascadeTests(unittest.TestCase):
    def setUp(self):
        self.sr = 44100
        self.dry = np.full((self.sr, 2), .02, np.float32)
        self.parent = str(Path("drums.wav").resolve())
        self.parts = [{"name": name, "path": name+".wav"} for name in midi.GM]
        self.lanes = {}
        for name in midi.GM:
            lane = np.zeros_like(self.dry)
            lane[8820:9040] = .4
            lane[26460:26680] = .2
            self.lanes[name+".wav"] = lane
        self.params = dict(parts=self.parts, partsSource=self.parent, thresholdDb=-40, sensitivity=100, bleed=100)

    def read(self, path, sr=None):
        return self.lanes[str(path)], self.sr

    def test_drumsep_result_caches_each_lane_waveform_without_normalizing(self):
        with tempfile.TemporaryDirectory() as folder:
            stems = []
            for index, name in enumerate(midi.GM):
                path = Path(folder) / (name + ".wav")
                data = self.lanes[name + ".wav"] * (index + 1)
                e.write_float(path, data, self.sr)
                stems.append({"name": name, "path": str(path)})
            with patch.object(models, "separate", return_value={"stems": stems}):
                result = e.separate({"model": "drumsep_mdx23c"})
            self.assertEqual(len(result["stems"]), 6)
            for index, stem in enumerate(result["stems"]):
                self.assertEqual(stem["waveform"], e.file_waveform(stem["path"]))
                self.assertAlmostEqual(max(stem["waveform"]), .4 * (index + 1), places=6)

    def test_isolated_simultaneous_hits_and_velocity(self):
        with patch.object(e, "read_audio", self.read):
            hits = e.trigger_events(self.dry, self.sr, self.params, self.parent)
        self.assertEqual(set(hits), set(midi.GM))
        for events in hits.values():
            np.testing.assert_allclose([p/self.sr for p,_ in events], [.2,.6], atol=.001)
            self.assertAlmostEqual(events[1][1]/events[0][1], .5, places=3)

    def test_stale_missing_or_misaligned_parts_fail(self):
        with patch.object(e, "read_audio", self.read):
            with self.assertRaises(ValueError): e.trigger_events(self.dry, self.sr, self.params, "new-drums.wav")
            with self.assertRaises(ValueError): e.trigger_events(self.dry, self.sr, {**self.params,"parts":self.parts[:5]},self.parent)
            self.lanes["kick.wav"] = self.lanes["kick.wav"][:-30]
            with self.assertRaises(ValueError): e.trigger_events(self.dry, self.sr, self.params,self.parent)
        with self.assertRaises(ValueError): e.trigger_events(self.dry,self.sr,{},self.parent,require_parts=True)

    def test_midi_and_audio_export_rates_share_the_same_hit_decisions(self):
        with patch.object(e, "read_audio", self.read):
            baseline = e.trigger_events(self.dry,self.sr,self.params,self.parent)
            for rate in (48000,96000):
                hits = e.trigger_events(np.zeros((rate,2),np.float32),rate,self.params,self.parent)
                for name in midi.GM:
                    self.assertEqual([v for _,v in hits[name]],[v for _,v in baseline[name]])
                    np.testing.assert_allclose([p/rate for p,_ in hits[name]],[p/self.sr for p,_ in baseline[name]],atol=1/rate)

    def test_layer_preserves_dry_room_and_adds_only_sample(self):
        sample = np.ones((100,2),np.float32)*.1
        params = {**self.params,"replacement":45,"blendMode":"layer"}
        with patch.object(e,"read_audio",self.read), patch.object(e,"load_trigger_samples",return_value=[sample]):
            out,counts = e.replace_drums(self.dry,self.sr,params,self.parent)
            hits = e.trigger_events(self.dry,self.sr,params,self.parent)
        expected = self.dry.copy()
        for lane in hits.values():
            for start,velocity in lane: expected[start:start+100] += sample*.45*velocity
        np.testing.assert_allclose(out,expected,atol=1e-7)
        np.testing.assert_array_equal(out[:8000],self.dry[:8000])
        self.assertEqual(sum(counts.values()),12)
        bypass,_ = e.replace_drums(self.dry,self.sr,{**params,"replacement":0},self.parent)
        np.testing.assert_array_equal(bypass,self.dry)

    def test_extra_lanes_require_real_samples(self):
        for name in ("toms","ride","crash"):
            self.assertEqual(e.load_trigger_samples(name,self.sr,{}),[])

    def test_selected_drum_layer_sample_and_gain_share_one_signal_path(self):
        parts = [{**p, "mute": p["name"] != "kick", "gain": -6} for p in self.parts]
        params = {**self.params, "parts": parts, "mixParts": True, "replacement": 50, "blendMode": "layer"}
        sample = np.ones((100, 2), np.float32)*.1
        with patch.object(e,"read_audio",self.read), patch.object(e,"load_trigger_samples",return_value=[sample]):
            out,counts=e.process_drums(self.dry,self.sr,params,self.parent)
            hits=e.trigger_events(self.dry,self.sr,params,self.parent)
        expected=self.lanes["kick.wav"].copy()
        self.assertEqual(set(hits), {"kick"})
        for start,velocity in hits["kick"]: expected[start:start+100] += sample*.5*velocity
        np.testing.assert_allclose(out,expected*10**(-6/20),atol=1e-7)
        self.assertEqual(set(counts), {"kick"})
        with patch.object(e,"read_audio",self.read), patch.object(e,"load_trigger_samples",return_value=[]):
            out,_=e.process_drums(self.dry,self.sr,{**params,"replacement":100,"blendMode":"replace"},self.parent)
        np.testing.assert_allclose(out,self.lanes["kick.wav"]*10**(-6/20),atol=1e-7)

    def test_midi_channel_timing_velocity_and_mapping(self):
        hits={name:[(8820,.5),(26460,1)] for name in midi.GM}
        encoded=midi.encode(hits,self.sr,137.3,{"kick":35})
        self.assertEqual(struct.unpack(">IHHH",encoded[4:14]),(6,0,1,960))
        self.assertEqual(struct.unpack(">I",encoded[18:22])[0],len(encoded)-22)
        tempo=int.from_bytes(encoded[26:29],"big")
        pos=29; tick=0; ons=[]; offs=[]
        while pos<len(encoded):
            delta=0
            while True:
                value=encoded[pos];pos+=1;delta=(delta<<7)|(value&127)
                if value<128:break
            tick+=delta
            status=encoded[pos];pos+=1
            if status==255: break
            note,velocity=encoded[pos:pos+2];pos+=2
            self.assertIn(status,(0x99,0x89))
            (ons if status==0x99 else offs).append((tick*tempo/960/1e6,note,velocity))
        self.assertEqual(len(ons),12);self.assertEqual(len(offs),12)
        self.assertEqual({n for _,n,_ in ons},{35,38,45,42,51,49})
        np.testing.assert_allclose([t for t,_,_ in ons], [.2]*6+[.6]*6,atol=.0005)
        self.assertEqual({v for _,_,v in ons},{64,127})
        for bpm,mapping in [(float("nan"),{}),(1,{}),(120,{"kick":38}),(120,{"kick":128})]:
            with self.assertRaises(ValueError): midi.encode(hits,self.sr,bpm,mapping)

    def test_failed_download_keeps_cache_and_cleans_partial(self):
        from io import BytesIO
        class Response(BytesIO):
            headers={"Content-Length":"3"}
        with tempfile.TemporaryDirectory() as folder:
            old=Path(folder)/"sw.ckpt";old.write_bytes(b"old")
            with patch.object(models.urllib.request,"urlopen",return_value=Response(b"bad")):
                with self.assertRaises(ValueError): models.assets("sw",folder,lambda _:None)
            self.assertEqual(old.read_bytes(),b"old")
            self.assertFalse(list(Path(folder).glob("*.part")))


if __name__ == "__main__": unittest.main()
