#!/usr/bin/env python3
"""Збирання відкритого багатомовного корпусу нотаток для досліджень E1–E4 (R-01).

Корпус складається з відкритих текстів: Вікіпедія, Wikivoyage, Wikibooks Cookbook, Вікіджерела (uk), Project Gutenberg
і питання-відповіді зі Stack Exchange. Мови: uk, en, de, pl, fr, es, it. Російської немає: не береться жодне джерело
ru.*, а кожен документ перевіряється детектором мови.

Самі тексти в Git не кладуться (розмір і ліцензії): скрипт відтворює їх з офіційних API —

* MediaWiki API (*.wikipedia.org, en.wikivoyage.org, en.wikibooks.org, uk.wikisource.org);
* Stack Exchange API без ключа;
* каталог і тексти gutenberg.org.

Запити йдуть послідовно з паузою, відповіді кешуються в `--cache`, тож повторний запуск майже не торкається мережі.
Вікімедія без контакту в User-Agent пускає лише ≈10 запитів на хвилину на всі свої сайти разом, тому повне збирання
вікі-частини триває близько години (пауза `--wiki-pause`); Gutenberg і Stack Exchange мають окремі ліміти.
Завантажене лише читається як текст і пишеться у файли — нічого не виконується.

Результат: `<out>/<джерело>/<мова>/<файл>` і `manifest.jsonl` (на теку вище): шлях, мова, джерело, URL, ліцензія, тема,
sha256 тощо. Запуск (з venv із requirements.txt):

    python eval/datasets/open/build_corpus.py --out eval/datasets/open/corpus
"""

from __future__ import annotations

import argparse
import csv
import datetime
import gzip
import hashlib
import io
import json
import random
import re
import shutil
import sys
import time
import unicodedata
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from html.parser import HTMLParser
from pathlib import Path

USER_AGENT = "LocalMind-corpus/1.0 (student research project)"

LANGS = ("uk", "en", "de", "pl", "fr", "es", "it")
SOURCES = ("wikipedia", "wikivoyage", "cookbook", "gutenberg", "wikisource", "stackexchange")

LICENSE_WIKI = "CC BY-SA 4.0"
LICENSE_PD = "Public domain"

MIN_CHARS = 250  # коротші документи (заглушки) не беремо
MAX_CHARS = {"md": 40000, "txt": 40000, "docx": 20000, "pdf": 12000}

