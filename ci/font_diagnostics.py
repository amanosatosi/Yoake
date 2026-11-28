"""Independent HarfBuzz reference and pinned font downloads, diagnostics only."""
import hashlib
import json
import pathlib
import sys
import urllib.request

PYI = "https://raw.githubusercontent.com/mcfnlp/Pyidaungsu/ff1cd8dfe8c451244329d7a6181c83e769226cac/"
NOTO = "https://raw.githubusercontent.com/notofonts/noto-fonts/690ec0701e8a07a2ff2b9ceebdceb0974391a4a4/"
MUA = "https://raw.githubusercontent.com/KoKoZaw-Eng/MMFonts/5c1d43a7723a199a90008c52392449c1ab9de010/"
FONTS = [
    ("pyidaungsu-1.8.3.ttf", PYI + "Pyidaungsu1.8.3/Release/Pyidaungsu-1.8.3_Regular.ttf", "02e64f60185e03cb25b0ad691e9fddcbc2e408c7"),
    ("pyidaungsu-2.5.3.ttf", PYI + "Pyidaungsu2.5.3/Release/Pyidaungsu-2.5.3_Regular.ttf", "fbea7504343083ae069bbb42f2b0e8b6d944534f"),
    ("noto.ttf", NOTO + "hinted/ttf/NotoSansMyanmar/NotoSansMyanmar-Regular.ttf", "3d7178c35ba81b213270068d278fe70a530422ff"),
    ("mua.ttf", MUA + "MUA_Office-Regular.ttf", "e655f235d3390e90751a148469bb331364c028006f97a7398932d706e55b449b"),
]
SAMPLES = ["မြန်မာ", "ကျွန်တော် ပြန်ကြည့်ကြရအောင်", "မင်္ဂလာပါ ပြည်ထောင်စု", "ကျောင်းသားများ", "miriam-dono ရေ", "marshall-sensei မြန်မာ ကျွန်တော် ပြန်ကြည့်ကြရအောင်"]

def main():
    import uharfbuzz as hb
    from fontTools.ttLib import TTFont
    root = pathlib.Path(sys.argv[1])
    root.mkdir(parents=True, exist_ok=True)
    inputs = root / "inputs"
    inputs.mkdir(exist_ok=True)
    (root / "samples.txt").write_text("\n".join(SAMPLES) + "\n", encoding="utf-8")
    manifest = []
    for name, url, expected in FONTS:
        with urllib.request.urlopen(url, timeout=60) as response:
            data = response.read(2 * 1024 * 1024)
        sha = hashlib.sha256(data).hexdigest()
        blob = hashlib.sha1(f"blob {len(data)}\0".encode("ascii") + data).hexdigest()
        if expected not in (sha, blob):
            raise RuntimeError("Pinned font mismatch: " + name)
        path = inputs / name
        path.write_bytes(data)
        dest = root / path.stem
        dest.mkdir(exist_ok=True)
        with TTFont(path) as tt:
            names = tt["name"]
            scripts = {tag: [r.ScriptTag for r in tt[tag].table.ScriptList.ScriptRecord]
                       for tag in ("GSUB", "GPOS") if tag in tt and tt[tag].table.ScriptList}
            meta = dict(file=name, source=url, sha256=sha, family=names.getDebugName(1),
                        version=names.getDebugName(5), postscript=names.getDebugName(6),
                        scripts=scripts, license=names.getDebugName(13), upem=tt["head"].unitsPerEm)
        manifest.append(meta)
        face = hb.Face(data)
        font = hb.Font(face)
        hb.ot_font_set_funcs(font)
        font.scale = (face.upem, face.upem)
        records = []
        for i, sample in enumerate(SAMPLES):
            for language in ("my", "en"):
                # Mixed-script text is shaped per contiguous script segment,
                # not incorrectly as one Myanmar script buffer.
                import re
                for match in re.finditer(r"[\u1000-\u109f\uaa60-\uaa7f\ua9e0-\ua9ff ]+|[^\u1000-\u109f\uaa60-\uaa7f\ua9e0-\ua9ff ]+", sample):
                    run = match.group()
                    if not run.strip():
                        continue
                    buffer = hb.Buffer()
                    buffer.add_str(run)
                    buffer.guess_segment_properties()
                    buffer.language = language
                    hb.shape(font, buffer)
                    records.append(dict(sample=i, run=run, start=match.start(), language=language,
                        script=buffer.script, direction=buffer.direction, glyphs=[
                            dict(glyph=g.codepoint, cluster=g.cluster, advance=p.x_advance*32/face.upem,
                                 offset_x=p.x_offset*32/face.upem, offset_y=-p.y_offset*32/face.upem)
                            for g,p in zip(buffer.glyph_infos,buffer.glyph_positions)]))
        (dest/"harfbuzz.json").write_text(json.dumps(records,ensure_ascii=False,indent=2),encoding="utf-8")
    (root/"font-manifest.json").write_text(json.dumps(dict(harfbuzz=hb.version_string(),fonts=manifest),ensure_ascii=False,indent=2),encoding="utf-8")

if __name__ == "__main__":
    main()
