# Архітектура LocalMind

Як влаштована система: компоненти, залежності між ними, основні потоки даних і модель даних.
Чому прийняті саме такі рішення — у [decisions.md](decisions.md). Модель загроз — у [threat-model.md](threat-model.md).

> Документ описує **цільову** архітектуру. Частина компонентів ще не реалізована —
> актуальний стан видно на дошці проєкту.

---

## 1. Огляд

LocalMind складається з двох незалежних частин:

- **Клієнт** (десктоп і CLI) — індексує файли користувача і виконує пошук **локально, офлайн**.
- **Сервер бекапу** — опційний: зберігає **зашифровані** резервні копії і автентифікує користувачів.
  Сервер не має доступу до вмісту нотаток і не бере участі в пошуку.

```
┌─────────────────────────── Комп'ютер користувача ───────────────────────────┐
│                                                                             │
│   LocalMind.App (Avalonia)              LocalMind.Cli (index/search/eval)   │
│            └─────────────────┬──────────────────┘                           │
│                              ▼                                              │
│   ┌──────────────── LocalMind.Search ────────────────┐                      │
│   │  Embeddings (ONNX) · Vectors · Hybrid (RRF)      │                      │
│   └─────────────────────────┬────────────────────────┘                      │
│                             ▼                                               │
│   ┌──────────────── LocalMind.Core ──────────────────────────────────┐      │
│   │  Models · Abstractions                                           │      │
│   │  Ingestion — парсинг, нарізка, відстеження папок                 │      │
│   │  Storage   — SQLite (EF Core + FTS5) ──► localmind.db            │      │
│   │  Backup    — шифрування AES-GCM + HTTP-клієнт ─────────┐         │      │
│   └────────────────────────────────────────────────────────┼─────────┘      │
└────────────────────────────────────────────────────────────┼────────────────┘
                                                             │ HTTPS + JWT
                                                             │ лише шифротекст
                                                             ▼
                          ┌──────────── LocalMind.Server ─────────────┐
                          │  Auth — Identity, JWT, refresh-токени     │
                          │  Backup API — зашифровані знімки          │
                          │  БД користувачів і метаданих (EF Core)    │
                          └───────────────────────────────────────────┘
```

---

## 2. Проєкти та залежності

| Проєкт | Тип | Посилається на | Відповідальність |
|---|---|---|---|
| `LocalMind.Core` | Class library | — | Моделі, інтерфейси, парсинг і нарізка, локальне сховище, шифрування і клієнт бекапу |
| `LocalMind.Search` | Class library | Core | Embeddings, векторний індекс, keyword-пошук, злиття результатів |
| `LocalMind.App` | Avalonia, WinExe | Core, Search | Десктопний інтерфейс (MVVM) |
| `LocalMind.Cli` | Console | Core, Search | Ручні перевірки та eval-раннер для експериментів |
| `LocalMind.Server` | ASP.NET Core | Core | Автентифікація, зберігання зашифрованих бекапів |
| `tests/*.Tests` | xUnit | відповідний проєкт | Юніт- та інтеграційні тести |

```
App ──┐
      ├──► Search ──► Core ◄── Server
Cli ──┘
```

### Правила

1. **Залежності йдуть лише вниз.** `Core` ні від чого не залежить; бібліотеки не знають про UI і хости.
2. **Логіка — лише в бібліотеках.** `App`, `Cli`, `Server` тільки конфігурують DI і з'єднують компоненти.
   Тому логіку можна покрити тестами без UI.
3. **Компоненти взаємодіють через інтерфейси** з `Core/Abstractions`. Реалізації підміняються
   в експериментах (інша модель, інший індекс) і в тестах (фейки).
