import io
import json
import sys
import unittest
from pathlib import Path
from unittest.mock import patch
from urllib.error import HTTPError, URLError

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'engine'))
import suno_lyrics as lyrics

ID = '52e94026-fa85-4779-b54e-da4d8df58d62'
URL = 'https://suno.com/song/' + ID


def response(data):
    return io.BytesIO(json.dumps(data, ensure_ascii=False).encode())


class SunoLyricsTests(unittest.TestCase):
    def clip(self, **values):
        return response(dict(id=ID, metadata={'prompt': '[Verse]\nLời thử tiếng Việt\nDòng thứ hai', 'duration': 20}, **values))

    def test_public_text_when_alignment_requires_login(self):
        with patch.object(lyrics, 'open_public', side_effect=[self.clip(), HTTPError(URL, 401, 'Unauthorized', {}, None)]):
            result = lyrics.fetch({'url': URL, 'duration': 20})
        self.assertEqual(result['timing'], 'untimed')
        self.assertEqual(result['segments'][1]['text'], 'Lời thử tiếng Việt')
        self.assertTrue(all(s['start'] is None and s['end'] is None for s in result['segments']))
        self.assertEqual(result['provider'], 'suno')

    def test_line_and_word_timing_preserved(self):
        aligned = {'data': {'aligned_lyrics': [
            {'text': 'Có thời gian', 'start_s': 1, 'end_s': 3},
            {'text': 'Giữ dấu Việt', 'words': [{'text': 'Giữ', 'start_s': 4, 'end_s': 4.5}, {'text': 'dấu Việt', 'start_s': 4.6, 'end_s': 6}]}
        ]}}
        with patch.object(lyrics, 'open_public', side_effect=[self.clip(), response(aligned)]):
            result = lyrics.fetch({'url': URL, 'duration': 20})
        self.assertEqual(result['timing'], 'suno')
        self.assertEqual([(s['start'], s['end']) for s in result['segments']], [(1, 3), (4, 6)])

    def test_invalid_timing_is_not_invented(self):
        rows = [{'text': 'Sai mốc', 'start_s': -1, 'end_s': 3}, {'text': 'Quá bài', 'start_s': 19, 'end_s': 25}, {'text': 'Đúng', 'start_s': 4, 'end_s': 5}]
        with patch.object(lyrics, 'open_public', side_effect=[self.clip(), response({'aligned_lyrics': rows})]):
            result = lyrics.fetch({'url': URL, 'duration': 20})
        self.assertEqual(result['timing'], 'mixed')
        self.assertIsNone(result['segments'][0]['start'])
        self.assertIsNone(result['segments'][1]['end'])

    def test_empty_or_unavailable_metadata_fails_without_fabricating_lyrics(self):
        for metadata in [{'id': ID, 'metadata': {}}, {'id': 'wrong', 'metadata': {'prompt': 'wrong song'}}]:
            with patch.object(lyrics, 'open_public', side_effect=[response(metadata), response({})]):
                with self.assertRaises(ValueError): lyrics.fetch({'url': URL, 'duration': 20})
        with patch.object(lyrics, 'open_public', side_effect=URLError('offline')):
            with self.assertRaisesRegex(ValueError, 'giữ nguyên'): lyrics.fetch({'url': URL, 'duration': 20})

    def test_host_id_and_response_limits(self):
        for url in ['https://example.com/song/' + ID, 'http://suno.com/song/' + ID, 'https://suno.com/s/short', 'https://user@suno.com/song/' + ID]:
            with patch.object(lyrics, 'open_public') as http:
                with self.assertRaises(ValueError): lyrics.fetch({'url': url, 'duration': 20})
                http.assert_not_called()
        self.assertEqual(lyrics.song_id('https://cdn1.suno.ai/' + ID + '.mp3'), ID)
        with patch.object(lyrics, 'open_public', return_value=io.BytesIO(b'x' * (lyrics.MAX_RESPONSE + 1))):
            with self.assertRaisesRegex(ValueError, '1 MB'): lyrics.fetch({'url': URL, 'duration': 20})

    def test_missing_alignment_falls_back_to_metadata(self):
        with patch.object(lyrics, 'open_public', side_effect=[self.clip(), response({'aligned_lyrics': []})]):
            result = lyrics.fetch({'url': URL, 'duration': 20})
        self.assertEqual(len(result['segments']), 3)


if __name__ == '__main__':
    unittest.main()
