#!/usr/bin/env python3
"""Інструменти розмітки запитів R-02 на відкритому корпусі.

Протокол (див. README.md поруч):
  1. sample          стратифікована вибірка документів-джерел під запити (за типами exact / paraphrase / crossLingual);
  2. show            показати текст документа, щоб писати запит від документа, а не від видачі пошуку;
  3. check-queries   перевірити запити (формат, paraphrase без спільних слів з документом, мова запиту, без російської);
  4. index, pool     проіндексувати корпус у окремій базі й зібрати top-N з keyword, semantic і hybrid (CLI localmind);
  5. candidates      об'єднати пул, додати документ-джерело й відповідники іншими мовами (автоматично), витягти фрагменти;
  6. check-judgments перевірити, що кожен кандидат має оцінку 0–3;
  7. build           записати queries.jsonl і qrels.jsonl (формат eval/datasets/README.md), нулі в qrels не пишемо;
  8. human-sample    вибірка пар для перевірки командою.

Лише стандартна бібліотека Python 3.10+. Дані (корпус, запити, оцінки) у Git не потрапляють.
"""

from __future__ import annotations

import argparse
import json
import os
import random
import re
import subprocess
import sys
from collections import Counter, defaultdict
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

QUERY_TYPES = ("exact", "paraphrase", "crossLingual")
MODES = ("keyword", "semantic", "hybrid")

# Частка мов у запитах типів exact і paraphrase (мова запиту = мова документа). Російської немає.
LANGUAGE_WEIGHTS = {"uk": 0.38, "en": 0.38, "de": 0.08, "pl": 0.08, "fr": 0.08}
# Мова запиту для crossLingual залежно від мови документа: запит однією мовою, документ іншою.
CROSS_QUERY_LANGUAGES = {"uk": ["en"], "en": ["uk"], "de": ["uk", "en"], "pl": ["uk", "en"], "fr": ["uk", "en"],
                         "es": ["uk", "en"], "it": ["uk", "en"]}
FORBIDDEN_RUSSIAN_LETTERS = set("ыэъёЫЭЪЁ")  # їх немає в українській; у запитах uk це ознака російської

STOP_WORDS: dict[str, set[str]] = {
    "en": set("the a an of and or to in on at by for with from as is are was were be been this that these those it its "
              "which who whom what when where why how not no but if then than so such into over under about between while during "
              "their there they them his her he she we you your our can could would should will may might also".split()),
    "uk": set("і й та а але або що це той ця ці як у в на з із до від по за при про для без між під над не ні чи "
              "є був була були буде також його її їх вони він вона воно ми ви ти я так тільки вже ще дуже де коли "
              "яка який які яке цей ця цього".split()),
    "de": set("der die das ein eine einer eines und oder zu in im auf an bei für mit von aus als ist sind war waren "
              "wird werden nicht kein aber wenn dann dass sich auch es er sie wir ihr ich den dem des durch über "
              "unter nach vor".split()),
    "pl": set("i w na z do od po za przy o dla bez między pod nad nie czy jest są był była były będzie także jego jej "
              "ich oni on ona ono my wy ty ja tak tylko już jeszcze bardzo gdzie kiedy który która które ten ta to "
              "oraz lub albo ale że się".split()),
    "fr": set("le la les un une des de du et ou à au aux en dans sur par pour avec sans sous entre est sont était "
              "étaient être ne pas qui que quoi dont où ce cet cette ces il elle ils elles nous vous je tu on se "
              "son sa ses leur leurs mais si comme aussi".split()),
    "es": set("el la los las un una unos unas de del y o a al en por para con sin sobre entre es son era eran ser "
              "no que quien cual donde cuando como este esta estos estas ese esa su sus se lo le les más muy también "
              "pero si".split()),
    "it": set("il lo la i gli le un una di del della dei delle e o a al alla in su per con senza tra fra è sono "
              "era erano essere non che chi quale dove quando come questo questa questi queste suo sua suoi sue si "
              "più molto anche ma se".split()),
}
ALL_STOP_WORDS = set().union(*STOP_WORDS.values())


