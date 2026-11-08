# LocalMind

**Семантичний пошук по вашій персональній базі знань.**
Питайте природною мовою і знаходьте потрібний фрагмент у своїх нотатках, конспектах і документах,
навіть якщо не пам'ятаєте точних слів.

> Запит: *«Як я минулого разу вирішував проблему з Docker networking?»*
> Знайдено: `notes/devops-2025.md` — *«Контейнери не бачили один одного — створив окрему bridge-мережу…»*

> **Статус:** проєкт на ранній стадії розробки. Зараз у репозиторії є лише структура solution і
> заготовки компонентів. Функціональність з'являтиметься поступово (див. [Можливості](#можливості)).

STEM-проєкт з дисциплін «Мультиплатформне програмування» та «Захист програм і даних»,
КНУ імені Тараса Шевченка, факультет інформаційних технологій.

---

## Принципи

- **Local-first.** Пошук, модель та індекс працюють на вашому комп'ютері, повністю офлайн. Дані нікуди не відправляються.
- **Кросплатформність.** Windows, Linux і macOS з одного .NET-коду.
- **Прозорість.** Для кожного результату видно, з якого він файла і наскільки схожий на запит.
- **Безпечний бекап.** Резервна копія шифрується на клієнті до відправки, тому сервер не може її прочитати.

## Можливості

| Можливість | Статус |
|---|---|
| Імпорт TXT, Markdown, PDF (текстових), DOCX | 🔲 заплановано |
| Відстеження папок і переіндексація змінених файлів | 🔲 заплановано |
| Пошук: keyword (BM25) · semantic (embeddings) · hybrid (RRF) | 🔲 заплановано |
| Картка результату з підсвіткою та переходом до джерела | 🔲 заплановано |
| Десктопний застосунок (Avalonia) | 🔲 заплановано |
| Сервер бекапу з автентифікацією (JWT) | 🔲 заплановано |
| Шифрування бекапу на клієнті (AES-GCM) | 🔲 заплановано |

## Технології

| Шар | Технологія |
|---|---|
| Платформа | .NET 10 (LTS), C# |
| UI | Avalonia UI, MVVM (CommunityToolkit.Mvvm) |
| Сервер | ASP.NET Core, ASP.NET Core Identity, JWT |
| Сховище | SQLite, EF Core, FTS5 (BM25) |
| Embeddings | ONNX Runtime, Microsoft.ML.Tokenizers |
| Тести | xUnit, coverlet |

---

## Швидкий старт

### Вимоги

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (версія закріплена в `global.json`)
- Git
- *(опційно)* Visual Studio 2022 17.14+ / Visual Studio 2026, JetBrains Rider або VS Code з C# Dev Kit

Перевірити, що SDK встановлено:

```bash
dotnet --version
```

### Клонування та збірка

```bash
git clone https://github.com/ShyriaievMykola/LocalMind.git
cd LocalMind
dotnet build LocalMind.slnx
```

### Тести

```bash
dotnet test LocalMind.slnx
```

### Запуск

Десктопний застосунок:

```bash
dotnet run --project src/LocalMind.App
```

Консольна утиліта:

```bash
dotnet run --project src/LocalMind.Cli -- index ./notes
dotnet run --project src/LocalMind.Cli -- search "що робити, якщо ноутбук зламався" --k 5
```

Запускати з кореня репозиторію, бо шляхи до моделі в `src/LocalMind.Cli/appsettings.json` відносні.
Спочатку треба завантажити модель (див. нижче). Повторний `index` оновлює лише змінені файли.
Якщо частину файлів не вдалося обробити, `index` завершується з кодом 1, решта файлів при цьому індексується.

Сервер бекапу (за замовчуванням `http://localhost:5133`):

```bash
dotnet run --project src/LocalMind.Server
```

Embedding-модель `multilingual-e5-small` (близько 475 МБ, у Git не зберігається):

```bash
./scripts/download-models.sh     # Linux, macOS
.\scripts\download-models.ps1    # Windows
```

> Запуск сервера через Docker (`deploy/docker-compose.yml`) буде описано тут, щойно з'явиться.

---

## Структура репозиторію

```
LocalMind/
├─ src/
│  ├─ LocalMind.Core/      Моделі, інтерфейси, парсинг і нарізка документів,
│  │                       сховище SQLite, шифрування та клієнт бекапу
│  ├─ LocalMind.Search/    Embeddings (ONNX), векторний пошук, BM25, hybrid (RRF)
│  ├─ LocalMind.App/       Десктопний застосунок (Avalonia, MVVM)
│  ├─ LocalMind.Cli/       Консольна утиліта: index / search / eval
│  └─ LocalMind.Server/    Сервер бекапу (ASP.NET Core)
├─ tests/                  Тести для Core, Search і Server (xUnit)
├─ docs/                   Архітектура, лог рішень, модель загроз, приймальні кейси
├─ eval/datasets/          Тестові запити і розмітка для дослідження якості пошуку
├─ deploy/                 Docker Compose для сервера
├─ scripts/                Допоміжні скрипти (завантаження моделей)
└─ models/                 ONNX-моделі (не зберігаються в Git)
```

Залежності між проєктами йдуть лише вниз: `App`, `Cli` → `Search` → `Core`.
Уся логіка живе в бібліотеках `Core` і `Search`, а `App`, `Cli` і `Server` лише їх підключають.

## Документація

- [Архітектура](docs/architecture.md)
- [Лог архітектурних рішень](docs/decisions.md)
- [Модель загроз](docs/threat-model.md)
- [Приймальні кейси](docs/acceptance-cases.md)
- [Декларація використання ШІ-інструментів](META-USAGE.md)

## Як ми працюємо

- Кожна задача — це issue. Робота ведеться в окремій гілці (`feature/<номер>-<опис>`) і потрапляє в `main` лише через pull request.
- Pull request вимагає review і зеленого CI.
- Задачі та їхній статус — на дошці проєкту в GitHub Projects.

## Команда

| Учасник | Зона відповідальності |
|---|---|
| — | — |
| — | — |
| — | — |
| — | — |
