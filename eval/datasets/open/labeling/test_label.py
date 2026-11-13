"""Тести інструментів розмітки: `python -m unittest discover -s eval/datasets/open/labeling -p "test_label.py"`."""

import json
import tempfile
import unittest
from collections import Counter
from pathlib import Path

import label


def fake_manifest() -> list[dict]:
    """Штучний маніфест: 5 мов, 3 джерела, 4 теми; для англійської, української й німецької є версії однієї статті."""
    rows = []
    for index in range(240):
        lang = ["uk", "en", "de", "pl", "fr"][index % 5]
        source = ["wikipedia", "stackexchange", "gutenberg"][index % 3]
        rows.append({
            "path": f"{source}/{lang}/doc{index}.md", "lang": lang, "source": source, "title": f"Doc {index}",
            "topic": ["science", "travel", "cooking", "law"][index % 4], "format": "md", "chars": 3000 + index,
            "group": "", "sha256": "0" * 64,
        })
    for article in range(60):
        for lang in ("uk", "en", "de", "es"):
            rows.append({
                "path": f"wikipedia/{lang}/art{article}.md", "lang": lang, "source": "wikipedia", "title": f"Art {article}",
                "topic": ["science", "travel", "cooking", "law"][article % 4], "format": "md", "chars": 4000,
                "group": f"art{article}", "sha256": "0" * 64,
            })
    return rows


class QueryRulesTests(unittest.TestCase):
    DOCUMENT = "SQLite uses write-ahead logging; the journal mode WAL lets readers work while one writer commits transactions."

    def meta(self, query_type: str, query_lang: str = "en", doc_lang: str = "en") -> dict:
        return {"type": query_type, "query_lang": query_lang, "doc_lang": doc_lang}

    def test_paraphrase_without_shared_words_passes(self):
        query = {"id": "q1", "type": "paraphrase", "text": "concurrent readers during persistence"}
        # «readers» є в документі, тому має бути помилка; нижче — варіант без спільних слів.
        self.assertTrue(label.check_query(query, self.meta("paraphrase"), self.DOCUMENT))
        query["text"] = "simultaneous access while storing changes"
        self.assertEqual([], label.check_query(query, self.meta("paraphrase"), self.DOCUMENT))

    def test_paraphrase_detects_shared_stem_not_only_exact_form(self):
        query = {"id": "q1", "type": "paraphrase", "text": "committing transaction"}
        problems = label.check_query(query, self.meta("paraphrase"), self.DOCUMENT)
        self.assertTrue(any("transaction" in problem or "commit" in problem for problem in problems), problems)

    def test_stop_words_do_not_count_as_overlap(self):
        query = {"id": "q1", "type": "paraphrase", "text": "while with the which"}
        self.assertEqual([], label.check_query(query, self.meta("paraphrase"), self.DOCUMENT))

    def test_exact_requires_query_terms_to_be_in_document(self):
        ok = {"id": "q1", "type": "exact", "text": "journal mode WAL"}
        bad = {"id": "q2", "type": "exact", "text": "journal mode Oracle"}
        self.assertEqual([], label.check_query(ok, self.meta("exact"), self.DOCUMENT))
        self.assertTrue(label.check_query(bad, self.meta("exact"), self.DOCUMENT))

    def test_cross_lingual_needs_a_different_language(self):
        query = {"id": "q1", "type": "crossLingual", "text": "режим журналу"}
        self.assertTrue(label.check_query(query, self.meta("crossLingual", "uk", "uk"), self.DOCUMENT))
        self.assertEqual([], label.check_query(query, self.meta("crossLingual", "uk", "en"), self.DOCUMENT))

    def test_same_language_query_must_not_be_marked_cross_lingual_reversed(self):
        query = {"id": "q1", "type": "exact", "text": "journal mode"}
        self.assertTrue(label.check_query(query, self.meta("exact", "uk", "en"), self.DOCUMENT))

    def test_russian_letters_in_ukrainian_query_are_rejected(self):
        query = {"id": "q1", "type": "crossLingual", "text": "как это работает, объясни"}
        self.assertTrue(any("російськ" in p for p in label.check_query(query, self.meta("crossLingual", "uk", "en"), self.DOCUMENT)))
        ukrainian = {"id": "q2", "type": "crossLingual", "text": "як працює журнал записів"}
        self.assertEqual([], label.check_query(ukrainian, self.meta("crossLingual", "uk", "en"), self.DOCUMENT))

    def test_empty_query_is_rejected(self):
        self.assertTrue(label.check_query({"id": "q1", "type": "exact", "text": "  "}, self.meta("exact"), self.DOCUMENT))


