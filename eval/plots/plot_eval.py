#!/usr/bin/env python3
"""Графіки для звіту з CSV команди `localmind eval` (EV-06).

Читає один або кілька CSV (файли чи теки з CSV) і будує три графіки, PNG та SVG, з підписами українською:

1. quality_vs_latency — якість (nDCG@k і Recall@k) проти затримки пошуку p50 (відрізок до p95);
2. by_query_type      — nDCG@k і Recall@k за типом запиту (exact / paraphrase / crossLingual) для кожного режиму;
3. recall_vs_latency  — Recall@k проти p50 для прогонів з різним k (це не E4: графік E4 з'явиться після #158).

Формат CSV описано в README.md цієї теки. Запуск:

    python eval/plots/plot_eval.py eval/results/demo-*.csv --out eval/plots/out
"""

from __future__ import annotations

import argparse
import csv
import sys
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path

import matplotlib

matplotlib.use("Agg")  # без вікон: скрипт працює і в CI, і на сервері
import matplotlib.pyplot as plt  # noqa: E402

REQUIRED_COLUMNS = (
    "dataset", "run", "mode", "type", "queries", "k",
    "recall", "mrr", "ndcg", "latency_p50_ms", "latency_p95_ms",
)

MODE_ORDER = ("keyword", "semantic", "hybrid")
MODE_LABELS = {"keyword": "Keyword (BM25)", "semantic": "Semantic", "hybrid": "Hybrid (RRF)"}
MODE_COLORS = {"keyword": "#d95f02", "semantic": "#1b9e77", "hybrid": "#7570b3"}
MODE_MARKERS = {"keyword": "s", "semantic": "o", "hybrid": "^"}

TYPE_ORDER = ("exact", "paraphrase", "crossLingual")
TYPE_LABELS = {
    "exact": "Точні слова\n(exact)",
    "paraphrase": "Перефразування\n(paraphrase)",
    "crossLingual": "Інша мова\n(crossLingual)",
}

ALL_TYPE = "all"


@dataclass(frozen=True)
class Row:
    dataset: str
    run: str
    mode: str
    type: str
    queries: int
    k: int
    recall: float
    mrr: float
    ndcg: float
    p50: float
    p95: float

    @property
    def label(self) -> str:
        return f"{self.dataset}/{self.run}"


class CsvFormatError(ValueError):
    """CSV не відповідає формату `localmind eval`."""


def find_csv_files(inputs: list[str]) -> list[Path]:
    files: list[Path] = []
    for item in inputs:
        path = Path(item)
        if path.is_dir():
            files.extend(sorted(path.glob("*.csv")))
        elif path.is_file():
            files.append(path)
        else:
            raise CsvFormatError(f"Немає файла чи теки: {item}")
    if not files:
        raise CsvFormatError("Не знайдено жодного CSV.")
    return files


def read_rows(path: Path) -> list[Row]:
    with path.open(newline="", encoding="utf-8-sig") as handle:
        reader = csv.DictReader(handle)
        missing = [c for c in REQUIRED_COLUMNS if c not in (reader.fieldnames or [])]
        if missing:
            raise CsvFormatError(f"{path}: немає колонок {', '.join(missing)}")
        rows = []
        for number, record in enumerate(reader, start=2):
            try:
                rows.append(Row(
                    dataset=record["dataset"], run=record["run"], mode=record["mode"], type=record["type"],
                    queries=int(record["queries"]), k=int(record["k"]),
                    recall=float(record["recall"]), mrr=float(record["mrr"]), ndcg=float(record["ndcg"]),
                    p50=float(record["latency_p50_ms"]), p95=float(record["latency_p95_ms"]),
                ))
            except ValueError as error:
                raise CsvFormatError(f"{path}, рядок {number}: {error}") from error
    if not rows:
        raise CsvFormatError(f"{path}: у файлі немає даних.")
    return rows


def ordered_modes(rows: list[Row]) -> list[str]:
    present = {r.mode for r in rows}
    return [m for m in MODE_ORDER if m in present] + sorted(present - set(MODE_ORDER))


def save(figure: plt.Figure, out: Path, name: str) -> list[Path]:
    paths = []
    for extension in ("png", "svg"):
        target = out / f"{name}.{extension}"
        figure.savefig(target, dpi=160, bbox_inches="tight")
        paths.append(target)
    plt.close(figure)
    return paths


def plot_quality_vs_latency(rows: list[Row], out: Path) -> list[Path]:
    """Якість проти затримки: точка — p50, відрізок — до p95; лише рядки `all`."""
    data = [r for r in rows if r.type == ALL_TYPE]
    if not data:
        return []
    figure, axes = plt.subplots(1, 2, figsize=(11, 4.6), sharex=True)
    for axis, (metric, title) in zip(axes, (("ndcg", "nDCG@k"), ("recall", "Recall@k")), strict=True):
        for mode in ordered_modes(data):
            points = [r for r in data if r.mode == mode]
            xs = [r.p50 for r in points]
            ys = [getattr(r, metric) for r in points]
            axis.errorbar(
                xs, ys, xerr=[[0] * len(points), [max(r.p95 - r.p50, 0) for r in points]],
                fmt=MODE_MARKERS.get(mode, "o"), color=MODE_COLORS.get(mode), markersize=8, capsize=3,
                label=MODE_LABELS.get(mode, mode),
            )
            for r, x, y in zip(points, xs, ys, strict=True):
                if len({p.run for p in data}) > 1:
                    axis.annotate(r.run, (x, y), textcoords="offset points", xytext=(6, 6), fontsize=8)
        axis.set_xlabel("Затримка пошуку, мс (точка — p50, відрізок — до p95)")
        axis.set_ylabel(title)
        axis.set_ylim(-0.03, 1.05)
        axis.grid(alpha=0.3)
    axes[0].legend(title="Режим")
    figure.suptitle("Якість пошуку проти затримки", fontsize=13)
    figure.tight_layout()
    return save(figure, out, "quality_vs_latency")


