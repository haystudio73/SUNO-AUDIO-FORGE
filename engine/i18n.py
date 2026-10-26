"""Catalog localization for first-party engine progress; never translates audio metadata."""
import json
import os
from pathlib import Path
from functools import lru_cache


@lru_cache(maxsize=1)
def _strings():
    language = os.environ.get('SAF_LANGUAGE', 'vi')
    result = {}
    for folder in (Path(__file__).resolve().parent.parent/'Languages',
                   Path(os.environ.get('SAF_USER_LANGUAGES', str(Path.home()/'.saf-languages')))):
        if not folder.is_dir():
            continue
        for file in sorted(folder.glob('*.json')):
            try:
                if file.stat().st_size > 4_000_000: continue
                data = json.loads(file.read_text(encoding='utf-8-sig'))
                if data.get('code') == language:
                    result.update({k:v for k,v in data.get('strings', {}).items() if isinstance(v,str) and v})
            except (OSError, ValueError, AttributeError, TypeError):
                continue
    return result


def tr(source, *args):
    translated = _strings().get(source, source)
    try:
        return translated.format(*args) if args else translated
    except (ValueError, IndexError, KeyError):
        return source.format(*args) if args else source