# --------------------------------------------------------------------------------------------------- спільне

def read_jsonl(path: Path) -> list[dict]:
    with path.open(encoding="utf-8") as handle:
        return [json.loads(line) for line in handle if line.strip()]


def write_jsonl(path: Path, rows: list[dict]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="\n") as handle:
        for row in rows:
            handle.write(json.dumps(row, ensure_ascii=False) + "\n")


def read_text(corpus_root: Path, rel_path: str) -> str:
    return (corpus_root / rel_path).read_text(encoding="utf-8", errors="replace")


def significant_tokens(text: str) -> set[str]:
    """Значущі слова: нижній регістр, довжина ≥ 4, не стоп-слова будь-якої мови."""
    words = re.findall(r"[^\W\d_]+", text.lower(), flags=re.UNICODE)
    return {word for word in words if len(word) >= 4 and word not in ALL_STOP_WORDS}


def stem(word: str) -> str:
    """Груба «лема» без словників: перші 5 літер для довгих слів (відкидає закінчення й більшість суфіксів)."""
    return word[:5] if len(word) >= 7 else word


def shared_words(query: str, document: str) -> list[str]:
    """Слова запиту, що збігаються зі словами документа за формою або за грубою лемою."""
    doc_tokens = significant_tokens(document)
    doc_stems = {stem(token) for token in doc_tokens}
    return sorted(word for word in significant_tokens(query) if word in doc_tokens or stem(word) in doc_stems)


def exact_coverage(query: str, document: str) -> tuple[int, int]:
    """(слів запиту, що є в документі, значущих слів запиту)."""
    words = significant_tokens(query)
    doc_tokens = significant_tokens(document)
    doc_stems = {stem(token) for token in doc_tokens}
    return sum(1 for word in words if word in doc_tokens or stem(word) in doc_stems), len(words)


def has_russian_letters(text: str) -> bool:
    return any(char in FORBIDDEN_RUSSIAN_LETTERS for char in text)


def query_id(index: int) -> str:
    return f"q{index:03d}"


# ---------------------------------------------------------------------------------------------------- sample

def eligible(entry: dict, min_chars: int, max_chars: int) -> bool:
    return entry.get("format") in ("md", "txt") and min_chars <= int(entry.get("chars", 0)) <= max_chars


def allocate(counts: dict[str, float], total: int) -> dict[str, int]:
    """Розподіл total за вагами методом найбільших залишків."""
    raw = {key: weight * total for key, weight in counts.items()}
    result = {key: int(value) for key, value in raw.items()}
    for key in sorted(raw, key=lambda k: raw[k] - result[k], reverse=True)[: total - sum(result.values())]:
        result[key] += 1
    return result


def stratified_pick(pool: list[dict], count: int, rng: random.Random, used_groups: set[str]) -> list[dict]:
    """Двоярусний круговий вибір: по черзі джерела, усередині джерела — теми; групи (статті) не повторюються."""
    by_source: dict[str, dict[str, list[dict]]] = defaultdict(lambda: defaultdict(list))
    for entry in pool:
        by_source[entry["source"]][entry["topic"]].append(entry)
    sources = sorted(by_source)
    rng.shuffle(sources)
    topics = {source: sorted(by_source[source]) for source in sources}
    for source in sources:
        rng.shuffle(topics[source])
        for topic in topics[source]:
            rng.shuffle(by_source[source][topic])
    cursor = dict.fromkeys(sources, 0)
    picked: list[dict] = []
    while len(picked) < count and any(by_source[s][t] for s in sources for t in topics[s]):
        for source in sources:
            if len(picked) >= count:
                break
            for _ in topics[source]:
                topic = topics[source][cursor[source] % len(topics[source])]
                cursor[source] += 1
                entries = by_source[source][topic]
                chosen = None
                while entries:
                    entry = entries.pop()
                    group = entry.get("group") or entry["path"]
                    if group not in used_groups:
                        used_groups.add(group)
                        chosen = entry
                        break
                if chosen:
                    picked.append(chosen)
                    break
    return picked


