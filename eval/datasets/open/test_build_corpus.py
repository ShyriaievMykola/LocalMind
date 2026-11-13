"""Тести build_corpus.py: нормалізація, розбір вікірозмітки й HTML, мова, формати, розбиття книг, запис і кеш.

Мережі не потребують. Запуск з теки eval/datasets/open (venv із requirements.txt):

    python -m unittest discover -s . -p "test_*.py"
"""

from __future__ import annotations

import hashlib
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).parent))

import build_corpus as bc  # noqa: E402

UK_TEXT = ("Київ — столиця України, розташована на берегах Дніпра. Місто має багату історію, чудові парки та музеї, "
           "а також відомі на весь світ собори й монастирі. Щороку сюди приїжджають тисячі туристів, щоб побачити "
           "Софійський собор і пройтися Хрещатиком. ") * 3
EN_TEXT = ("The quick brown fox jumps over the lazy dog while the sun rises above the quiet river. Travelers often "
           "describe the old town as friendly, affordable and full of history, with narrow streets and small cafes. ") * 4
DE_TEXT = ("Die Stadt liegt am Fluss und hat eine lange Geschichte. Viele Besucher kommen jedes Jahr, um die alten "
           "Gebäude, die Museen und die gemütlichen Cafés in der Altstadt zu sehen und das Essen zu probieren. ") * 4


class TextTests(unittest.TestCase):
    def test_normalize(self) -> None:
        raw = "a b​\r\nline  \t\n\n\n\n\nend  "
        self.assertEqual(bc.normalize_text(raw), "a b\nline\n\nend")

    def test_wiki_headings(self) -> None:
        text = "Intro\n\n== Історія ==\ntext\n=== Підрозділ ===\nmore"
        self.assertEqual(bc.wiki_headings_to_markdown(text), "Intro\n\n## Історія\ntext\n### Підрозділ\nmore")

    def test_truncate_on_paragraph(self) -> None:
        text = "перший абзац\n\nдругий абзац\n\nтретій абзац"
        self.assertEqual(bc.truncate_text(text, 30), "перший абзац\n\nдругий абзац")
        self.assertEqual(bc.truncate_text(text, 1000), text)

    def test_slugify_keeps_cyrillic_and_drops_symbols(self) -> None:
        self.assertEqual(bc.slugify("Київ: «Місто» (2024)"), "київ-місто-2024")
        self.assertEqual(bc.slugify("???"), "doc")
        self.assertLessEqual(len(bc.slugify("a" * 200)), 60)


class WikitextTests(unittest.TestCase):
    SAMPLE = (
        "{{Infobox person|name=X}}\n"
        "'''Python''' is a [[programming language|language]] made by [[Guido van Rossum]].<ref>Cite</ref>\n"
        "[[File:Logo.png|thumb|The logo]]\n"
        "== History ==\n"
        "It started in [[1991]].\n"
        "* first item\n"
        "# numbered item\n"
        "{| class=\"wikitable\"\n|-\n| cell\n|}\n"
        "== See also ==\n"
        "* [[Ruby]]\n"
        "== References ==\n"
        "{{reflist}}\n"
        "[[Category:Programming languages]]\n"
    )

    def test_wikitext_to_text(self) -> None:
        text = bc.wikitext_to_text(self.SAMPLE)
        self.assertIn("Python is a language made by Guido van Rossum.", text)
        self.assertIn("## History", text)
        self.assertIn("It started in 1991.", text)
        self.assertIn("- first item", text)
        self.assertIn("- numbered item", text)
        for junk in ("Infobox", "Cite", "Logo.png", "wikitable", "cell", "Ruby", "Category", "reflist", "See also"):
            self.assertNotIn(junk, text)

    def test_drop_stop_sections_keeps_following_sections(self) -> None:
        text = "# A\n\nбуква\n\n## Примітки\n\nвиноска\n\n### Дрібніше\n\nще виноска\n\n## Історія\n\nтекст"
        result = bc.drop_stop_sections(text)
        self.assertNotIn("виноска", result)
        self.assertIn("## Історія", result)
        self.assertIn("текст", result)


