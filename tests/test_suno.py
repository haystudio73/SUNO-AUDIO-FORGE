"""Offline HTTP regression cases; the live user-song check is opt-in separately."""
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
from urllib.error import HTTPError, URLError
from urllib.request import Request

import numpy as np
import soundfile as sf

sys.path.insert(0, str(Path(__file__).resolve().parents[1]/"engine"))
import engine
import suno_download as suno

ID = "52e94026-fa85-4779-b54e-da4d8df58d62"
PAGE = f"https://suno.com/song/{ID}"
API = f"https://{suno.API_HOST}/api/clip/{ID}"
AUDIO = f"https://cdn1.suno.ai/{ID}.mp3"
VIDEO = f"https://cdn1.suno.ai/{ID}.mp4"
MP4 = b"\x00\x00\x00\x20ftypisom"+b"\x00"*64


class Response(io.BytesIO):
    def __init__(self, data, content_type="application/json", length=None):
        super().__init__(data)
        self.headers = {"Content-Type": content_type, "Content-Length": str(len(data) if length is None else length)}


class SunoTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()

    def metadata(self, **overrides):
        return Response(json.dumps({"id": ID, "title": "Bài thử tiếng Việt", "audio_url": AUDIO,
                                    "video_url": VIDEO, **overrides}).encode())

    def test_current_forbidden_audio_uses_video_not_encoded_stream(self):
        encoded = {"url": f"https://d2lwuy8qc234o3.cloudfront.net/1/clip/{ID}.m4a", "encoding": "1.0.0"}
        with patch.object(suno, "open_public", side_effect=[
                self.metadata(audio_url=f"https://{suno.API_HOST}/api/forbidden", media_urls=[encoded]),
                Response(MP4, "video/mp4")]) as http:
            result = suno.download(PAGE, self.root)
        self.assertEqual([call.args[0] for call in http.call_args_list], [API, VIDEO])
        self.assertEqual(result["downloadKind"], "video")
        self.assertEqual(result["name"], "Bài thử tiếng Việt")
        self.assertEqual(result["path"].suffix, ".mp4")
        self.assertEqual(result["path"].read_bytes(), MP4)

    def test_403_or_nonmedia_audio_falls_back_without_partial_files(self):
        for response in [HTTPError(AUDIO, 403, "Forbidden", {}, None),
                         Response(b"encrypted/random bytes", "audio/mp4"),
                         Response(b"<html>blocked</html>", "text/html")]:
            with patch.object(suno, "open_public", side_effect=[self.metadata(), response, Response(MP4, "video/mp4")]):
                result = suno.download(PAGE, self.root)
            self.assertEqual(result["downloadKind"], "video")
            self.assertFalse(list(self.root.glob("*.part")))

    def test_signed_audio_query_preserved_and_video_not_fetched(self):
        signed = AUDIO+"?Signature=test&Key-Pair-Id=test"
        with patch.object(suno, "open_public", side_effect=[self.metadata(audio_url=signed), Response(b"ID3"+b"\0"*80, "audio/mpeg")]) as http:
            result = suno.download(PAGE+"?source=share", self.root)
        self.assertEqual(http.call_args_list[-1].args[0], signed)
        self.assertEqual(http.call_count, 2)
        self.assertEqual(result["sourceUrl"], PAGE)

    def test_unavailable_metadata_and_media_have_actionable_errors(self):
        for response in [HTTPError(API, 403, "Forbidden", {}, None), HTTPError(API, 404, "Not found", {}, None)]:
            with patch.object(suno, "open_public", side_effect=response), self.assertRaisesRegex(ValueError, "Mở audio từ máy"):
                suno.download(PAGE, self.root)
        with patch.object(suno, "open_public", side_effect=URLError("DNS")), self.assertRaisesRegex(ValueError, "Kiểm tra mạng"):
            suno.download(PAGE, self.root)
        with patch.object(suno, "open_public", return_value=self.metadata(audio_url="", video_url="")), self.assertRaisesRegex(ValueError, "không cung cấp"):
            suno.download(PAGE, self.root)

    def test_size_truncation_and_validation_failure_clean_up(self):
        for response in [Response(MP4, "video/mp4", suno.MAX_DOWNLOAD+1), Response(MP4, "video/mp4", len(MP4)+1), Response(b"", "video/mp4")]:
            with patch.object(suno, "open_public", return_value=response), self.assertRaises(ValueError):
                suno.download(VIDEO, self.root)
            self.assertEqual(list(self.root.iterdir()), [])
        def invalid(path):
            raise ValueError("No audio stream")
        with patch.object(suno, "open_public", return_value=Response(MP4, "video/mp4")), self.assertRaisesRegex(ValueError, "No audio stream"):
            suno.download(VIDEO, self.root, validate=invalid)
        self.assertEqual(list(self.root.iterdir()), [])
        with patch.object(suno, "MAX_DOWNLOAD", 16), patch.object(suno, "open_public", return_value=Response(MP4, "video/mp4")) as http, self.assertRaises(ValueError):
            del http.return_value.headers["Content-Length"]
            suno.download(VIDEO, self.root)
        self.assertEqual(list(self.root.iterdir()), [])

    def test_untrusted_urls_and_redirects_rejected_before_network(self):
        bad_urls = ["http://suno.com/song/"+ID, "https://suno.com.evil.test/song/"+ID,
                    "https://user:pass@suno.com/song/"+ID, "https://suno.com:444/song/"+ID,
                    "https://127.0.0.1/a.mp3", "file:///audio.wav"]
        for url in bad_urls:
            with patch.object(suno, "open_public") as http, self.assertRaises(ValueError):
                suno.download(url, self.root)
            http.assert_not_called()
        for url in ["http://cdn1.suno.ai/file.mp3", "https://example.com/file.mp3", "https://127.0.0.1/private"]:
            with self.assertRaises(ValueError):
                suno.SafeRedirect(suno.MEDIA_HOSTS).redirect_request(Request(AUDIO), None, 302, "Found", {}, url)
        self.assertEqual(suno.media_candidates({"audio_url": "https://evil.cloudfront.net/file.mp3"}), [])

    def test_malformed_or_wrong_clip_metadata_never_downloads(self):
        for response in [Response(b"not JSON"), self.metadata(id="different-id"), Response(b"[]")]:
            with patch.object(suno, "open_public", return_value=response) as http, self.assertRaises(ValueError):
                suno.download(PAGE, self.root)
            self.assertEqual(http.call_count, 1)

    def test_import_retains_title_source_and_decodes_real_audio(self):
        buffer = io.BytesIO()
        sr = 48000
        tone = .1*np.sin(2*np.pi*440*np.arange(sr)/sr)
        sf.write(buffer, np.column_stack([tone, tone]), sr, format="WAV", subtype="PCM_16")
        wav_url = f"https://cdn1.suno.ai/{ID}.wav"
        with patch.object(suno, "open_public", side_effect=[self.metadata(audio_url=wav_url), Response(buffer.getvalue(), "audio/wav")]):
            result = engine.import_audio({"url": PAGE, "directory": str(self.root)})
        self.assertEqual(result["name"], "Bài thử tiếng Việt")
        self.assertEqual(result["sourceUrl"], PAGE)
        self.assertEqual(result["downloadKind"], "audio")
        self.assertEqual(result["sampleRate"], sr)
        self.assertAlmostEqual(result["duration"], 1)
        self.assertTrue(Path(result["preview"]).is_file())


if __name__ == "__main__":
    unittest.main(verbosity=2)
