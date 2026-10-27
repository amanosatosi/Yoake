"""Source-only regression checks for the deliberately supported SVG subset."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

spec = importlib.util.spec_from_file_location("icons", Path(__file__).resolve().parents[1] / "tools/generate-icons.py")
icons = importlib.util.module_from_spec(spec)
spec.loader.exec_module(icons)


class SvgSemantics(unittest.TestCase):
    def generate(self, attributes, path):
        original = icons.SOURCE
        with tempfile.TemporaryDirectory(prefix="yoake-svg-") as folder:
            icons.SOURCE = Path(folder)
            (icons.SOURCE / "example.svg").write_text(
                '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" ' + attributes + '>' + path + '</svg>', encoding="utf-8")
            try:
                return icons.generate()
            finally:
                icons.SOURCE = original

    def test_inherited_stroke_and_fill_are_not_lost(self):
        generated = self.generate('fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="bevel"', '<path d="M0 0L10 10"/>')
        self.assertIn('("F1 M0 0L10 10", false, 2, PenLineCap.Round, PenLineJoin.Bevel)', generated)

    def test_path_override_and_evenodd_survive(self):
        generated = self.generate('fill="none" stroke="currentColor"', '<path d="M0 0L10 10Z" fill="currentColor" stroke="none" fill-rule="evenodd"/>')
        self.assertIn('("F0 M0 0L10 10Z", true, 0,', generated)

    def test_unsupported_semantics_are_rejected(self):
        for attributes in ('transform="rotate(30)"', 'style="fill:none"', 'fill="#ff0000"', 'opacity=".5"', 'stroke-dasharray="1 2"'):
            with self.subTest(attributes=attributes), self.assertRaises(SystemExit):
                self.generate(attributes, '<path d="M0 0L10 10"/>')


class EditorIconWiring(unittest.TestCase):
    def test_toolbar_actions_keep_distinct_reference_icons_and_tooltips(self):
        root = ET.parse(icons.ROOT / "src/Yoake.UI/MainWindow.axaml").getroot()
        controls = list(root.iter())
        # These pairs are easy to accidentally swap while preserving valid XAML.
        expected = {
            "timing/jump-start": "VideoToStart",
            "timing/jump-end": "VideoToEnd",
            "time/snap/start_video": "StartToVideo",
            "time/snap/end_video": "EndToVideo",
            "time/prev": "TimingPrevious",
            "time/next": "TimingNext",
            "audio/play/selection/before": "AudioPlayBefore",
            "audio/play/selection/after": "AudioPlayAfter",
            "audio/play/selection/begin": "AudioPlayBegin",
            "audio/play/selection/end": "AudioPlayEnd",
            "audio/karaoke": "KaraokeTiming",
        }
        for command, geometry in expected.items():
            with self.subTest(command=command):
                buttons = [
                    node for node in controls
                    if node.tag.rsplit("}", 1)[-1] in ("Button", "ToggleButton")
                    and node.get("Command") == "{Binding Actions[" + command + "]}"
                ]
                self.assertTrue(buttons, command)
                self.assertTrue(any(node.get("ToolTip.Tip") for node in buttons))
                self.assertTrue(any(
                    any(child.get("Data") == "{x:Static icons:IconGeometries." + geometry + "}"
                        for child in node.iter())
                    for node in buttons
                ), geometry)

    def test_start_and_end_timing_colors_follow_theme_resources(self):
        app = ET.parse(icons.ROOT / "src/Yoake.App/App.axaml").getroot()
        keys = "{http://schemas.microsoft.com/winfx/2006/xaml}Key"
        for name in ("IconTimingStartBrush", "IconTimingEndBrush"):
            self.assertGreaterEqual(sum(node.get(keys) == name for node in app.iter()), 2)


if __name__ == "__main__":
    unittest.main()
