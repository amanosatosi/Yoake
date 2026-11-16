#!/usr/bin/env python3
"""Pinned OFL MUA Office plus an intentionally unshaped cmap-only trap.
Test inputs only; these fonts are not included in the portable application.
"""
import hashlib
import pathlib
import struct
import sys
import urllib.request
from create_font_collector_fixtures import checksum, rename, u16, u32

SOURCE = "https://raw.githubusercontent.com/KoKoZaw-Eng/MMFonts/5c1d43a7723a199a90008c52392449c1ab9de010/MUA_Office-Regular.ttf"


def unshaped_trap(data):
    data = rename(data, "Regular", "Yoake UI Legacy Trap")
    tables = {}
    for i in range(u16(data, 4)):
        entry = 12 + i * 16
        tag = bytes(data[entry:entry + 4])
        if tag not in (b"GSUB", b"GPOS", b"GDEF"):
            offset, size = u32(data, entry + 8), u32(data, entry + 12)
            tables[tag] = bytearray(data[offset:offset + size])
    struct.pack_into(">I", tables[b"head"], 8, 0)
    count = len(tables)
    power = 1 << (count.bit_length() - 1)
    result = bytearray(data[:4] + struct.pack(">4H", count, power * 16, power.bit_length() - 1, count * 16 - power * 16) + bytes(count * 16))
    head = 0
    for i, (tag, table) in enumerate(sorted(tables.items())):
        result.extend(bytes((-len(result)) % 4))
        offset = len(result)
        result.extend(table)
        struct.pack_into(">4s3I", result, 12 + i * 16, tag, checksum(table), offset, len(table))
        if tag == b"head":
            head = offset
    struct.pack_into(">I", result, head + 8, (0xB1B0AFBA - checksum(result)) & 0xFFFFFFFF)
    return result


def main():
    root = pathlib.Path(sys.argv[1]) / "fonts"
    root.mkdir(parents=True, exist_ok=True)
    with urllib.request.urlopen(SOURCE, timeout=60) as response:
        data = response.read(2 * 1024 * 1024)
    if hashlib.sha256(data).hexdigest() != "e655f235d3390e90751a148469bb331364c028006f97a7398932d706e55b449b":
        raise RuntimeError("Pinned MUA Office hash mismatch")
    (root / "MUA Office.ttf").write_bytes(data)
    (root / "UI Legacy Trap.ttf").write_bytes(unshaped_trap(data))
    license_text = (root / "OFL.txt").read_text(encoding="utf-8-sig")
    license_text = license_text[license_text.index("SIL OPEN FONT LICENSE"):]
    (root / "MUA-OFL.txt").write_text("Copyright (c) 2014 MUA. www.mmunicode.org. All Rights Reserved\nDesigner: Khon Soe Zaw Thu\nMUA Office version 2.10 (2017-06-24); licensed under OFL 1.1 per the embedded name-table license.\nUnmodified reference font; Yoake UI Legacy Trap is renamed and has shaping tables removed for fallback regression testing.\nSource: " + SOURCE + "\n\n" + license_text, encoding="utf-8")
    print("Pinned MUA Office and cmap-only fallback trap prepared")


if __name__ == "__main__":
    main()