def command_sample(args: argparse.Namespace) -> int:
    manifest = read_jsonl(Path(args.manifest))
    rng = random.Random(args.seed)
    by_group: dict[str, list[dict]] = defaultdict(list)
    for entry in manifest:
        if entry.get("group"):
            by_group[entry["group"]].append(entry)

    used_groups: set[str] = set()
    rows: list[dict] = []
    languages = allocate(LANGUAGE_WEIGHTS, args.per_type)

    for query_type in ("exact", "paraphrase"):
        picked: list[dict] = []
        for lang, count in languages.items():
            pool = [e for e in manifest if e["lang"] == lang and eligible(e, args.min_chars, args.max_chars)]
            picked += stratified_pick(pool, count, rng, used_groups)
        # Якщо в якійсь мові не вистачило документів, добираємо з інших мов (спершу uk і en).
        for lang in sorted(languages, key=lambda l: -languages[l]):
            if len(picked) >= args.per_type:
                break
            pool = [e for e in manifest if e["lang"] == lang and eligible(e, args.min_chars, args.max_chars)]
            picked += stratified_pick(pool, args.per_type - len(picked), rng, used_groups)
        rows += [sample_row(query_type, entry, entry["lang"], by_group) for entry in picked]

    # crossLingual: документ із відповідниками іншими мовами (та сама стаття); запит іншою мовою, ніж документ.
    multilingual = [
        e for e in manifest
        if eligible(e, args.min_chars, args.max_chars)
        and e.get("group") and len({s["lang"] for s in by_group[e["group"]]}) > 1
        and e["lang"] in CROSS_QUERY_LANGUAGES
    ]
    doc_languages = allocate({"uk": 0.3, "en": 0.3, "de": 0.1, "pl": 0.1, "fr": 0.1, "es": 0.05, "it": 0.05}, args.per_type)
    cross: list[dict] = []
    for lang, count in doc_languages.items():
        cross += stratified_pick([e for e in multilingual if e["lang"] == lang], count, rng, used_groups)
    for lang in sorted(doc_languages, key=lambda l: -doc_languages[l]):
        if len(cross) >= args.per_type:
            break
        cross += stratified_pick([e for e in multilingual if e["lang"] == lang], args.per_type - len(cross), rng, used_groups)
    for entry in cross:
        rows.append(sample_row("crossLingual", entry, rng.choice(CROSS_QUERY_LANGUAGES[entry["lang"]]), by_group))

    rng.shuffle(rows)
    for index, row in enumerate(rows, start=1):
        row["id"] = query_id(index)
    write_jsonl(Path(args.out), rows)
    counts = Counter((row["type"], row["doc_lang"]) for row in rows)
    print(f"Документів-джерел: {len(rows)}; за типом і мовою документа: {dict(sorted(counts.items()))}")
    print(f"Записано: {args.out}")
    return 0


def sample_row(query_type: str, entry: dict, query_lang: str, by_group: dict[str, list[dict]]) -> dict:
    siblings = [s["path"] for s in by_group.get(entry.get("group", ""), []) if s["path"] != entry["path"]]
    return {
        "id": "", "type": query_type, "path": entry["path"], "title": entry.get("title", ""),
        "doc_lang": entry["lang"], "query_lang": query_lang, "source": entry["source"], "topic": entry["topic"],
        "group": entry.get("group", ""), "siblings": siblings,
    }


# ------------------------------------------------------------------------------------------------------ show