4. **Сервер використовує з `Core` лише DTO** (`Core/Backup/Dtos`) і ніколи — пошук чи сховище нотаток.
5. **Зміна інтерфейсу в `Core/Abstractions`** — лише через PR з review від усіх, кого вона зачіпає.
6. **Новий проєкт** виносимо лише тоді, коли папка в `Core` суттєво розрослась ([DR-010](decisions.md#dr-010--пять-проєктів-у-solution-замість-окремого-проєкту-на-кожен-модуль)).

---

## 3. Компоненти

### 3.1. Ключові інтерфейси (`Core/Abstractions`)

| Інтерфейс | Що робить | Реалізації |
|---|---|---|
| `IDocumentParser` | Файл → чистий текст | `PlainTextParser`, `MarkdownParser`, `PdfParser`, `DocxParser` |
| `IChunker` | Текст → фрагменти | `FixedSizeChunker`, `ParagraphChunker` |
| `ITokenCounter` | Підрахунок токенів для нарізки | `HfTokenCounter` (токенізатор моделі) |
| `IEmbedder` | Тексти → вектори | `OnnxEmbedder` |
| `IVectorIndex` | Пошук найближчих векторів | `BruteForceIndex`, `HnswIndex` |
| `IKeywordIndex` | Повнотекстовий пошук BM25 | `Fts5KeywordIndex` |
| `IDocumentRepository` | Збереження документів і фрагментів | `DocumentRepository` |
| `IIndexingService` | Фонова індексація | `IndexingService` |
| `ISearchService` | Єдина точка входу в пошук | `SearchService` |

### 3.2. Ingestion (`Core/Ingestion`)

- `ParserRegistry` обирає парсер за розширенням файла.
- `ChangeDetector` рахує SHA-256 вмісту і порівнює зі збереженим — незмінені файли не переіндексовуються.
- `FolderWatcher` стежить за папками (`FileSystemWatcher` + debounce) і ставить зміни в чергу.
- `IndexingService` обробляє чергу у фоні: парсинг → нарізка → embeddings → збереження.

### 3.3. Storage (`Core/Storage`)

- `LocalMindDbContext` — EF Core поверх SQLite, міграції застосовуються при старті.
- FTS5-таблиця `ChunksFts` (external content над `Chunks`) синхронізується тригерами; запити до неї — параметризований SQL,
  кожне слово запиту в лапках (`Fts5KeywordIndex`, [DR-019](decisions.md)).
- Вектори зберігаються як BLOB і при старті завантажуються в `IVectorIndex` у пам'яті.

### 3.4. Search (`LocalMind.Search`)

- `OnnxEmbedder` — токенізація, інференс ONNX Runtime, mean pooling, L2-нормалізація; для e5-моделей — префікси `query:` / `passage:`.
- `VectorMath` — косинусна схожість (для нормалізованих векторів — скалярний добуток) з SIMD.
- `ReciprocalRankFusion` — злиття списків keyword і semantic.
- `SearchService` — режими Keyword / Semantic / Hybrid, формування `SearchResult`.

### 3.5. Backup (`Core/Backup`)

- `KeyDerivation` — PBKDF2-HMAC-SHA256, ключ із пароля шифрування.
- `AesGcmCipher` + `EncryptedEnvelope` — шифрування і формат конверта.
- `BackupApiClient` — HTTP-клієнт до сервера: JWT, автоматичне оновлення через refresh-токен.
- `BackupService` / `RestoreService` — повний цикл бекапу і відновлення.

### 3.6. Server (`LocalMind.Server`)

- Minimal API, префікс `/api/v1`, OpenAPI-документація.
- `AuthEndpoints` — реєстрація, вхід, оновлення токена, вихід.
- `BackupEndpoints` — завантаження, список, отримання, видалення знімків.
- `JwtTokenService`, `RefreshTokenService` — видача й ротація токенів.
- `FileSystemBlobStore` — зберігання зашифрованих знімків на диску.
- Rate limiting на ендпоінтах автентифікації, перевірка «власник ресурсу» на ендпоінтах бекапу.

---

## 4. Потоки даних

### 4.1. Індексація

```
Файл ─► ChangeDetector ─► ParserRegistry ─► IChunker ─► IEmbedder ─► IDocumentRepository
        чи змінився?      файл → текст      фрагменти   вектори      транзакція: документ,
        (SHA-256)                                                    фрагменти, вектори, FTS5
```

- Документ і всі його фрагменти зберігаються **в одній транзакції**: переіндексація або повністю замінює старі дані, або нічого не змінює.
- Помилка в одному файлі не зупиняє індексацію: файл отримує статус «помилка» з причиною.
- Прогрес передається в UI через `IndexingProgress`.

### 4.2. Пошук

```
Запит ─┬─► IEmbedder (query) ─► IVectorIndex.Search ─► top-k semantic ─┐
       │                                                               ├─► RRF ─► SearchResult[]
       └─► IKeywordIndex (FTS5, BM25) ─────────────────► top-k keyword ┘
```

RRF: `score(d) = Σ 1 / (k + rank_i(d))`, `k ≈ 60`. Працює з рангами, тому оцінки різних шкал не треба нормалізувати.

### 4.3. Бекап і відновлення

```
Бекап:        localmind.db ─► знімок (VACUUM INTO) ─► пароль → PBKDF2 → ключ ─► AES-GCM ─► POST /api/v1/backups
Відновлення:  GET /api/v1/backups/{id} ─► перевірка тегу GCM ─► розшифрування ─► тимчасовий файл ─► атомарна заміна БД
```

Формат `EncryptedEnvelope`: `version | salt | nonce | ciphertext | tag`.
Пароль шифрування бекапу не передається на сервер і не збігається з паролем входу.

---

## 5. Модель даних

### 5.1. Клієнт — SQLite

```
SourceFolder 1 ──── * Document 1 ──── * Chunk 1 ──── 0..1 Embedding * ──── 1 EmbeddingModel
                                            │
                                            └── chunks_fts (FTS5, синхронізується тригерами)
```

| Таблиця | Ключові поля | Обмеження та індекси |
|---|---|---|
| `SourceFolders` | `Id`, `Path` | PK; унікальний `Path` |
| `Documents` | `Id`, `SourceFolderId`, `Path`, `ContentHash`, `Status`, `Error`, `IndexedAt` | PK; FK → SourceFolders (каскад); унікальний `Path`; індекс по `SourceFolderId` |
| `Chunks` | `Id`, `DocumentId`, `Ordinal`, `Text`, `StartOffset` | PK; FK → Documents (каскад); унікальна пара (`DocumentId`, `Ordinal`) |
| `Embeddings` | `ChunkId`, `ModelId`, `Vector` (BLOB) | PK (`ChunkId`, `ModelId`); FK → Chunks (каскад), FK → EmbeddingModels |
| `EmbeddingModels` | `Id`, `Name`, `Dimensions` | PK; унікальний `Name` |
| `chunks_fts` | `rowid` = `Chunks`, `Text` | Віртуальна таблиця FTS5 |

`Embeddings` прив'язані до моделі, тому зміна моделі не змішує вектори різної розмірності, а для експериментів можна тримати вектори кількох моделей.

### 5.2. Сервер

| Таблиця | Ключові поля | Обмеження |
|---|---|---|
| Таблиці ASP.NET Core Identity | користувачі, ролі, хеші паролів | Стандартні |
| `RefreshTokens` | `Id`, `UserId`, `TokenHash`, `ExpiresAt`, `RevokedAt`, `ReplacedById` | FK → Users; індекс по `TokenHash` |
| `Snapshots` | `Id`, `OwnerId`, `CreatedAt`, `SizeBytes`, `BlobName` | FK → Users; індекс по `OwnerId` |

Вміст знімків зберігається поза БД (`FileSystemBlobStore`), у БД — лише метадані.

---

## 6. Наскрізні аспекти

### Конфігурація і розташування даних

| ОС | Тека даних клієнта |
|---|---|
| Windows | `%AppData%\LocalMind` |
| Linux | `~/.local/share/LocalMind` |
| macOS | `~/Library/Application Support/LocalMind` |

Там лежать `localmind.db` і налаштування користувача. Моделі — у `models/` (завантажуються скриптом, не в Git).
Секрети сервера (ключ підпису JWT, рядок підключення) — через user-secrets у розробці та змінні оточення в Docker, ніколи в репозиторії.

### Багатопотоковість

- Індексація — у фоні: черга на `System.Threading.Channels`, обчислення embeddings батчами.
- Усі довгі операції асинхронні та підтримують `CancellationToken`.
- UI оновлюється лише з UI-потоку (через `Dispatcher`); ядро нічого не знає про UI-потік.
- `IVectorIndex` потокобезпечний: пошук може йти паралельно з додаванням нових векторів.

### Обробка помилок

- Бібліотеки кидають доменні винятки з `Core/Errors` з зрозумілим повідомленням.
- Хости (App, Cli, Server) перехоплюють їх на межі: App показує повідомлення, Cli повертає ненульовий код виходу,
  Server відповідає `ProblemDetails` без stack trace.
- Помилки логуються; паролі, токени й вміст нотаток у логи не потрапляють.

### Тестування

| Рівень | Що | Де |
|---|---|---|
| Юніт | Парсери, нарізка, RRF, VectorMath, метрики, криптографія | `tests/LocalMind.Core.Tests`, `tests/LocalMind.Search.Tests` |
| Інтеграційний | Сховище на SQLite in-memory, FTS5 | `tests/LocalMind.Core.Tests` |
| Інтеграційний | API сервера через `WebApplicationFactory`, сценарії безпеки | `tests/LocalMind.Server.Tests` |
| Приймальний | Сценарії з [acceptance-cases.md](acceptance-cases.md) | Авто + ручний прогін |

Компоненти з важкими залежностями (ONNX-модель, БД) у юніт-тестах замінюються фейками через інтерфейси `Core/Abstractions`.
Фейки (`FakeEmbedder`, `FakeTokenCounter`, `InMemoryDocumentRepository`, `InMemoryKeywordIndex`) і фабрики `TestData`
лежать у `tests/LocalMind.TestUtilities` — спільні для всіх тестових проєктів і не враховуються в покритті.
