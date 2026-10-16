"""Controlled OFL fonts for native Windows acceptance; no runtime dependency.

Source: googlefonts/cousine c0fbdb438443968c884a5c13f5f9bee916a7f89b.
OFL 1.1 has no Reserved Font Names in this version. Only name metadata is
changed; glyphs are untouched. Regular/Bold become a deterministic TTC.
"""
import hashlib
import pathlib
import struct
import sys
import urllib.request

BASE = "https://raw.githubusercontent.com/googlefonts/cousine/c0fbdb438443968c884a5c13f5f9bee916a7f89b/"
HASHES = {
    "Cousine-Regular.ttf": "5a57f0184000371cb22fe3fcea4c500354cab1a69efaf7beaf3e6eca6ecfefea",
    "Cousine-Bold.ttf": "331215ec6445f41e98d8971251bb6237e722907f4101faaE990583accfe79545".lower(),
    "Cousine-Italic.ttf": "afb869eee8643d09915c3286edb61dad66278ceb1511c79b474ce21548421bcd",
    "OFL.txt": "b81c4d4dc0a9f72c9155e78187316e016e2012a8102468804173dc61468b906d",
}
FAMILY = "Yoake CI 日本語"


def u16(data, offset):
    return struct.unpack_from(">H", data, offset)[0]


def u32(data, offset):
    return struct.unpack_from(">I", data, offset)[0]


def checksum(data):
    padded = bytes(data) + bytes((-len(data)) % 4)
    return sum(struct.unpack(">" + "I" * (len(padded) // 4), padded)) & 0xFFFFFFFF


def rename(data, style):
    result = bytearray(data)
    entries = {bytes(result[12 + i * 16:16 + i * 16]): 12 + i * 16 for i in range(u16(result, 4))}
    entry = entries[b"name"]
    offset, length = u32(result, entry + 8), u32(result, entry + 12)
    names = result[offset:offset + length]
    count, strings = u16(names, 2), u16(names, 4)
    records, values = [], bytearray()
    replacements = {1: FAMILY, 2: style, 3: "Yoake-CI-" + style, 4: FAMILY + " " + style, 6: "YoakeCI-" + style, 16: FAMILY, 17: style}
    for i in range(count):
        platform, encoding, language, name_id, size, start = struct.unpack_from(">6H", names, 6 + i * 12)
        text = bytes(names[strings + start:strings + start + size])
        if name_id in replacements:
            platform, encoding, language = 3, 1, 0x409
            text = replacements[name_id].encode("utf-16-be")
        records.append((platform, encoding, language, name_id, len(text), len(values)))
        values.extend(text)
    table = struct.pack(">3H", 0, len(records), 6 + len(records) * 12) + b"".join(struct.pack(">6H", *r) for r in records) + values
    result.extend(bytes((-len(result)) % 4))
    new_offset = len(result)
    result.extend(table)
    result.extend(bytes((-len(result)) % 4))
    struct.pack_into(">3I", result, entry + 4, checksum(table), new_offset, len(table))
    head_entry = entries[b"head"]
    head_offset, head_length = u32(result, head_entry + 8), u32(result, head_entry + 12)
    struct.pack_into(">I", result, head_offset + 8, 0)
    struct.pack_into(">I", result, head_entry + 4, checksum(result[head_offset:head_offset + head_length]))
    struct.pack_into(">I", result, head_offset + 8, (0xB1B0AFBA - checksum(result)) & 0xFFFFFFFF)
    return result


def collection(fonts):
    result = bytearray(struct.pack(">3I", 0x74746366, 0x10000, len(fonts)) + bytes(4 * len(fonts)))
    for i, font in enumerate(fonts):
        result.extend(bytes((-len(result)) % 4))
        base = len(result)
        struct.pack_into(">I", result, 12 + i * 4, base)
        current = bytearray(font)
        for j in range(u16(current, 4)):
            entry = 12 + j * 16
            old_offset = u32(current, entry + 8)
            if current[entry:entry + 4] == b"head":
                struct.pack_into(">I", current, old_offset + 8, 0)
            struct.pack_into(">I", current, entry + 8, old_offset + base)
        result.extend(current)
    # Legal unused zero padding makes real cancellation reproducible while
    # exercising streaming IO rather than a test-only delay in production.
    result.extend(bytes(32 * 1024 * 1024))
    return result


def main():
    root = pathlib.Path(sys.argv[1]) / "fonts"
    root.mkdir(parents=True, exist_ok=True)
    files = {}
    for name, digest in HASHES.items():
        path = "OFL.txt" if name == "OFL.txt" else "fonts/ttf/" + name
        with urllib.request.urlopen(BASE + path, timeout=60) as response:
            data = response.read(2 * 1024 * 1024)
        if hashlib.sha256(data).hexdigest() != digest:
            raise RuntimeError("Pinned font fixture hash mismatch: " + name)
        files[name] = data
    regular = rename(files["Cousine-Regular.ttf"], "Regular")
    bold = rename(files["Cousine-Bold.ttf"], "Bold")
    italic = rename(files["Cousine-Italic.ttf"], "Italic")
    (root / "日本語 collection.ttc").write_bytes(collection([regular, bold]))
    (root / "Italic fixture.ttf").write_bytes(italic)
    (root / "OFL.txt").write_bytes(files["OFL.txt"])
    (root / "SOURCE.txt").write_text("Cousine source commit c0fbdb438443968c884a5c13f5f9bee916a7f89b; OFL 1.1. Family names replaced for isolated process-private native tests; glyphs unchanged; Regular/Bold packed into TTC with unused zero padding.\n", encoding="utf-8")
    print("Pinned OFL native font fixtures prepared")


if __name__ == "__main__":
    main()