# Теми й вихідні статті англійської Вікіпедії. Категорії з API не беремо: пошук по деревах категорій видає
# випадкове (Physics → Elvis Presley), а ліміт Вікімедії не дозволяє обходити їх запитами. Список — ручний;
# назви без сторінки або без української версії просто пропускаються.
TOPIC_SEEDS: dict[str, list[str]] = {
    "programming": [
        "Python (programming language)", "JavaScript", "Java (programming language)", "C++", "C (programming language)",
        "Rust (programming language)", "Go (programming language)", "TypeScript", "Kotlin (programming language)",
        "Swift (programming language)", "Haskell", "Lisp (programming language)", "Prolog", "SQL", "Assembly language",
        "Compiler", "Interpreter (computing)", "Garbage collection (computer science)", "Recursion (computer science)",
        "Object-oriented programming", "Functional programming", "Concurrency (computer science)",
        "Software design pattern", "Observer pattern", "Singleton pattern", "Unit testing", "Test-driven development",
        "Code refactoring", "Version control", "Git", "Agile software development", "Software architecture",
        "Application programming interface", "Regular expression", "Hash table", "Linked list", "Binary tree",
        "Binary search algorithm", "Quicksort", "Merge sort", "Dijkstra's algorithm", "Dynamic programming",
        "Big O notation", "Algorithm", "Data structure", "Array (data structure)", "Stack (abstract data type)",
        "Queue (abstract data type)", "Heap (data structure)", "Computer programming", "Debugging", "Open-source software",
    ],
    "it": [
        "Computer network", "Internet Protocol", "Transmission Control Protocol", "Domain Name System", "HTTP", "HTTPS",
        "Transport Layer Security", "Firewall (computing)", "Virtual private network", "Router (computing)", "Ethernet",
        "Wi-Fi", "Bluetooth", "Cloud computing", "Virtualization", "Docker (software)", "Kubernetes", "Linux", "Unix",
        "Microsoft Windows", "MacOS", "Android (operating system)", "Operating system", "Kernel (operating system)",
        "File system", "Database", "Relational database", "NoSQL", "PostgreSQL", "MySQL", "SQLite", "Computer security",
        "Encryption", "Public-key cryptography", "Password", "Phishing", "Malware", "Ransomware",
        "Multi-factor authentication", "Blockchain", "Artificial intelligence", "Machine learning",
        "Artificial neural network", "Web browser", "Search engine", "Email", "World Wide Web", "Central processing unit",
        "Random-access memory", "Solid-state drive", "Graphics processing unit", "Computer data storage", "Backup",
    ],
    "science": [
        "Physics", "Quantum mechanics", "General relativity", "Special relativity", "Thermodynamics", "Electromagnetism",
        "Optics", "Nuclear fission", "Photon", "Black hole", "Big Bang", "Solar System", "Milky Way", "Sun", "Moon", "Mars",
        "Jupiter", "Exoplanet", "Chemistry", "Periodic table", "Atom", "Chemical bond", "Oxygen", "Hydrogen", "Carbon",
        "Water", "Photosynthesis", "DNA", "Evolution", "Cell (biology)", "Genetics", "Mathematics", "Calculus",
        "Prime number", "Pi", "Probability", "Statistics", "Linear algebra", "Geometry", "Number theory",
        "Fibonacci sequence", "Pythagorean theorem", "Albert Einstein", "Isaac Newton", "Marie Curie", "Nikola Tesla",
        "Charles Darwin", "Plate tectonics", "Volcano", "Earthquake",
    ],
    "medicine": [
        "Diabetes", "Hypertension", "Asthma", "Influenza", "COVID-19", "Tuberculosis", "Malaria", "HIV/AIDS", "Vaccine",
        "Antibiotic", "Cancer", "Stroke", "Heart", "Myocardial infarction", "Cardiology", "Neurology", "Psychiatry",
        "Major depressive disorder", "Anxiety", "Sleep", "Insomnia", "Allergy", "Pneumonia", "Anemia", "Obesity",
        "Nutrition", "Vitamin", "Vitamin D", "Protein", "Dietary fiber", "Mediterranean diet", "Exercise",
        "Physical therapy", "Dentistry", "Pediatrics", "Surgery", "Anesthesia", "Blood pressure", "Cholesterol", "Liver",
        "Kidney", "Lung", "Human brain", "Immune system", "Public health", "Epidemiology", "First aid", "Mental health",
        "Pregnancy", "Osteoporosis", "Migraine", "Arthritis",
    ],
    "history": [
        "History of Ukraine", "Kievan Rus'", "Cossack Hetmanate", "Zaporozhian Cossacks", "Ukrainian People's Republic",
        "Holodomor", "Chernobyl disaster", "Orange Revolution", "Revolution of Dignity", "Roman Empire", "Julius Caesar",
        "Augustus", "Fall of the Western Roman Empire", "Byzantine Empire", "Ancient Greece", "Alexander the Great",
        "Middle Ages", "Crusades", "Black Death", "Renaissance", "Reformation", "Hundred Years' War", "Thirty Years' War",
        "French Revolution", "Napoleon", "Industrial Revolution", "American Revolution", "American Civil War",
        "World War I", "World War II", "Holocaust", "Cold War", "Berlin Wall", "Dissolution of the Soviet Union",
        "Cuban Missile Crisis", "Vikings", "Holy Roman Empire", "Ottoman Empire", "Austria-Hungary",
        "Polish–Lithuanian Commonwealth", "Magna Carta", "Printing press", "Johannes Gutenberg", "Silk Road",
        "Mongol Empire", "Genghis Khan", "Ancient Egypt", "Egyptian pyramids", "Colosseum", "Spanish Armada",
    ],
    "geography": [
        "Ukraine", "Poland", "Germany", "France", "Spain", "Italy", "Kyiv", "Lviv", "Warsaw", "Berlin", "Paris", "Madrid",
        "Rome", "Vienna", "Prague", "Budapest", "Danube", "Dnieper", "Vistula", "Rhine", "Seine", "Carpathian Mountains",
        "Alps", "Pyrenees", "Apennine Mountains", "Black Sea", "Baltic Sea", "Mediterranean Sea", "North Sea", "Iceland",
        "Sicily", "Sardinia", "Crete", "Mount Everest", "Sahara", "Amazon River", "Nile", "Pacific Ocean",
        "Atlantic Ocean", "Antarctica", "Arctic", "Greenland", "Australia", "Canada", "Brazil", "Japan", "India", "China",
        "Egypt", "Switzerland", "Netherlands", "Portugal", "Norway", "Sweden", "Finland", "Greece", "Turkey",
    ],
    "travel": [
        "Tourism", "Ecotourism", "Backpacking (travel)", "Hostel", "Hotel", "Airbnb", "Cruise ship", "Passport",
        "Travel visa", "Schengen Area", "Travel insurance", "Package holiday", "Camping", "Hiking", "Pilgrimage",
        "Camino de Santiago", "UNESCO", "World Heritage Site", "Eiffel Tower", "Sagrada Família", "Louvre",
        "Acropolis of Athens", "Machu Picchu", "Great Wall of China", "Taj Mahal", "Venice", "Florence", "Barcelona",
        "Amsterdam", "Lisbon", "Istanbul", "Santorini", "Dubrovnik", "Neuschwanstein Castle", "Mont-Saint-Michel",
        "Palace of Versailles", "Alhambra", "Pompeii", "Stonehenge", "Cappadocia", "Bruges", "Kraków",
        "Wieliczka Salt Mine", "St. Peter's Basilica", "Tower of London", "Big Ben", "Budget airline", "Airport",
        "High-speed rail", "Youth hostel",
    ],
    "cooking": [
        "Cooking", "Baking", "Roasting", "Grilling", "Frying", "Sous-vide", "Fermentation in food processing", "Pickling",
        "Smoking (cooking)", "Marination", "Maillard reaction", "Caramelization", "Yeast", "Sourdough", "Bread", "Pasta",
        "Pizza", "Risotto", "Paella", "Borscht", "Varenyky", "Pierogi", "Sushi", "Ramen", "Curry", "Hummus", "Falafel",
        "Ratatouille", "Goulash", "Sauerkraut", "Kimchi", "Cheese", "Yogurt", "Butter", "Olive oil", "Vinegar",
        "Soy sauce", "Salt", "Black pepper", "Saffron", "Garlic", "Onion", "Tomato", "Potato", "Rice", "Flour",
        "Chocolate", "Coffee", "Tea", "Honey", "Ukrainian cuisine", "Italian cuisine", "French cuisine",
        "Spanish cuisine", "Polish cuisine", "German cuisine",
    ],
    "sports": [
        "Association football", "FIFA World Cup", "UEFA Champions League", "Olympic Games", "Summer Olympic Games",
        "Winter Olympic Games", "Paralympic Games", "Tennis", "Wimbledon Championships", "Basketball", "Volleyball",
        "Handball", "Rugby union", "Cricket", "Baseball", "Ice hockey", "Boxing", "Judo", "Fencing", "Swimming (sport)",
        "Athletics (sport)", "Marathon", "Gymnastics", "Weightlifting", "Cycling", "Tour de France", "Formula One", "Golf",
        "Skiing", "Biathlon", "Figure skating", "Chess", "FIDE", "Table tennis", "Badminton", "Rowing", "Triathlon",
        "Sergey Bubka", "Andriy Shevchenko", "Lionel Messi", "Cristiano Ronaldo", "Usain Bolt", "Michael Phelps",
        "Vitali Klitschko", "Oleksandr Usyk", "FC Dynamo Kyiv", "FC Shakhtar Donetsk", "Real Madrid CF", "FC Barcelona",
        "Mountaineering", "Surfing",
    ],
    "finance": [
        "Finance", "Money", "Bank", "Central bank", "Interest rate", "Inflation", "Deflation", "Recession",
        "Gross domestic product", "Stock market", "Stock", "Bond (finance)", "Mutual fund", "Exchange-traded fund",
        "Index fund", "Dividend", "Mortgage loan", "Credit card", "Credit score", "Loan", "Insurance", "Pension",
        "Compound interest", "Time value of money", "Diversification (finance)", "Hedge fund", "Venture capital",
        "Cryptocurrency", "Bitcoin", "Ethereum", "Foreign exchange market", "Exchange rate", "Euro", "United States dollar",
        "Ukrainian hryvnia", "Accounting", "Balance sheet", "Income statement", "Cash flow statement", "Audit", "Taxation",
        "Value-added tax", "Income tax", "Tax haven", "Budget", "Microfinance", "Crowdfunding",
        "Financial crisis of 2007–2008", "Great Depression", "Bretton Woods system", "International Monetary Fund",
        "World Bank",
    ],
    "law": [
        "Law", "Contract", "Tort", "Property law", "Criminal law", "Civil law (legal system)", "Common law",
        "Constitutional law", "International law", "Human rights", "Universal Declaration of Human Rights",
        "European Convention on Human Rights", "International Criminal Court", "Intellectual property", "Copyright",
        "Patent", "Trademark", "Trade secret", "Creative Commons license", "General Data Protection Regulation",
        "Information privacy law", "Labour law", "Consumer protection", "Competition law", "Bankruptcy", "Arbitration",
        "Statute of limitations", "Habeas corpus", "Rule of law", "Separation of powers", "Judiciary", "Supreme court",
        "Notary public", "Lawyer", "Jury", "Plea bargain", "Legal liability", "Negligence", "Fraud", "Money laundering",
        "Corruption", "Inheritance", "Marriage", "Divorce", "Adoption", "Immigration law", "Refugee",
        "Geneva Conventions", "Treaty", "United Nations", "Nuremberg trials",
    ],
    "art": [
        "Painting", "Impressionism", "Cubism", "Surrealism", "Baroque", "Gothic architecture", "Romanesque architecture",
        "Art Nouveau", "Bauhaus", "Modernism", "Leonardo da Vinci", "Mona Lisa", "Michelangelo", "Pablo Picasso",
        "Vincent van Gogh", "Claude Monet", "Salvador Dalí", "Rembrandt", "Kazimir Malevich", "Taras Shevchenko",
        "Lesya Ukrainka", "Ivan Franko", "Mykola Lysenko", "Ludwig van Beethoven", "Wolfgang Amadeus Mozart",
        "Johann Sebastian Bach", "Frédéric Chopin", "Piano", "Violin", "Guitar", "Bandura", "Orchestra", "Opera", "Jazz",
        "Rock music", "Hip hop", "Film", "Cinema of Ukraine", "Photography", "Sculpture", "Theatre", "Ballet", "Literature",
        "Poetry", "Novel", "William Shakespeare", "Dante Alighieri", "Johann Wolfgang von Goethe",
        "Miguel de Cervantes", "Adam Mickiewicz", "Victor Hugo", "Museum",
    ],
    "nature": [
        "Ecology", "Ecosystem", "Biodiversity", "Food chain", "Climate change", "Global warming", "Greenhouse gas",
        "Carbon dioxide", "Ozone layer", "Renewable energy", "Solar power", "Wind power", "Hydroelectricity",
        "Nuclear power", "Deforestation", "Rainforest", "Coral reef", "Wetland", "Tundra", "Desert", "Savanna", "Taiga",
        "National park", "Wolf", "Brown bear", "European bison", "Eurasian lynx", "Red fox", "Eurasian beaver",
        "White stork", "Eagle", "Owl", "Honey bee", "Butterfly", "Oak", "Pine", "Birch", "Mushroom", "Moss", "Lichen",
        "Tsunami", "Tropical cyclone", "Tornado", "Flood", "Drought", "Wildfire", "Avalanche", "Glacier", "Soil",
        "Recycling", "Pollution", "Plastic pollution", "Endangered species",
    ],
}
ARTICLES_PER_TOPIC = 45
OTHER_LANGS = ("de", "pl", "fr", "es", "it")
OTHER_LANG_PROBABILITY = 0.55  # шанс взяти de/pl/fr/es/it версію статті (en і uk — завжди)
INTRO_ONLY_SHARE = 25  # % статей, що йдуть короткою нотаткою (лише вступ)

# Ліміт Вікімедії для клієнта без контакту в User-Agent: ≈10 запитів на хвилину на всі їхні сайти разом (виміряно).
# Тому пакетні запити і пауза ≈6,5 с між будь-якими двома запитами до *.wikipedia/wikivoyage/wikibooks/wikisource.
WIKI_PAUSE = 6.5
WIKITEXT_BATCH = 8
EXTRACT_BATCH = 20

VOYAGE_DESTINATIONS = [
    "Lviv", "Kyiv", "Odesa", "Kharkiv", "Warsaw", "Kraków", "Gdańsk", "Berlin", "Munich", "Hamburg", "Vienna", "Paris",
    "Lyon", "Marseille", "Nice", "Madrid", "Barcelona", "Seville", "Valencia", "Rome", "Milan", "Florence", "Venice",
    "Naples", "Prague", "Budapest", "Amsterdam", "Lisbon", "Porto", "Athens", "Istanbul", "London", "Edinburgh", "Dublin",
    "Copenhagen", "Stockholm", "Oslo", "Helsinki", "Tallinn", "Riga", "Vilnius", "Brussels", "Zürich", "Geneva",
    "Bratislava", "Ljubljana", "Zagreb", "Dubrovnik", "Split", "Sofia", "Bucharest", "Tbilisi", "Yerevan",
    "New York City", "San Francisco", "Los Angeles", "Chicago", "Toronto", "Vancouver", "Mexico City", "Buenos Aires",
    "Rio de Janeiro", "Lima", "Tokyo", "Kyoto", "Seoul", "Beijing", "Shanghai", "Hong Kong", "Bangkok", "Singapore",
    "Hanoi", "Bali", "Delhi", "Mumbai", "Dubai", "Cairo", "Marrakesh", "Cape Town", "Sydney", "Melbourne", "Auckland",
    "Iceland", "Norway", "Switzerland", "Poland", "Ukraine", "Germany", "France", "Spain", "Italy", "Carpathians",
    "Alps", "Tuscany", "Andalusia", "Provence", "Bavaria", "Camino de Santiago", "Transylvania", "Dolomites",
    "Lake District", "Scottish Highlands", "Sicily", "Crete", "Mallorca", "Madeira", "Azores", "Canary Islands",
]
VOYAGE_LANG_PROBABILITY = 0.7
COOKBOOK_RECIPES = 330

