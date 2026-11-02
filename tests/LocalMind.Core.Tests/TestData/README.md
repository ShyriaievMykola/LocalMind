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