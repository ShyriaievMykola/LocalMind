# Статичний аналіз (критерій C4)

Що саме перевіряється, чим і де це можна приглушити. Налаштування — у `Directory.Build.props` і `.editorconfig`.

## Що перевіряється

| Перевірка | Де налаштовано | Що дає |
|---|---|---|
| Аналізатори .NET, рівень `latest-recommended` | `AnalysisLevel` | якість коду, надійність, продуктивність, використання API |
| Правила безпеки (CA2xxx, CA3xxx, CA5xxx) повністю | `AnalysisLevelSecurity=latest-all` | наприклад, небезпечний генератор випадкових чисел, слабка криптографія, SQL-ін'єкції |
| Стиль коду (`.editorconfig`) під час збірки | `EnforceCodeStyleInBuild` | file-scoped простори імен, `using` поза namespace, фігурні дужки, `_camelCase` для приватних полів, зайві `using` і змінні |
| Nullable reference types | `Nullable=enable` | помилки `null` на етапі компіляції |
| Попередження — помилки в CI | `TreatWarningsAsErrors` при `CI=true` | жодного нового попередження в `main` |
| Вразливі NuGet-пакети (прямі й транзитивні) | `NuGetAudit`, `NuGetAuditMode=all` | NU1901–NU1904 у CI ламають збірку |
| CodeQL | `.github/workflows/codeql.yml` | окремий аналіз безпеки C# |

CI збирає рішення командою `dotnet build LocalMind.slnx --configuration Release` з `CI=true` (його задає GitHub Actions).
Локально те саме: `CI=true dotnet build LocalMind.slnx -c Release` (у PowerShell: `$env:CI='true'`).

## Приглушення: повний перелік

Приглушення без пояснення не допускаються; це перевіряє `StaticAnalysisPolicyTests` (`#pragma warning disable` без коментаря,
`SuppressMessage` без `Justification`, `GlobalSuppressions.cs`, `NoWarn` поза `Directory.Build.props`, правило `= none` в
`.editorconfig` без коментаря над ним).

| Що приглушено | Де | Чому |
|---|---|---|
| `CS1591` (немає XML-документації публічного члена) | `Directory.Build.props`, `NoWarn` | XML-коментарі потрібні для змістовних API, а не для кожного члена; `GenerateDocumentationFile` увімкнено, щоб працював `IDE0005` |
| `CA2007` (`ConfigureAwait`) | `.editorconfig`, усе рішення | у ядрі немає `SynchronizationContext`, а в UI продовження мусить повертатися на UI-потік |
| `CA1707` (підкреслення в назвах) | `.editorconfig`, лише `tests/` | назви тестів `Метод_Умова_Результат` |
| `CA5394` (небезпечний `Random`) | `.editorconfig`, лише `tests/` | тестові дані з фіксованим зерном: потрібна відтворюваність, а не криптостійкість; у `src/` правило діє |
| `CA1031` (загальний `catch`) | `#pragma` у `ModelTokenizer.Load` | бібліотека токенізатора кидає різні типи винятків на пошкоджений файл, усі вони означають одне; `OutOfMemoryException` і `LocalMindException` не перехоплюються |
| Міграції EF Core | `.editorconfig`, `generated_code = true` | код згенеровано інструментом |

## Аудит (D-06)

Збірка з `CI=true`: **0 попереджень**. Неприглушених без причини правил, а також `SuppressMessage` і `GlobalSuppressions.cs`, немає.

Змінено за результатами аудиту:

- `AnalysisLevelSecurity=latest-all`: у `src/` нових попереджень немає (тож правила лише захищають від майбутніх), у `tests/` лише
  `CA5394` для тестового `Random` — приглушено з поясненням.
- Явні `NuGetAuditMode=all` / `NuGetAuditLevel=low`, щоб поведінка не залежала від версії SDK.
- `StaticAnalysisPolicyTests`: нове приглушення без пояснення не пройде тести.

Оцінено й **не** прийнято (рівень `latest-all` для решти категорій дає близько 230 попереджень):

| Правило | Кількість у `src/` | Рішення |
|---|---|---|
| CA1515 (типи в застосунках мають бути `internal`) | App 42, Server 38 | шум для проєктів-застосунків: масова зміна видимості зачепила б тести й не дає захисту |
| CA1307, CA1308 (культура рядків) | Core 5, Search 4 | рядки порівнюються порядково там, де це важливо; решта — не про безпеку. Варто прибрати разом із черговим рефакторингом |
| CA1062 (перевірка `null` публічних аргументів) | Core 5, Server 6, App 1 | дублює nullable-аналіз |
| CA1031 (загальний `catch`) | App 16, Core 2, Server 1 | кожен випадок — свідоме рішення (обробники UI, журнал аудиту не має валити запит); безпечно лише з окремим поясненням у коді |
| CA2000 (`Dispose`) | Server 1 (`BackupEndpoints.DownloadAsync`: хибне спрацювання, потік віддається в `TypedResults.File`, який його закриває) | не вмикаємо як помилку: приглушення дало б більше шуму, ніж користі |
| CA1819, CA2227, CA1002, CA1003, CA1054, CA1056, CA1724 | по кілька місць | стиль API/EF-сутностей, ризику немає |

## Знахідки в `src/LocalMind.App`

App — зона команди застосунку, тут нічого не змінювалось; за аудитом (`latest-all`) там:

- `CA2025` — `Program.cs:39`: перевірити, що задача з `IDisposable` завершується до звільнення об'єкта (надійність, найцікавіша знахідка);
- `CA1849` ×2 — `ResultCardViewModel.cs:119`, `SearchViewModel.cs:179`: `CancellationTokenSource.Cancel()` блокує, є `CancelAsync()`;
- `CA1031` ×16 — загальні `catch` у ViewModel-ах, `CoreStatus`, `UnhandledErrorReporter`;
- `CA1054`/`CA1056` ×3 — `string` замість `Uri` для адрес; `CA1724`, `CA1062` — по одному;
- `CA1515` ×42 — типи `public` у застосунку (шум, див. вище).