def plot_by_query_type(rows: list[Row], out: Path) -> list[Path]:
    """Згруповані стовпчики: тип запиту × режим; для кількох прогонів береться перший за порядком у CSV."""
    first_run = rows[0].run
    data = [r for r in rows if r.run == first_run and r.type in TYPE_ORDER]
    if not data:
        return []
    modes = ordered_modes(data)
    types = [t for t in TYPE_ORDER if any(r.type == t for r in data)]
    width = 0.8 / len(modes)
    figure, axes = plt.subplots(1, 2, figsize=(11, 4.6), sharey=True)
    for axis, (metric, title) in zip(axes, (("ndcg", "nDCG@k"), ("recall", "Recall@k")), strict=True):
        for index, mode in enumerate(modes):
            values = []
            for kind in types:
                match = [r for r in data if r.mode == mode and r.type == kind]
                values.append(getattr(match[0], metric) if match else 0.0)
            positions = [i + (index - (len(modes) - 1) / 2) * width for i in range(len(types))]
            bars = axis.bar(positions, values, width, color=MODE_COLORS.get(mode), label=MODE_LABELS.get(mode, mode))
            axis.bar_label(bars, fmt="%.2f", fontsize=8, padding=2)
        axis.set_xticks(range(len(types)), [TYPE_LABELS.get(t, t) for t in types])
        axis.set_ylabel(title)
        axis.set_ylim(0, 1.12)
        axis.grid(axis="y", alpha=0.3)
    handles, labels = axes[0].get_legend_handles_labels()
    figure.legend(handles, labels, title="Режим", loc="lower center", ncol=len(modes))
    figure.suptitle(f"Якість за типом запиту (прогін «{first_run}»)", fontsize=13)
    figure.tight_layout(rect=(0, 0.1, 1, 1))
    return save(figure, out, "by_query_type")


def plot_recall_vs_latency(rows: list[Row], out: Path) -> list[Path]:
    """Recall@k проти p50 для прогонів з різним k: лінія на режим, підпис точки — k."""
    data = [r for r in rows if r.type == ALL_TYPE]
    if len({r.k for r in data}) < 2:
        return []  # одного k замало для залежності; перший графік уже показує цей прогін
    figure, axis = plt.subplots(figsize=(7, 4.8))
    for mode in ordered_modes(data):
        points = sorted((r for r in data if r.mode == mode), key=lambda r: r.k)
        axis.plot(
            [r.p50 for r in points], [r.recall for r in points],
            marker=MODE_MARKERS.get(mode, "o"), color=MODE_COLORS.get(mode), label=MODE_LABELS.get(mode, mode),
        )
        for r in points:
            axis.annotate(f"k={r.k}", (r.p50, r.recall), textcoords="offset points", xytext=(6, -12), fontsize=8)
    axis.set_xlabel("Затримка пошуку p50, мс")
    axis.set_ylabel("Recall@k")
    axis.set_ylim(-0.03, 1.05)
    axis.grid(alpha=0.3)
    axis.legend(title="Режим")
    axis.set_title("Recall проти затримки для різної глибини k")
    figure.tight_layout()
    return save(figure, out, "recall_vs_latency")


def main(argv: list[str] | None = None) -> int:
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):  # консоль Windows за замовчуванням не cp1251/UTF-8
            stream.reconfigure(encoding="utf-8", errors="replace")

    parser = argparse.ArgumentParser(description="Графіки з CSV команди `localmind eval`.")
    parser.add_argument("inputs", nargs="+", help="CSV-файли або теки з CSV (наприклад, eval/results)")
    parser.add_argument("--out", default="eval/plots/out", help="тека для PNG і SVG (за замовчуванням eval/plots/out)")
    args = parser.parse_args(argv)

    try:
        rows: list[Row] = []
        for path in find_csv_files(args.inputs):
            rows.extend(read_rows(path))
    except CsvFormatError as error:
        print(f"Помилка: {error}", file=sys.stderr)
        return 1

    plt.rcParams["font.family"] = "DejaVu Sans"  # входить до matplotlib, має кирилицю
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)

    by_dataset: dict[str, list[Row]] = defaultdict(list)
    for row in rows:
        by_dataset[row.dataset].append(row)

    written: list[Path] = []
    for dataset, items in by_dataset.items():
        target = out / dataset if len(by_dataset) > 1 else out
        target.mkdir(parents=True, exist_ok=True)
        written += plot_quality_vs_latency(items, target)
        written += plot_by_query_type(items, target)
        written += plot_recall_vs_latency(items, target)

    for path in written:
        print(f"Збережено: {path}")
    if not written:
        print("Помилка: у CSV немає рядків для побудови графіків.", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
