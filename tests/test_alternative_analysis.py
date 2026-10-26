import sys
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import json
import numpy as np
import soundfile as sf

sys.path.insert(0, str(Path(__file__).resolve().parents[1]/'engine'))
from alternative_analysis import analyze_wav
import test_audio_analysis as fixtures


class AlternativeAnalysisTests(unittest.TestCase):
    def run_audio(self, y):
        with tempfile.TemporaryDirectory() as folder:
            p=Path(folder)/'nhạc thử.wav'
            sf.write(p, y, 22050, subtype='FLOAT')
            result=analyze_wav(p)
            json.dumps(result, allow_nan=False)
            return result

    def test_known_tempo_and_stereo_polarity(self):
        fixture=fixtures.AudioAnalysisTests()
        for bpm in (90,120,174):
            y=fixture.clicks(bpm)
            result=self.run_audio(np.column_stack([y,-y]))
            self.assertAlmostEqual(result['bpm'],bpm,delta=1)
            self.assertIsNone(result['key'])

    def test_major_and_minor(self):
        for tonic,minor,key in [(60,False,'C Major'),(57,True,'A Minor')]:
            result=self.run_audio(fixtures.AudioAnalysisTests().harmony(tonic,minor))
            self.assertEqual(result['key'],key)

    def test_abstains_on_silence_noise_short_and_single_tone(self):
        for y in [np.zeros(22050*8), np.ones(100)*.1,
                  np.sin(2*np.pi*440*np.arange(22050*8)/22050)*.2,
                  np.random.default_rng(42).normal(0,.1,22050*10)]:
            result=self.run_audio(y)
            self.assertIsNone(result['bpm'])
            self.assertIsNone(result['key'])

    def test_upstream_errors_skip_without_fabricated_defaults(self):
        with patch('vendor.libraz_bpm.tempo.BPMDetector.detect',side_effect=RuntimeError('test failure')):
            result=self.run_audio(fixtures.AudioAnalysisTests().clicks(120))
            self.assertIsNone(result['bpm'])
        with patch('vendor.libraz_bpm.key_detector.KeyDetector.detect_key',return_value={
                'key':'C','mode':'Major','confidence':5,'key_strength':.1,'analysis_notes':'Fallback due to audio processing error'}):
            result=self.run_audio(fixtures.AudioAnalysisTests().harmony())
            self.assertIsNone(result['key'])

    def test_long_track_is_bounded(self):
        result=self.run_audio(fixtures.AudioAnalysisTests().clicks(120,140))
        self.assertLessEqual(result['analyzedSeconds'],90)
        self.assertAlmostEqual(result['bpm'],120,delta=1)

if __name__=='__main__': unittest.main()
