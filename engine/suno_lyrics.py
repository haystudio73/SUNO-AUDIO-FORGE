"""Public Suno lyrics; independent implementation, no Whisper or account tokens."""
import json
import math
import uuid
from pathlib import PurePosixPath
from urllib.error import HTTPError, URLError

from i18n import tr
from suno_download import API_HOST, PAGE_HOSTS, CDN_HOSTS, check_url, open_public

MAX_RESPONSE = 1024 * 1024


def song_id(url):
    parsed = check_url(url, PAGE_HOSTS | CDN_HOSTS)
    parts = parsed.path.strip('/').split('/')
    value = parts[1] if parsed.hostname in PAGE_HOSTS and len(parts) == 2 and parts[0] == 'song' else None
    if parsed.hostname in CDN_HOSTS:
        value = PurePosixPath(parsed.path).stem
    try:
        return str(uuid.UUID(value or ''))
    except ValueError:
        raise ValueError(tr('Cần URL Suno đầy đủ dạng suno.com/song/<id> để lấy lời.')) from None


def request_json(path):
    with open_public(f'https://{API_HOST}{path}', {API_HOST}) as response:
        raw = response.read(MAX_RESPONSE + 1)
    if len(raw) > MAX_RESPONSE:
        raise ValueError(tr('Dữ liệu lyrics Suno vượt giới hạn 1 MB.'))
    obj = json.loads(raw)
    if not isinstance(obj, dict):
        raise ValueError(tr('Suno trả dữ liệu lyrics không hợp lệ.'))
    return obj


def number(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def clean(value):
    return ''.join(c for c in value if c in '\n\t' or ord(c) >= 32).strip() if isinstance(value, str) else ''


def aligned_segments(data, duration):
    payload = data.get('data') if isinstance(data.get('data'), dict) else data
    rows = payload.get('aligned_lyrics')
    if not isinstance(rows, list):
        return []
    result = []
    for row in rows[:10000]:
        if not isinstance(row, dict):
            continue
        words = row.get('words') if isinstance(row.get('words'), list) else []
        text = clean(row.get('text')) or ' '.join(clean(w.get('text') or w.get('word')) for w in words if isinstance(w, dict)).strip()
        if not text:
            continue
        start, end = row.get('start_s'), row.get('end_s')
        timed = [w for w in words if isinstance(w, dict) and number(w.get('start_s')) and number(w.get('end_s')) and 0 <= w['start_s'] < w['end_s'] <= duration]
        if timed and len(timed) == len(words):
            start, end = min(w['start_s'] for w in timed), max(w['end_s'] for w in timed)
        if not (number(start) and number(end) and 0 <= start < end <= duration):
            start = end = None
        result.append(dict(text=text, start=start, end=end))
    return result


def fetch(req, progress=lambda _: None):
    identifier = song_id(req.get('url', ''))
    progress(tr('Đang lấy lời trực tiếp từ Suno…'))
    try:
        clip = request_json(f'/api/clip/{identifier}')
        if str(clip.get('id', '')).lower() != identifier:
            raise ValueError(tr('Thông tin lyrics không khớp bài Suno.'))
        metadata = clip.get('metadata') if isinstance(clip.get('metadata'), dict) else {}
        duration = req.get('duration') or metadata.get('duration')
        if not number(duration) or duration <= 0:
            raise ValueError(tr('Chưa có thời lượng audio hợp lệ để đọc lyrics.'))
        segments = []
        try:
            segments = aligned_segments(request_json(f'/api/gen/{identifier}/aligned_lyrics/v2/'), duration)
        except HTTPError as error:
            error.close()  # 401/403: public text may still be available, never fetch account cookies.
        except (URLError, TimeoutError, OSError, ValueError, UnicodeError):
            pass
        if not segments:
            segments = [dict(text=line.strip(), start=None, end=None) for line in clean(metadata.get('prompt')).splitlines() if line.strip()]
        if not segments:
            raise ValueError(tr('Suno chưa cung cấp lời cho bài này. Bản lời hiện tại được giữ nguyên.'))
        count = sum(s['start'] is not None for s in segments)
        return dict(segments=segments, language='', provider='suno', sourceUrl=f'https://suno.com/song/{identifier}',
                    timing='suno' if count == len(segments) else 'mixed' if count else 'untimed', duration=duration)
    except HTTPError as error:
        code = error.code
        error.close()
        raise ValueError(tr('Không lấy được lời công khai từ Suno. Bản lời hiện tại được giữ nguyên.') + f' HTTP {code}.') from None
    except (URLError, TimeoutError, OSError) as error:
        raise ValueError(tr('Không kết nối được Suno. Bản lời hiện tại được giữ nguyên.')) from error