class HtmlTests(unittest.TestCase):
    def test_html_to_text(self) -> None:
        html = ("<p>Use <code>git log</code> to see history.</p><ul><li>one</li><li>two</li></ul>"
                "<pre><code>x = 1\ny = 2</code></pre><script>alert(1)</script><h2>Title</h2><p>End &amp; done</p>")
        text = bc.html_to_text(html)
        self.assertIn("Use `git log` to see history.", text)
        self.assertIn("- one\n- two", text)
        self.assertIn("```\nx = 1\ny = 2\n```", text)
        self.assertNotIn("alert", text)
        self.assertIn("## Title", text)
        self.assertIn("End & done", text)

    def test_skipped_classes_and_malformed_html(self) -> None:
        text = bc.html_to_text('<div class="mw-editsection">[edit]</div><p>keep<b>bold</p><p>next')
        self.assertEqual(text, "keepbold\n\nnext")

    def test_se_document(self) -> None:
        question = {"title": "How do I &lt;b&gt; this?", "body": "<p>" + "Question text. " * 20 + "</p>", "link": "https://x/q/1",
                    "question_id": 1, "content_license": "CC BY-SA 3.0", "owner": {"display_name": "Ann"}}
        answer = {"body": "<p>" + "Answer text. " * 20 + "</p>", "content_license": "CC BY-SA 4.0", "owner": {"display_name": "Bob"}}
        doc = bc.build_se_doc(question, answer, "en", "it")
        assert doc is not None
        self.assertEqual(doc.license, "CC BY-SA 3.0 / CC BY-SA 4.0")
        self.assertEqual(doc.authors, "Ann, Bob")
        self.assertIn("## Answer", doc.text)
        self.assertIsNone(bc.build_se_doc(question, None, "en", "it"))
        self.assertIsNone(bc.build_se_doc({**question, "body": "<p>short</p>"}, {**answer, "body": "<p>x</p>"}, "en", "it"))


class LanguageTests(unittest.TestCase):
    def test_accepts_expected_languages(self) -> None:
        self.assertTrue(bc.language_ok(UK_TEXT, "uk"))
        self.assertTrue(bc.language_ok(EN_TEXT, "en"))
        self.assertTrue(bc.language_ok(DE_TEXT, "de"))

    def test_rejects_mismatch(self) -> None:
        self.assertFalse(bc.language_ok(DE_TEXT, "uk"))
        self.assertFalse(bc.language_ok(EN_TEXT, "uk"))

    def test_rejects_detector_ru(self) -> None:
        with mock.patch.object(bc, "detect_language", return_value="ru"):
            self.assertFalse(bc.language_ok(UK_TEXT, "uk"))

    def test_rejects_russian_only_letters_but_not_one_off(self) -> None:
        self.assertFalse(bc.language_ok("Київ ыыыы ээээ " * 20 + UK_TEXT, "uk"))
        self.assertTrue(bc.language_ok(UK_TEXT + " ы", "uk"))  # одна літера на сотні — випадковість, а не російська

    def test_short_text_not_detected(self) -> None:
        self.assertIsNone(bc.detect_language("коротко"))
        self.assertTrue(bc.language_ok("коротко", "uk"))

    def test_code_is_ignored_in_detection(self) -> None:
        text = EN_TEXT + "\n```\n" + "int привіт = 1;\n" * 50 + "```\n"
        self.assertEqual(bc.detect_language(text), "en")


class FormatTests(unittest.TestCase):
    def test_choose_format_is_deterministic_with_target_shares(self) -> None:
        formats = [bc.choose_format(f"key{i}", "wikipedia") for i in range(10000)]
        self.assertEqual(formats, [bc.choose_format(f"key{i}", "wikipedia") for i in range(10000)])
        share = {f: formats.count(f) / len(formats) for f in set(formats)}
        self.assertAlmostEqual(share["docx"], 0.04, delta=0.01)
        self.assertAlmostEqual(share["pdf"], 0.04, delta=0.01)
        self.assertGreater(share["md"], share["txt"])

    def test_books_are_txt_and_no_font_means_docx(self) -> None:
        formats = {bc.choose_format(f"key{i}", "gutenberg") for i in range(500)}
        self.assertEqual(formats, {"docx", "pdf", "txt"})
        self.assertNotIn("pdf", {bc.choose_format(f"key{i}", "gutenberg", pdf_available=False) for i in range(500)})