class SampleTests(unittest.TestCase):
    def run_sample(self, manifest: list[dict], seed: int = 1) -> list[dict]:
        with tempfile.TemporaryDirectory() as folder:
            manifest_path, out = Path(folder, "manifest.jsonl"), Path(folder, "sample.jsonl")
            label.write_jsonl(manifest_path, manifest)
            label.main(["sample", "--manifest", str(manifest_path), "--out", str(out), "--seed", str(seed)])
            return label.read_jsonl(out)

    def test_thirty_per_type_balanced_and_deterministic(self):
        rows = self.run_sample(fake_manifest())

        self.assertEqual(90, len(rows))
        self.assertEqual({"exact": 30, "paraphrase": 30, "crossLingual": 30}, dict(Counter(r["type"] for r in rows)))
        self.assertEqual(rows, self.run_sample(fake_manifest()))
        self.assertNotEqual([r["path"] for r in rows], [r["path"] for r in self.run_sample(fake_manifest(), seed=2)])

    def test_each_document_group_is_used_once_and_ids_are_unique(self):
        rows = self.run_sample(fake_manifest())

        keys = [r["group"] or r["path"] for r in rows]
        self.assertEqual(len(keys), len(set(keys)))
        self.assertEqual(len(rows), len({r["id"] for r in rows}))

    def test_languages_include_uk_and_en_majority_and_no_russian(self):
        rows = self.run_sample(fake_manifest())

        queries = [r["query_lang"] for r in rows]
        self.assertNotIn("ru", queries)
        for lang in ("uk", "en", "de", "pl", "fr"):
            self.assertIn(lang, queries)
        self.assertGreater(queries.count("uk") + queries.count("en"), 60)

    def test_cross_lingual_rows_have_other_language_siblings_and_a_different_query_language(self):
        rows = [r for r in self.run_sample(fake_manifest()) if r["type"] == "crossLingual"]

        self.assertEqual(30, len(rows))
        for row in rows:
            self.assertNotEqual(row["query_lang"], row["doc_lang"])
            self.assertTrue(row["siblings"], row)

    def test_strata_cover_several_sources_and_topics(self):
        rows = self.run_sample(fake_manifest())

        self.assertGreaterEqual(len({r["source"] for r in rows}), 2)
        self.assertGreaterEqual(len({r["topic"] for r in rows}), 3)


