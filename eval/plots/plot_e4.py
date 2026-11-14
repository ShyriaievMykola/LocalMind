#!/usr/bin/env python3
"""Графік E4 (R-06): recall проти затримки, повний перебір (BruteForce) проти HNSW на 1k / 10k / 100k векторів.

Вхід — два CSV з проєкту `benchmarks/LocalMind.Benchmarks`:

1. звіт BenchmarkDotNet `*SearchBenchmarks-report.csv` (роздільник «;», колонки Size, Kind, Mean, ...):
   затримка одного запиту top-10 за замовчуванням HNSW (efSearch = 100) і повного перебору;
2. `e4-recall.csv` (`-- --recall`): recall@10 HNSW проти точних сусідів повного перебору для кількох efSearch
   і затримка в тому самому прогоні для двох наборів даних — uniform (ті самі випадкові вектори, що в BenchmarkDotNet)
   і clustered (з темами, ближче до справжніх embeddings); колонки data;size;index;ef_search;recall_at_10;latency_ms_per_query.

BenchmarkDotNet recall не міряє, тому крива «recall — затримка» береться з другого файла, а точки з першого
накладаються хрестиками: це незалежний замір тієї самої затримки. Запуск:

    python eval/plots/plot_e4.py --bdn <звіт BenchmarkDotNet>.csv --recall eval/results/e4-recall.csv --out eval/plots/out
"""

from __future__ import annotations

import argparse
import csv
import sys
from dataclasses import dataclass
from pathlib import Path

import matplotlib

matplotlib.use("Agg")  # без вікон: скрипт працює і в CI, і на сервері
import matplotlib.pyplot as plt  # noqa: E402
from matplotlib.ticker import NullFormatter  # noqa: E402

BDN_REQUIRED = ("Size", "Kind", "Mean")
RECALL_REQUIRED = ("data", "size", "index", "ef_search", "recall_at_10", "latency_ms_per_query")

DATA_ORDER = ("uniform", "clustered", "real")
DATA_TITLES = {
    "uniform": "Рівномірні випадкові вектори (як у BenchmarkDotNet)",
    "clustered": "Кластеризовані вектори (теми + шум)",
    "real": "Справжні вектори e5-small (відкритий корпус)",
}

BRUTE = "BruteForce"
HNSW = "Hnsw"
DEFAULT_EF_SEARCH = 100  # значення VectorIndexOptions.EfSearch за замовчуванням: його міряє BenchmarkDotNet

# Одиниці BenchmarkDotNet → мілісекунди. μ буває двох кодових точок (U+03BC і U+00B5), а в старих версіях — «us».
UNIT_TO_MS = {"ns": 1e-6, "us": 1e-3, "μs": 1e-3, "µs": 1e-3, "ms": 1.0, "s": 1000.0}

COLORS = {BRUTE: "#d95f02", HNSW: "#1b9e77"}


class CsvFormatError(ValueError):
    """CSV не відповідає очікуваному формату."""


@dataclass(frozen=True)
class BdnPoint:
    size: int
    kind: str
    mean_ms: float  # середній час одного запиту, мс


@dataclass(frozen=True)
class RecallPoint:
    data: str
    size: int
    index: str
    ef_search: int
    recall: float
    latency_ms: float


def parse_bdn_time(text: str) -> float:
    """«1,165.7 μs» → мілісекунди (кома — роздільник тисяч, крапка — десяткова, як у звітах BenchmarkDotNet)."""
    parts = text.strip().replace(" ", " ").split()
    if len(parts) != 2 or parts[1] not in UNIT_TO_MS:
        raise CsvFormatError(f"не вдалося розібрати час BenchmarkDotNet: «{text}»")
    try:
        return float(parts[0].replace(",", "")) * UNIT_TO_MS[parts[1]]
    except ValueError as error:
        raise CsvFormatError(f"не вдалося розібрати час BenchmarkDotNet: «{text}»") from error