def command_show(args: argparse.Namespace) -> int:
    sample = {row["id"]: row for row in read_jsonl(Path(args.sample))}
    row = sample[args.id]
    text = read_text(Path(args.corpus_root), row["path"])
    print(f"# {args.id} · тип {row['type']} · запит мовою {row['query_lang']} · документ {row['doc_lang']}, "
          f"{row['source']}, тема {row['topic']}")
    print(f"# {row['path']}  «{row['title']}»")
    print(text[: args.max_chars])
    if len(text) > args.max_chars:
        print(f"\n[… ще {len(text) - args.max_chars} символів]")
    return 0


# ---------------------------------------------------------------------------------------------- check-queries

def check_query(query: dict, meta: dict, document: str) -> list[str]:
    """Повертає список порушень протоколу для запиту (порожній — усе гаразд)."""
    problems: list[str] = []
    text = query["text"].strip()
    if not text:
        return ["порожній текст запиту"]
    if query["type"] not in QUERY_TYPES:
        problems.append(f"невідомий type «{query['type']}»")
    if meta["query_lang"] == "uk" and has_russian_letters(text):
        problems.append("у запиті літери, яких немає в українській (ы, э, ъ, ё): схоже на російську")
    if meta["query_lang"] == "ru":
        problems.append("російські запити не допускаються")
    if query["type"] == "crossLingual" and meta["query_lang"] == meta["doc_lang"]:
        problems.append("crossLingual: мова запиту збігається з мовою документа")
    if query["type"] != "crossLingual" and meta["query_lang"] != meta["doc_lang"]:
        problems.append("мова запиту не збігається з мовою документа, а тип не crossLingual")
    if query["type"] == "paraphrase":
        shared = shared_words(text, document)
        if shared:
            problems.append(f"paraphrase: спільні значущі слова з документом: {', '.join(shared)}")
    if query["type"] == "exact":
        found, total = exact_coverage(text, document)
        if total == 0:
            problems.append("exact: у запиті немає значущих слів")
        elif found < total:
            problems.append(f"exact: у документі є лише {found} зі {total} значущих слів запиту")
    return problems


def command_check_queries(args: argparse.Namespace) -> int:
    queries = read_jsonl(Path(args.queries))
    meta = {row["id"]: row for row in read_jsonl(Path(args.meta))}
    corpus_root = Path(args.corpus_root)
    failed = 0
    seen: set[str] = set()
    for query in queries:
        problems: list[str] = []
        if query["id"] in seen:
            problems.append("id повторюється")
        seen.add(query["id"])
        if query["id"] not in meta:
            problems.append("немає запису в meta (документа-джерела)")
        else:
            problems += check_query(query, meta[query["id"]], read_text(corpus_root, meta[query["id"]]["path"]))
            if meta[query["id"]]["type"] != query["type"]:
                problems.append(f"type не збігається з вибіркою ({meta[query['id']]['type']})")
        if problems:
            failed += 1
            print(f"{query['id']} [{query.get('type')}] «{query['text']}»")
            for problem in problems:
                print(f"    ✗ {problem}")
    missing = sorted(set(meta) - seen)
    if missing:
        print(f"Немає запитів для: {', '.join(missing)}")
    counts = Counter((q["type"], meta[q["id"]]["query_lang"]) for q in queries if q["id"] in meta)
    print(f"Запитів: {len(queries)}, з порушеннями: {failed}; за типом і мовою запиту: {dict(sorted(counts.items()))}")
    return 1 if failed or missing else 0


# -------------------------------------------------------------------------------------------- index and pool

def cli_command(args: argparse.Namespace) -> list[str]:
    return args.cli.split()


def cli_environment(args: argparse.Namespace) -> dict[str, str]:
    env = dict(os.environ)
    env["LocalMind__Storage__DatabasePath"] = str(Path(args.db).resolve())
    return env


def command_index(args: argparse.Namespace) -> int:
    process = subprocess.run(cli_command(args) + ["index", str(Path(args.corpus_root).resolve())],
                             env=cli_environment(args), text=True, encoding="utf-8")
    return process.returncode