class PoolingTests(unittest.TestCase):
    OUTPUT = """1. [0.812] /data/corpus/wikipedia/en/art1.md
   …the journal [mode] of …

2. [0.700] C:\\data\\corpus\\wikipedia\\uk\\art2.md
   другий уривок

"""

    def test_parse_search_output_maps_paths_to_corpus_relative(self):
        results = label.parse_search_output(self.OUTPUT, Path("/data/corpus"))

        self.assertEqual([1, 2], [r["rank"] for r in results])
        self.assertEqual("wikipedia/en/art1.md", results[0]["path"])
        self.assertEqual(0.812, results[0]["score"])
        self.assertEqual("…the journal [mode] of …", results[0]["snippet"])

    def test_plain_snippet_removes_cli_markup(self):
        self.assertEqual("the journal mode of", label.plain_snippet("…the journal [mode] of …"))

    def test_fragment_context_surrounds_the_snippet(self):
        document = "Початок. " * 100 + "Особливий рядок про WAL режим журналу. " + "Кінець. " * 100

        context = label.fragment_context(document, "…Особливий рядок про [WAL] режим…")

        self.assertIn("Особливий рядок про WAL", context)
        self.assertLess(len(context), 1000)

    def build(self):
        queries = [
            {"id": "q001", "text": "wal mode", "type": "exact"},
            {"id": "q002", "text": "режим журналу", "type": "crossLingual"},
        ]
        meta = {
            "q001": {"id": "q001", "type": "exact", "path": "d/en/a.md", "siblings": [], "doc_lang": "en", "query_lang": "en"},
            "q002": {"id": "q002", "type": "crossLingual", "path": "d/en/b.md", "siblings": ["d/uk/b.md", "d/de/b.md"],
                     "doc_lang": "en", "query_lang": "uk"},
        }
        pool = [
            {"query": "q001", "mode": "keyword", "rank": 1, "path": "d/en/a.md", "score": 1.0, "snippet": "a"},
            {"query": "q001", "mode": "semantic", "rank": 2, "path": "d/en/c.md", "score": 0.9, "snippet": "c"},
            {"query": "q001", "mode": "hybrid", "rank": 1, "path": "d/en/c.md", "score": 0.1, "snippet": "c"},
            {"query": "q001", "mode": "hybrid", "rank": 9, "path": "d/en/late.md", "score": 0.1, "snippet": "late"},
            {"query": "q002", "mode": "semantic", "rank": 1, "path": "d/uk/b.md", "score": 0.9, "snippet": "b"},
            {"query": "q002", "mode": "semantic", "rank": 2, "path": "d/en/x.md", "score": 0.8, "snippet": "x"},
        ]
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            for path in ("d/en/a.md", "d/en/c.md", "d/en/late.md", "d/en/b.md", "d/uk/b.md", "d/de/b.md", "d/en/x.md"):
                (root / path).parent.mkdir(parents=True, exist_ok=True)
                (root / path).write_text(f"Text of {path}", encoding="utf-8")
            return queries, label.build_candidates(queries, meta, pool, {}, root, top=7)

    def test_candidates_are_the_union_of_modes_within_top_n_plus_source(self):
        _, candidates = self.build()

        first = {c["document"]: c for c in candidates if c["query"] == "q001"}
        self.assertEqual({"d/en/a.md", "d/en/c.md"}, set(first))  # rank 9 поза top-7
        self.assertEqual({"semantic": 2, "hybrid": 1}, {k: v for k, v in first["d/en/c.md"]["modes"].items()})
        self.assertTrue(first["d/en/a.md"]["auto"])  # документ-джерело
        self.assertFalse(first["d/en/c.md"]["auto"])

    def test_cross_lingual_translations_are_automatic_candidates(self):
        _, candidates = self.build()

        second = {c["document"]: c for c in candidates if c["query"] == "q002"}
        self.assertEqual({"d/en/b.md", "d/uk/b.md", "d/de/b.md", "d/en/x.md"}, set(second))
        self.assertTrue(all(second[p]["auto"] for p in ("d/en/b.md", "d/uk/b.md", "d/de/b.md")))
        self.assertFalse(second["d/en/x.md"]["auto"])