def _open_rows(path: Path, required: tuple[str, ...], delimiter: str) -> list[tuple[int, dict[str, str]]]:
    if not path.is_file():
        raise CsvFormatError(f"Немає файла: {path}")
    with path.open(newline="", encoding="utf-8-sig") as handle:
        reader = csv.DictReader(handle, delimiter=delimiter)
        missing = [c for c in required if c not in (reader.fieldnames or [])]
        if missing:
            raise CsvFormatError(f"{path}: немає колонок {', '.join(missing)}")
        rows = list(enumerate(reader, start=2))
    if not rows:
        raise CsvFormatError(f"{path}: у файлі немає даних.")
    return rows


def read_bdn(path: Path) -> list[BdnPoint]:
    points = []
    for number, record in _open_rows(path, BDN_REQUIRED, ";"):
        try:
            points.append(BdnPoint(int(record["Size"]), record["Kind"], parse_bdn_time(record["Mean"])))
        except (ValueError, CsvFormatError) as error:
            raise CsvFormatError(f"{path}, рядок {number}: {error}") from error
    return points


def read_recall(path: Path) -> list[RecallPoint]:
    points = []
    for number, record in _open_rows(path, RECALL_REQUIRED, ";"):
        try:
            points.append(RecallPoint(
                record["data"], int(record["size"]), record["index"], int(record["ef_search"]),
                float(record["recall_at_10"]), float(record["latency_ms_per_query"]),
            ))
        except ValueError as error:
            raise CsvFormatError(f"{path}, рядок {number}: {error}") from error
    return points


def format_latency(ms: float) -> str:
    return f"{ms * 1000:,.0f} мкс" if ms < 1 else f"{ms:,.2f} мс"


def build_table(bdn: list[BdnPoint], recall: list[RecallPoint]) -> list[str]:
    """Рядки Markdown-таблиці: набір даних × розмір × індекс; HNSW — із efSearch за замовчуванням.

    Затримка BenchmarkDotNet є лише для випадкових векторів (так задано в бенчмарку), для кластеризованих там «—».
    """
    lines = [
        "| Дані | Векторів | Індекс | Recall@10 | Затримка (BenchmarkDotNet) | Затримка (прогін recall) |",
        "|---|---:|---|---:|---:|---:|",
    ]
    for data in DATA_ORDER:
        for size in sorted({p.size for p in recall if p.data == data}):
            for kind in (BRUTE, HNSW):
                ef = 0 if kind == BRUTE else DEFAULT_EF_SEARCH
                point = next((p for p in recall if p.data == data and p.size == size and p.index == kind and p.ef_search == ef), None)
                if point is None:
                    continue
                measured = next((p for p in bdn if p.size == size and p.kind == kind), None) if data == "uniform" else None
                name = kind if kind == BRUTE else f"{kind} (efSearch {ef})"
                lines.append(
                    f"| {data} | {size:,} | {name} | {point.recall:.3f} "
                    f"| {format_latency(measured.mean_ms) if measured else '—'} | {format_latency(point.latency_ms)} |".replace(",", " ")
                )
    return lines


