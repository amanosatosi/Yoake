#!/usr/bin/env python3
"""Tiny deterministic uncompressed media for packaged native-provider checks.
No downloads, codecs, or external media tools are needed to make these fixtures.
"""
import math
from pathlib import Path
import struct
import sys
import wave

root = Path(sys.argv[1])
root.mkdir(parents=True, exist_ok=True)

def chunk(name, data):
    return name + struct.pack('<I', len(data)) + data + (b'\0' if len(data) & 1 else b'')

def list_chunk(name, data):
    return chunk(b'LIST', name + data)

width, height, frames, fps = 160, 90, 10, 5
frame_bytes = width * height * 3
avih = struct.pack('<14I', 1_000_000 // fps, frame_bytes * fps, 0, 0x10, frames, 0, 1, frame_bytes, width, height, 0, 0, 0, 0)
strh = struct.pack('<4s4sIHHIIIIIIIIhhhh', b'vids', b'DIB ', 0, 0, 0, 0, 1, fps, 0, frames, frame_bytes, 0xFFFFFFFF, 0, 0, 0, width, height)
strf = struct.pack('<IiiHHIIiiII', 40, width, height, 1, 24, 0, frame_bytes, 0, 0, 0, 0)
header = list_chunk(b'hdrl', chunk(b'avih', avih) + list_chunk(b'strl', chunk(b'strh', strh) + chunk(b'strf', strf)))
video, index, offset = bytearray(), bytearray(), 4
for frame in range(frames):
    payload = bytes((20 + frame, 12, 8)) * (width * height)
    packet = chunk(b'00db', payload)
    video.extend(packet)
    index.extend(struct.pack('<4sIII', b'00db', 0x10, offset, len(payload)))
    offset += len(packet)
(root / 'video.avi').write_bytes(chunk(b'RIFF', b'AVI ' + header + list_chunk(b'movi', video) + chunk(b'idx1', index)))
with wave.open(str(root / 'audio.wav'), 'wb') as audio:
    audio.setparams((1, 2, 48000, 96000, 'NONE', 'not compressed'))
    audio.writeframes(b''.join(struct.pack('<h', 0 if i < 24000 else int(16000 * math.sin(2 * math.pi * 440 * i / 48000))) for i in range(96000)))
print('Created deterministic AVI and PCM audio fixtures')

# A real large multilingual document for container virtualization and end/middle scrolling.
scripts = ['日本語', 'မြန်မာ', 'Latin e\u0301', 'العربية', '👩‍👩‍👧‍👦']
with (root / 'large.ass').open('w', encoding='utf-8', newline='') as ass:
    ass.write('[Script Info]\nScriptType: v4.00+\nPlayResX: 1920\nPlayResY: 1080\n[V4+ Styles]\nFormat: Name,Fontname,Fontsize,PrimaryColour,Alignment,MarginL,MarginR,MarginV\nStyle: Default,Arial,60,&H00FFFFFF,2,30,30,30\n[Events]\nFormat: Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Text\n')
    for i in range(20000):
        kind = 'Comment' if i % 13 == 0 else 'Dialogue'
        ass.write(f'{kind}: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,,{scripts[i % len(scripts)]} {i}\n')
