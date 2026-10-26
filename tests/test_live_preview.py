import base64
import copy
import json
from pathlib import Path
import sys
import tempfile
import time
import unittest
from unittest.mock import patch
import numpy as np
import soundfile as sf
from scipy import signal

sys.path.insert(0, str(Path(__file__).resolve().parents[1]/'engine'))
import live_preview as live
import engine as dsp


class LiveTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.folder=Path(self.tmp.name)
        t=np.arange(10*48000)/48000
        self.audio=np.stack([.1*np.sin(2*np.pi*80*t)+.03*np.sin(2*np.pi*3200*t),.1*np.sin(2*np.pi*80*t)],axis=1).astype('float32')
        self.source=self.folder/'original.wav';sf.write(self.source,self.audio,48000,subtype='FLOAT')
        self.request={'source':str(self.source),'processingMode':'legacy','refix':{'enabled':False}}
        self.mixer=live.LiveMixer();dsp.progress=lambda _:None
    def tearDown(self):self.tmp.cleanup()
    def pull(self,mix=None,start=48000,count=9600):
        result=self.mixer.pull({'mix':mix or self.request,'frame':start,'frames':count})
        return np.frombuffer(base64.b64decode(result['pcm']),dtype='<f4').reshape(-1,2),result
    def test_bypass_cache_and_no_audio_writes(self):
        before=set(self.folder.iterdir())
        with patch.object(dsp,'run_ffmpeg',side_effect=AssertionError('Preview must not encode files')):
            audio,first=self.pull();again,second=self.pull(start=57600)
        np.testing.assert_array_equal(audio,self.audio[48000:57600])
        self.assertEqual(first['diskReads'],second['diskReads'])
        self.assertEqual(before,set(self.folder.iterdir()))
        self.assertLess(second['cacheBytes'],256*1024*1024)
    def test_stem_gain_mute_solo_and_no_original_overlap(self):
        paths=[]
        for i in range(2):
            path=self.folder/f'stem{i}.wav';sf.write(path,self.audio*.25,48000,subtype='FLOAT');paths.append(str(path))
        req={**self.request,'stems':[{'name':'bass','path':paths[0],'gain':0},{'name':'vocals','path':paths[1],'gain':0}]}
        audio,_=self.pull(req);np.testing.assert_allclose(audio,self.audio[48000:57600]*.5,atol=1e-7)
        req['stems'][0].update(gain=-6.020599913,solo=True)
        audio,_=self.pull(req,start=96000);np.testing.assert_allclose(audio,self.audio[96000:105600]*.125,atol=1e-7)
        req['stems'][0]['mute']=True;audio,_=self.pull(req,start=120000);self.assertEqual(float(np.abs(audio).max()),0)
    def test_waveform_preview_stays_in_memory(self):
        before=set(self.folder.iterdir())
        with patch.object(dsp,'run_ffmpeg',side_effect=AssertionError('Waveform must not encode files')):
            result=dsp.preview_waveform(self.request)
        self.assertTrue(len(result['waveform'])>0)
        np.testing.assert_allclose(result['waveform'],dsp.waveform(self.audio),atol=1e-7)
        self.assertEqual(before,set(self.folder.iterdir()))
    def test_drumsep_only_selected_parts_replace_parent(self):
        parts=[]
        for name in ('kick','snare','toms','hat','ride','crash'):
            path=self.folder/(name+'.wav');sf.write(path,self.audio*.1,48000,subtype='FLOAT')
            parts.append({'name':name,'path':str(path),'gain':0,'mute':name!='kick'})
        req={**self.request,'stems':[{'name':'drums','path':str(self.source)}],
             'drums':{'mixParts':True,'parts':parts,'partsSource':str(self.source)}}
        audio,_=self.pull(req);np.testing.assert_allclose(audio,self.audio[48000:57600]*.1,atol=1e-7)
        parts[0]['mute']=True;audio,_=self.pull(req,start=96000);self.assertFalse(np.any(audio))
    def test_refix_drum_fx_and_structural_match_shared_dsp_away_from_edges(self):
        for mode in ('legacy','structural'):
            req={**self.request,'processingMode':mode,'refix':{'enabled':True},'stereo':{'attack80':4,'mud180':-5}}
            expected=dsp.mix_audio(req)[0]
            audio,_=self.pull(req,start=3*48000)
            np.testing.assert_allclose(audio,expected[3*48000:3*48000+9600],atol=2e-5)
        req={**self.request,'stems':[{'name':'drums','path':str(self.source)}],'drums':{'transient':45,'multiband':35,'parallel':30}}
        audio,_=self.pull(req,start=3*48000);expected=dsp.mix_audio(req)[0]
        np.testing.assert_allclose(audio,expected[3*48000:3*48000+9600],atol=2e-5)
    def test_parameter_crossfade_uses_same_time_and_then_new_settings(self):
        req={**self.request,'stems':[{'name':'bass','path':str(self.source),'gain':0}]}
        self.pull(req);req=copy.deepcopy(req);req['stems'][0]['gain']=-48
        audio,_=self.pull(req,start=57600)
        np.testing.assert_allclose(audio[960:],self.audio[58560:67200]*10**(-48/20),atol=1e-7)
        np.testing.assert_allclose(audio[0],self.audio[57600],atol=1e-7)
    def test_structural_blocks_match_continuous_transients_across_seams(self):
        audio=self.audio.copy();rng=np.random.default_rng(17)
        for start in range(10000,len(audio)-6000,13711):
            audio[start:start+6000]+=rng.normal(0,.1,(6000,2))*np.exp(-np.arange(6000)[:,None]/900)
        sf.write(self.source,audio,48000,subtype='FLOAT')
        req={**self.request,'processingMode':'structural','stereo':{'attack80':5,'attack3200':4,'mud180':-6}}
        expected=dsp.mix_audio(req)[0]
        chunks=[self.pull(req,start=start)[0] for start in range(48000,144000,9600)]
        np.testing.assert_allclose(np.concatenate(chunks),expected[48000:144000],atol=3e-5)
    def test_rate_conversion_page_seam_and_bound(self):
        source=self.folder/'441.wav';native=signal.resample_poly(self.audio,147,160,axis=0);sf.write(source,native,44100,subtype='FLOAT')
        cache=live.PcmCache(limit=4*1024*1024)
        actual=cache.read(source,7*48000,9*48000)
        expected=signal.resample_poly(native,160,147,axis=0)[7*48000:9*48000]
        np.testing.assert_allclose(actual,expected,atol=1e-6)
        cache.read(self.source,0,48000)
        self.assertLessEqual(cache.bytes,cache.limit)
    def test_invalid_range_and_nonfinite_inputs(self):
        with self.assertRaises(ValueError):self.pull(start=-1)
        with self.assertRaises(ValueError):self.pull(count=48001)
        bad=self.folder/'bad.wav';audio=self.audio.copy();audio[50000]=np.nan;sf.write(bad,audio,48000,subtype='FLOAT')
        with self.assertRaises(ValueError):self.pull({**self.request,'source':str(bad)})


if __name__=='__main__':unittest.main()
