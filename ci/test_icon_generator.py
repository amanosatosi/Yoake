"""Source-only regression checks for the deliberately supported SVG subset."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

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


if __name__ == "__main__":
    unittest.main()