# Stack Exchange: (сайт, мова, тема, кількість документів).
SE_SITES = [
    ("stackoverflow", "en", "programming", 260), ("es.stackoverflow", "es", "programming", 100),
    ("superuser", "en", "it", 130), ("serverfault", "en", "it", 100), ("cooking", "en", "cooking", 140),
    ("travel", "en", "travel", 140), ("law", "en", "law", 100), ("money", "en", "finance", 140),
    ("fitness", "en", "sports", 100), ("chess", "en", "sports", 40), ("biology", "en", "science", 80),
    ("physics", "en", "science", 80), ("history", "en", "history", 100), ("outdoors", "en", "nature", 60),
    ("gardening", "en", "nature", 60), ("photo", "en", "art", 60), ("ukrainian", "en", "languages", 80),
    ("german", "en", "languages", 60),
]

# Сайти, де англійські питання рясніють українськими прикладами: мова документа визначається детектором (uk або en).
SE_MIXED_SITES = {"ukrainian"}

# Project Gutenberg: книг на мову (випадкова вибірка з каталогу, художня проза авторів, що померли до 1926 року).
GUTENBERG_BOOKS = {"en": 16, "de": 6, "fr": 6, "es": 5, "it": 5, "pl": 3}
GUTENBERG_MAX_DEATH_YEAR = 1925
GUTENBERG_CHAPTERS_PER_BOOK = 40
GUTENBERG_CATALOG = "https://www.gutenberg.org/cache/epub/feeds/pg_catalog.csv"

WIKISOURCE_PREFIXES = ("Поезія ", "Казки ", "Оповідання ", "Проза ")
WIKISOURCE_MAX_PAGES = 600


@dataclass
class Doc:
    """Один документ корпусу до запису у файл."""

    source: str
    lang: str
    topic: str
    title: str
    url: str
    license: str
    text: str  # markdown: `# Заголовок`, потім текст
    group: str = ""  # ключ «та сама тема різними мовами»: англійська назва статті
    authors: str = ""
    mixed: bool = False  # текст про мову й мовою (ukrainian.stackexchange): lang = uk чи en за детектором


# ---------------------------------------------------------------------------------------------------------------------
# Текст: нормалізація, HTML → текст, мова
# ---------------------------------------------------------------------------------------------------------------------

_ZERO_WIDTH = dict.fromkeys(map(ord, "\u200b\u200c\u200d\u2060\ufeff"), None)


def normalize_text(text: str) -> str:
    """NFC, єдині переноси рядків, без нульової ширини й табуляції, не більше однієї порожньої строки підряд."""
    text = unicodedata.normalize("NFC", text).translate(_ZERO_WIDTH)
    text = text.replace("\r\n", "\n").replace("\r", "\n").replace("\u00a0", " ").replace("\t", " ")
    lines = [line.rstrip() for line in text.split("\n")]
    text = "\n".join(lines)
    text = re.sub(r"\n{3,}", "\n\n", text)
    return text.strip()


def wiki_headings_to_markdown(text: str) -> str:
    """Заголовки з extracts (`== Назва ==`) → markdown (`## Назва`)."""

    def replace(match: re.Match[str]) -> str:
        level = len(match.group(1))
        return "#" * min(level, 6) + " " + match.group(2).strip()

    return re.sub(r"^(={2,6})\s*(.+?)\s*\1\s*$", replace, text, flags=re.MULTILINE)


def truncate_text(text: str, limit: int) -> str:
    """Обрізає по межі абзацу (або речення), щоб не рвати слово."""
    if len(text) <= limit:
        return text
    cut = text[:limit]
    boundary = cut.rfind("\n\n")
    if boundary < limit // 2:
        boundary = max(cut.rfind(". "), cut.rfind("\n"))
    if boundary < limit // 2:
        boundary = limit
    return cut[:boundary].rstrip()


class _HtmlToText(HTMLParser):
    """Мінімальний HTML → markdown-подібний текст (абзаци, списки, заголовки, код). Нічого не виконує."""

    BLOCK = {"p", "div", "section", "article", "ul", "ol", "table", "tr", "dl", "figure"}
    VOID = {"br", "hr", "img", "meta", "link", "input", "wbr", "col"}
    SKIP_TAGS = {"script", "style", "noscript", "sup", "math"}
    SKIP_CLASSES = ("mw-editsection", "noprint", "ws-noexport", "reference", "navbox", "toc", "mw-empty-elt", "metadata")

    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.parts: list[str] = []
        self._stack: list[tuple[str, bool]] = []
        self._skip = 0
        self._pre = 0

    def _emit(self, text: str) -> None:
        if not self._skip:
            self.parts.append(text)

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        if tag in self.VOID:
            if tag in ("br", "hr"):
                self._emit("\n")
            return
        classes = (dict(attrs).get("class") or "")
        skipping = tag in self.SKIP_TAGS or any(marker in classes for marker in self.SKIP_CLASSES)
        self._stack.append((tag, skipping))
        if skipping:
            self._skip += 1
            return
        if tag == "pre":
            self._pre += 1
            self._emit("\n\n```\n")
        elif tag == "code" and not self._pre:
            self._emit("`")
        elif tag in self.BLOCK or tag == "blockquote":
            self._emit("\n\n" + ("> " if tag == "blockquote" else ""))
        elif tag == "li":
            self._emit("\n- ")
        elif len(tag) == 2 and tag[0] == "h" and tag[1].isdigit():
            self._emit("\n\n" + "#" * int(tag[1]) + " ")
        elif tag in ("td", "th"):
            self._emit(" ")

    def handle_endtag(self, tag: str) -> None:
        if tag in self.VOID:
            return
        # закриваємо до найближчого відкритого тега з такою назвою (HTML буває недбалим)
        for index in range(len(self._stack) - 1, -1, -1):
            if self._stack[index][0] == tag:
                while len(self._stack) > index:
                    closed, skipping = self._stack.pop()
                    if skipping:
                        self._skip -= 1
                    elif closed == "pre":
                        self._pre -= 1
                        self._emit("\n```\n\n")
                    elif closed == "code" and not self._pre:
                        self._emit("`")
                    elif closed in self.BLOCK or closed == "blockquote" or (len(closed) == 2 and closed[0] == "h"):
                        self._emit("\n\n")
                return

    def handle_data(self, data: str) -> None:
        if self._skip:
            return
        self._emit(data if self._pre else re.sub(r"\s+", " ", data))


def html_to_text(html: str) -> str:
    parser = _HtmlToText()
    parser.feed(html)
    parser.close()
    text = "".join(parser.parts)
    text = re.sub(r"[ ]+\n", "\n", text)
    text = re.sub(r"\n[ ]+", "\n", text) if "```" not in text else text
    return normalize_text(text)


_FENCED_CODE = re.compile(r"```.*?```", re.DOTALL)
_RUSSIAN_ONLY_LETTERS = re.compile("[ыэъёЫЭЪЁ]")
_CYRILLIC = re.compile("[\u0400-\u04ff]")
_detector_ready = False


def detect_language(text: str) -> str | None:
    """Мова основного тексту (без блоків коду) за langdetect; None, якщо тексту замало."""
    global _detector_ready
    from langdetect import DetectorFactory, detect
    from langdetect.lang_detect_exception import LangDetectException

    if not _detector_ready:
        DetectorFactory.seed = 0  # детермінований результат
        _detector_ready = True
    prose = _FENCED_CODE.sub(" ", text)
    prose = re.sub(r"`[^`]*`|https?://\S+", " ", prose)
    if len(prose.strip()) < 80:
        return None
    try:
        return str(detect(prose[:4000]))
    except LangDetectException:
        return None


def language_ok(text: str, expected: str) -> bool:
    """Відсікає російську і документи, мова яких не збігається з очікуваною.

    1. літер ы э ъ ё (їх немає в українській) — менше трьох і не більше 0,5 % кирилиці: цитата чи ім'я російською
       у статті теж відсікається, корпус має бути без російського тексту;
    2. детектор не показує ru ні на якому тексті;
    3. для довгих текстів мова збігається з очікуваною.
    """
    cyrillic = len(_CYRILLIC.findall(text))
    russian_letters = len(_RUSSIAN_ONLY_LETTERS.findall(text))
    if russian_letters >= 3 or (cyrillic and russian_letters / cyrillic > 0.005):
        return False
    detected = detect_language(text)
    if detected is None:
        return True
    if detected == "ru":
        return False
    if len(text) >= 600 and detected != expected:
        return False
    return True


