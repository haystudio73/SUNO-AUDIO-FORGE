"""Resolve Suno's public metadata instead of guessing an unsigned MP3 address.

Only ordinary, unencrypted public media is supported. No account cookies, DRM
keys, or third-party downloader services are used.
"""
from i18n import tr
import json
import uuid
from http.client import HTTPException
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.parse import urlparse
from urllib.request import HTTPRedirectHandler, Request, build_opener


PAGE_HOSTS = {"suno.com", "www.suno.com", "suno.ai", "www.suno.ai"}
CDN_HOSTS = {"cdn1.suno.ai", "cdn2.suno.ai", "cdn1.suno.com", "cdn2.suno.com"}
API_HOST = "studio-api.prod.suno.com"
# Exact distribution observed in Suno's public clip metadata, not *.cloudfront.net.
MEDIA_HOSTS = CDN_HOSTS | {"d2lwuy8qc234o3.cloudfront.net"}
MAX_DOWNLOAD = 512*1024*1024
HEADERS = {"User-Agent": "StemForge/0.3.1", "Referer": "https://suno.com/"}
DOWNLOAD_HELP = "Mở bài trên Suno → Download, rồi dùng ‘Mở audio từ máy’ để nhập file."


def check_url(url, hosts):
    if not isinstance(url, str):
        raise ValueError("URL Suno không hợp lệ.")
    parsed = urlparse(url)
    if (parsed.scheme != "https" or parsed.hostname not in hosts or parsed.username
            or parsed.password or parsed.port not in (None, 443)):
        raise ValueError("Chỉ nhận liên kết HTTPS trên máy chủ Suno được hỗ trợ.")
    return parsed


class SafeRedirect(HTTPRedirectHandler):
    def __init__(self, hosts):
        super().__init__()
        self.hosts = hosts

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        check_url(newurl, self.hosts)
        return super().redirect_request(req, fp, code, msg, headers, newurl)


def open_public(url, hosts):
    check_url(url, hosts)
    return build_opener(SafeRedirect(hosts)).open(Request(url, headers=HEADERS), timeout=45)


def media_candidates(clip):
    candidates = []
    def add(url, kind):
        if not url:
            return
        try:
            parsed = check_url(url, MEDIA_HOSTS)
        except ValueError:
            return
        extension = Path(parsed.path).suffix.lower()
        if extension not in (".mp3", ".m4a", ".mp4", ".wav", ".flac", ".ogg", ".webm"):
            return
        if not any(item[0] == url for item in candidates):
            candidates.append((url, kind, extension))
    add(clip.get("audio_url"), "audio")
    for media in clip.get("media_urls") or []:
        # Suno's encoded streams are not normal audio containers. Never try to
        # decrypt them or present those bytes as an MP3 download.
        if isinstance(media, dict) and not media.get("encoding") and not media.get("encryption"):
            add(media.get("url"), "audio")
    add(clip.get("video_url"), "video")
    return candidates


def looks_like_media(data):
    return (data.startswith((b"ID3", b"fLaC", b"OggS", b"\x1aE\xdf\xa3"))
            or len(data) >= 12 and (data[4:8] == b"ftyp" or data[:4] == b"RIFF" and data[8:12] == b"WAVE")
            or len(data) >= 2 and data[0] == 255 and data[1] & 224 == 224)


