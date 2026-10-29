#!/usr/bin/env python3
"""Regression checks for source evidence and ledger coverage; no application build."""
from __future__ import annotations

import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]


def module(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / "tools" / (name + ".py"))
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


audit = module("audit_aegisub_commands")
renderer = module("render_aegisub_parity")


class SourceAuditTests(unittest.TestCase):
    def fixture(self, files, hotkeys=None, toolbar=None):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        root = Path(directory.name)
        (root / "src/command").mkdir(parents=True)
        (root / "src/libresrc").mkdir()
        for name, text in files.items():
            (root / "src/command" / name).write_text(text, encoding="utf-8")
        (root / "src/libresrc/default_hotkey.json").write_text(json.dumps(hotkeys or {}), encoding="utf-8")
        (root / "src/libresrc/default_toolbar.json").write_text(json.dumps(toolbar or {}), encoding="utf-8")
        with patch.object(audit, "verify_reference"):
            return audit.inventory(root, "3.5.0")

    def test_ignores_commented_ids_and_braces_inside_literals(self):
        records = self.fixture({"edit.cpp": r'''
// CMD_NAME("fake/comment")
struct action final : public Command {
 CMD_NAME ("edit/real") STR_MENU("A { name }") STR_DISP("日本語") STR_HELP("A help")
 void operator()() { const char *s = "} // { CMD_NAME(\"fake/string\")"; }
};
'''})
        self.assertEqual(["edit/real"], list(records))
        self.assertEqual("日本語", records["edit/real"]["display"])

    def test_file_local_validator_does_not_borrow_another_file(self):
        records = self.fixture({"a.cpp": '''
struct validator : public Command { CMD_TYPE(COMMAND_RADIO) };
struct one : public validator<int> { CMD_NAME("video/one") STR_DISP("One") };
''', "b.cpp": '''
struct validator : public Command { CMD_TYPE(COMMAND_TOGGLE) };
struct two : public validator { CMD_NAME("audio/two") STR_DISP("Two") };
'''})
        self.assertEqual(["COMMAND_RADIO"], records["video/one"]["flags"])
        self.assertEqual(["COMMAND_TOGGLE"], records["audio/two"]["flags"])
        self.assertEqual("src/command/a.cpp", records["video/one"]["base_definitions"][0]["source"])

    def test_duplicate_or_missed_id_fails_instead_of_silent_partial_audit(self):
        with self.assertRaisesRegex(ValueError, "Duplicate command"):
            self.fixture({"a.cpp": 'struct one : Command { CMD_NAME("edit/real") }; struct two : Command { CMD_NAME("edit/real") };'})
        with self.assertRaisesRegex(ValueError, "missed command IDs"):
            self.fixture({"a.cpp": 'struct unrecognized { CMD_NAME ("edit/missed") };'})

    def test_contextual_labels_and_all_exposures_are_retained(self):
        records = self.fixture({"a.cpp": '''
struct one : Command { CMD_NAME("edit/undo")
 const char *StrDisplay(int c) { return c ? "Undo %s" : "Nothing to undo"; }
};''',}, {"Default": {"edit/undo": ["Ctrl-Z", "F9"]}, "Audio": {"edit/undo": ["G"]}}, {"main": ["edit/undo", ""]})
        record = records["edit/undo"]
        self.assertEqual(["Undo %s", "Nothing to undo"], record["contextual_display"])
        self.assertEqual(["Ctrl-Z", "F9"], record["default_hotkeys"]["Default"])
        self.assertIn("src/libresrc/default_toolbar.json/main", record["exposure"])
        self.assertTrue(any(":Audio:G" in value for value in record["exposure"]))

    def test_recent_keyframes_is_one_semantic_feature_despite_plural_entries(self):
        records = self.fixture({"recent.cpp": '''
COMMAND_GROUP(recent_keyframes, "recent/keyframe", "Recent", "Recent", "Open keyframes");
struct entry : Command { CMD_NAME("recent/keyframes/") STR_DISP("Recent") };
'''})
        self.assertEqual(["recent/keyframe"], list(records))
        self.assertEqual("recent/keyframes/{index}", records["recent/keyframe"]["dynamic_entry_pattern"])


class PinnedEvidenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.inventory = json.loads((renderer.DIRECTORY / "aegisub-command-source-inventory.json").read_text(encoding="utf-8"))
        cls.review = json.loads((renderer.DIRECTORY / "aegisub-command-feature-status.json").read_text(encoding="utf-8"))

    def test_exact_release_pins_and_complete_hotkey_contexts(self):
        expected = {"Always", "Audio", "Default", "Styling Assistant", "Subtitle Edit Box", "Subtitle Grid", "Translation Assistant", "Video"}
        for version, reference in self.inventory["references"].items():
            self.assertEqual(audit.PINS[version][2], reference["commit"])
            table = self.inventory["hotkeys"][version]["contexts"]
            self.assertEqual(expected, set(table))
            self.assertEqual(120, sum(len(keys) for commands in table.values() for keys in commands.values()))
        self.assertEqual(237, len(self.inventory["commands"]))
        self.assertEqual(5, sum(row["id"].startswith("recent/") for row in self.inventory["commands"]))

    def test_all_shipped_binding_changes_are_accounted_for(self):
        changes = self.inventory["hotkey_delta"]
        self.assertEqual({("Default", command) for command in ("edit/line/duplicate/shift", "edit/line/duplicate/shift_back", "edit/line/split/before", "edit/line/split/after")}, {(row["context"], row["command"]) for row in changes})
        self.assertEqual(4, len(changes))
        self.assertEqual(["Ctrl-D"], self.inventory["hotkeys"]["3.2.2"]["contexts"]["Default"]["edit/line/duplicate/shift"])
        self.assertEqual(["Ctrl-D"], self.inventory["hotkeys"]["3.5.0"]["contexts"]["Default"]["edit/line/split/before"])

    def test_full_default_tables_match_independently_recorded_digests(self):
        expected = {
            "3.2.2": "854f5cca90a39f40a50283bf0509a4a83f66c05da3c9024753736b735ba6dda5",
            "3.5.0": "b560c4e4dee65369fe863d8647a6461f76c447692a503659856c701afda37470",
        }
        for version, digest in expected.items():
            table = self.inventory["hotkeys"][version]["contexts"]
            canonical = json.dumps(table, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()
            self.assertEqual(digest, hashlib.sha256(canonical).hexdigest(), "Do not change imported defaults from memory")

    def test_ledger_rejects_missing_or_invented_workflow_rows(self):
        altered = self.review | {"commands": self.review["commands"][1:]}
        with self.assertRaisesRegex(ValueError, "cover every unique"):
            renderer.render(self.inventory, altered)
        altered = self.review | {"commands": self.review["commands"] + [self.review["commands"][0]]}
        with self.assertRaisesRegex(ValueError, "cover every unique"):
            renderer.render(self.inventory, altered)

    def test_checked_in_ledger_is_current(self):
        actual = (renderer.DIRECTORY / "aegisub-command-feature-parity.md").read_text(encoding="utf-8")
        self.assertEqual(renderer.render(self.inventory, self.review), actual)


if __name__ == "__main__":
    unittest.main()