def slugify(title: str, limit: int = 60) -> str:
    text = unicodedata.normalize("NFC", title).lower()
    text = re.sub(r"[^\w]+", "-", text, flags=re.UNICODE).strip("-_")
    text = text[:limit].rstrip("-_")
    return text or "doc"


def stable_bucket(key: str, modulo: int = 100) -> int:
    return int(hashlib.sha1(key.encode("utf-8")).hexdigest()[:8], 16) % modulo


def choose_format(key: str, source: str, pdf_available: bool = True) -> str:
    """md / txt / docx / pdf за хешем ключа: ≈4 % docx, ≈4 % pdf, решта md і txt (книги й Вікіджерела — txt)."""
    bucket = stable_bucket(key)
    if bucket < 4:
        return "docx"
    if bucket < 8:
        return "pdf" if pdf_available else "docx"
    if source in ("gutenberg", "wikisource"):
        return "txt"
    return "txt" if bucket % 3 == 0 else "md"


_NAMESPACE_PREFIXES = {
    "file", "image", "category", "media", "datei", "kategorie", "fichier", "catégorie", "archivo", "categoría",
    "imagen", "immagine", "categoria", "plik", "kategoria", "файл", "категорія", "зображення", "категория",
    "wikipedia", "help", "template", "special", "portal", "wikt", "commons", "wikisource", "s", "w", "c",
}
_REMOVED_TAGS = {"table", "ref", "references", "gallery", "timeline", "imagemap", "math", "syntaxhighlight",
                 "source", "score", "mapframe", "maplink", "noinclude", "pagequality", "templatestyles", "nowiki"}
_STOP_SECTIONS = {
    "see also", "references", "external links", "further reading", "notes", "bibliography", "citations", "sources",
    "footnotes", "gallery", "примітки", "посилання", "література", "джерела", "див. також", "зовнішні посилання",
    "weblinks", "einzelnachweise", "literatur", "siehe auch", "anmerkungen", "quellen", "fußnoten",
    "przypisy", "linki zewnętrzne", "bibliografia", "zobacz też", "uwagi",
    "notes et références", "voir aussi", "liens externes", "bibliographie", "références", "notes et référence",
    "referencias", "enlaces externos", "véase también", "notas", "bibliografía", "fuentes",
    "note", "voci correlate", "altri progetti", "collegamenti esterni", "riferimenti", "fonti",
}
_HEADING_MARK = "\u0001"


def wikitext_to_text(wikitext: str) -> str:
    """Вікірозмітка → читабельний текст із markdown-заголовками (без шаблонів, виносок, таблиць, файлів і категорій).

    Пакетний запит wikitext — єдиний спосіб брати повний текст кількох статей за один виклик API (extracts
    повертає повний текст лише по одній сторінці), а запити в нас дорогі через ліміт Вікімедії.
    """
    import mwparserfromhell

    code = mwparserfromhell.parse(wikitext)
    for tag in list(code.ifilter_tags(recursive=True)):
        if str(tag.tag).strip().lower() in _REMOVED_TAGS and code.contains(tag):
            try:
                code.remove(tag)
            except ValueError:
                pass
    for link in list(code.ifilter_wikilinks(recursive=True)):
        prefix = str(link.title).split(":", 1)[0].strip().lower() if ":" in str(link.title) else ""
        if prefix in _NAMESPACE_PREFIXES and code.contains(link):
            try:
                code.remove(link)
            except ValueError:
                pass
    for tag in list(code.ifilter_tags(recursive=True)):  # списки: розмітка * # : ; у mwparserfromhell — теги
        marker = tag.wiki_markup
        if marker in ("*", "#") and code.contains(tag):
            code.replace(tag, "\n- " + str(tag.contents).strip())
        elif marker in (":", ";") and code.contains(tag):
            code.replace(tag, "\n" + str(tag.contents).strip())
    for heading in list(code.ifilter_headings(recursive=False)):
        title = heading.title.strip_code().strip()
        code.replace(heading, f"\n\n{_HEADING_MARK}{heading.level}{_HEADING_MARK}{title}\n\n")
    text = code.strip_code(normalize=True, collapse=True)
    text = re.sub(r"^[#*:;]+[ ]*", "- ", text, flags=re.MULTILINE)  # списки, які розмітка залишила текстом
    text = re.sub(r"^- [ ]+", "- ", text, flags=re.MULTILINE)
    text = re.sub(rf"{_HEADING_MARK}(\d){_HEADING_MARK}", lambda m: "#" * int(m.group(1)) + " ", text)
    text = re.sub(r"^\s*(?:thumb|left|right|upright)\b.*$", "", text, flags=re.MULTILINE)  # недоїдені підписи до файлів
    text = re.sub(r"\{\|.*?\|\}", "", text, flags=re.DOTALL)  # таблиці, що не розпізнались як теги
    text = re.sub(r"__[A-Z]+__", "", text)  # магічні слова (__NOTOC__)
    text = re.sub(r"^[|!].*$", "", text, flags=re.MULTILINE)  # залишки клітинок і підписів таблиць
    return drop_stop_sections(normalize_text(text))


_pool = None


def _convert_wikitext(contents: list[str]) -> list[str]:
    """wikitext_to_text для пачки сторінок: розбір великих статей повільний, тому паралельно в кількох процесах."""
    global _pool
    if len(contents) < 2:
        return [wikitext_to_text(content) if content else "" for content in contents]
    if _pool is None:
        import concurrent.futures
        import os

        _pool = concurrent.futures.ProcessPoolExecutor(max_workers=min(8, os.cpu_count() or 2))
    return list(_pool.map(_convert_one, contents))


def _convert_one(content: str) -> str:
    return wikitext_to_text(content) if content else ""


def drop_stop_sections(text: str) -> str:
    """Прибирає службові розділи («Примітки», «See also», «Weblinks»…) разом із вмістом."""
    kept: list[str] = []
    skip_level = 0
    for line in text.split("\n"):
        heading = re.match(r"^(#{1,6}) (.*)", line)
        if heading:
            level = len(heading.group(1))
            if skip_level and level <= skip_level:
                skip_level = 0
            if not skip_level and heading.group(2).strip().lower() in _STOP_SECTIONS:
                skip_level = level
        if not skip_level:
            kept.append(line)
    return normalize_text("\n".join(kept))


# ---------------------------------------------------------------------------------------------------------------------
# Мережа: послідовні запити з паузою, кеш, повтори
# ---------------------------------------------------------------------------------------------------------------------

_WIKIMEDIA_SUFFIXES = (".wikipedia.org", ".wikivoyage.org", ".wikibooks.org", ".wikisource.org", ".wikimedia.org")


class Http:
    """GET з паузою між запитами, кешем на диску і повторами при 429/5xx (з урахуванням Retry-After).

    Для сайтів Вікімедії діє спільна пауза `wiki_pause` на всі хости разом: їхній ліміт загальний для IP.
    """

    def __init__(self, cache: Path, pause: float = 0.5, wiki_pause: float = WIKI_PAUSE) -> None:
        self.cache = cache
        self.pause = pause
        self.wiki_pause = wiki_pause
        self._last: dict[str, float] = {}
        self.requests = 0
        cache.mkdir(parents=True, exist_ok=True)

    def get(self, url: str, pause: float | None = None) -> bytes:
        key = self.cache / (hashlib.sha1(url.encode("utf-8")).hexdigest() + ".bin")
        if key.is_file():
            return key.read_bytes()
        host = urllib.parse.urlparse(url).hostname or ""
        family = "wikimedia" if host.endswith(_WIKIMEDIA_SUFFIXES) else host
        spacing = self.wiki_pause if family == "wikimedia" else (self.pause if pause is None else pause)
        delay = 10.0
        for attempt in range(10):
            wait = spacing - (time.monotonic() - self._last.get(family, 0.0))
            if wait > 0:
                time.sleep(wait)
            self._last[family] = time.monotonic()
            self.requests += 1
            request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT, "Accept-Encoding": "gzip"})
            try:
                with urllib.request.urlopen(request, timeout=120) as response:
                    body = response.read()
                    if response.headers.get("Content-Encoding") == "gzip":
                        body = gzip.decompress(body)
                key.write_bytes(body)
                return body
            except urllib.error.HTTPError as error:
                if error.code not in (429, 500, 502, 503, 504) or attempt == 9:
                    raise
                retry_after = error.headers.get("Retry-After")
                # вікно ліміту Вікімедії ≈ 60 с: після 429 чекаємо, поки воно закінчиться
                time.sleep(float(retry_after) if retry_after and retry_after.isdigit() else (65.0 if error.code == 429 else delay))
                delay = min(delay * 2, 120)
            except (urllib.error.URLError, TimeoutError, ConnectionError):
                if attempt == 9:
                    raise
                time.sleep(delay)
                delay = min(delay * 2, 120)
        raise RuntimeError("недосяжно")

    def json(self, url: str, pause: float | None = None) -> dict:
        return json.loads(self.get(url, pause))


