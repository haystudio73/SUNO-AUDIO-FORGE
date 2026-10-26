"""Transient-only GM percussion export; no pitch estimation or quantization."""
import math
import os
from pathlib import Path
import struct
import uuid

GM = {"kick": 36, "snare": 38, "toms": 45, "hat": 42, "ride": 51, "crash": 49}
PPQ = 960


def variable(value):
    if not 0 <= value <= 0x0fffffff:
        raise ValueError("MIDI delta-time ngoài giới hạn.")
    result = [value & 127]
    while value := value >> 7:
        result.insert(0, (value & 127) | 128)
    return bytes(result)


def encode(hits, sr, bpm, mapping):
    if not math.isfinite(bpm) or not 20 <= bpm <= 300:
        raise ValueError("Tempo MIDI phải từ 20 đến 300 BPM.")
    notes = {**GM, **mapping}
    if set(notes) != set(GM) or any(type(n) is not int or not 0 <= n <= 127 for n in notes.values()) or len(set(notes.values())) != 6:
        raise ValueError("Mỗi rãnh cần một MIDI note khác nhau, từ 0 đến 127.")
    tempo = round(60000000/bpm)
    # Use encoded tempo for conversion so BPM rounding cannot accumulate drift.
    ticks_per_second = PPQ*1000000/tempo
    events = []
    for name, lane in hits.items():
        starts = [round(pos/sr*ticks_per_second) for pos, _ in lane]
        for i, ((_, velocity), start) in enumerate(zip(lane, starts)):
            end = start+max(1, round(.03*ticks_per_second))
            if i+1 < len(starts):
                end = min(end, starts[i+1])
            events.extend([(start, 1, bytes([0x99, notes[name], max(1, min(127, round(velocity*127)))])),
                           (end, 0, bytes([0x89, notes[name], 0]))])
    track = bytearray(b"\x00\xff\x51\x03"+tempo.to_bytes(3,"big"))
    previous = 0
    for tick, _, message in sorted(events):
        track.extend(variable(tick-previous)+message); previous = tick
    track.extend(b"\x00\xff\x2f\x00")
    return b"MThd"+struct.pack(">IHHH",6,0,1,PPQ)+b"MTrk"+struct.pack(">I",len(track))+track


def export(req, read_audio, trigger_events):
    output = Path(req["output"])
    params = req["drums"]
    inputs = [req["source"], *[p["path"] for p in params.get("parts", [])]]
    if output.suffix.lower() not in (".mid", ".midi") or any(output.resolve() == Path(p).resolve() for p in inputs):
        raise ValueError("Chọn file MIDI mới, khác các file audio nguồn.")
    data, sr = read_audio(req["source"])
    hits = trigger_events(data, sr, params, req["source"], require_parts=True)
    payload = encode(hits, sr, float(params.get("midiBpm", 120)), params.get("midiMapping", {}))
    output.parent.mkdir(parents=True, exist_ok=True)
    partial = output.with_name(output.name+"."+uuid.uuid4().hex+".tmp")
    try:
        partial.write_bytes(payload)
        os.replace(partial, output)
    finally:
        partial.unlink(missing_ok=True)
    return {"output": str(output), "hits": {k:len(v) for k,v in hits.items()}, "bpm": params.get("midiBpm", 120)}
