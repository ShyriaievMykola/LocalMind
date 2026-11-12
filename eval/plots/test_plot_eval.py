"""Перевірки скрипта графіків: python -m unittest discover -s eval/plots -p "test_*.py"."""

import tempfile
import unittest
from pathlib import Path

import plot_eval

EXAMPLE = Path(__file__).parent / "example" / "demo.csv"


class PlotEvalTests(unittest.TestCase):
    def test_reads_example_csv(self) -> None:
        rows = plot_eval.read_rows(EXAMPLE)

        self.assertEqual(12, len(rows))
        self.assertEqual({"keyword", "semantic", "hybrid"}, {r.mode for r in rows})
        self.assertEqual({"all", "exact", "paraphrase", "crossLingual"}, {r.type for r in rows})

    def test_builds_png_and_svg_from_example(self) -> None:
        with tempfile.TemporaryDirectory() as out:
            code = plot_eval.main([str(EXAMPLE), "--out", out])

            names = sorted(p.name for p in Path(out).iterdir())
        self.assertEqual(0, code)
        # графік за різними k не будується: в прикладі лише одне k
        self.assertEqual(
            ["by_query_type.png", "by_query_type.svg", "quality_vs_latency.png", "quality_vs_latency.svg"], names)

    def test_missing_column_is_reported(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            bad = Path(folder) / "bad.csv"
            bad.write_text("dataset,run\ndemo,cli\n", encoding="utf-8")

            with self.assertRaisesRegex(plot_eval.CsvFormatError, "немає колонок"):
                plot_eval.read_rows(bad)

    def test_bad_number_reports_row(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            header = EXAMPLE.read_text(encoding="utf-8").splitlines()[0]
            bad = Path(folder) / "bad.csv"
            bad.write_text(header + "\ndemo,cli,keyword,all,8,10,abc,0,0,1,2,6,6,1,1,1\n", encoding="utf-8")

            with self.assertRaisesRegex(plot_eval.CsvFormatError, "рядок 2"):
                plot_eval.read_rows(bad)

    def test_unknown_input_returns_error_code(self) -> None:
        self.assertEqual(1, plot_eval.main(["definitely-missing.csv"]))


if __name__ == "__main__":
    unittest.main()