class MediaWiki:
    """Мінімум MediaWiki API: міжмовні посилання, пакетні extracts і wikitext, списки категорій."""

    def __init__(self, http: Http, host: str) -> None:
        self.http = http
        self.host = host

    def query(self, **params: str | int) -> dict:
        params = {"action": "query", "format": "json", "formatversion": 2, **params}
        return self.http.json(f"https://{self.host}/w/api.php?" + urllib.parse.urlencode(sorted(params.items())))

    @staticmethod
    def resolve(data: dict, titles: list[str]) -> dict[str, str]:
        """Запитана назва → остаточна (після нормалізації й перенаправлень)."""
        mapping = {title: title for title in titles}
        for step in ("normalized", "redirects"):
            for item in data.get("query", {}).get(step, []):
                for key, value in mapping.items():
                    if value == item["from"]:
                        mapping[key] = item["to"]
        return mapping

    def has_language(self, titles: list[str], lang: str) -> dict[str, str]:
        """Для кожної запитаної статті з посиланням на мову `lang` — її назва там (пачками по 50 за запит)."""
        found: dict[str, str] = {}
        for start in range(0, len(titles), 50):
            batch = titles[start:start + 50]
            token = None
            links: dict[str, str] = {}
            mapping = {title: title for title in batch}
            while True:
                extra = {"llcontinue": token} if token else {}
                data = self.query(prop="langlinks", titles="|".join(batch), lllang=lang, lllimit="max", redirects=1, **extra)
                mapping = {key: self.resolve(data, [key])[key] for key in batch} if not token else mapping
                for page in data["query"]["pages"]:
                    for link in page.get("langlinks", []):
                        links[page["title"]] = link["title"]
                token = data.get("continue", {}).get("llcontinue")
                if not token:
                    break
            for requested, final in mapping.items():
                if final in links:
                    found[requested] = links[final]
        return found

    def langlinks_many(self, titles: list[str], langs: tuple[str, ...]) -> dict[str, dict[str, str]]:
        """{запитана назва: {мова: назва}} для кількох мов (по одному проходу на мову)."""
        result: dict[str, dict[str, str]] = {title: {} for title in titles}
        for lang in langs:
            for requested, local in self.has_language(titles, lang).items():
                result[requested][lang] = local
        return result

    def texts(self, titles: list[str], intro_only: bool = False) -> dict[str, tuple[str, str]]:
        """{запитана назва: (остаточна назва, текст)}: вступи — extracts по 20 назв, повний текст — wikitext по 8."""
        result: dict[str, tuple[str, str]] = {}
        size = EXTRACT_BATCH if intro_only else WIKITEXT_BATCH
        for start in range(0, len(titles), size):
            batch = titles[start:start + size]
            if intro_only:
                data = self.query(prop="extracts", explaintext=1, exintro=1, exlimit="max", titles="|".join(batch), redirects=1)
            else:
                data = self.query(prop="revisions", rvprop="content", rvslots="main", titles="|".join(batch), redirects=1)
            mapping = self.resolve(data, batch)
            pages = [page for page in data["query"]["pages"] if not page.get("missing")]
            if intro_only:
                converted = [page.get("extract") or "" for page in pages]
            else:
                contents = [(page.get("revisions") or [{}])[0].get("slots", {}).get("main", {}).get("content", "") for page in pages]
                converted = _convert_wikitext(contents)
            by_title = {page["title"]: text for page, text in zip(pages, converted, strict=True) if text}
            for requested, final in mapping.items():
                if final in by_title:
                    result[requested] = (final, by_title[final])
        return result

    def category_titles(self, category: str, limit: int = 3000, prefix: str = "") -> list[str]:
        """Назви сторінок категорії (з префіксом, якщо задано)."""
        titles: list[str] = []
        token = None
        while len(titles) < limit:
            extra = {"cmcontinue": token} if token else {}
            data = self.query(list="categorymembers", cmtitle=category, cmlimit=500, **extra)
            titles.extend(m["title"] for m in data["query"]["categorymembers"] if m["title"].startswith(prefix))
            token = data.get("continue", {}).get("cmcontinue")
            if not token:
                break
        return sorted(set(titles))


def page_url(host: str, title: str) -> str:
    return f"https://{host}/wiki/" + urllib.parse.quote(title.replace(" ", "_"), safe="/:()!,'*~-._")


# ---------------------------------------------------------------------------------------------------------------------
# Джерела
# ---------------------------------------------------------------------------------------------------------------------

def _scaled(count: int, scale: float) -> int:
    return max(1, round(count * scale))


def _fetch_versions(http: Http, source: str, project: str, license_: str, topic_of,
                    articles: dict[str, dict[str, str]], intro_of) -> list[Doc]:
    """Тексти всіх мовних версій статей `articles` ({англійська назва: {мова: місцева назва}}) пакетами по хостах.

    `intro_of(en_title)` — брати лише вступ; `topic_of(en_title)` — тема документа.
    """
    docs: list[Doc] = []
    for lang in LANGS:
        wiki = MediaWiki(http, f"{lang}.{project}.org")
        for intro in (True, False):
            wanted = {en: local[lang] for en, local in articles.items() if lang in local and intro_of(en) == intro}
            if not wanted:
                continue
            fetched = wiki.texts(list(wanted.values()), intro_only=intro)
            for en, local in wanted.items():
                if local not in fetched:
                    continue
                final, text = fetched[local]
                body = text
                docs.append(Doc(source, lang, topic_of(en), final, page_url(f"{lang}.{project}.org", final), license_,
                                f"# {final}\n\n{body}", group=en))
    return docs


def collect_wikipedia(http: Http, rng: random.Random, scale: float, log) -> list[Doc]:
    """Статті англійської Вікіпедії за темами + їхні версії іншими мовами (через міжмовні посилання)."""
    english = MediaWiki(http, "en.wikipedia.org")
    seeds = {topic: titles[: ARTICLES_PER_TOPIC * 2] for topic, titles in TOPIC_SEEDS.items()}
    all_titles = list(dict.fromkeys(t for titles in seeds.values() for t in titles))
    with_uk = english.has_language(all_titles, "uk")
    topic_of: dict[str, str] = {}
    chosen: list[str] = []
    for topic, titles in seeds.items():
        picked = [t for t in titles if t in with_uk and t not in topic_of][: _scaled(ARTICLES_PER_TOPIC, scale)]
        topic_of.update({t: topic for t in picked})
        chosen.extend(picked)
        log(f"wikipedia/{topic}: з uk-версією {sum(t in with_uk for t in titles)} з {len(titles)}, беремо {len(picked)}")
    links = english.langlinks_many(chosen, OTHER_LANGS)
    articles: dict[str, dict[str, str]] = {}
    for title in chosen:
        versions = {"en": title, "uk": with_uk[title]}
        for lang, local in links[title].items():
            if stable_bucket(f"lang:{lang}:{title}", 1000) < OTHER_LANG_PROBABILITY * 1000:
                versions[lang] = local
        articles[title] = versions
    docs = _fetch_versions(http, "wikipedia", "wikipedia", LICENSE_WIKI, topic_of.__getitem__, articles,
                           lambda title: stable_bucket("mode:" + title) < INTRO_ONLY_SHARE)
    log(f"wikipedia: {len(docs)} документів із {len(chosen)} статей")
    return docs


def collect_wikivoyage(http: Http, rng: random.Random, scale: float, log) -> list[Doc]:
    english = MediaWiki(http, "en.wikivoyage.org")
    destinations = VOYAGE_DESTINATIONS[: _scaled(len(VOYAGE_DESTINATIONS), scale)]
    links = english.langlinks_many(destinations, tuple(l for l in LANGS if l != "en"))
    articles = {}
    for title in destinations:
        versions = {"en": title}
        for lang, local in links[title].items():
            if lang == "uk" or stable_bucket(f"voy:{lang}:{title}", 1000) < VOYAGE_LANG_PROBABILITY * 1000:
                versions[lang] = local
        articles[title] = versions
    docs = _fetch_versions(http, "wikivoyage", "wikivoyage", LICENSE_WIKI, lambda _: "travel", articles, lambda _: False)
    log(f"wikivoyage: {len(docs)} документів із {len(destinations)} напрямків")
    return docs


