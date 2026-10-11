#!/usr/bin/env python3
"""Render reviewed workflow status against pinned source evidence, without builds."""
from __future__ import annotations

import argparse
from collections import Counter
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DIRECTORY = ROOT / "docs/compatibility"
STATUSES = {"already complete", "partially implemented", "missing", "deliberately not applicable", "superseded by better Yoake behavior"}

INTRO = """# Aegisub command and user-feature parity

This is the implementation ledger for the ongoing parity pass, not a completion claim. A command name or a dialog is insufficient evidence. Existing workflows are credited only where a backend and usable UI exist; partial rows identify remaining work. Hotkey parity is tracked independently from backend presence. Baseline inspection was on `automation-4-layer` at `f07a69f5e05c07af1d5bb79a61a7396d5b05950a`, after fetching origin and fast-forward synchronization. The pre-existing local deletion of `luna.md` and untracked `agents.md` were preserved.

## Reference authority and reproducibility

1. Aegisub 3.2.2: [exact release commit](https://github.com/Aegisub/Aegisub/tree/f743d1411e09cbb2bd34ddd2d4b6738101fab710), annotated tag object `185258a02cc533708faa94bc9294c5ab9296056d`.
2. Aegisub 3.5.0: [exact release commit](https://github.com/TypesettingTools/Aegisub/tree/43af875ea01e80052cc6c44f24fc8c5258261c6d), annotated tag object `e10a4c9e893e0b27f738690cff1473b2a71d6f43`. The beta commit is not the authority.
3. Matching source tests, then shipped menu/toolbar/hotkey JSON, then matching manuals. The frozen 3.2 manual is at `C:\\aegisub source\\3.2`. The command sources in the local 3.2.2 archive were compared against the exact Git checkout and matched.
4. [Current command page](https://aegisub.org/docs/latest/commands/) is a discovery checklist only. `automation/v4-docs/*` is historical and is not an API specification.

[Machine-readable source inventory](aegisub-command-source-inventory.json) contains exact file/line links, metadata, validation/radio flags, inherited helper hashes, shipped menu/context/toolbar locations, and both full hotkey tables. [Reviewed status manifest](aegisub-command-feature-status.json) is the editable workflow assessment. `tools/audit_aegisub_commands.py` requires clean checkouts at the two pinned HEAD/tag commits; `tools/render_aegisub_parity.py --check` checks this document against those manifests. Neither tool builds or executes Aegisub or Yoake.

The lexical inventory covers 217 records in 3.2.2 and 235 in 3.5.0, with 237 union records. Five recent-item families are counted once each, including the upstream singular group `recent/keyframe` and plural entry prefix `recent/keyframes/`. Dynamic Automation macros are one capability below. Platform/conditional commands are retained as evidence, not assumed to be available on every platform. Body hashes detect source changes; unchanged command source does **not** imply unchanged called backends.

## Exact 3.2.2 to 3.5.0 delta

There are 20 added IDs, two removed IDs, 48 changed command/helper sources, and 167 unchanged command sources. These counts describe source records, not newly invented user features.

| Change | Source-derived behavior and Yoake policy |
| --- | --- |
| Six macOS application/window IDs | `app/bring_to_front`, `hide`, `hide_others`, `maximize`, `minimize`, `show_all` are registered under `__WXMAC__`. Keep native Windows behavior. |
| Two audio boundary scroll IDs | `audio/go_to/start` and `/end` select new explicit audio scroll modes; existing `audio/go_to` still centers the selection. |
| `subtitle/close` | Closes the Aegisub project window. Yoake closes an independent document tab with the existing unsaved workflow. On macOS, 3.5 New/Open/autosave/charset/recent subtitle operations create project contexts; Windows retains replacement semantics. Yoake independent tabs exceed that limitation. |
| Two transparent image IDs | `video/frame/copy/subs` and `/save/subs` call `GetSubtitles` at the current frame time and preserve alpha. Raw/full frame exports remain separate. Screenshot path resolution becomes document-local, with Unicode home-path handling. |
| `video/reset_pan` and display changes | Reset zeroes pan **and** content zoom. 3.5 separates window zoom from content zoom; middle-button pan, pointer-anchored scroll/gesture zoom, configurable Ctrl/Shift scroll actions and clamping affect the visual-tool coordinate transform. Implement a shared presentation transform for image and overlay. |
| Eight vector-clip IDs | Bicubic/convert/drag/freehand/smooth/insert/line/remove gain registry radio actions. All eight modes already exist in the [3.2.2 toolbar implementation](https://github.com/Aegisub/Aegisub/blob/f743d1411e09cbb2bd34ddd2d4b6738101fab710/src/visual_tool_vector_clip.cpp#L34). They are command routing improvements, not eight new visual tools. Preserve Yoake's existing eight modes and Mangetsu distort. |
| Removed `help/forums`, `tool/assdraw` | Obsolete forum and external ASSDraw launcher; integrated structured vector editing is retained. |
| Frame split correction | `duplicate_lines` helper fixes split-after: old line End = current frame END; copy Start = next frame START. Both frame split IDs already exist in 3.2.2. Do not resurrect the erroneous old helper branch. |
| Karaoke join/split | Join now prepends first-line duration and appends each subsequent line duration, replacing the old gap-only tag. Split-by-karaoke moves to the shared karaoke parser, fixes ownership and chooses an active line belonging to the new selection. |
| Clipboard | Paste and Paste Over accept Unicode clipboard text explicitly. Preserve Yoake's Unicode and unknown-field/tag behavior. |
| Search | Skip-tags mapping moves into `tagless_find_helper`, covered by added tag stripping/range-mapping tests in `tests/tests/ifind.cpp`; case-insensitive Unicode tests retain source-offset semantics. Literal/raw-source search is not sufficient parity. |
| Audio timing snapping | Dialogue marker controller initializes drag state, owns signal connections, excludes moving markers from snap targets, and uses snap-specific keyframe/current-position markers. Maintain rollback on capture loss and consistent VFR snapping. |
| Translation Assistant | Unicode whitespace detection replaces byte-oriented classification; a real `Tool/Translation Assistant/Skip Whitespace` preference controls block skipping. Commit/preview/navigation muscle memory is retained. Styling Assistant changes are primarily widget ownership/layout maintenance. |
| Resolution resampling | Axis-specific size classes, geometric mean for XY sizes, corrected style spacing, no eighth-pixel drawing rounding, LayoutRes preservation, and richer YCbCr conversion/header handling. Use structured tag/drawing transforms and retain unknown/Mangetsu syntax. |
| VFR validation | Added tests reject NaN/infinite/huge/tiny rates and oversized v1 ranges; expanded rational-rate round trips distinguish START/END/exact mappings. Use one document-local timeline map. |
| Application semantics | Exit closes all project contexts; New Window creates an in-process context instead of restarting a process. Options dialog factory changes and translated language restart prompt are not new editor commands. |
| Maintenance/exposure | Help URLs move to maintained HTTPS destinations; media filters add Opus/EAC3 and H264/HEVC; keyframe/timecode dialogs update text/encoding plumbing. Visual command helpers switch ownership factories; zoom commands explicitly call WindowZoom. Config menu placement is independently recorded in the source inventory. |

Primary implementation evidence: pinned [3.5 edit commands](https://github.com/TypesettingTools/Aegisub/blob/43af875ea01e80052cc6c44f24fc8c5258261c6d/src/command/edit.cpp), [video display](https://github.com/TypesettingTools/Aegisub/blob/43af875ea01e80052cc6c44f24fc8c5258261c6d/src/video_display.cpp), [audio timing](https://github.com/TypesettingTools/Aegisub/blob/43af875ea01e80052cc6c44f24fc8c5258261c6d/src/audio_timing_dialogue.cpp), [resampler](https://github.com/TypesettingTools/Aegisub/blob/43af875ea01e80052cc6c44f24fc8c5258261c6d/src/resolution_resampler.cpp), [translation](https://github.com/TypesettingTools/Aegisub/blob/43af875ea01e80052cc6c44f24fc8c5258261c6d/src/dialog_translation.cpp), and matching [tests](https://github.com/TypesettingTools/Aegisub/tree/43af875ea01e80052cc6c44f24fc8c5258261c6d/tests/tests). Helper/backend deltas above apply even when the individual row says "same command source".

## Default hotkeys are part of the workflow

The authoritative common [3.2.2 defaults](https://github.com/Aegisub/Aegisub/blob/f743d1411e09cbb2bd34ddd2d4b6738101fab710/src/libresrc/default_hotkey.json) and [3.5.0 defaults](https://github.com/TypesettingTools/Aegisub/blob/43af875ea01e80052cc6c44f24fc8c5258261c6d/src/libresrc/default_hotkey.json) each contain **120 gestures across eight contexts**. Both full tables, original names, order and file SHA-256 are stored in the source inventory. Matching macOS tables are separate platform evidence; the Windows baseline is the common file. Menu accelerators and remembered shortcuts are not the source of defaults.

Only four `(context, command)` rows differ, representing two ID migrations, with physical gestures unchanged:

| Context | 3.2.2 shipped ID | 3.5.0 shipped ID | Gesture | Verified migration/behavior |
| --- | --- | --- | --- | --- |
| Default | `edit/line/duplicate/shift` | `edit/line/split/before` | Ctrl-D | `src/hotkey.cpp` migrates every existing user binding to the new ID, then removes the old ID. Exact 3.2.2 command source already names split/before; its shipped JSON is stale. Modern behavior duplicates selected blocks while splitting before current frame using END/START mappings. |
| Default | `edit/line/duplicate/shift_back` | `edit/line/split/after` | Ctrl-Shift-D | Same migration preserves context and gesture. Exact 3.2.2 source already names split/after; 3.5 fixes its helper as described above. Ordinary duplicate must get a deliberate non-conflicting policy. |

Source history confirms the distinction: [2013 frame-split revamp `0ef9963782c637d6a8afd1282a8d49deb86c07b8`](https://github.com/TypesettingTools/Aegisub/commit/0ef9963782c637d6a8afd1282a8d49deb86c07b8) replaced duration-shifting duplicates with frame splits and introduced the preserving migration. If the current frame lies outside a line, the useful modern behavior makes a one-frame copy at that frame instead of splitting outside the interval. [2023 defaults correction `affdefa40cbb1c95ee9fe5d4a43fdff236551250`](https://github.com/TypesettingTools/Aegisub/commit/affdefa40cbb1c95ee9fe5d4a43fdff236551250) fixed the stale shipped JSON, including resetting to defaults. Thus 3.2.2 shipped IDs and effective migrated actions must be recorded separately; this is not a 3.5 invention of frame splitting.

Exact implementation and tests were inspected in both releases: `libaegisub/common/hotkey.cpp`, `src/hotkey.cpp`, `tests/tests/hotkey.cpp`, and `src/hotkey_data_view_model.cpp`. `Scan(context, gesture, always)` uses enabled **Always first**, then requested local context, then Default. With Medusa/global overrides disabled, Always is not implicitly enabled. Multiple gestures per command are a multimap. Duplicate physical gestures can exist upstream; its scan does not offer Yoake's strict collision rejection. Yoake should display/reject ambiguous same-context conflicts explicitly, permit the same key in different contexts, and document that deviation. A matched disabled command is handled through command validation; it does not justify accidentally invoking a fallback mutation.

Upstream reads user `hotkey.json` instead of defaults when available, recognizes legacy object-form modifiers/key bindings, backs up old format as `.3_1`, writes atomically through `io::Save`, and emits change notifications after map replacement. Addition migrations only fill unoccupied context/gesture pairs; duplicate-to-split migrations preserve custom bindings. Key normalization in `src/hotkey.cpp` distinguishes keypad keys and orders Ctrl/Alt/Shift, with platform command-modifier mapping.

Yoake now generates the full 3.5 common default table outside controls and command constructors. Persisted user overrides support multiple gestures, explicit removals and command/context/all resets. The live Options window captures real keys and reports conflicts without partial replacement; menu shortcut display and command tooltips update immediately. Enabled Always overrides resolve before local contexts, then Default; exclusive modes suppress ordinary fallback. Native text input, caret/clipboard/word deletion, AltGr and IME preedit retain ownership. Dynamic Automation adapters share the profile and document context without changing Lua compatibility. Plain duplicate and text-cursor split move to unoccupied Ctrl-Alt-D and Ctrl-Alt-Shift-D defaults; Ctrl-D/Ctrl-Shift-D reserve the modern frame split actions. Explicit edit-box Ctrl-I remains Yoake italic formatting while Default Ctrl-I remains Shift Times. All missing action backends below remain missing/partial even though their exact defaults are now imported. Assistant context tables do not substitute for functioning assistant workflows. Core/adapter tests and a separate packaged keyboard/settings acceptance launch cover this implementation; its managed and separate packaged real-input acceptance passed at commit 4deb4d4 (run 38152244505).

Tables below record exact defaults including unbound commands. `—` means no shipped/effective binding, not a guessed accelerator. Yoake defaults are the inspected **pre-pass baseline**, so differences are gaps unless the notes explicitly justify a lasting deviation. Ctrl-I formatting in Subtitle Edit Box is an existing intentional context-local Yoake extension; Shift Times must still be available in Default elsewhere. Ctrl-D plain duplicate and Ctrl-Shift-D estimated cursor split are current conflicts requiring replacement by the modern frame-split defaults.

## Workflow ledger

Each row links to exact source presence. Version delta is lexical command/helper evidence; consult the behavior delta above for called backends. The source JSON records exact upstream menu/context/toolbar locations and validation/radio flags. Test names in partial rows are existing supporting coverage, **not** a claim of full parity. Missing rows have no workflow test until implemented. Completion of a backend does not waive the separate hotkey/menu/settings acceptance requirements.

"""


