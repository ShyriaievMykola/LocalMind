# LocalMind

**Семантичний пошук по вашій персональній базі знань.**
Питайте природною мовою і знаходьте потрібний фрагмент у своїх нотатках, конспектах і документах,
навіть якщо не пам'ятаєте точних слів.

> Запит: *«Як я минулого разу вирішував проблему з Docker networking?»*
> Знайдено: `notes/devops-2025.md` — *«Контейнери не бачили один одного — створив окрему bridge-мережу…»*

LocalMind індексує ваші папки з нотатками (TXT, Markdown, PDF з текстом, DOCX) і шукає в них трьома способами:
за ключовими словами (BM25), за змістом (embeddings) і гібридно. Усе працює локально й офлайн, а зашифровану
резервну копію бази можна зберігати на власному сервері.

STEM-проєкт з дисциплін «Мультиплатформне програмування» та «Захист програм і даних»,
КНУ імені Тараса Шевченка, факультет інформаційних технологій.

---

## Принципи

- **Local-first.** Пошук, модель та індекс працюють на вашому комп'ютері, повністю офлайн. Дані нікуди не відправляються.
- **Кросплатформність.** Windows, Linux і macOS з одного .NET-коду.
- **Прозорість.** Для кожного результату видно, з якого він файла і наскільки схожий на запит.
- **Безпечний бекап.** Резервна копія шифрується на клієнті до відправки, тому сервер не може її прочитати.

## Можливості

| Можливість | Де |
|---|---|
| Імпорт TXT, Markdown, PDF (з текстовим шаром), DOCX | застосунок, CLI |
| Відстеження папок і переіндексація лише змінених файлів | застосунок, CLI |
| Пошук keyword (BM25, SQLite FTS5) · semantic (embeddings) · hybrid (RRF) | застосунок, CLI |
| Пошук українською, англійською та між мовами (багатомовні моделі) | застосунок, CLI |
| Картка результату: файл, оцінка, підсвітка, контекст навколо фрагмента, відкриття джерела | застосунок |
| Історія запитів, гарячі клавіші (`Ctrl+K` / `Ctrl+F`, на macOS `Cmd`) | застосунок |
| Каталог із 4 embedding-моделей з перевіркою SHA-256 (вибір у `appsettings.json`) | застосунок, CLI, скрипти |
| Наближений векторний індекс HNSW для великих баз (вимкнений за замовчуванням) | налаштування |
| Сервер бекапу: реєстрація, вхід (JWT), rate limiting, адмін-ендпоінти, аудит | сервер |
| Шифрування бекапу на клієнті (AES-256-GCM, ключ з пароля через PBKDF2-SHA256) | застосунок |
| Оцінка якості пошуку: Recall@k, MRR, nDCG, затримки, графіки | CLI `eval` |

## Технології

| Шар | Технологія |
|---|---|
| Платформа | .NET 10 (LTS), C# |
| UI | Avalonia UI, MVVM (CommunityToolkit.Mvvm) |
| Сервер | ASP.NET Core, ASP.NET Core Identity, JWT |
| Сховище | SQLite, EF Core, FTS5 (BM25) |
| Embeddings | ONNX Runtime, Microsoft.ML.Tokenizers |
| Тести | xUnit, coverlet, BenchmarkDotNet |

---

## Встановлення з релізу (без .NET)