class JudgmentsTests(unittest.TestCase):
    CANDIDATES = [
        {"query": "q001", "document": "a.md", "auto": True, "title": "A", "context": "a"},
        {"query": "q001", "document": "c.md", "auto": False, "title": "C", "context": "c"},
        {"query": "q001", "document": "d.md", "auto": False, "title": "D", "context": "d"},
    ]
    QUERIES = [{"id": "q001", "text": "x", "type": "exact"}]

    def judgment(self, document: str, grade: int, **extra) -> dict:
        return {"query": "q001", "document": document, "grade": grade, "by": "агент", "how": "інструкція v1", **extra}

    def test_complete_judgments_have_no_problems(self):
        self.assertEqual([], label.check_judgments(self.CANDIDATES, [self.judgment("c.md", 2), self.judgment("d.md", 0)]))

    def test_missing_invalid_duplicate_and_unknown_judgments_are_reported(self):
        problems = label.check_judgments(self.CANDIDATES, [
            self.judgment("c.md", 5), self.judgment("c.md", 1), self.judgment("zzz.md", 1),
            {"query": "q001", "document": "d.md", "grade": 1},
        ])

        text = "\n".join(problems)
        self.assertIn("0–3", text)
        self.assertIn("разів", text)
        self.assertIn("немає серед кандидатів", text)
        self.assertIn("by / how", text)

    def test_missing_judgment_is_reported(self):
        self.assertTrue(any("немає оцінки" in p for p in label.check_judgments(self.CANDIDATES, [self.judgment("c.md", 2)])))

    def test_qrels_drop_zeros_and_include_automatic_sources_with_grade_three(self):
        qrels, without = label.build_qrels(self.QUERIES, self.CANDIDATES, [self.judgment("c.md", 2), self.judgment("d.md", 0)])

        self.assertEqual([
            {"query": "q001", "document": "a.md", "relevance": 3},
            {"query": "q001", "document": "c.md", "relevance": 2},
        ], qrels)
        self.assertEqual([], without)

    def test_query_without_relevant_documents_is_reported(self):
        _, without = label.build_qrels(self.QUERIES, [self.CANDIDATES[1]], [self.judgment("c.md", 0)])

        self.assertEqual(["q001"], without)

    def test_human_sample_has_requested_size_covers_grades_and_is_deterministic(self):
        candidates = [{"query": "q001", "document": f"d{i}.md", "auto": False, "title": f"T{i}", "context": f"ctx {i} | x"}
                      for i in range(80)]
        judgments = [self.judgment(f"d{i}.md", i % 4) for i in range(80)]

        rows = label.human_sample(candidates, judgments, self.QUERIES, 30, seed=3)

        self.assertEqual(30, len(rows))
        self.assertEqual({0, 1, 2, 3}, {r["judgment"]["grade"] for r in rows})
        self.assertEqual([r["judgment"]["document"] for r in rows],
                         [r["judgment"]["document"] for r in label.human_sample(candidates, judgments, self.QUERIES, 30, seed=3)])
        markdown = label.render_human_sample(rows)
        self.assertEqual(30, sum(1 for line in markdown.splitlines() if line.startswith("| ") and line.split("|")[1].strip().isdigit()))
        self.assertIn("згоден/ні", markdown)
        self.assertNotIn("ctx 1 | x", markdown)  # вертикальна риска в клітинці екранується


class EndToEndOnTinyDatasetTests(unittest.TestCase):
    def test_build_writes_files_in_the_eval_dataset_format(self):
        with tempfile.TemporaryDirectory() as folder:
            out = Path(folder)
            label.write_jsonl(out / "queries-in.jsonl", [{"id": "q001", "text": "wal", "type": "exact"}])
            label.write_jsonl(out / "cand.jsonl", [
                {"query": "q001", "document": "a.md", "auto": True, "title": "A", "context": "a", "modes": {"source": 0}},
                {"query": "q001", "document": "c.md", "auto": False, "title": "C", "context": "c", "modes": {"hybrid": 1}},
            ])
            label.write_jsonl(out / "judg.jsonl", [
                {"query": "q001", "document": "c.md", "grade": 1, "by": "агент", "how": "v1"},
            ])

            code = label.main(["build", "--queries", str(out / "queries-in.jsonl"), "--candidates", str(out / "cand.jsonl"),
                               "--judgments", str(out / "judg.jsonl"), "--out-dir", str(out / "ds")])

            self.assertEqual(0, code)
            self.assertEqual({"id": "q001", "text": "wal", "type": "exact"}, json.loads((out / "ds/queries.jsonl").read_text(encoding="utf-8")))
            qrels = label.read_jsonl(out / "ds/qrels.jsonl")
            self.assertEqual([("a.md", 3), ("c.md", 1)], [(r["document"], r["relevance"]) for r in qrels])


if __name__ == "__main__":
    unittest.main()