RESULT_LINE = re.compile(r"^\s*(\d+)\.\s+\[(-?[\d.]+)\]\s+(.+?)\s*$")


def parse_search_output(output: str, corpus_root: Path) -> list[dict]:
    """Розбирає вивід `localmind search`: рядок «N. [оцінка] шлях», далі рядок із уривком."""
    results: list[dict] = []
    lines = output.splitlines()
    root = corpus_root.resolve()
    for index, line in enumerate(lines):
        match = RESULT_LINE.match(line)
        if not match:
            continue
        path = Path(match.group(3))
        try:
            relative = path.resolve().relative_to(root).as_posix()
        except ValueError:
            relative = path.as_posix()
        snippet = lines[index + 1].strip() if index + 1 < len(lines) else ""
        results.append({"rank": int(match.group(1)), "score": float(match.group(2)), "path": relative, "snippet": snippet})
    return results


def run_search(args: argparse.Namespace, query: dict, mode: str) -> list[dict]:
    process = subprocess.run(
        cli_command(args) + ["search", query["text"], "--mode", mode, "--k", str(args.top)],
        env=cli_environment(args), capture_output=True, text=True, encoding="utf-8")
    if process.returncode != 0:
        raise RuntimeError(f"search {query['id']} {mode}: код {process.returncode}\n{process.stderr or process.stdout}")
    return parse_search_output(process.stdout, Path(args.corpus_root))


def command_pool(args: argparse.Namespace) -> int:
    queries = read_jsonl(Path(args.queries))
    jobs = [(q, mode) for q in queries for mode in MODES]
    with ThreadPoolExecutor(max_workers=args.workers) as executor:
        outputs = list(executor.map(lambda job: run_search(args, job[0], job[1]), jobs))
    rows = [
        {"query": q["id"], "mode": mode, **result}
        for (q, mode), results in zip(jobs, outputs) for result in results
    ]
    write_jsonl(Path(args.out), rows)
    print(f"Запитів: {len(queries)}, рядків пулу: {len(rows)} → {args.out}")
    return 0


# ------------------------------------------------------------------------------------------------ candidates

def plain_snippet(snippet: str) -> str:
    """Уривок без службових позначок виводу CLI: «…» на краях і «[слово]» навколо збігів."""
    text = snippet.strip().strip("…").strip()
    return re.sub(r"\[([^\[\]]+)\]", r"\1", text)


