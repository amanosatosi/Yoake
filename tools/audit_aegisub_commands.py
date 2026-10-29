#!/usr/bin/env python3
"""Source-only inventory of two exact Aegisub releases; never builds/runs them.

The inventory is evidence, not a claim that matching command names implement
matching workflows. Body changes need semantic review of their called backends.
Recent slots are represented once per media family. No runtime macros are cloned.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
from pathlib import Path

PINS = {
    "3.2.2": ("https://github.com/Aegisub/Aegisub", "v3.2.2", "f743d1411e09cbb2bd34ddd2d4b6738101fab710"),
    "3.5.0": ("https://github.com/TypesettingTools/Aegisub", "v3.5.0", "43af875ea01e80052cc6c44f24fc8c5258261c6d"),
}
LEXER = re.compile(r'//[^\n]*|/\*[\s\S]*?\*/|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'')
STRING = re.compile(r'"((?:\\.|[^"\\])*)"')


def mask(source: str, strings: bool = True) -> str:
    def replace(match: re.Match) -> str:
        value = match.group()
        if not strings and value[0] in "\"'":
            return value
        return "".join("\n" if char == "\n" else " " for char in value)
    return LEXER.sub(replace, source)


def literal(value: str) -> str:
    # Preserve Unicode; only C++ escapes used in English command metadata.
    return re.sub(r'\\([nrt"\\])', lambda m: {"n": "\n", "r": "\r", "t": "\t"}.get(m[1], m[1]), value)


def macro(body: str, name: str) -> str | None:
    clean = mask(body, strings=False)
    match = re.search(r'\b' + name + r'\s*\(\s*(?:_\s*\(\s*)?"((?:\\.|[^"\\])*)"', clean)
    return literal(match[1]) if match else None


def contextual_strings(body: str, method: str) -> list[str]:
    structural = mask(body)
    match = re.search(r'\b' + method + r'\s*\([^)]*\)[^{;]*\{', structural)
    if not match:
        return []
    start, end, depth = match.end(), match.end(), 1
    while depth and end < len(structural):
        depth += (structural[end] == "{") - (structural[end] == "}")
        end += 1
    return [literal(value) for value in STRING.findall(mask(body[start:end], strings=False))]


def canonical(body: str) -> str:
    clean = mask(body, strings=False)
    tokens = re.findall(r'"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|[A-Za-z_]\w*|\d+(?:\.\d+)?|[^\s]', clean)
    return hashlib.sha256("\n".join(tokens).encode()).hexdigest()


def verify_reference(root: Path, version: str) -> None:
    expected = PINS[version][2]
    actual = subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip()
    tag = subprocess.check_output(["git", "-C", str(root), "rev-parse", PINS[version][1] + "^{commit}"], text=True).strip()
    if actual != expected or tag != expected:
        raise ValueError(f"{version}: expected exact release commit {expected}; HEAD={actual}, tag={tag}")
    if subprocess.check_output(["git", "-C", str(root), "status", "--porcelain", "--untracked-files=no"], text=True).strip():
        raise ValueError(f"{version}: reference checkout has modified tracked files")


def exposure(root: Path) -> dict[str, list[str]]:
    result: dict[str, set[str]] = {}
    def add(command: str, where: str) -> None:
        if command:
            result.setdefault(command, set()).add(where)
    def walk(value, where):
        if isinstance(value, dict):
            if isinstance(value.get("command"), str):
                add(value["command"], where)
            for key, child in value.items():
                walk(child, where + "/" + key)
        elif isinstance(value, list):
            for child in value:
                walk(child, where)
        elif isinstance(value, str) and "toolbar" in where:
            add(value, where)
    for path in sorted((root / "src/libresrc").rglob("*.json")):
        if not any(name in path.name for name in ("menu", "hotkey", "toolbar")):
            continue
        data = json.loads(path.read_text(encoding="utf-8"))
        relative = path.relative_to(root).as_posix()
        if "hotkey" in path.name:
            for context, commands in data.items():
                if isinstance(commands, dict):
                    for command, keys in commands.items():
                        if keys:
                            add(command, f"{relative}:{context}:" + ",".join(keys))
        else:
            walk(data, relative)
    return {key: sorted(values) for key, values in result.items()}


def inventory(root: Path, version: str) -> dict[str, dict]:
    verify_reference(root, version)
    definitions = {}
    named = {}
    observed = set()
    config = exposure(root)
    defaults = json.loads((root / "src/libresrc/default_hotkey.json").read_text(encoding="utf-8"))
    for path in sorted((root / "src/command").glob("*.cpp")):
        source = path.read_text(encoding="utf-8-sig")
        structural = mask(source)
        relative = path.relative_to(root).as_posix()
        observed.update(re.findall(r'CMD_NAME\s*\(\s*"([^"\n]+)"', mask(source, strings=False)))
        for match in re.finditer(r'\b(?:struct|class)\s+(\w+)\s*(?:final\s*)?:\s*([^;{]+)\{', structural):
            start = match.end() - 1
            depth = 1
            end = start + 1
            while depth and end < len(structural):
                depth += (structural[end] == "{") - (structural[end] == "}")
                end += 1
            if depth:
                raise ValueError(f"Unterminated command class: {relative}:{match[1]}")
            body = source[start + 1:end - 1]
            record = {"class": match[1], "base": match[2].strip(), "source": relative,
                "line": source.count("\n", 0, match.start()) + 1, "body_sha256": canonical(body),
                "display": macro(body, "STR_DISP"), "menu": macro(body, "STR_MENU"),
                "help": macro(body, "STR_HELP"),
                "flags": sorted(set(re.findall(r'\bCOMMAND_[A-Z_]+\b', mask(body, strings=False))))}
            if record["display"] is None:
                values = contextual_strings(body, "StrDisplay") or contextual_strings(body, "StrMenu")
                record["contextual_display"] = values
                record["display"] = " / ".join(values) or None
            # Command helpers live in file-local anonymous namespaces. Several
            # files use the same helper name; never borrow another file's base.
            definitions[(relative, match[1])] = record
            command = macro(body, "CMD_NAME")
            if command:
                if command in named:
                    raise ValueError("Duplicate command: " + command)
                named[command] = record
        for match in re.finditer(r'COMMAND_GROUP\s*\(([^;]+)\);', mask(source, strings=False)):
            strings = STRING.findall(match[1])
            if len(strings) != 4:
                raise ValueError("Unrecognized command group: " + match[0])
            command, menu, display, help_text = map(literal, strings)
            named[command] = {"class": match[1].split(",")[0].strip(), "source": relative,
                "line": source.count("\n", 0, match.start()) + 1, "display": display, "menu": menu,
                "help": help_text, "flags": [], "kind": "menu group", "body_sha256": canonical(match[0])}
    if observed - named.keys():
        raise ValueError("Extractor missed command IDs: " + ", ".join(sorted(observed - named.keys())))
    for command, record in named.items():
        base = record.get("base", "")
        seen = set()
        bases = []
        while base and base not in seen:
            seen.add(base)
            parent_name = base.removeprefix("public ").strip().split("<")[0]
            parent = definitions.get((record["source"], parent_name))
            if parent is None:
                break
            bases.append({"class": parent_name, "source": parent["source"], "line": parent["line"], "body_sha256": parent["body_sha256"]})
            record["flags"] = sorted(set(record["flags"] + parent["flags"]))
            for key in ("display", "menu", "help"):
                record[key] = record[key] or parent[key]
            base = parent.get("base", "")
        record["base_definitions"] = bases
        record["implementation_sha256"] = hashlib.sha256((record["body_sha256"] + "".join(base["body_sha256"] for base in bases)).encode()).hexdigest()
        record["exposure"] = config.get(command, [])
        record["default_hotkeys"] = {context: commands[command] for context, commands in defaults.items() if command in commands}
        record["category"] = command.split("/")[0]
        record["url"] = f"{PINS[version][0]}/blob/{PINS[version][2]}/{record['source']}#L{record['line']}"
    # Collapse menu groups and generated mru_wrapper slots into five semantic families.
    for group, prefix in (("recent/audio", "recent/audio/"), ("recent/keyframe", "recent/keyframes/"),
            ("recent/subtitle", "recent/subtitle/"), ("recent/timecodes", "recent/timecodes/"), ("recent/video", "recent/video/")):
        if prefix in named:
            entry = named.pop(prefix)
            named[group]["dynamic_entry_pattern"] = prefix + "{index}"
            named[group]["entry_source"] = entry["url"]
            named[group]["entry_body_sha256"] = entry["body_sha256"]
            named[group]["kind"] = "semantic recent family"
    return named


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--old", type=Path, required=True)
    parser.add_argument("--new", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    old = inventory(args.old, "3.2.2")
    new = inventory(args.new, "3.5.0")
    records = []
    for command in sorted(old.keys() | new.keys()):
        before, after = old.get(command), new.get(command)
        change = "added" if before is None else "removed" if after is None else "source changed" if any(
            before.get(key) != after.get(key) for key in ("implementation_sha256", "entry_body_sha256", "flags", "display", "help")) else "same command source"
        records.append({"id": command, "3.2.2": before, "3.5.0": after, "delta": change})
    hotkeys = {}
    for version, root in (("3.2.2", args.old), ("3.5.0", args.new)):
        path = root / "src/libresrc/default_hotkey.json"
        hotkeys[version] = {"source": "src/libresrc/default_hotkey.json", "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
            "url": f"{PINS[version][0]}/blob/{PINS[version][2]}/src/libresrc/default_hotkey.json",
            "contexts": json.loads(path.read_text(encoding="utf-8"))}
    hotkey_delta = []
    old_keys, new_keys = hotkeys["3.2.2"]["contexts"], hotkeys["3.5.0"]["contexts"]
    for context in sorted(old_keys.keys() | new_keys.keys()):
        before, after = old_keys.get(context, {}), new_keys.get(context, {})
        for command in sorted(before.keys() | after.keys()):
            if before.get(command, []) != after.get(command, []):
                hotkey_delta.append({"context": context, "command": command, "3.2.2": before.get(command, []), "3.5.0": after.get(command, [])})
    result = {"schema": 1, "references": {version: {"repository": pin[0], "tag": pin[1], "commit": pin[2]}
        for version, pin in PINS.items()}, "method": "Exact source inventories; source-body delta is not automatically a behavior delta. Includes conditional/platform commands; collapses recent slots into semantic families.",
        "hotkeys": hotkeys, "hotkey_delta": hotkey_delta, "commands": records}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    for name, values in (("3.2.2", old), ("3.5.0", new)):
        print(f"{name}: {len(values)} records (five recent families, no expanded slots)")
    for name in ("added", "removed", "source changed", "same command source"):
        selected = [record["id"] for record in records if record["delta"] == name]
        print(f"{name}: {len(selected)}" + (": " + ", ".join(selected) if name in ("added", "removed") else ""))


if __name__ == "__main__":
    main()
