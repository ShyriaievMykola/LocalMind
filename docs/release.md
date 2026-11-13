# Релізи

## Як випустити

```
git tag v0.2.0
git push origin v0.2.0
```

`.github/workflows/release.yml` збирає App і CLI self-contained (.NET ставити не треба) під `win-x64`, `linux-x64`, `osx-arm64`
і створює GitHub Release з архівами та автоматичними нотатками. Тег ставити лише на коміт, що вже в `main`
(інакше workflow падає), версія програми береться з тегу, тег з дефісом (`v0.2.0-rc1`) — пререліз. Кожна ОС збирається на своєму раннері: на macOS arm64
apphost має бути підписаний, а це вміє лише macOS. PR, що змінює workflow, збирає архіви без релізу — так його і перевіряти.

## Чекліст D-02: перевірка архівів

**Автоматично** (`release.yml`, після Pack, на кожній з трьох ОС; запускається і в PR зі зміною workflow). Архіви
розпаковуються в чисту теку, як це зробить користувач:

- [x] в архіві CLI є `localmind`, `appsettings.json`, `scripts/download-models.sh` (на Linux і macOS — з бітом виконання) і `.ps1`;
- [x] `localmind --help` і `localmind eval --help` завершуються успішно, а `localmind --version` збігається з версією
  збірки (з тегу; у PR — `0.0.0-dev`, далі CLI дописує `+<коміт>`);
- [x] `localmind eval --dataset eval/datasets/demo --mode keyword` без моделі завершується кодом 1 зі зрозумілою
  помилкою про модель (у тексті є підказка `download-models`) — CLI стартує й читає датасет; крок розрахований на
  відсутність моделі, з кешем моделей код стане 0 і його треба буде замінити;
- [x] в архіві App є `LocalMind.App` (`.exe` на Windows) і `appsettings.json`; на Linux і macOS у бінарника є біт
  виконання. Вікно застосунку в CI не запускається.

Модель (~470 МБ) у CI не завантажується: навіть режим `keyword` індексує з ембедінгами, тож `eval` без моделі
метрик не дає (варіант з ним — окрема задача).

**Вручну** перед публікацією релізу (на машині з потрібною ОС, з архіву з GitHub Release):

- [ ] розпакувати, виконати `scripts/download-models.sh` / `.ps1` (SHA-256 збігається);
- [ ] **повний прогін з моделлю:** `localmind eval --dataset eval/datasets/demo` — таблиця метрик по трьох режимах,
  Recall@10 і nDCG@10 для semantic і hybrid не нижчі за ≈ 0,9 (AC-39);
- [ ] запустити застосунок, додати папку, виконати пошук (AC-01, AC-13 з [acceptance-cases.md](acceptance-cases.md));
- [ ] macOS: перший запуск через «Відкрити» (див. «Обмеження»); Windows: SmartScreen попереджає, але запускає.

## Що в архіві

| Архів | Вміст |
|---|---|
| `LocalMind-App-<rid>` | застосунок (`LocalMind.App`), `appsettings.json`, `scripts/download-models.*` |
| `LocalMind-Cli-<rid>` | CLI (`localmind`), `appsettings.json`, `scripts/download-models.*` |

Windows — `.zip`, Linux і macOS — `.tar.gz` (зберігає біт виконання), усередині одна папка з назвою архіву. Застосунок і CLI окремо, бо в кожного свій
`appsettings.json`. Символи налагодження й XML-документацію з архіву прибрано (застосунок ~150 МБ без моделі).

## Модель

Модель (~470 МБ) в архів не входить. Після розпакування — з папки архіву:

```
./scripts/download-models.sh        # Linux / macOS
.\scripts\download-models.ps1       # Windows
```

Скрипт кладе `models/` поруч із програмою і перевіряє SHA-256. Застосунок шукає модель поруч із собою або в теці даних
користувача. CLI бере відносний `ModelPath` від поточної папки, тож запускати його з папки архіву або задати
`LocalMind__Embedding__ModelPath` / `LocalMind__Embedding__TokenizerPath`.

## Обмеження

- macOS: звичайна папка, не `.app`-бандл, без нотаризації — перший запуск через «Відкрити» в контекстному меню
  або `xattr -dr com.apple.quarantine <папка>`.
- Windows: без підпису коду, SmartScreen попередить при першому запуску.
