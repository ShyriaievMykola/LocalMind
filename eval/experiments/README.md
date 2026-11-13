# Конфіги експериментів

Файл для `localmind eval --dataset <тека> --config <файл>`. Кожен прогін може задати власні нарізку й модель:

```json
{"name": "e3-128", "modes": ["hybrid"], "k": 10,
 "chunking": {"strategy": "fixedSize", "chunkSizeTokens": 128, "overlapPercent": 15},
 "model": "multilingual-e5-base"}
```

| Поле | Значення |
|---|---|
| `chunking.strategy` | `fixedSize` або `paragraph`; не вказано — з конфігурації (`LocalMind:Chunking`) |
| `chunking.chunkSizeTokens` | розмір фрагмента в токенах; не більший за ліміт моделі |
| `chunking.overlapPercent` | перекриття, % від розміру фрагмента (0–50) |
| `model` | назва з каталогу `LocalMind:ModelCatalog` (DR-024); модель має бути завантажена: `scripts/download-models <модель>` |

Усі поля `chunking` необов'язкові, без `chunking` і `model` береться конфігурація за замовчуванням.
Прогони з однаковими нарізкою й моделлю ділять один індекс; для кожної іншої комбінації корпус індексується заново
у власній тимчасовій базі (бібліотека користувача не чіпається), тож вона займає ще стільки ж часу й місця на диску.
Пікова пам'ять — пік усього процесу: у комбінацій, що йдуть пізніше, вона не менша за попередніх.

Приклади:

- [`e2.json`](e2.json) — порівняння чотирьох моделей каталогу при однаковому розмірі фрагмента (120 токенів: у
  `paraphrase-*` ліміт 128);
- [`e3.json`](e3.json) — розмір фрагмента 64/128/256/512, перекриття 0/15/30 % і стратегія `paragraph` на моделі за замовчуванням.

```bash
dotnet run --project src/LocalMind.Cli -c Release -- eval --dataset eval/datasets/demo --config eval/experiments/e3.json
```

У CSV кожен рядок має колонки `model`, `chunking`, `chunk_size_tokens`, `overlap_percent` (фактичні значення корпусу),
а також `documents`, `chunks`, `index_seconds`, `db_bytes` для цього корпусу — за ними `eval/plots` групує E2 і E3.