class GutenbergTests(unittest.TestCase):
    def test_strip_header_and_footer(self) -> None:
        raw = "Title page\n*** START OF THE PROJECT GUTENBERG EBOOK X ***\n\nReal text.\n\n*** END OF THE PROJECT GUTENBERG EBOOK X ***\nLicense"
        self.assertEqual(bc.strip_gutenberg(raw), "Real text.")

    def test_split_chapters(self) -> None:
        body = "Слово. " * 400
        text = "\n\n".join(f"CHAPTER {n}\n\n{body}" for n in range(1, 5))
        chapters = bc.split_chapters(text)
        self.assertEqual([heading for heading, _ in chapters], ["CHAPTER 1", "CHAPTER 2", "CHAPTER 3", "CHAPTER 4"])

    def test_split_without_headings_falls_back_to_paragraphs(self) -> None:
        text = "\n\n".join(["Абзац речення. " * 40] * 60)
        chapters = bc.split_chapters(text, max_chars=8000)
        self.assertGreater(len(chapters), 3)
        self.assertTrue(all(len(body) <= 8000 for _, body in chapters))

    def test_long_chapter_is_split_and_tiny_is_merged(self) -> None:
        big = "\n\n".join(["Речення тексту. " * 30] * 80)
        text = f"CHAPTER I\n\n{big}\n\nCHAPTER II\n\nкоротко\n\nCHAPTER III\n\n{'Ще текст. ' * 300}\n\nCHAPTER IV\n\n{'Ще. ' * 500}"
        chapters = bc.split_chapters(text, min_chars=1500, max_chars=10000)
        self.assertTrue(all(len(body) <= 10000 for _, body in chapters))
        self.assertFalse(any(heading == "CHAPTER II" for heading, _ in chapters))

    def test_catalog_filters(self) -> None:
        header = "Text#,Type,Issued,Title,Language,Authors,Subjects,LoCC,Bookshelves\n"
        rows = [
            '10,Text,2000-01-01,Old Novel,en,"Austen, Jane, 1775-1817",Fiction,PR,',
            '11,Text,2000-01-01,Modern Novel,en,"Newer, Author, 1900-1990",Fiction,PR,',
            '12,Text,2000-01-01,Russian Novel,ru,"Old, Author, 1800-1850",Fiction,PR,',
            '13,Sound,2000-01-01,Audio,en,"Old, Author, 1800-1850",Fiction,PR,',
            '14,Text,2000-01-01,Two Authors,de,"A, B, 1800-1850; C, D, 1801-1851",Fiction,PR,',
            '15,Text,2000-01-01,Essays,en,"Old, Author, 1800-1850",Philosophy,PR,',
            '16,Text,2000-01-01,Faust,de,"Goethe, Johann Wolfgang von, 1749-1832",Drama; Fiction,PT,',
        ]
        result = bc.parse_gutenberg_catalog(header + "\n".join(rows) + "\n")
        self.assertEqual([r["Text#"] for r in result], ["10", "16"])


