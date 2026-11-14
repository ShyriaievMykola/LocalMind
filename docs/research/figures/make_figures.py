#!/usr/bin/env python3
"""Графіки E1–E3 на open-eval з CSV команди `localmind eval` (docs/research/data/*.csv).

    python docs/research/figures/make_figures.py

Створює PNG і SVG у docs/research/figures/. Потрібен лише matplotlib (див. eval/plots/requirements.txt).
"""

from __future__ import annotations

import csv
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402

HERE = Path(__file__).resolve().parent
DATA = HERE.parent / "data"

MODE_COLORS = {"keyword": "#d95f02", "semantic": "#1b9e77", "hybrid": "#7570b3"}
MODE_LABELS = {"keyword": "keyword (BM25)", "semantic": "semantic", "hybrid": "hybrid (RRF)"}
TYPES = ("exact", "paraphrase", "crossLingual")
TYPE_LABELS = {"exact": "exact", "paraphrase": "paraphrase", "crossLingual": "crossLingual*"}


def read(name: str) -> list[dict]:
    with (DATA / name).open(newline="", encoding="utf-8-sig") as handle:
        rows = list(csv.DictReader(handle))
    for row in rows:
        for key in ("ndcg", "recall", "mrr", "latency_p50_ms", "latency_p95_ms", "index_seconds", "db_bytes",
                    "peak_memory_bytes", "chunks", "queries"):
            row[key] = float(row[key])
    return rows


def save(figure, name: str) -> None:
    figure.tight_layout()
    for extension in ("png", "svg"):
        figure.savefig(HERE / f"{name}.{extension}", dpi=150, bbox_inches="tight")
    plt.close(figure)
    print(f"saved: {name}")


def e1() -> None:
    rows = [r for r in read("e1-open-eval.csv")]
    figure, axis = plt.subplots(figsize=(7.5, 4.2))
    width = 0.26
    for offset, mode in enumerate(("keyword", "semantic", "hybrid")):
        values = [next(r["ndcg"] for r in rows if r["mode"] == mode and r["type"] == t) for t in (*TYPES, "all")]
        positions = [i + (offset - 1) * width for i in range(4)]
        bars = axis.bar(positions, values, width, label=MODE_LABELS[mode], color=MODE_COLORS[mode])
        axis.bar_label(bars, fmt="%.2f", fontsize=8, padding=2)
    axis.set_xticks(range(4), [*(TYPE_LABELS[t] for t in TYPES), "усі запити"])
    axis.set_ylabel("nDCG@10")
    axis.set_ylim(0, 1.08)
    axis.grid(axis="y", alpha=0.3)
    axis.legend()
    axis.set_title("E1: метод пошуку за типом запиту (open-eval, e5-small)")
    figure.text(0.01, -0.02, "* оцінка crossLingual завищена: версії статті іншими мовами отримують 3 автоматично", fontsize=8)
    save(figure, "e1_by_query_type")


def e2() -> None:
    rows = read("e2-open-eval.csv")
    models = []
    for row in rows:
        if row["model"] not in models:
            models.append(row["model"])
    short = {m: m.replace("multilingual-", "").replace("paraphrase-", "para-").replace("-v2", "") for m in models}

    figure, (left, right) = plt.subplots(1, 2, figsize=(11, 4.4), gridspec_kw={"width_ratios": [1.25, 1]})
    markers = {"semantic": "o", "hybrid": "^"}
    for mode in ("semantic", "hybrid"):
        for row in rows:
            if row["mode"] == mode and row["type"] == "all":
                left.scatter(row["latency_p50_ms"], row["ndcg"], s=80, marker=markers[mode], color=MODE_COLORS[mode])
                left.annotate(short[row["model"]], (row["latency_p50_ms"], row["ndcg"]), textcoords="offset points",
                              xytext=(6, 4), fontsize=8)
    for mode in ("semantic", "hybrid"):
        left.scatter([], [], marker=markers[mode], color=MODE_COLORS[mode], label=MODE_LABELS[mode])
    left.set_xlabel("Затримка пошуку p50, мс")
    left.set_ylabel("nDCG@10 (усі запити)")
    left.grid(alpha=0.3)
    left.legend(loc="center right")
    left.set_title("Якість проти затримки")

    index_minutes = [next(r["index_seconds"] for r in rows if r["model"] == m) / 60 for m in models]
    db_mb = [next(r["db_bytes"] for r in rows if r["model"] == m) / 1024 / 1024 for m in models]
    positions = range(len(models))
    bars = right.bar([p - 0.2 for p in positions], index_minutes, 0.4, color="#4c78a8", label="індексація, хв")
    right.bar_label(bars, fmt="%.0f", fontsize=8, padding=2)
    right.set_ylabel("Час індексації, хв")
    twin = right.twinx()
    bars2 = twin.bar([p + 0.2 for p in positions], db_mb, 0.4, color="#f58518", label="розмір бази, МБ")
    twin.bar_label(bars2, fmt="%.0f", fontsize=8, padding=2)
    twin.set_ylabel("Розмір бази, МБ")
    right.set_xticks(list(positions), [short[m] for m in models], rotation=15, fontsize=8)
    right.set_title("Вартість моделі")
    handles = [bars, bars2]
    right.legend(handles, ["індексація, хв", "розмір бази, МБ"], loc="upper left", fontsize=8)
    figure.suptitle("E2: embedding-моделі (open-eval, фрагмент 120 токенів)", fontsize=12)
    save(figure, "e2_models")


def e3() -> None:
    rows = read("e3-open-eval.csv")
    figure, (left, right) = plt.subplots(1, 2, figsize=(11, 4.2))
    sizes = sorted({int(r["chunk_size_tokens"]) for r in rows})
    for mode, linestyle in (("semantic", "-"), ("hybrid", "--")):
        for overlap, marker in ((0, "o"), (15, "s")):
            values = [
                next(r["ndcg"] for r in rows if r["mode"] == mode and r["type"] == "all"
                     and int(r["chunk_size_tokens"]) == size and int(r["overlap_percent"]) == overlap)
                for size in sizes
            ]
            left.plot(sizes, values, linestyle=linestyle, marker=marker, color=MODE_COLORS[mode],
                      alpha=1 if overlap == 15 else 0.55, label=f"{mode}, перекриття {overlap} %")
    left.set_xscale("log", base=2)
    left.set_xticks(sizes, [str(s) for s in sizes])
    left.set_xlabel("Розмір фрагмента, токенів")
    left.set_ylabel("nDCG@10 (усі запити)")
    left.set_ylim(0.5, 0.7)
    left.grid(alpha=0.3)
    left.legend(fontsize=8)
    left.set_title("Якість")

    for overlap, marker in ((0, "o"), (15, "s")):
        chunks = [
            next(r["chunks"] for r in rows if int(r["chunk_size_tokens"]) == size and int(r["overlap_percent"]) == overlap)
            for size in sizes
        ]
        right.plot(sizes, chunks, marker=marker, label=f"перекриття {overlap} %", color="#4c78a8" if overlap else "#9ecae9")
    right.set_xscale("log", base=2)
    right.set_xticks(sizes, [str(s) for s in sizes])
    right.set_xlabel("Розмір фрагмента, токенів")
    right.set_ylabel("Фрагментів у корпусі")
    right.grid(alpha=0.3)
    right.legend(fontsize=8)
    right.set_title("Ціна: кількість фрагментів")
    figure.suptitle("E3: нарізка на фрагменти (open-eval, e5-small)", fontsize=12)
    save(figure, "e3_chunking")


if __name__ == "__main__":
    e1()
    e2()
    e3()