def download(url, directory, progress=lambda message: None, validate=None):
    parsed = check_url(url, PAGE_HOSTS | CDN_HOSTS)
    source_url, title = url, ""
    if parsed.hostname in PAGE_HOSTS:
        pieces = parsed.path.strip("/").split("/")
        if len(pieces) != 2 or pieces[0] != "song":
            raise ValueError("Dán URL đầy đủ suno.com/song/<id>; mở link chia sẻ /s/ trong trình duyệt để lấy URL bài hát.")
        try:
            song_id = str(uuid.UUID(pieces[1]))
        except ValueError:
            raise ValueError("ID bài Suno không hợp lệ. Dán lại URL đầy đủ từ trang bài hát.") from None
        source_url = f"https://suno.com/song/{song_id}"
        progress(tr("Đang lấy thông tin và link media hiện tại từ Suno…"))
        try:
            with open_public(f"https://{API_HOST}/api/clip/{song_id}", {API_HOST}) as response:
                raw = response.read(1024*1024+1)
            if len(raw) > 1024*1024:
                raise ValueError("Phản hồi Suno quá lớn.")
            clip = json.loads(raw)
            if not isinstance(clip, dict) or str(clip.get("id", "")).lower() != song_id:
                raise ValueError("Suno trả thông tin không khớp bài hát.")
            title = str(clip.get("title") or "")[:500]
            candidates = media_candidates(clip)
        except HTTPError as exc:
            exc.close()
            reason = "Bài không còn tồn tại hoặc không công khai." if exc.code == 404 else "Suno từ chối cung cấp thông tin bài hát."
            raise ValueError(f"{reason} HTTP {exc.code}. {DOWNLOAD_HELP}") from None
        except (URLError, TimeoutError, HTTPException) as exc:
            raise ValueError("Không kết nối được Suno. Kiểm tra mạng rồi thử lại.") from exc
        except (json.JSONDecodeError, UnicodeError):
            raise ValueError("Suno trả phản hồi không hợp lệ. Thử lại sau hoặc tải file trên Suno.") from None
    else:
        extension = Path(parsed.path).suffix.lower()
        candidates = [(url, "video" if extension == ".mp4" else "audio", extension)]
        if extension not in (".mp3", ".m4a", ".mp4", ".wav", ".flac", ".ogg", ".webm"):
            raise ValueError("Liên kết CDN phải trỏ tới file audio/video được hỗ trợ.")
    if not candidates:
        raise ValueError("Suno không cung cấp file audio/video công khai có thể nhập. " + DOWNLOAD_HELP)
    directory = Path(directory)
    directory.mkdir(parents=True, exist_ok=True)
    failures = []
    for media_url, kind, extension in candidates:
        destination = directory/f"suno-{uuid.uuid4().hex}{extension}"
        partial = destination.with_suffix(extension+".part")
        progress(tr("Đang tải video công khai để lấy audio (không phải WAV gốc)…") if kind == "video" else tr("Đang tải audio Suno công khai…"))
        try:
            with open_public(media_url, MEDIA_HOSTS) as response:
                content_type = response.headers.get("Content-Type", "").lower()
                if any(value in content_type for value in ("text/", "json", "xml")):
                    raise ValueError("Suno trả trang lỗi thay vì media.")
                length = response.headers.get("Content-Length")
                if length and int(length) > MAX_DOWNLOAD:
                    raise ValueError("File tải xuống vượt giới hạn 512 MB.")
                size, first = 0, True
                with partial.open("wb") as stream:
                    while chunk := response.read(1024*1024):
                        if first and not looks_like_media(chunk):
                            raise ValueError("Media không phải audio/video thông thường hoặc đang được mã hóa.")
                        first = False
                        size += len(chunk)
                        if size > MAX_DOWNLOAD:
                            raise ValueError("File tải xuống vượt giới hạn 512 MB.")
                        stream.write(chunk)
                if size == 0 or length and size != int(length):
                    raise ValueError("File Suno tải chưa đầy đủ. Thử lại.")
            if validate:
                validate(partial)
            partial.replace(destination)
            return {"path": destination, "name": title or destination.stem, "sourceUrl": source_url, "downloadKind": kind}
        except HTTPError as exc:
            failures.append(f"{kind}: HTTP {exc.code}")
            exc.close()
        except (URLError, TimeoutError, HTTPException):
            failures.append(f"{kind}: không kết nối được hoặc quá thời gian chờ")
        except ValueError as exc:
            failures.append(f"{kind}: {exc}")
        finally:
            partial.unlink(missing_ok=True)
    detail = "; ".join(failures)
    raise ValueError(f"Không tải được media công khai từ Suno ({detail}). Bài Public vẫn có thể bị hạn chế tải. {DOWNLOAD_HELP}")