class HttpAndWikiTests(unittest.TestCase):
    def test_cache_hit_does_not_touch_network(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            http = bc.Http(Path(folder), wiki_pause=0)
            url = "https://en.wikipedia.org/w/api.php?x=1"
            (Path(folder) / (hashlib.sha1(url.encode()).hexdigest() + ".bin")).write_bytes(b'{"ok": true}')
            with mock.patch("urllib.request.urlopen", side_effect=AssertionError("мережа")):
                self.assertEqual(http.json(url), {"ok": True})
            self.assertEqual(http.requests, 0)

    def test_resolve_follows_normalization_and_redirects(self) -> None:
        data = {"query": {"normalized": [{"from": "kyiv", "to": "Kyiv"}], "redirects": [{"from": "Kyiv", "to": "Kiev"}]}}
        self.assertEqual(bc.MediaWiki.resolve(data, ["kyiv", "Paris"]), {"kyiv": "Kiev", "Paris": "Paris"})

    def test_page_url(self) -> None:
        self.assertEqual(bc.page_url("uk.wikipedia.org", "Київ (місто)"), "https://uk.wikipedia.org/wiki/%D0%9A%D0%B8%D1%97%D0%B2_(%D0%BC%D1%96%D1%81%D1%82%D0%BE)")


class WriteTests(unittest.TestCase):
    def setUp(self) -> None:
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.root = Path(self.folder.name)

    def _doc(self, title: str, text: str = UK_TEXT, lang: str = "uk") -> bc.Doc:
        return bc.Doc("wikipedia", lang, "geography", title, f"https://{lang}.wikipedia.org/wiki/{title}", bc.LICENSE_WIKI,
                      f"# {title}\n\n{text}", group="Kyiv")

    def test_manifest_entry_matches_file(self) -> None:
        for index in range(60):  # перебираємо ключі, поки не отримаємо кожен формат
            entry = bc.write_document(self.root, self._doc(f"Київ {index}"), bc.find_pdf_font())
            assert entry is not None
            data = (self.root / entry["path"]).read_bytes()
            self.assertEqual(entry["sha256"], hashlib.sha256(data).hexdigest())
            self.assertEqual(entry["bytes"], len(data))
            self.assertTrue(entry["path"].startswith("wikipedia/uk/"))
            self.assertEqual({"path", "lang", "source", "url", "license", "topic", "sha256"} - set(entry), set())
            json.dumps(entry, ensure_ascii=False)

    def test_all_formats_are_readable(self) -> None:
        seen = set()
        font = bc.find_pdf_font()
        for index in range(300):
            entry = bc.write_document(self.root, self._doc(f"Київ {index}"), font)
            assert entry is not None
            seen.add(entry["format"])
            path = self.root / entry["path"]
            if entry["format"] == "docx":
                import docx

                paragraphs = [p.text for p in docx.Document(str(path)).paragraphs]
                self.assertIn("Київ", " ".join(paragraphs))
            elif entry["format"] in ("md", "txt"):
                text = path.read_text(encoding="utf-8")
                self.assertIn("Київ", text)
                self.assertEqual(text.startswith("# "), entry["format"] == "md")
            else:
                self.assertTrue(path.read_bytes().startswith(b"%PDF"))
        self.assertEqual(seen, {"md", "txt", "docx", "pdf"} if font else {"md", "txt", "docx"})

    def test_write_is_deterministic(self) -> None:
        font = bc.find_pdf_font()
        first = [bc.write_document(self.root, self._doc(f"Київ {i}"), font) for i in range(40)]
        second = [bc.write_document(self.root, self._doc(f"Київ {i}"), font) for i in range(40)]
        self.assertEqual([e["sha256"] for e in first if e], [e["sha256"] for e in second if e])

    def test_rejects_short_and_wrong_language(self) -> None:
        self.assertIsNone(bc.write_document(self.root, self._doc("Заглушка", "дуже коротко"), None))
        self.assertIsNone(bc.write_document(self.root, self._doc("Німецька", DE_TEXT), None))
        self.assertFalse(list(self.root.rglob("*.*")))

    def test_manifest_merge_replaces_only_its_source(self) -> None:
        path = self.root / "manifest.jsonl"
        a = {"path": "wikipedia/uk/a.md", "source": "wikipedia"}
        b = {"path": "gutenberg/en/b.txt", "source": "gutenberg"}
        bc.merge_manifest(path, "wikipedia", [a])
        bc.merge_manifest(path, "gutenberg", [b])
        self.assertEqual(bc.read_manifest(path), [b, a])  # відсортовано за шляхом
        bc.merge_manifest(path, "wikipedia", [])
        self.assertEqual(bc.read_manifest(path), [b])

    def test_summary(self) -> None:
        entries = [{"lang": "uk", "source": "wikipedia", "format": "md", "topic": "art", "bytes": 1048576},
                   {"lang": "en", "source": "cookbook", "format": "txt", "topic": "cooking", "bytes": 1048576}]
        text = bc.summarize(entries)
        self.assertIn("Усього: 2 документів, 2.0 МБ", text)
        self.assertIn("uk", text)


if __name__ == "__main__":
    unittest.main()