def fragment_context(document: str, snippet: str, radius: int = 450) -> str:
    """Фрагмент документа навколо уривка з видачі; якщо уривок не знайдено — початок документа."""
    flat = re.sub(r"\s+", " ", document)
    needle = re.sub(r"\s+", " ", plain_snippet(snippet))
    for length in (len(needle), 60, 30):
        position = flat.find(needle[:length]) if length and needle else -1
        if position >= 0:
            start = max(0, position - radius // 3)
            return flat[start: start + radius * 2].strip()
    return flat[: radius * 2].strip()


def build_candidates(queries: list[dict], meta: dict[str, dict], pool: list[dict], titles: dict[str, str],
                     corpus_root: Path, top: int) -> list[dict]:
    """Кандидати на оцінку: об'єднання top-N режимів, документ-джерело; відповідники іншими мовами — автоматично 3."""
    by_query: dict[str, dict[str, dict]] = defaultdict(dict)
    for row in pool:
        if row["rank"] > top:
            continue
        candidate = by_query[row["query"]].setdefault(
            row["path"], {"modes": {}, "snippet": row["snippet"], "best_rank": row["rank"]})
        candidate["modes"][row["mode"]] = row["rank"]
        if row["rank"] < candidate["best_rank"]:
            candidate["best_rank"], candidate["snippet"] = row["rank"], row["snippet"]

    candidates: list[dict] = []
    for query in queries:
        info = meta[query["id"]]
        found = by_query.get(query["id"], {})
        auto_paths = {info["path"], *info.get("siblings", [])} if query["type"] == "crossLingual" else {info["path"]}
        for path in sorted(set(found) | auto_paths):
            document = read_text(corpus_root, path)
            record = {
                "query": query["id"], "document": path, "title": titles.get(path, ""),
                "modes": found.get(path, {}).get("modes", {}),
                "auto": path in auto_paths and (path == info["path"] or query["type"] == "crossLingual"),
                "snippet": plain_snippet(found.get(path, {}).get("snippet", "")),
            }
            record["context"] = fragment_context(document, record["snippet"]) if record["snippet"] else document[:900].strip()
            if record["auto"]:
                record["modes"] = {**record["modes"], "source": 0} if path == info["path"] else {**record["modes"], "translation": 0}
            candidates.append(record)
    return candidates


def command_candidates(args: argparse.Namespace) -> int:
    queries = read_jsonl(Path(args.queries))
    meta = {row["id"]: row for row in read_jsonl(Path(args.meta))}
    pool = read_jsonl(Path(args.pool))
    titles = {row["path"]: row.get("title", "") for row in read_jsonl(Path(args.manifest))}
    candidates = build_candidates(queries, meta, pool, titles, Path(args.corpus_root), args.top)
    write_jsonl(Path(args.out), candidates)
    to_judge = sum(1 for c in candidates if not c["auto"])
    print(f"Кандидатів: {len(candidates)}, з них автоматично (джерело й переклади): {len(candidates) - to_judge}, "
          f"на оцінку: {to_judge} → {args.out}")
    return 0


# ----------------------------------------------------------------------------------------- judgments / build

VALID_GRADES = (0, 1, 2, 3)


def check_judgments(candidates: list[dict], judgments: list[dict]) -> list[str]:
    problems: list[str] = []
    expected = {(c["query"], c["document"]) for c in candidates if not c["auto"]}
    seen: Counter = Counter()
    for judgment in judgments:
        key = (judgment.get("query"), judgment.get("document"))
        seen[key] += 1
        if judgment.get("grade") not in VALID_GRADES:
            problems.append(f"{key}: оцінка має бути 0–3, а не {judgment.get('grade')!r}")
        if not judgment.get("by") or not judgment.get("how"):
            problems.append(f"{key}: немає полів by / how (хто й за якою інструкцією оцінив)")
        if key not in expected:
            problems.append(f"{key}: такої пари немає серед кандидатів на оцінку")
    problems += [f"{key}: оцінено {count} разів" for key, count in seen.items() if count > 1]
    problems += [f"{key}: немає оцінки" for key in sorted(expected - set(seen))]
    return problems


def command_check_judgments(args: argparse.Namespace) -> int:
    problems = check_judgments(read_jsonl(Path(args.candidates)), read_jsonl(Path(args.judgments)))
    for problem in problems[:50]:
        print("✗", problem)
    print(f"Порушень: {len(problems)}")
    return 1 if problems else 0


def build_qrels(queries: list[dict], candidates: list[dict], judgments: list[dict]) -> tuple[list[dict], list[str]]:
    """qrels за оцінками: нулі не пишемо; джерело й переклади (auto) — 3. Повертає (qrels, запити без релевантних)."""
    grades: dict[tuple[str, str], int] = {}
    for candidate in candidates:
        if candidate["auto"]:
            grades[(candidate["query"], candidate["document"])] = 3
    for judgment in judgments:
        key = (judgment["query"], judgment["document"])
        grades[key] = max(grades.get(key, 0), int(judgment["grade"]))
    qrels = [
        {"query": query, "document": document, "relevance": grade}
        for (query, document), grade in sorted(grades.items()) if grade > 0
    ]
    relevant = {row["query"] for row in qrels}
    return qrels, [q["id"] for q in queries if q["id"] not in relevant]


def command_build(args: argparse.Namespace) -> int:
    queries = read_jsonl(Path(args.queries))
    candidates = read_jsonl(Path(args.candidates))
    judgments = read_jsonl(Path(args.judgments))
    problems = check_judgments(candidates, judgments)
    if problems:
        print(f"Оцінки неповні або некоректні ({len(problems)}), спершу check-judgments.")
        return 1
    qrels, without = build_qrels(queries, candidates, judgments)
    if without:
        print(f"Запити без жодного релевантного документа: {', '.join(without)}")
        return 1
    out = Path(args.out_dir)
    write_jsonl(out / "queries.jsonl", [{"id": q["id"], "text": q["text"], "type": q["type"]} for q in queries])
    write_jsonl(out / "qrels.jsonl", qrels)
    grades = Counter(row["relevance"] for row in qrels)
    print(f"queries.jsonl: {len(queries)}; qrels.jsonl: {len(qrels)} (за оцінками {dict(sorted(grades.items()))}) → {out}")
    return 0


# ------------------------------------------------------------------------------------------- human-sample

def human_sample(candidates: list[dict], judgments: list[dict], queries: list[dict], count: int, seed: int) -> list[dict]:
    """Випадкові пари для перевірки людьми; щоб серед них були й релевантні, вибірка рівномірна за оцінкою 0/1/2/3."""
    rng = random.Random(seed)
    candidate_of = {(c["query"], c["document"]): c for c in candidates}
    query_of = {q["id"]: q for q in queries}
    by_grade: dict[int, list[dict]] = defaultdict(list)
    for judgment in judgments:
        by_grade[int(judgment["grade"])].append(judgment)
    for items in by_grade.values():
        rng.shuffle(items)
    share = allocate({str(g): 1 / len(VALID_GRADES) for g in VALID_GRADES}, count)
    chosen: list[dict] = []
    for grade in VALID_GRADES:
        chosen += by_grade[grade][: share[str(grade)]]
    leftovers = [j for g in VALID_GRADES for j in by_grade[g][share[str(g)]:]]
    rng.shuffle(leftovers)
    chosen += leftovers[: count - len(chosen)]
    rng.shuffle(chosen)
    return [
        {"query": query_of[j["query"]], "candidate": candidate_of[(j["query"], j["document"])], "judgment": j}
        for j in chosen
    ]


def cell(text: str, limit: int) -> str:
    flat = re.sub(r"\s+", " ", text).strip().replace("|", "\\|")
    return flat if len(flat) <= limit else flat[:limit].rstrip() + "…"


def render_human_sample(rows: list[dict]) -> str:
    lines = [
        "# Вибірка для перевірки командою (R-02)",
        "",
        "Пари «запит → документ (фрагмент) → оцінка», які поставили агенти за інструкцією з `README.md`. "
        "Для кожної пари відкрийте документ, перегляньте фрагмент і заголовок, оцініть самостійно за тією ж шкалою "
        "(0 — не відповідає, 1 — частково, 2 — добре, 3 — ідеально) і впишіть у колонку «згоден/ні»: «так», або «ні: ваша оцінка». "
        "Вибірка рівномірна за оцінками, тому нулів не більшість, як у всьому пулі.",
        "",
        "| № | Запит (тип, мова) | Документ | Фрагмент | Оцінка агента | згоден/ні |",
        "|---:|---|---|---|---:|---|",
    ]
    for number, row in enumerate(rows, start=1):
        query, candidate, judgment = row["query"], row["candidate"], row["judgment"]
        lines.append(
            f"| {number} | {cell(query['text'], 160)} ({query['type']}) | {cell(candidate['title'], 60)}<br>`{candidate['document']}` "
            f"| {cell(candidate['context'], 420)} | {judgment['grade']} |  |")
    return "\n".join(lines) + "\n"


def command_human_sample(args: argparse.Namespace) -> int:
    rows = human_sample(read_jsonl(Path(args.candidates)), read_jsonl(Path(args.judgments)),
                        read_jsonl(Path(args.queries)), args.n, args.seed)
    Path(args.out).write_text(render_human_sample(rows), encoding="utf-8", newline="\n")
    print(f"Пар для перевірки: {len(rows)} → {args.out}")
    return 0


# -------------------------------------------------------------------------------------------------- main

def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)

    def add(name: str, handler, **kwargs) -> argparse.ArgumentParser:
        p = sub.add_parser(name, **kwargs)
        p.set_defaults(handler=handler)
        return p

    p = add("sample", command_sample, help="стратифікована вибірка документів-джерел")
    p.add_argument("--manifest", required=True)
    p.add_argument("--out", default="labeling/sample.jsonl")
    p.add_argument("--per-type", type=int, default=30)
    p.add_argument("--seed", type=int, default=1)
    p.add_argument("--min-chars", type=int, default=1500)
    p.add_argument("--max-chars", type=int, default=40000)

    p = add("show", command_show, help="показати документ-джерело запиту")
    p.add_argument("--sample", default="labeling/sample.jsonl")
    p.add_argument("--corpus-root", required=True)
    p.add_argument("--max-chars", type=int, default=4000)
    p.add_argument("id")

    p = add("check-queries", command_check_queries, help="перевірити запити за протоколом")
    p.add_argument("--queries", required=True)
    p.add_argument("--meta", default="labeling/sample.jsonl")
    p.add_argument("--corpus-root", required=True)

    for name, handler in (("index", command_index), ("pool", command_pool)):
        p = add(name, handler, help="проіндексувати корпус" if name == "index" else "top-N з трьох режимів")
        p.add_argument("--corpus-root", required=True)
        p.add_argument("--db", default="labeling/eval.db", help="окрема база, не бібліотека користувача")
        p.add_argument("--cli", default="dotnet run --project src/LocalMind.Cli -c Release --")
        if name == "pool":
            p.add_argument("--queries", required=True)
            p.add_argument("--out", default="labeling/pool.jsonl")
            p.add_argument("--top", type=int, default=10)
            p.add_argument("--workers", type=int, default=4)

    p = add("candidates", command_candidates, help="кандидати на оцінку з пулу")
    p.add_argument("--queries", required=True)
    p.add_argument("--meta", default="labeling/sample.jsonl")
    p.add_argument("--pool", default="labeling/pool.jsonl")
    p.add_argument("--manifest", required=True)
    p.add_argument("--corpus-root", required=True)
    p.add_argument("--top", type=int, default=10)
    p.add_argument("--out", default="labeling/candidates.jsonl")

    p = add("check-judgments", command_check_judgments, help="перевірити повноту оцінок")
    p.add_argument("--candidates", default="labeling/candidates.jsonl")
    p.add_argument("--judgments", default="labeling/judgments.jsonl")

    p = add("build", command_build, help="записати queries.jsonl і qrels.jsonl")
    p.add_argument("--queries", required=True)
    p.add_argument("--candidates", default="labeling/candidates.jsonl")
    p.add_argument("--judgments", default="labeling/judgments.jsonl")
    p.add_argument("--out-dir", required=True)

    p = add("human-sample", command_human_sample, help="вибірка пар для перевірки командою")
    p.add_argument("--queries", required=True)
    p.add_argument("--candidates", default="labeling/candidates.jsonl")
    p.add_argument("--judgments", default="labeling/judgments.jsonl")
    p.add_argument("--n", type=int, default=30)
    p.add_argument("--seed", type=int, default=1)
    p.add_argument("--out", default="labeling/sample-for-humans.md")
    return parser


def main(argv: list[str] | None = None) -> int:
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):  # консоль Windows за замовчуванням не UTF-8
            stream.reconfigure(encoding="utf-8", errors="replace")
    args = build_parser().parse_args(argv)
    return args.handler(args)


if __name__ == "__main__":
    sys.exit(main())
