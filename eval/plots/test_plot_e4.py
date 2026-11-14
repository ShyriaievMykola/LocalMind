"""Тести plot_e4.py: розбір CSV BenchmarkDotNet і recall, таблиця, побудова графіка, помилки.

Запуск з кореня репозиторію: python -m unittest discover -s eval/plots -p "test_*.py"
"""

from __future__ import annotations

import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))

import plot_e4  # noqa: E402

EXAMPLE = Path(__file__).parent / "example" / "e4"

# Рядки у форматі справжнього звіту BenchmarkDotNet (роздільник «;», тисячі через кому, час з одиницею).
BDN_CSV = (
    "Method;Job;Size;Kind;Mean;Error;StdDev;Allocated\n"
    'Search;ShortRun;1000;BruteForce;129.2 μs;151.1 μs;8.28 μs;1.69 KB\n'
    'Search;ShortRun;1000;Hnsw;238.5 μs;"1,165.7 μs";63.90 μs;23.22 KB\n'
    'Search;ShortRun;100000;BruteForce;"10,946.8 μs";"19,432.6 μs";"1,065.17 μs";1.69 KB\n'
    'Search;ShortRun;100000;Hnsw;"1,192.5 μs";753.7 μs;41.31 μs;36.56 KB\n'
)

RECALL_CSV = (
    "data;size;index;ef_search;recall_at_10;latency_ms_per_query\n"
    "uniform;1000;BruteForce;0;1.0000;0.12000\n"
    "uniform;1000;Hnsw;50;0.9000;0.12000\n"
    "uniform;1000;Hnsw;100;0.9830;0.20000\n"
    "uniform;100000;BruteForce;0;1.0000;11.00000\n"
    "uniform;100000;Hnsw;50;0.0720;0.85000\n"
    "uniform;100000;Hnsw;100;0.1200;1.45000\n"
    "clustered;1000;BruteForce;0;1.0000;0.13000\n"
    "clustered;1000;Hnsw;100;0.9990;0.15000\n"
    "clustered;100000;BruteForce;0;1.0000;11.50000\n"
    "clustered;100000;Hnsw;100;0.9500;0.60000\n"
)


class ParseTimeTests(unittest.TestCase):
    def test_units_and_thousands_separator(self) -> None:
        self.assertAlmostEqual(plot_e4.parse_bdn_time("129.2 μs"), 0.1292)
        self.assertAlmostEqual(plot_e4.parse_bdn_time("1,165.7 μs"), 1.1657)
        self.assertAlmostEqual(plot_e4.parse_bdn_time("10,946.8 μs"), 10.9468)
        self.assertAlmostEqual(plot_e4.parse_bdn_time("1.5 ms"), 1.5)
        self.assertAlmostEqual(plot_e4.parse_bdn_time("500 ns"), 0.0005)
        self.assertAlmostEqual(plot_e4.parse_bdn_time("2 s"), 2000.0)

    def test_both_micro_signs_and_ascii_us(self) -> None:
        for unit in ("μs", "µs", "us"):
            self.assertAlmostEqual(plot_e4.parse_bdn_time(f"3 {unit}"), 0.003)

    def test_unknown_unit_or_garbage_is_error(self) -> None:
        for text in ("3 mm", "abc", "", "12"):
            with self.assertRaises(plot_e4.CsvFormatError):
                plot_e4.parse_bdn_time(text)


class ReadTests(unittest.TestCase):
    def setUp(self) -> None:
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.path = Path(self.folder.name)

    def write(self, name: str, text: str) -> Path:
        target = self.path / name
        target.write_text(text, encoding="utf-8")
        return target

    def test_read_bdn(self) -> None:
        points = plot_e4.read_bdn(self.write("bdn.csv", BDN_CSV))
        self.assertEqual(len(points), 4)
        self.assertEqual((points[0].size, points[0].kind), (1000, "BruteForce"))
        self.assertAlmostEqual(points[3].mean_ms, 1.1925)

    def test_read_recall(self) -> None:
        points = plot_e4.read_recall(self.write("recall.csv", RECALL_CSV))
        self.assertEqual(len(points), 10)
        self.assertEqual(points[2].ef_search, 100)
        self.assertAlmostEqual(points[2].recall, 0.983)

    def test_missing_column_is_error(self) -> None:
        with self.assertRaises(plot_e4.CsvFormatError):
            plot_e4.read_bdn(self.write("bad.csv", "Size;Kind\n1000;Hnsw\n"))

    def test_bad_number_reports_row(self) -> None:
        text = RECALL_CSV.replace("0.9830", "abc")
        with self.assertRaisesRegex(plot_e4.CsvFormatError, "рядок 4"):
            plot_e4.read_recall(self.write("bad.csv", text))

    def test_missing_file_and_empty_file_are_errors(self) -> None:
        with self.assertRaises(plot_e4.CsvFormatError):
            plot_e4.read_bdn(self.path / "none.csv")
        with self.assertRaises(plot_e4.CsvFormatError):
            plot_e4.read_recall(self.write("empty.csv", "data;size;index;ef_search;recall_at_10;latency_ms_per_query\n"))