def plot_recall_vs_latency(bdn: list[BdnPoint], recall: list[RecallPoint], out: Path, name: str = "e4_recall_vs_latency") -> list[Path]:
    """Сітка: рядок — набір даних, стовпчик — кількість векторів; вісь X — затримка (логарифмічна), Y — recall@10."""
    datasets = [d for d in DATA_ORDER if any(p.data == d for p in recall)]
    sizes = sorted({p.size for p in recall})
    if not datasets:
        raise CsvFormatError("У файлі recall немає наборів даних uniform чи clustered.")
    figure, axes = plt.subplots(len(datasets), len(sizes), figsize=(4.4 * len(sizes), 4.0 * len(datasets) + 0.9),
                                sharey=True, squeeze=False)
    for row, data in enumerate(datasets):
        for column, size in enumerate(sizes):
            axis = axes[row][column]
            curve = sorted((p for p in recall if p.data == data and p.size == size and p.index == HNSW), key=lambda p: p.ef_search)
            brute = next((p for p in recall if p.data == data and p.size == size and p.index == BRUTE), None)
            if curve:
                axis.plot([p.latency_ms for p in curve], [p.recall for p in curve], marker="o", color=COLORS[HNSW],
                          label="HNSW: крива за efSearch")
                for p in curve:
                    axis.annotate(f"ef={p.ef_search}", (p.latency_ms, p.recall), textcoords="offset points",
                                  xytext=(5, -12), fontsize=7)
            if brute:
                axis.plot([brute.latency_ms], [brute.recall], marker="s", linestyle="", markersize=9, color=COLORS[BRUTE],
                          label="Повний перебір (recall = 1)")
            if data == "uniform":  # BenchmarkDotNet міряє саме випадкові вектори
                for kind, ef in ((BRUTE, 0), (HNSW, DEFAULT_EF_SEARCH)):
                    measured = next((p for p in bdn if p.size == size and p.kind == kind), None)
                    point = next((p for p in recall if p.data == data and p.size == size and p.index == kind and p.ef_search == ef), None)
                    if measured and point:
                        axis.plot([measured.mean_ms], [point.recall], marker="x", linestyle="", markersize=10,
                                  markeredgewidth=2, color=COLORS[kind],
                                  label="Замір BenchmarkDotNet" if kind == BRUTE else None)
            axis.set_xscale("log")
            axis.xaxis.set_minor_formatter(NullFormatter())  # підписи малих поділок на log-осі налізають одне на одного
            axis.set_title(f"{size:,} векторів".replace(",", " "), fontsize=10)
            low = min(point.recall for point in recall)
            axis.set_ylim((max(0.0, low - 0.04), 1.01) if low > 0.85 else (-0.03, 1.05))  # узкий діапазон, якщо recall скрізь високий
            axis.grid(alpha=0.3, which="both")
            if row == len(datasets) - 1:
                axis.set_xlabel("Затримка запиту top-10, мс (log)")
        axes[row][0].set_ylabel(f"{DATA_TITLES.get(data, data)}\nRecall@10", fontsize=9)
    handles, labels = [], []
    for axis in axes.flat:
        for handle, label in zip(*axis.get_legend_handles_labels(), strict=True):
            if label not in labels:
                handles.append(handle)
                labels.append(label)
    figure.legend(handles, labels, loc="lower center", ncol=len(labels))
    figure.suptitle("E4: повний перебір проти HNSW — точність і затримка", fontsize=13)
    figure.tight_layout(rect=(0, 0.06, 1, 1))
    paths = []
    for extension in ("png", "svg"):
        target = out / f"{name}.{extension}"
        figure.savefig(target, dpi=160, bbox_inches="tight")
        paths.append(target)
    plt.close(figure)
    return paths


def main(argv: list[str] | None = None) -> int:
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):  # консоль Windows за замовчуванням не UTF-8
            stream.reconfigure(encoding="utf-8", errors="replace")

    parser = argparse.ArgumentParser(description="Графік E4: recall проти затримки, BruteForce проти HNSW.")
    parser.add_argument("--bdn", required=True, help="звіт BenchmarkDotNet *SearchBenchmarks-report.csv")
    parser.add_argument("--recall", required=True, help="e4-recall.csv з `dotnet run ... -- --recall`")
    parser.add_argument("--real", default=None, help="e4-real.csv (`-- --real`): справжні вектори; окремий графік e4_real_recall_vs_latency")
    parser.add_argument("--out", default="eval/plots/out", help="тека для PNG, SVG і таблиці (за замовчуванням eval/plots/out)")
    args = parser.parse_args(argv)

    try:
        bdn = read_bdn(Path(args.bdn))
        recall = read_recall(Path(args.recall))
        plt.rcParams["font.family"] = "DejaVu Sans"  # входить до matplotlib, має кирилицю
        out = Path(args.out)
        out.mkdir(parents=True, exist_ok=True)
        written = plot_recall_vs_latency(bdn, recall, out)
        if args.real:
            real = read_recall(Path(args.real))
            written += plot_recall_vs_latency([], real, out, name="e4_real_recall_vs_latency")
            recall = recall + real
    except CsvFormatError as error:
        print(f"Помилка: {error}", file=sys.stderr)
        return 1

    table = build_table(bdn, recall)
    (out / "e4_table.md").write_text("\n".join(table) + "\n", encoding="utf-8")
    print("\n".join(table))
    for path in written:
        print(f"Збережено: {path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