def collect_cookbook(http: Http, rng: random.Random, scale: float, log) -> list[Doc]:
    wiki = MediaWiki(http, "en.wikibooks.org")
    recipes = wiki.category_titles("Category:Recipes", prefix="Cookbook:")
    random.Random(f"{rng.random()}-cookbook").shuffle(recipes)
    want = _scaled(COOKBOOK_RECIPES, scale)
    texts = wiki.texts(recipes[: want + want // 4])
    docs = []
    for title in list(texts)[:want]:
        final, text = texts[title]
        name = final.split(":", 1)[-1]
        docs.append(Doc("cookbook", "en", "cooking", name, page_url("en.wikibooks.org", final), LICENSE_WIKI, f"# {name}\n\n{text}"))
    log(f"cookbook: {len(docs)} рецептів")
    return docs


_GUTENBERG_START = re.compile(r"^\*\*\* ?START OF (?:THE|THIS) PROJECT GUTENBERG.*$", re.MULTILINE | re.IGNORECASE)
_GUTENBERG_END = re.compile(r"^\*\*\* ?END OF (?:THE|THIS) PROJECT GUTENBERG.*$", re.MULTILINE | re.IGNORECASE)
_CHAPTER_WORDS = (
    r"CHAPTER|Chapter|BOOK|Book|PART|Part|CANTO|ACT|Act|KAPITEL|Kapitel|BUCH|Buch|AKT|Akt|CHAPITRE|Chapitre|LIVRE|Livre|"
    r"CAP[ÍI]TULO|Cap[íi]tulo|LIBRO|Libro|CAPITOLO|Capitolo|PARTE|Parte|ROZDZIA[ŁL]|Rozdzia[łl]|CZĘŚĆ|Część|TOM|Tom"
)
_CHAPTER_HEADING = re.compile(rf"^\s*(?:(?:{_CHAPTER_WORDS})\b.{{0,70}}|[IVXLC]{{1,7}}\.?)\s*$", re.MULTILINE)


def strip_gutenberg(raw: str) -> str:
    """Лише текст книги: без шапки й підвалу Project Gutenberg з ліцензією проєкту."""
    start = _GUTENBERG_START.search(raw)
    if start:
        raw = raw[start.end():]
    end = _GUTENBERG_END.search(raw)
    if end:
        raw = raw[: end.start()]
    return normalize_text(raw)


def split_chapters(text: str, min_chars: int = 1500, max_chars: int = 25000) -> list[tuple[str, str]]:
    """Книга → [(заголовок розділу, текст)]. Без розпізнаних заголовків — шматки по абзацах до `max_chars`."""
    matches = list(_CHAPTER_HEADING.finditer(text))
    sections: list[tuple[str, str]] = []
    if len(matches) >= 3:
        for index, match in enumerate(matches):
            end = matches[index + 1].start() if index + 1 < len(matches) else len(text)
            body = text[match.end():end].strip()
            heading = match.group(0).strip()
            if sections and len(body) < min_chars:
                previous_heading, previous_body = sections[-1]
                sections[-1] = (previous_heading, f"{previous_body}\n\n{heading}\n\n{body}".strip())
            elif body:
                sections.append((heading, body))
    else:
        sections = [(f"частина {index + 1}", body) for index, body in enumerate(_split_by_paragraphs(text, max_chars // 2))]
    result: list[tuple[str, str]] = []
    for heading, body in sections:
        if len(body) <= max_chars:
            result.append((heading, body))
            continue
        for index, piece in enumerate(_split_by_paragraphs(body, max_chars)):
            result.append((f"{heading} ({index + 1})", piece))
    return [(heading, body) for heading, body in result if len(body) >= MIN_CHARS]


def _split_by_paragraphs(text: str, size: int) -> list[str]:
    pieces, current = [], ""
    for paragraph in text.split("\n\n"):
        if current and len(current) + len(paragraph) > size:
            pieces.append(current.strip())
            current = ""
        current += paragraph + "\n\n"
    if current.strip():
        pieces.append(current.strip())
    return pieces


def parse_gutenberg_catalog(csv_text: str) -> list[dict[str, str]]:
    """Рядки каталогу, що підходять: текст однією з наших мов, один автор, що помер не пізніше GUTENBERG_MAX_DEATH_YEAR."""
    candidates = []
    for row in csv.DictReader(io.StringIO(csv_text)):
        if row.get("Type") != "Text" or row.get("Language") not in GUTENBERG_BOOKS:
            continue
        authors = row.get("Authors", "")
        years = re.findall(r"(\d{4})-(\d{4})", authors)
        if ";" in authors or len(years) != 1 or int(years[0][1]) > GUTENBERG_MAX_DEATH_YEAR:
            continue
        subjects = (row.get("Subjects", "") + " " + row.get("Bookshelves", "")).lower()
        if not any(word in subjects for word in ("fiction", "short stories", "novel")):
            continue
        if re.search(r"\b(vol\.|volume|index|dictionary|letters)\b", row.get("Title", ""), re.IGNORECASE):
            continue
        candidates.append(row)
    return sorted(candidates, key=lambda r: int(r["Text#"]))


def collect_gutenberg(http: Http, rng: random.Random, scale: float, log) -> list[Doc]:
    catalog = http.get(GUTENBERG_CATALOG, pause=1.0).decode("utf-8", errors="replace")
    candidates = parse_gutenberg_catalog(catalog)
    docs: list[Doc] = []
    for lang, quota in GUTENBERG_BOOKS.items():
        pool = [row for row in candidates if row["Language"] == lang]
        random.Random(f"{rng.random()}-gutenberg-{lang}").shuffle(pool)
        taken = 0
        for row in pool:
            if taken >= _scaled(quota, scale):
                break
            number = row["Text#"]
            try:
                raw = http.get(f"https://www.gutenberg.org/cache/epub/{number}/pg{number}.txt", pause=1.0).decode("utf-8-sig", errors="replace")
            except urllib.error.HTTPError:
                continue
            text = strip_gutenberg(raw)
            chapters = split_chapters(text)[:GUTENBERG_CHAPTERS_PER_BOOK]
            if len(text) < 20000 or not chapters or not language_ok(text[:20000], lang):
                continue
            taken += 1
            title = row["Title"].replace("\n", " ").strip()
            author = row["Authors"].split(",")[0].strip()
            for index, (heading, body) in enumerate(chapters, start=1):
                name = f"{title} — {heading}"
                docs.append(Doc("gutenberg", lang, "literature", name, f"https://www.gutenberg.org/ebooks/{number}",
                                LICENSE_PD, f"# {name}\n\n{body}", group=f"pg{number}", authors=author))
        log(f"gutenberg/{lang}: книг {taken}")
    return docs


def _group_poems(pieces: list[tuple[str, str]], size: int = 6000) -> list[list[tuple[str, str]]]:
    """Вірші одного автора — добірками приблизно по `size` символів (окремий вірш надто короткий для документа)."""
    groups: list[list[tuple[str, str]]] = []
    current: list[tuple[str, str]] = []
    length = 0
    for piece in pieces:
        current.append(piece)
        length += len(piece[1])
        if length >= size:
            groups.append(current)
            current, length = [], 0
    if current and (not groups or length >= MIN_CHARS):
        groups.append(current)
    elif current:
        groups[-1].extend(current)
    return groups


def collect_wikisource(http: Http, rng: random.Random, scale: float, log) -> list[Doc]:
    """Українські вірші, казки й проза з uk.wikisource.org; вірші одного автора — однією добіркою (окремі надто короткі)."""
    wiki = MediaWiki(http, "uk.wikisource.org")
    categories: list[tuple[str, int]] = []
    for prefix in WIKISOURCE_PREFIXES:
        data = wiki.query(list="allcategories", acprefix=prefix.strip(), aclimit=200, acprop="size")
        categories.extend((c["category"], c["pages"]) for c in data["query"]["allcategories"] if c["pages"] >= 3)
    categories = sorted(set(categories), key=lambda item: (-item[1], item[0]))
    budget = _scaled(WIKISOURCE_MAX_PAGES, scale)
    members: dict[str, list[str]] = {}
    used = 0
    for name, _ in categories:
        if used >= budget:
            break
        data = wiki.query(list="categorymembers", cmtitle=f"Категорія:{name}", cmlimit=60, cmnamespace=0)
        members[name] = [m["title"] for m in data["query"]["categorymembers"]]
        used += len(members[name])
    texts = wiki.texts(sorted({title for titles in members.values() for title in titles}))
    docs: list[Doc] = []
    for name, titles in members.items():
        pieces = [(texts[t][0], texts[t][1]) for t in titles if t in texts and len(texts[t][1]) >= 40]
        if not pieces:
            continue
        if name.startswith("Поезія "):
            for number, group in enumerate(_group_poems(pieces), start=1):
                title = f"{name} — добірка {number}"
                body = "\n\n".join(f"## {poem}\n\n{text}" for poem, text in group)
                docs.append(Doc("wikisource", "uk", "literature", title, page_url("uk.wikisource.org", f"Категорія:{name}"),
                                LICENSE_PD, f"# {title}\n\n{body}"))
        else:
            for title, text in pieces:
                if len(text) >= 600:
                    docs.append(Doc("wikisource", "uk", "literature", title, page_url("uk.wikisource.org", title),
                                    LICENSE_PD, f"# {title}\n\n{text}"))
    log(f"wikisource: {len(docs)} документів із {used} сторінок у {len(members)} категоріях")
    return docs


def collect_stackexchange(http: Http, rng: random.Random, scale: float, log) -> list[Doc]:
    """Питання з прийнятою відповіддю, за рейтингом; розмова оформлюється як нотатка «питання + відповідь»."""
    docs: list[Doc] = []
    for site, lang, topic, count in SE_SITES:
        want = _scaled(count, scale)
        questions: list[dict] = []
        page = 1
        while len(questions) < want * 2 and page <= 8:
            url = ("https://api.stackexchange.com/2.3/search/advanced?" + urllib.parse.urlencode(
                {"accepted": "True", "sort": "votes", "order": "desc", "site": site, "pagesize": 100, "page": page,
                 "filter": "withbody"}))
            data = http.json(url, pause=1.0)
            questions.extend(q for q in data.get("items", []) if q.get("accepted_answer_id"))
            if data.get("backoff"):
                time.sleep(float(data["backoff"]))
            if not data.get("has_more"):
                break
            page += 1
        random.Random(f"{rng.random()}-se-{site}").shuffle(questions)
        questions = questions[: want * 2]
        answers: dict[int, dict] = {}
        for start in range(0, len(questions), 100):
            ids = ";".join(str(q["accepted_answer_id"]) for q in questions[start:start + 100])
            data = http.json(f"https://api.stackexchange.com/2.3/answers/{ids}?" + urllib.parse.urlencode(
                {"site": site, "pagesize": 100, "filter": "withbody"}), pause=1.0)
            answers.update({a["answer_id"]: a for a in data.get("items", [])})
            if data.get("backoff"):
                time.sleep(float(data["backoff"]))
        taken = 0
        for question in questions:
            if taken >= want:
                break
            doc = build_se_doc(question, answers.get(question["accepted_answer_id"]), lang, topic, site in SE_MIXED_SITES)
            if doc:
                docs.append(doc)
                taken += 1
        log(f"stackexchange/{site}: {taken} з {want}")
    return docs


def se_license(created: int) -> str:
    """Ліцензія допису Stack Exchange за датою створення: до 2011-04 — CC BY-SA 2.5, до 2018-05 — 3.0, далі 4.0.

    Поле content_license в API буває без версії або різним у питання й відповіді, а в документі їх двоє,
    тому беремо дату пізнішого допису (одна версія на документ).
    """
    moment = datetime.datetime.fromtimestamp(created, datetime.timezone.utc)
    if moment < datetime.datetime(2011, 4, 8, tzinfo=datetime.timezone.utc):
        return "CC BY-SA 2.5"
    if moment < datetime.datetime(2018, 5, 2, tzinfo=datetime.timezone.utc):
        return "CC BY-SA 3.0"
    return "CC BY-SA 4.0"


def build_se_doc(question: dict, answer: dict | None, lang: str, topic: str, mixed: bool = False) -> Doc | None:
    if not answer:
        return None
    title = html_to_text(question["title"]).replace("\n", " ")
    question_text = html_to_text(question["body"])
    answer_text = html_to_text(answer["body"])
    if len(question_text) + len(answer_text) < 500:
        return None
    license_ = se_license(max(question.get("creation_date", 0), answer.get("creation_date", 0)))
    authors = ", ".join(sorted({o.get("display_name", "") for o in (question.get("owner", {}), answer.get("owner", {})) if o.get("display_name")}))
    text = f"# {title}\n\n{question_text}\n\n## Answer\n\n{answer_text}"
    return Doc("stackexchange", lang, topic, title, question["link"], license_, text,
               group=f"se{question['question_id']}", authors=html_to_text(authors), mixed=mixed)


COLLECTORS = {
    "wikipedia": collect_wikipedia, "wikivoyage": collect_wikivoyage, "cookbook": collect_cookbook,
    "gutenberg": collect_gutenberg, "wikisource": collect_wikisource, "stackexchange": collect_stackexchange,
}


# ---------------------------------------------------------------------------------------------------------------------
# Запис: md, txt, docx, pdf
# ---------------------------------------------------------------------------------------------------------------------

def find_pdf_font(explicit: str | None = None) -> Path | None:
    """TTF з кирилицею й діакритикою для PDF (DejaVu Sans, Arial або Liberation)."""
    candidates = [explicit] if explicit else []
    try:
        import matplotlib  # необов'язково: якщо є, у ньому лежить DejaVu Sans
        candidates.append(str(Path(matplotlib.get_data_path()) / "fonts" / "ttf" / "DejaVuSans.ttf"))
    except ImportError:
        pass
    candidates += [
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "/usr/share/fonts/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
        "/Library/Fonts/Arial Unicode.ttf", "/System/Library/Fonts/Supplemental/Arial.ttf",
        "C:/Windows/Fonts/arial.ttf", "C:/Windows/Fonts/segoeui.ttf",
    ]
    return next((Path(c) for c in candidates if c and Path(c).is_file()), None)


def plain_text(markdown: str) -> str:
    """Для .txt: без markdown-розмітки заголовків."""
    return re.sub(r"^#{1,6} ", "", markdown, flags=re.MULTILINE)


def write_docx(path: Path, markdown: str) -> None:
    import docx

    document = docx.Document()
    for line in markdown.split("\n"):
        heading = re.match(r"^(#{1,6}) (.*)", line)
        if heading:
            document.add_heading(heading.group(2), level=min(len(heading.group(1)), 4))
        elif line.strip():
            document.add_paragraph(line)
    # фіксовані дати: однакові вхідні дані дають однаковий файл
    stamp = datetime.datetime(2000, 1, 1)
    document.core_properties.created = stamp
    document.core_properties.modified = stamp
    document.core_properties.author = "LocalMind corpus"
    document.core_properties.last_modified_by = "LocalMind corpus"
    document.save(str(path))
    _freeze_zip_dates(path)


def _freeze_zip_dates(path: Path) -> None:
    """DOCX — це zip з поточними датами записів; фіксуємо їх, щоб файл не залежав від часу збирання."""
    import zipfile

    with zipfile.ZipFile(path) as source:
        items = [(info.filename, source.read(info.filename)) for info in source.infolist()]
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as target:
        for name, data in items:
            info = zipfile.ZipInfo(name, (2000, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            target.writestr(info, data)


def write_pdf(path: Path, markdown: str, font: Path) -> None:
    from xml.sax.saxutils import escape

    from reportlab.lib.pagesizes import A4
    from reportlab.lib.styles import ParagraphStyle
    from reportlab.pdfbase import pdfmetrics
    from reportlab.pdfbase.ttfonts import TTFont
    from reportlab.platypus import Paragraph, SimpleDocTemplate, Spacer

    if "LocalMindFont" not in pdfmetrics.getRegisteredFontNames():
        pdfmetrics.registerFont(TTFont("LocalMindFont", str(font)))
    body = ParagraphStyle("body", fontName="LocalMindFont", fontSize=10, leading=14)
    heading = ParagraphStyle("heading", parent=body, fontSize=14, leading=18, spaceBefore=8, spaceAfter=6)
    story = []
    for line in markdown.split("\n"):
        match = re.match(r"^(#{1,6}) (.*)", line)
        if match:
            story.append(Paragraph(escape(match.group(2)), heading))
        elif line.strip():
            story.append(Paragraph(escape(line), body))
            story.append(Spacer(1, 3))
    SimpleDocTemplate(str(path), pagesize=A4, invariant=1, title="", author="LocalMind corpus").build(story)


def write_document(root: Path, doc: Doc, pdf_font: Path | None) -> dict | None:
    """Пише документ у `root/<джерело>/<мова>/` і повертає рядок маніфесту (None — відкинуто)."""
    key = f"{doc.source}|{doc.lang}|{doc.url}|{doc.title}"
    fmt = choose_format(key, doc.source, pdf_font is not None)
    text = normalize_text(doc.text)
    text = truncate_text(text, MAX_CHARS[fmt])
    body = text.split("\n", 2)[-1] if text.startswith("# ") else text
    lang = doc.lang
    if doc.mixed and detect_language(text) in ("uk", "en"):
        lang = str(detect_language(text))
    if len(body) < MIN_CHARS or not language_ok(text, lang):
        return None
    folder = root / doc.source / lang
    folder.mkdir(parents=True, exist_ok=True)
    # у назву йде хеш і тексту: у книгах повторюються заголовки розділів («CHAPTER I.») з однаковим url
    name = f"{slugify(doc.title)}-{hashlib.sha1((key + chr(10) + text).encode('utf-8')).hexdigest()[:6]}.{fmt}"
    path = folder / name
    if fmt == "docx":
        write_docx(path, text)
    elif fmt == "pdf":
        assert pdf_font is not None
        write_pdf(path, text, pdf_font)
    else:
        path.write_text((plain_text(text) if fmt == "txt" else text) + "\n", encoding="utf-8", newline="\n")
    try:
        data = path.read_bytes()
    except OSError:
        # антивірус Windows блокує й вилучає файли з подібним до експлойтів кодом (Q&A про безпеку): такі документи
        # у корпус не беремо, щоб він не лякав антивіруси на машинах користувачів
        path.unlink(missing_ok=True)
        return None
    return {
        "path": path.relative_to(root).as_posix(), "lang": lang, "source": doc.source, "url": doc.url,
        "license": doc.license, "topic": doc.topic, "sha256": hashlib.sha256(data).hexdigest(), "title": doc.title,
        "group": doc.group, "format": fmt, "chars": len(text), "bytes": len(data), "authors": doc.authors,
        "retrieved": datetime.date.today().isoformat(),
    }


def read_manifest(path: Path) -> list[dict]:
    if not path.is_file():
        return []
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]


def merge_manifest(path: Path, source: str, entries: list[dict]) -> list[dict]:
    """Підміняє в маніфесті записи одного джерела; файл читається заново, тож паралельні запуски різних джерел не заважають."""
    merged = [entry for entry in read_manifest(path) if entry["source"] != source] + entries
    merged.sort(key=lambda e: e["path"])
    path.write_text("".join(json.dumps(e, ensure_ascii=False) + "\n" for e in merged), encoding="utf-8", newline="\n")
    return merged


def prune_missing(root: Path, entries: list[dict], log) -> list[dict]:
    """Лишає записи, чий файл на місці й збігається за sha256 (антивірус міг прибрати файл після запису)."""
    kept = []
    for entry in entries:
        path = root / entry["path"]
        try:
            ok = hashlib.sha256(path.read_bytes()).hexdigest() == entry["sha256"]
        except OSError:
            ok = False
        if ok:
            kept.append(entry)
        else:
            path.unlink(missing_ok=True)
            log(f"прибрано з маніфесту (файл зник або змінився): {entry['path']}")
    return kept


def build(out: Path, manifest_path: Path, cache: Path, sources: list[str], scale: float, seed: int, pause: float,
          wiki_pause: float, pdf_font: Path | None, log) -> list[dict]:
    http = Http(cache, pause, wiki_pause)
    out.mkdir(parents=True, exist_ok=True)
    for source in sources:
        rng = random.Random(seed)
        started = time.monotonic()
        docs = COLLECTORS[source](http, rng, scale, log)
        if (out / source).is_dir():
            shutil.rmtree(out / source)  # лише згенеровані нами теки відомих джерел; після збирання, щоб збій не лишав порожнечу
        entries = []
        seen: set[str] = set()
        for doc in docs:
            digest = hashlib.sha1(normalize_text(doc.text).encode("utf-8")).hexdigest()
            if digest in seen:  # однакові тексти (різні назви-перенаправлення на одну статтю)
                continue
            seen.add(digest)
            entry = write_document(out, doc, pdf_font)
            if entry:
                entries.append(entry)
        merge_manifest(manifest_path, source, prune_missing(out, entries, log))
        log(f"== {source}: записано {len(entries)}, відкинуто {len(docs) - len(entries)} (мова/розмір), "
            f"{time.monotonic() - started:.0f} с, запитів {http.requests}")
    return read_manifest(manifest_path)


def summarize(entries: list[dict]) -> str:
    """Таблиця «документів / МБ» за мовами, джерелами й форматами."""
    lines = [f"Усього: {len(entries)} документів, {sum(e['bytes'] for e in entries) / 1048576:.1f} МБ"]
    for field_name, label in (("lang", "Мова"), ("source", "Джерело"), ("format", "Формат"), ("topic", "Тема")):
        totals: dict[str, list[int]] = {}
        for entry in entries:
            bucket = totals.setdefault(entry[field_name], [0, 0])
            bucket[0] += 1
            bucket[1] += entry["bytes"]
        lines.append(f"\n{label}:")
        for key, (count, size) in sorted(totals.items(), key=lambda item: -item[1][0]):
            lines.append(f"  {key:<14}{count:>6} док.{size / 1048576:>9.1f} МБ")
    return "\n".join(lines)


GROUPED_SOURCES = ("wikipedia", "wikivoyage")  # мовні версії однієї статті беруться тільки цілими групами


def _allocate(strata: dict[tuple, list[dict]], quota: int) -> dict[tuple, int]:
    """Пропорційний розподіл квоти за стратами (метод найбільших залишків), не більше, ніж є в страті."""
    total = sum(len(v) for v in strata.values())
    raw = {key: quota * len(items) / total for key, items in strata.items()}
    taken = {key: min(int(value), len(strata[key])) for key, value in raw.items()}
    for key in sorted(raw, key=lambda k: (-(raw[k] - int(raw[k])), k)):
        if sum(taken.values()) >= quota:
            break
        if taken[key] < len(strata[key]):
            taken[key] += 1
    return taken


def select_subset(entries: list[dict], size: int, seed: int) -> list[dict]:
    """Стратифікована підвибірка приблизно з `size` документів, детермінована за `seed`.

    * частка кожного джерела — як у повному корпусі;
    * Вікіпедія й Wikivoyage — цілими групами (усі мовні версії статті), групи чергуються за темами, тож мови,
      теми й cross-lingual збережені;
    * решта джерел — пропорційно за (мова, тема).
    """
    rng = random.Random(seed)
    by_source: dict[str, list[dict]] = {}
    for entry in entries:
        by_source.setdefault(entry["source"], []).append(entry)
    chosen: list[dict] = []
    for source in sorted(by_source):
        items = sorted(by_source[source], key=lambda e: e["path"])
        quota = round(size * len(items) / len(entries))
        if quota <= 0:
            continue
        if source in GROUPED_SOURCES:
            groups: dict[str, list[dict]] = {}
            for item in items:
                groups.setdefault(item["group"], []).append(item)
            by_topic: dict[str, list[str]] = {}
            for group in sorted(groups):
                by_topic.setdefault(groups[group][0]["topic"], []).append(group)
            for topic_groups in by_topic.values():
                rng.shuffle(topic_groups)
            taken = 0
            while taken < quota and any(by_topic.values()):
                for topic in sorted(by_topic):
                    if by_topic[topic] and taken < quota:
                        group = by_topic[topic].pop()
                        chosen.extend(groups[group])
                        taken += len(groups[group])
        else:
            strata: dict[tuple, list[dict]] = {}
            for item in items:
                strata.setdefault((item["lang"], item["topic"]), []).append(item)
            for key, count in _allocate(strata, quota).items():
                pool = list(strata[key])
                rng.shuffle(pool)
                chosen.extend(pool[:count])
    return sorted(chosen, key=lambda e: e["path"])


def build_subset(corpus: Path, manifest_path: Path, out: Path, size: int, seed: int) -> list[dict]:
    """Копіює підвибірку в `out/corpus` і пише `out/manifest.jsonl` (підмножина повного маніфесту)."""
    selected = select_subset(read_manifest(manifest_path), size, seed)
    target = out / "corpus"
    if target.is_dir():
        shutil.rmtree(target)  # лише згенерована раніше підвибірка
    for entry in selected:
        destination = target / entry["path"]
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(corpus / entry["path"], destination)
    out.mkdir(parents=True, exist_ok=True)
    (out / "manifest.jsonl").write_text("".join(json.dumps(e, ensure_ascii=False) + "\n" for e in selected),
                                        encoding="utf-8", newline="\n")
    return selected


def main(argv: list[str] | None = None) -> int:
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8", errors="replace")
    here = Path(__file__).parent
    parser = argparse.ArgumentParser(description="Збирання відкритого багатомовного корпусу (R-01).")
    parser.add_argument("--out", default=str(here / "corpus"), help="тека корпусу (за замовчуванням corpus/ поруч зі скриптом)")
    parser.add_argument("--manifest", default=None, help="manifest.jsonl (за замовчуванням на теку вище --out)")
    parser.add_argument("--cache", default=str(here / ".cache"), help="кеш відповідей API")
    parser.add_argument("--sources", default=",".join(SOURCES), help=f"через кому з: {', '.join(SOURCES)}")
    parser.add_argument("--scale", type=float, default=1.0, help="множник кількості документів (0.05 — швидка перевірка)")
    parser.add_argument("--seed", type=int, default=2026)
    parser.add_argument("--pause", type=float, default=0.5, help="пауза між запитами до не-вікімедійних API, с")
    parser.add_argument("--wiki-pause", type=float, default=WIKI_PAUSE, help="пауза між запитами до сайтів Вікімедії, с")
    parser.add_argument("--pdf-font", default=None, help="TTF з кирилицею для PDF (інакше шукається в системі)")
    parser.add_argument("--subset", type=int, default=0, help="не збирати, а вибрати з готового корпусу підкорпус на N документів")
    parser.add_argument("--subset-out", default=None, help="тека підкорпусу (corpus/ і manifest.jsonl всередині; за замовчуванням open-eval поряд з --out)")
    args = parser.parse_args(argv)

    sources = [s.strip() for s in args.sources.split(",") if s.strip()]
    unknown = [s for s in sources if s not in COLLECTORS]
    if unknown:
        print(f"Невідомі джерела: {', '.join(unknown)}", file=sys.stderr)
        return 1
    out = Path(args.out)
    manifest = Path(args.manifest) if args.manifest else out.parent / "manifest.jsonl"
    if args.subset:
        target = Path(args.subset_out) if args.subset_out else out.parent.parent / "open-eval"
        print(summarize(build_subset(out, manifest, target, args.subset, args.seed)))
        return 0
    pdf_font = find_pdf_font(args.pdf_font)
    if pdf_font is None:
        print("Увага: шрифт для PDF не знайдено (--pdf-font), PDF-документи замінюються на DOCX.", file=sys.stderr)

    def log(message: str) -> None:
        print(f"[{datetime.datetime.now():%H:%M:%S}] {message}", flush=True)

    entries = build(out, manifest, Path(args.cache), sources, args.scale, args.seed, args.pause, args.wiki_pause, pdf_font, log)
    print(summarize(entries))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
