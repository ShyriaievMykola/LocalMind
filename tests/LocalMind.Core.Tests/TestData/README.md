# TestData Fixtures

Набір тестових файлів для перевірки парсерів документів (IDocumentParser).

## Plain Text (.txt / .bin)

- `utf8.txt`: Текст в кодуванні UTF-8 без BOM.
- `utf8-bom.txt`: Текст в кодуванні UTF-8 з префіксом BOM (`0xEF, 0xBB, 0xBF`).
- `utf16-le.txt`: Текст в кодуванні UTF-16 Little Endian з BOM (`0xFF, 0xFE`).
- `utf16-be.txt`: Текст в кодуванні UTF-16 Big Endian з BOM (`0xFE, 0xFF`).
- `cyrillic.txt`: Текст з українськими та латинськими символами (UTF-8).
- `crlf.txt`: Текст із закінченнями рядків Windows (`\r\n`).
- `empty.txt`: Порожній файл (0 байт).
- `invalid-utf8.bin`: Бінарний файл із невалідними для UTF-8 байтами.

## Markdown (.md)

- `sample.md`: Заголовки, абзац, вкладений список, код-блок, посилання,
  зображення з alt-текстом і таблиця.
- `front-matter.md`: YAML front matter, заголовок першого рівня та текст
  документа.

## PDF (.pdf)

- `sample.pdf`: Текстовий PDF із кирилицею, латиницею та метаданими Title.
- `multipage.pdf`: Багатосторінковий текстовий PDF для перевірки розділення
  сторінок порожнім рядком.
- `scanned.pdf`: PDF без текстового шару; очікується порожній текст без винятку.
- `encrypted.pdf`: Зашифрований PDF; очікується `DocumentParseException`.
- `corrupted.pdf`: Обрізаний пошкоджений PDF; очікується
  `DocumentParseException`.

## DOCX (.docx)

- `sample.docx`: Заголовок, абзац, список, таблиця, кирилиця та латиниця.
- `corrupted.docx`: Обрізаний пошкоджений DOCX; очікується
  `DocumentParseException`.

Усі fixtures копіюються у вихідну теку тестів через
`CopyToOutputDirectory`. Особисті дані у файлах відсутні. Великі файли
приблизно на 5 МБ не зберігаються у репозиторії — їх потрібно генерувати
безпосередньо в тестах.