1. На сторінці [Releases](https://github.com/ShyriaievMykola/LocalMind/releases) завантажте архів під свою систему:
   `LocalMind-App-<rid>` (застосунок) і за потреби `LocalMind-Cli-<rid>` (консольна утиліта),
   де `<rid>` — `win-x64`, `linux-x64` або `osx-arm64`.
2. Розпакуйте архів і з його папки завантажте модель (~470 МБ, один раз):

   ```bash
   ./scripts/download-models.sh     # Linux, macOS
   ```

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\download-models.ps1    # Windows
   ```

   На Windows запуск `.ps1` за замовчуванням заборонений, тому `-ExecutionPolicy Bypass`: дозвіл діє лише на цей запуск і нічого не змінює в системі.
3. Запустіть `LocalMind.App` (на Windows `LocalMind.App.exe`).
   - macOS: перший запуск через «Відкрити» в контекстному меню або `xattr -dr com.apple.quarantine <папка>`.
   - Windows: SmartScreen попередить про непідписану програму: «Докладніше» → «Виконати однаково».
4. У розділі «Бібліотека» додайте папку з нотатками, дочекайтеся індексації і шукайте в розділі «Пошук».

Подробиці про архіви й обмеження — у [docs/release.md](docs/release.md).

### Де лежать дані

База `localmind.db` лежить у теці даних користувача: `%LOCALAPPDATA%\LocalMind` на Windows,
`~/.local/share/LocalMind` на Linux, `~/Library/Application Support/LocalMind` на macOS.
Застосунок шукає модель поруч із собою (`models/`) або в цій теці.

---

## Збірка з вихідного коду

### Вимоги

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (версія закріплена в `global.json`)
- Git
- *(опційно)* Visual Studio 2022 17.14+ / Visual Studio 2026, JetBrains Rider або VS Code з C# Dev Kit

```bash
git clone https://github.com/ShyriaievMykola/LocalMind.git
cd LocalMind
dotnet build LocalMind.slnx
dotnet test LocalMind.slnx
```

Тести, яким потрібна модель, без неї пропускаються. Звіт покриття формується в CI.

### Модель

Embedding-модель у Git не зберігається. З кореня репозиторію:

```bash
./scripts/download-models.sh     # Linux, macOS
```

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\download-models.ps1    # Windows
```

Без параметра завантажується `multilingual-e5-small` (модель за замовчуванням). Інші моделі з каталогу передаються
параметром: `multilingual-e5-base`, `paraphrase-multilingual-MiniLM-L12-v2`, `paraphrase-multilingual-mpnet-base-v2` або `all`.
Модель обирається в `appsettings.json` (`LocalMind:Embedding:ModelName`). Без моделі працює лише пошук за ключовими словами.

### Запуск

Десктопний застосунок:

```bash
dotnet run --project src/LocalMind.App
```

Консольна утиліта (з кореня репозиторію, бо шляхи до моделі в `src/LocalMind.Cli/appsettings.json` відносні):

```bash
dotnet run --project src/LocalMind.Cli -- index ./notes
dotnet run --project src/LocalMind.Cli -- search "що робити, якщо ноутбук зламався" --k 5
dotnet run --project src/LocalMind.Cli -- search "ssh-keygen" --mode keyword
```

Повторний `index` оновлює лише змінені файли. Якщо частину файлів не вдалося обробити, `index` завершується з кодом 1,
а решта файлів при цьому індексується. `search --mode` приймає `keyword`, `semantic` або `hybrid` (за замовчуванням).

Сервер бекапу локально (`http://localhost:5133`, Swagger у Development):

```bash
dotnet run --project src/LocalMind.Server
```

Для постійного запуску сервера — Docker, див. нижче.

### Оцінка якості пошуку

```bash
dotnet run --project src/LocalMind.Cli -c Release -- eval --dataset eval/datasets/demo
```

Датасет складається з корпусу документів, запитів і розмітки релевантності ([формат](eval/datasets/README.md)). Результат —
CSV і Markdown у `eval/results/`. Прогони з різною нарізкою й моделями задаються через `--config` ([eval/experiments](eval/experiments/README.md)),
графіки будуються в [eval/plots](eval/plots/README.md), бенчмарки векторного індексу лежать у `benchmarks/LocalMind.Benchmarks`.
Результати досліджень — у [docs/research](docs/research).

### Сервер бекапу в Docker

Потрібні Docker і Docker Compose. З кореня репозиторію:

```bash
cp deploy/.env.example deploy/.env
```

У `deploy/.env` впишіть ключ підпису JWT у `LOCALMIND_JWT_KEY` (`openssl rand -base64 48`); файл у Git не потрапляє. Без ключа
контейнер не запуститься. Далі:

```bash
docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d --build
```

Сервер слухає `127.0.0.1:8080` (HTTP, порт змінюється через `LOCALMIND_PORT`) і перевіряється на `/api/v1/health`. База й бекапи лежать у двох томах Docker
(`localmind-db`, `localmind-backups`) і переживають перезапуск та оновлення образу. Секретів в образі немає: ключ
передається лише змінною середовища `Jwt__SigningKey`.

**Адміністратор.** Щоб з'явився адміністратор, задайте в `deploy/.env` `LOCALMIND_ADMIN_EMAIL` і
`LOCALMIND_ADMIN_PASSWORD` (пароль щонайменше 12 символів): акаунт створюється під час запуску, якщо такого email ще немає.
Уже зареєстрований email адміністратором не стає. Після першого запуску пароль можна прибрати з `.env`. Адмін-ендпоінти
(`/api/v1/admin/users`, `/api/v1/admin/users/{id}/block`, `/api/v1/admin/users/{id}/unblock`, `/api/v1/admin/audit`) описані в Swagger у Development.

**HTTPS.** Сервер усередині контейнера працює по HTTP, а TLS має завершувати reverse proxy на тому ж хості
(Caddy, nginx, Traefik). Клієнт бекапу приймає лише `https://` (HTTP дозволено тільки для localhost). Проксі повинен
передавати `X-Forwarded-For` і `X-Forwarded-Proto`. Сервер довіряє цим заголовкам лише від адрес із
`ReverseProxy__KnownProxies__N` (IP) і `ReverseProxy__KnownNetworks__N` (CIDR); у `docker-compose.yml` за
замовчуванням довірена мережа Docker `172.16.0.0/12`, змінюється через `LOCALMIND_PROXY_NETWORK`. Без цього всі
клієнти виглядали б як одна IP, і rate limiting (DR-032) блокував би їх разом. Приклад для Caddy:

```text
backup.example.com {
    reverse_proxy 127.0.0.1:8080
}
```

Це вся приватна мережа Docker, тож будь-який інший контейнер у ній міг би підробити ці заголовки: якщо в мережі є
не лише проксі, звузьте `LOCALMIND_PROXY_NETWORK` до його адреси (`/32`). Сервер бере лише останню адресу в ланцюжку
`X-Forwarded-For` (`ForwardLimit = 1`): якщо перед ним стоять два проксі (наприклад, CDN і nginx), клієнтом вважатиметься
найближчий із них, і ліміт запитів стане спільним для всіх. Якщо проксі працює на хості, а не в мережі Docker, його запити приходять з адреси шлюзу мережі Docker (зазвичай
`172.17.0.1`): тоді задайте `LOCALMIND_PROXY_NETWORK=172.17.0.1/32`. Також вкажіть свій домен у
`LOCALMIND_ALLOWED_HOSTS`.

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
├─ tests/                  Тести для Core, Search, App і Server (xUnit)
├─ benchmarks/             BenchmarkDotNet: brute-force vs HNSW, recall
├─ docs/                   Архітектура, рішення, безпека, приймальні кейси, дослідження
├─ eval/                   Датасети, конфіги експериментів, графіки
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
- [Тестування безпеки (OWASP API Top 10)](docs/security-testing.md)
- [Статичний аналіз](docs/static-analysis.md)
- [Приймальні кейси](docs/acceptance-cases.md)
- [Релізи](docs/release.md)
- [Результати досліджень](docs/research)
- [Декларація використання ШІ-інструментів](META-USAGE.md)

## Як ми працюємо

- Кожна задача — це issue. Робота ведеться в окремій гілці (`<номер issue>-<опис>`, наприклад `168-d-02-release-smoke`) і потрапляє в `main` лише через pull request.
- Pull request вимагає review і зеленого CI.
- CI на Windows, Linux і macOS: збірка з попередженнями як помилками, тести, покриття, перевірка вразливих пакетів, CodeQL.
- Релізи ставляться тегом `v*`, див. [docs/release.md](docs/release.md).

## Команда

| Учасник | GitHub | Зона відповідальності |
|---|---|---|
| Микола Ширяєв | [@ShyriaievMykola](https://github.com/ShyriaievMykola) | ядро (C, ST), пошук (SE), CLI, сервер і безпека (B, SV, SEC), реліз, дослідження |
| Павло Святий | [@OnlySven](https://github.com/OnlySven) | парсери TXT, Markdown, PDF; навігація, пошук, картка результату й контекст у застосунку |
| Ігор Свириденко | [@Igorettto](https://github.com/Igorettto) | парсер DOCX, реєстр парсерів, нарізка; теми, обробка помилок, тести ViewModel, обліковий запис і бекап у застосунку |
| Назар Міщенко | [@Nazar2903](https://github.com/Nazar2903) | виявлення змін, спостерігач папок, фонова індексація; бібліотека, прогрес, налаштування, drag & drop, гарячі клавіші |