class TableAndPlotTests(unittest.TestCase):
    def setUp(self) -> None:
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.path = Path(self.folder.name)
        (self.path / "bdn.csv").write_text(BDN_CSV, encoding="utf-8")
        (self.path / "recall.csv").write_text(RECALL_CSV, encoding="utf-8")

    def test_table_has_both_indexes_and_default_ef_only(self) -> None:
        table = plot_e4.build_table(
            plot_e4.read_bdn(self.path / "bdn.csv"), plot_e4.read_recall(self.path / "recall.csv"),
        )
        text = "\n".join(table)
        self.assertIn("Hnsw (efSearch 100)", text)
        self.assertNotIn("efSearch 50", text)
        # Для випадкових векторів є затримка BenchmarkDotNet (10.95 мс на 100k), для кластеризованих — прочерк.
        uniform_100k = next(line for line in table if "uniform | 100 000 | BruteForce" in line)
        self.assertIn("10.95 мс", uniform_100k)
        clustered = next(line for line in table if line.startswith("| clustered | 1 000 | BruteForce"))
        self.assertIn("| — |", clustered)

    def test_main_writes_png_svg_and_table(self) -> None:
        out = self.path / "out"
        code = plot_e4.main([
            "--bdn", str(self.path / "bdn.csv"), "--recall", str(self.path / "recall.csv"), "--out", str(out),
        ])
        self.assertEqual(code, 0)
        for name in ("e4_recall_vs_latency.png", "e4_recall_vs_latency.svg", "e4_table.md"):
            self.assertGreater((out / name).stat().st_size, 0, name)

    def test_main_with_only_uniform_data(self) -> None:
        uniform_only = "\n".join(
            line for line in RECALL_CSV.splitlines() if not line.startswith("clustered")
        ) + "\n"
        (self.path / "recall.csv").write_text(uniform_only, encoding="utf-8")
        code = plot_e4.main([
            "--bdn", str(self.path / "bdn.csv"), "--recall", str(self.path / "recall.csv"), "--out", str(self.path / "out"),
        ])
        self.assertEqual(code, 0)

    def test_main_with_real_vectors_writes_separate_chart(self) -> None:
        real = (
            "data;size;index;ef_search;recall_at_10;latency_ms_per_query;build_seconds\n"
            "real;1000;BruteForce;0;1.0000;0.15000;0\n"
            "real;1000;Hnsw;100;0.9900;0.20000;0.80\n"
            "real;96000;BruteForce;0;1.0000;15.00000;0\n"
            "real;96000;Hnsw;10;0.4000;0.20000;300.00\n"
            "real;96000;Hnsw;100;0.9000;0.90000;300.00\n"
        )
        (self.path / "real.csv").write_text(real, encoding="utf-8")
        out = self.path / "out"
        code = plot_e4.main([
            "--bdn", str(self.path / "bdn.csv"), "--recall", str(self.path / "recall.csv"),
            "--real", str(self.path / "real.csv"), "--out", str(out),
        ])
        self.assertEqual(code, 0)
        self.assertGreater((out / "e4_real_recall_vs_latency.png").stat().st_size, 0)
        self.assertIn("| real | 96 000 | Hnsw (efSearch 100) | 0.900", (out / "e4_table.md").read_text(encoding="utf-8"))

    def test_main_unknown_input_returns_1(self) -> None:
        code = plot_e4.main([
            "--bdn", str(self.path / "none.csv"), "--recall", str(self.path / "recall.csv"), "--out", str(self.path / "out"),
        ])
        self.assertEqual(code, 1)


@unittest.skipUnless((EXAMPLE / "bdn-search-report.csv").is_file(), "приклад ще не додано")
class ExampleDataTests(unittest.TestCase):
    """Справжній прогін, доданий як приклад: має читатися й будувати графік."""

    def test_example_files_build_the_chart(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            code = plot_e4.main([
                "--bdn", str(EXAMPLE / "bdn-search-report.csv"), "--recall", str(EXAMPLE / "e4-recall.csv"), "--out", folder,
            ])
            self.assertEqual(code, 0)
            self.assertTrue((Path(folder) / "e4_recall_vs_latency.png").is_file())


if __name__ == "__main__":
    unittest.main()