def cell(value) -> str:
    return str(value or "—").replace("|", "\\|").replace("\n", " ")


def keys(value: dict) -> str:
    return "; ".join(f"{context}: {', '.join(gestures) or 'unbound'}" for context, gestures in value.items()) or "—"


def render(inventory: dict, review: dict) -> str:
    evidence = {record["id"]: record for record in inventory["commands"]}
    assessed = {record["id"]: record for record in review["commands"]}
    if len(assessed) != len(review["commands"]) or evidence.keys() != assessed.keys():
        raise ValueError("Workflow status must cover every unique exact source command, with no extra IDs")
    for row in assessed.values():
        if row["status"] not in STATUSES:
            raise ValueError("Unknown workflow status: " + row["status"])
        for field in ("backend", "exposure", "tests", "notes", "yoake_default_hotkeys"):
            if field not in row:
                raise ValueError(f"Missing {field}: {row['id']}")
    lines = [INTRO]
    counts = Counter(row["status"] for row in assessed.values())
    lines.append("Baseline source-record assessment: " + "; ".join(f"{name}: {counts[name]}" for name in sorted(STATUSES)) + ". Dynamic macros are assessed separately.\n")
    for category in sorted({command.split("/")[0] for command in evidence}):
        lines.append(f"\n### {category}\n")
        lines.extend(["| Aegisub ID / display | 3.2.2 | 3.5.0 / delta | Yoake command / status | Actual backend / remaining work | Yoake exposure | Tests | Notes / deviations |",
            "| --- | --- | --- | --- | --- | --- | --- | --- |"])
        ids = [command for command in sorted(evidence) if command.split("/")[0] == category]
        for command in ids:
            source, row = evidence[command], assessed[command]
            old, new = source["3.2.2"], source["3.5.0"]
            name = (new or old)["display"]
            columns = [f"`{command}` / {name}", f"[yes]({old['url']})" if old else "no",
                (f"[yes]({new['url']})" if new else "removed") + "; " + source["delta"],
                (f"`{row['yoake_command']}`" if row["yoake_command"] else "—") + "; " + row["status"], row["backend"],
                "; ".join(row["exposure"]) or "Missing", "; ".join(row["tests"]) or "None yet", row["notes"]]
            lines.append("| " + " | ".join(map(cell, columns)) + " |")
        lines.extend(["\nDefault hotkeys by context:\n", "| Aegisub command | 3.2.2 defaults | 3.5.0 defaults | Changed? | Yoake defaults |", "| --- | --- | --- | --- | --- |"])
        for command in ids:
            source, row = evidence[command], assessed[command]
            before, after = (source["3.2.2"] or {}).get("default_hotkeys", {}), (source["3.5.0"] or {}).get("default_hotkeys", {})
            lines.append("| " + " | ".join(map(cell, [f"`{command}`", keys(before), keys(after), "yes" if before != after else "no", keys(row["yoake_default_hotkeys"])])) + " |")
    dynamic = review["dynamic_automation"]
    lines.extend(["\n### Dynamic Automation macro feature\n", f"Status: **{dynamic['status']}**. Command pattern: `{dynamic['yoake_command']}`. {dynamic['backend']}",
        "\nExposure: " + "; ".join(dynamic["exposure"]) + ". Tests: " + "; ".join(dynamic["tests"]) + ". " + dynamic["notes"],
        "\nNo default hardcoded macro gestures exist in either common table. Runtime registration/validation/toggle state is document-local; user bindings belong in the unified hotkey settings model.",
        "\n## Incremental delivery and acceptance\n",
        "1. Exact source ledger and full hotkey evidence (this phase); contextual command metadata and settings/hotkey contract gaps.",
        "2. Application/view/preferences/toolbar/log/typed recents. Four view modes already passed real-window empty/loaded state and persistence tests; retain one workspace layout with audio above editor.",
        "3. Grid/edit/selection/tag projection/production find. Use transactional structured editing, multi-selection and grapheme boundaries; preserve unknown fields/tags/Mangetsu source.",
        "4. Timing/keyframe/timecode I/O, provider fallback and VFR-sensitive operations on one central timeline.",
        "5. Independent audio ownership/timing/playback/options/clips/karaoke and exact Audio focus routing.",
        "6. Video navigation/presentation transform/images/detachment and real provider-state retention.",
        "7. Finish usable document-local major tools, assistants, export/recovery/attachments; do not add placeholder dialogs or duplicate existing rich managers.",
        "8. Complete menu/context/toolbar discoverability and exact context defaults plus immediate user remapping, preserving Yoake extensions and exclusive modes.",
        "9. Separate broader packaged acceptance from existing startup verification; update each status only with real workflow/test evidence and passing relevant CI.",
        "\nEvery implemented mutation must cover enable state, undo/redo, multi-selection, Unicode and unknown/Mangetsu preservation as applicable. Media actions additionally cover absence/presence, VFR and cancellation/error paths. Hotkey tests send representative real key gestures through shipped routing; direct command invocation is only backend coverage. Settings tests cover round trip, removal/reset and damaged members. Never weaken existing packaged assertions.",
        "\nCI baseline: [f07a69f passing managed/native Automation/NativeAOT/packaged verification](https://github.com/amanosatosi/Yoake/actions/runs/38093286199), including all five startup profiles and the four view modes with empty and loaded media. [3307526 source audit passed all jobs](https://github.com/amanosatosi/Yoake/actions/runs/38128974024). [4deb4d4 passed managed tests, source policy, native Automation and NativeAOT packaged startup, contextual keyboard and timing/keyframe acceptance](https://github.com/amanosatosi/Yoake/actions/runs/38152244505). Follow-up keyframe edge-case/native recent-menu verification remains pending its own CI. No local compilation/publishing/native builds are allowed.",
        "\nRemaining manual QA: physical audio devices and playback latency, clipboard alpha interoperability, IME composition on Japanese/Burmese and other complex scripts, DPI/multi-monitor/native chrome, modifier/keypad keyboard layouts, and detached-video behavior. These do not replace CI acceptance.",
        "\nRequired features marked missing/partial remain planned work, not silently deferred. Deliberate exclusions are the macOS-only application menu on Windows, upstream development synthetic audio, obsolete Aegisub community destinations/external ASSDraw, and renderer-provider cycling incompatible with the Mangetsu renderer boundary. Automation 3.5 runtime profiles and DependencyControl are intentionally outside this task.\n"])
    return "\n".join(lines)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    inventory = json.loads((DIRECTORY / "aegisub-command-source-inventory.json").read_text(encoding="utf-8"))
    review = json.loads((DIRECTORY / "aegisub-command-feature-status.json").read_text(encoding="utf-8"))
    result = render(inventory, review)
    output = DIRECTORY / "aegisub-command-feature-parity.md"
    if args.check:
        if not output.exists() or output.read_text(encoding="utf-8") != result:
            raise SystemExit("Parity ledger is stale; run python tools/render_aegisub_parity.py")
    else:
        output.write_text(result, encoding="utf-8")
    print("parity-ledger: OK")


if __name__ == "__main__":
    main()
