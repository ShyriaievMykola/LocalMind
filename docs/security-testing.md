# Ручне тестування сервера за OWASP API Security Top 10 (2023)

Задача SEC-05 (#151). Сервер перевірено вручну `curl`-ом на локальному екземплярі; автоматичні перевірки
загроз лишаються в `ThreatModelCoverageTests` (див. [threat-model.md](threat-model.md)).

- **Що тестували:** `LocalMind.Server` з `main` (стек #136–#140: Docker, CodeQL, ролі й аудит, rate limit, квоти).
- **Дата:** 2026-11-12.
- **Результат:** критичних і високих знахідок немає. П'ять дрібних недоліків (F-1…F-5) виправлено в цьому ж PR
  з тестами; три відомі залишкові ризики вже описані в DR і threat-model (розділ «Залишкові ризики» нижче).

У прикладах: `<token>` — access-токен (`Authorization: Bearer <token>`), `<refresh>` — refresh-токен,
`<id>` — GUID. Справжніх токенів, ключів і паролів у звіті немає.

## Стенд

Сервер запускається в режимі `Production` (як у Docker: Swagger вимкнено, HTTP на локальній адресі), порожня
база й тека бекапів. Ключ JWT генерується на місці, у репозиторії його немає.

```bash
export ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://127.0.0.1:5199
export Jwt__SigningKey="$(openssl rand -base64 48)"
export ConnectionStrings__Server="Data Source=/tmp/sec/server.db" Storage__RootPath=/tmp/sec/backups
export Storage__MaxBackupBytes=1048576                       # ліміт 1 МБ, щоб швидко перевірити 413
export Admin__Email=root@localmind.test Admin__Password="<пароль адміністратора, ≥12 символів>"
dotnet run --project src/LocalMind.Server -c Release --no-launch-profile
```

Далі `B=http://127.0.0.1:5199/api/v1`. Користувачі: `alice`, `bob`, `carol` (звичайні), `root` (адміністратор).
Токени отримано через `POST $B/auth/login`. Rate limit (20 запитів/хв на IP) для `auth`-ендпоінтів враховувався:
де потрібно, перед серією запитів чекали скидання вікна.

---

## API1:2023 — Broken Object Level Authorization

Бекапи адресуються за GUID; доступ лише до власних.

| Перевірка | Команда | Очікувано | Фактично | Висновок |
|---|---|---|---|---|
| Власник завантажує, читає, бачить у списку | `curl -X POST $B/backups -H "Authorization: Bearer <token>" -H 'Content-Type: application/octet-stream' --data-binary @blob.bin`; `GET $B/backups/<id>` | 201; 200 з тими ж байтами | 201; 200, 200 байт | ✅ |
| Чужий бекап: читання | `curl $B/backups/<id> -H "Authorization: Bearer <token bob>"` | 404 (не 403, щоб не підтверджувати існування) | 404 | ✅ |
| Чужий бекап: видалення | `curl -X DELETE $B/backups/<id> -H "Authorization: Bearer <token bob>"` | 404, файл на місці | 404; у Alice бекап лишився | ✅ |
| Список не містить чужого | `curl $B/backups -H "Authorization: Bearer <token bob>"` | `[]` | `[]` | ✅ |
| Неіснуючий GUID vs чужий | `curl -X DELETE $B/backups/00000000-0000-0000-0000-000000000000 …` | та сама відповідь | 404 (збігається з чужим) | ✅ |
| Без токена | `curl $B/backups/<id>` | 401 | 401 | ✅ |
| Обхід шляху в id | `curl "$B/backups/..%2f..%2fappsettings.json" …`, `$B/backups/1` | 404 (маршрут `{id:guid}`) | 404, 404 | ✅ |

**Висновок:** BOLA немає. Власник береться з токена, а не з запиту; чужий об'єкт для користувача «не існує».
Покрито тестами `BackupEndpointsTests` і `ServerThreatTests`.

## API2:2023 — Broken Authentication

| Перевірка | Команда | Очікувано | Фактично | Висновок |
|---|---|---|---|---|
| Без токена / сміття | `curl $B/auth/me`; `-H "Authorization: Bearer abc.def.ghi"` | 401 + `WWW-Authenticate: Bearer` | 401; 401 | ✅ |
| Токен з `alg:none` | підроблений JWT з `role=Admin`, порожній підпис → `curl $B/auth/me -H "Authorization: Bearer <forged>"` | 401 | 401 (також на `/admin/users`) | ✅ |
| Підпис чужим ключем | JWT HS256, `role=Admin`, інший ключ | 401 | 401 | ✅ |
| Підміна payload у справжньому токені | заголовок і підпис Alice, payload з `role=Admin` | 401 | 401 | ✅ |
| Прострочений токен | `exp` у минулому | 401 | 401 | ✅ |
| Неіснуючий користувач vs хибний пароль | `POST $B/auth/login` для `nobody@…` і для `alice@…` з хибним паролем | однакова відповідь | статус і тіло однакові (відрізняється лише `traceId`) | ✅ |
| Час відповіді login | по 4 пари запитів | без помітної різниці | ≈ 0,15 с в обох випадках | ✅ |
| Час реєстрації нового vs зайнятого email | 4 пари `POST $B/auth/register` | відповіді однакові | 201 в обох; 50–67 мс проти 48–54 мс | ⚠ різниця кілька мс, відома (DR-032) |
| Слабкий пароль | `{"password":"short"}` | 400 | 400 «щонайменше 12 символів» | ✅ |
| Зайнятий email при реєстрації | повторна реєстрація `alice@…` | не розкривати факт | 201, як і для нового | ✅ |
| Блокування акаунта | 5 хибних паролів для `carol`, далі правильний пароль | 6-й вхід відхилено, відповідь та сама | 401 з тим самим текстом | ✅ |
| Ротація refresh-токена | `POST $B/auth/refresh` з `<refresh>`, потім ще раз зі старим | новий токен; повтор → 401 і відкликання сесії | новий ≠ старий; повтор старого 401; новий після цього теж 401 | ✅ |
| Сміттєвий refresh | `{"refreshToken":"AAAA"}` | 401 | 401 | ✅ |
| Вихід | `POST $B/auth/logout`, далі refresh | refresh → 401 | 204; 401 | ✅ |
| Спроби у журналі | `GET $B/admin/audit` | `login.failed`, `refresh.reuse_detected`, `logout` | усі присутні, без паролів і токенів | ✅ |
| Rate limit | 25 хибних логінів підряд | 429 після 20 | перші 17 (до цього вже були запити у вікні) — 401, далі 429 + `Retry-After: 60` | ✅ |
| Підміна `X-Forwarded-For` | `-H 'X-Forwarded-For: 203.0.113.77'` після ліміту | ліміт не обходиться | 429 | ✅ |

**Висновок:** автентифікація стійка. Залишковий ризик: access-токен діє до 15 хв після блокування (див. нижче).

## API3:2023 — Broken Object Property Level Authorization

| Перевірка | Команда | Очікувано | Фактично | Висновок |
|---|---|---|---|---|
| Mass assignment при реєстрації | `POST $B/auth/register` з додатковими полями `role`, `roles`, `isAdmin`, `emailConfirmed`, `id`, `lockoutEnabled` | поля ігноруються | 201; у `GET $B/admin/users` користувач — звичайний | ✅ |
| Надлишкові поля у відповідях | `GET $B/auth/me` | лише `id`, `email` | `{"id":…,"email":…}` | ✅ |
| Список користувачів для адміна | `GET $B/admin/users` | без хешів, `SecurityStamp`, токенів | поля: `id`, `email`, `isAdmin`, `isBlocked`, `backupCount`, `backupBytes` | ✅ |
| Аудит | `GET $B/admin/audit?take=100` | без секретів | поля: `id`, `occurredAt`, `action`, `userId`, `actorId`, `ipAddress`, `detail`; паролів і JWT немає | ✅ |
| Неправильні типи JSON | `{"email":["a"],"password":{"x":1}}`; `null`; не JSON; `text/plain` | 400/401/415, без 500 | 400; 401; 400; 415 | ✅ |

**Висновок:** DTO явні, зайві поля не зв'язуються. Роль змінити через API неможливо.

## API4:2023 — Unrestricted Resource Consumption

(у стенді `Storage__MaxBackupBytes=1048576`)

| Перевірка | Команда | Очікувано | Фактично | Висновок |
|---|---|---|---|---|
| Бекап 2 МБ з `Content-Length` | `--data-binary @big2m.bin` | 413 | 413 | ✅ |
| 1 МБ + 1 байт | `--data-binary @big1m1.bin` | 413 | 413 | ✅ |
| 2 МБ без `Content-Length` | `-H 'Transfer-Encoding: chunked'` | 413 під час запису | 413 | ✅ |
| Рівно 1 МБ | `--data-binary @big1m.bin` | 201 | 201 | ✅ |
| Недописані файли після відмов | `ls backups/` | немає сиріт | лише файли успішних бекапів | ✅ |
| Квота кількості | 25 завантажень поспіль | 20 успішних, далі 409 | 201 ×17 (+3 раніше) → 409 ×8, у списку 20, на диску 20 файлів | ✅ |
| Порожнє тіло, 1 байт | `--data-binary ''`; `--data-binary 'x'` | відмова | **201 — бекап на 0 і 1 байт**; конверт менший за 49 байт відновити неможливо | ❌ F-3, виправлено |
| JSON 10 МБ у `login` | `--data-binary @bigjson.txt` | швидка відмова | 401 за 0,33 с | ✅ |
| JSON 40 МБ | те саме | 413 | 413 за 4 мс | ✅ |
| Пароль 10 МБ у реєстрації | `POST $B/auth/register` | 400 | 400 за 0,30 с | ✅ |
| Завеликі заголовки | `-H "X-Big: <40 КБ>"`, `Authorization: Bearer <60 КБ>` | 431 | 431, 431 | ✅ |
| Пагінація адміна | `GET $B/admin/users?take=999999&skip=-5` | обмежено | повернуто без помилки, `take` обрізається до 500 | ✅ |
| Rate limit | див. API2 | 429 | 429 + `Retry-After` | ✅ |

**Висновок:** розмір, кількість і частота обмежені. Знахідка F-3 (бекапи менші за мінімальний конверт) виправлена.

## API5:2023 — Broken Function Level Authorization

| Перевірка | Команда | Очікувано | Фактично | Висновок |
|---|---|---|---|---|
| Звичайний користувач на адмін-ендпоінтах | `GET $B/admin/users`, `GET $B/admin/audit`, `POST $B/admin/users/<id>/block`, `…/unblock` з `<token alice>` | 403 | 403 ×4 | ✅ |
| Без токена | `curl $B/admin/users` | 401 | 401 | ✅ |
| Підроблена роль у токені | див. API2 | 401 | 401 | ✅ |
| Обхід через заголовки | `-H "X-HTTP-Method-Override: POST" -H "X-Original-URL: /api/v1/admin/users"` | 403 | 403 | ✅ |
| Варіанти шляху | `ADMIN/users`, `admin/users/`, `%61dmin/users`, `admin//users` | 403 або 404, не 200 | 403, 403, 403, 404 | ✅ |
| Адмін блокує себе | `POST $B/admin/users/<свій id>/block` | 409 | 409 | ✅ |
| Невідомий користувач | `POST $B/admin/users/nope/block` | 404 | 404 | ✅ |
| Фільтр аудиту з SQL-ін'єкцією | `GET "$B/admin/audit?userId=' OR 1=1--"` | параметризовано | 200, нічого зайвого | ✅ |
| Блокування діє | `POST …/block` для `bob`, далі його refresh і login | refresh і login → 401 | 401, 401 (однакова відповідь) | ✅ |
| Розблокування | `POST …/unblock` | 204 | 204 | ✅ |

**Висновок:** розмежування за роллю працює; адмін-політика перевіряє і роль, і стан користувача в БД.

## API6:2023 — Unrestricted Access to Sensitive Business Flows

Чутливі потоки: реєстрація, вхід, завантаження бекапів.

| Перевірка | Результат | Висновок |
|---|---|---|
| Масова реєстрація | обмежена rate limit (20/хв на IP). CAPTCHA й підтвердження email відсутні — свідомо (DR-032, R-09) | ⚠ залишковий ризик |
| Перебір акаунтів | відповіді login і register не розкривають існування email (див. API2) | ✅ |
| Підбір пароля | lockout 5 спроб + rate limit | ✅ |
| Заповнення диска | квоти (кількість, обсяг), максимальний розмір; паралельні завантаження серіалізуються `UserUploadGate` | ✅ |
| Навмисне блокування чужого акаунта | можливе тимчасово (15 хв), прийнято як R-04 | ⚠ відомо |

## API7:2023 — Server Side Request Forgery

**Не застосовно.** Сервер не робить вихідних HTTP-запитів і не приймає URL від клієнта:
у `src/LocalMind.Server` немає `HttpClient`, `WebRequest`, `Process.Start` (перевірено пошуком по коду).
Єдині зовнішні дані — тіло бекапу (непрозорі байти) і JSON з email і паролем.

## API8:2023 — Security Misconfiguration

| Перевірка | Команда | Очікувано | Фактично (до виправлень) | Висновок |
|---|---|---|---|---|
| Swagger/OpenAPI у Production | `curl /openapi/v1.json`, `/swagger`, `/swagger/index.html` | 404 | 404 | ✅ |
| Службові шляхи | `/.env`, `/appsettings.json`, `/metrics`, `/actuator`, `/debug` | 404 | 404 | ✅ |
| Зайві методи | `TRACE`, `OPTIONS`, `PUT`, `PATCH`, `DELETE` на `/backups` і `/health` | 405 | 405 | ✅ |
| CORS | `OPTIONS /auth/login` з `Origin: https://evil.example` | без `Access-Control-*` | 405, заголовків CORS немає | ✅ |
| Помилки без внутрішніх деталей | `GET /backups/xyz`; журнал | `ProblemDetails` без стека | так; у журналі 0 винятків | ✅ |
| Заголовок `Server` | `curl -i $B/health` | не розкривати | **`Server: Kestrel`** | ❌ F-2, виправлено |
| `X-Content-Type-Options`, `Cache-Control` | `curl -i` на login і download | `nosniff`; `no-store` для токенів | **немає** на login (токени!), download, помилках | ❌ F-1, виправлено |
| Шум у журналі | `wc -l server.log` після ~150 запитів | помірний | **3098 рядків: кожен SQL-запит EF на рівні Information** | ❌ F-4, виправлено (параметри, зокрема паролі, у них маскуються `'?'`) |
| Секрети в журналі | пошук паролів, `eyJ`, refresh, ключа, email у `server.log` | 0 збігів | 0 | ✅ |
| Host header | `-H 'Host: evil.example'` | відмова при заданому `AllowedHosts` | 200, бо за замовчуванням `AllowedHosts: *` | ⚠ у Docker задається `LOCALMIND_ALLOWED_HOSTS` (DR-033) |
| HSTS / переспрямування HTTPS | — | HSTS поза Development | не перевірено вручну: стенд слухає HTTP; ввімкнено в коді (`UseHsts`), TLS завершує reverse proxy (DR-033) | ⚠ не перевірено наживо |

Після виправлень (`curl -i`, без автотеста для `Server` — див. F-2): `Cache-Control: no-store` і `X-Content-Type-Options: nosniff` на login, download,
health; заголовка `Server` немає; у журналі 16 рядків за сеанс.

## API9:2023 — Improper Inventory Management

| Перевірка | Результат | Висновок |
|---|---|---|
| Єдина версія API | усе під `/api/v1`; `/api/v2/health`, `/api/health`, `/health` — 404 | ✅ |
| Невстановлені/забуті ендпоінти | перебір типових шляхів (див. API8) — лише задокументовані | ✅ |
| Опис API | OpenAPI/Swagger доступні лише в Development; адмін-ендпоінти описані в README | ✅ |
| Адмін-ендпоінти | є лише `/api/v1/admin/{users,audit}` і блокування/розблокування; інших немає | ✅ |

## API10:2023 — Unsafe Consumption of APIs

**Не застосовно** в частині сторонніх API: сервер не споживає зовнішніх сервісів. Дані, що надходять від клієнта,
обробляються безпечно: тіло бекапу — непрозорий шифротекст, який лише зберігається й віддається назад (без
розбору, десеріалізації чи виконання); ім'я файлу береться з GUID (`FileSystemBlobStore.ResolvePath` перевіряє,
що шлях не виходить за теку сховища); SQL — лише параметризований EF (два виклики `ExecuteSqlAsync` у
`RefreshTokenCleanupService` мають лише параметри).

---

## Знахідки

| № | Пункт | Опис | Серйозність | Стан |
|---|---|---|---|---|
| F-1 | API8 | Відповіді з токенами й шифротекстом без `Cache-Control: no-store` і `X-Content-Type-Options: nosniff` | низька | виправлено: `Hosting/SecurityHeaders.cs`, `OwaspHardeningTests` |
| F-2 | API8 | `Server: Kestrel` розкриває веб-сервер | інформаційна | виправлено: `AddServerHeader = false`. Перевірено вручну `curl`-ом на Kestrel; автотеста немає, бо тестовий сервер (TestServer) заголовок `Server` не додає взагалі |
| F-3 | API4 | Бекап менший за мінімальний конверт (заголовок + тег = `EncryptedEnvelope.HeaderSize + TagSize` = 49 байт) приймався: 0 і 1 байт давали 201, займали слот квоти й не відновлювались | низька | виправлено: 400 для менших за 49 байт, `Upload_SmallerThanMinimalEnvelope_Returns400AndStoresNothing`, `Upload_MinimalEnvelopeSize_IsAccepted` |
| F-4 | API8 | Журнал у Production засмічений SQL-запитами EF (параметри маскуються) | інформаційна | виправлено: `Microsoft.EntityFrameworkCore: Warning` у `appsettings.json`, повідомлення про міграції лишено (`…Migrations: Information`) |
| F-5 | API3 | Некоректний email: повідомлення «Некоректний email.» повторювалося двічі (перевірки email і логіна) | косметична | виправлено: `Distinct()`, `Register_InvalidEmail_ReportsEachMessageOnce` |

## Залишкові ризики (не виправляли — відомі й прийняті)

| Ризик | Де описано |
|---|---|
| Access-токен діє до 15 хв після блокування користувача чи відкликання сесії (перевірено: `GET /auth/me` і `GET /backups` з токеном заблокованого → 200). Адмін-ендпоінти це враховують і перевіряють БД одразу | DR-018, R-02 |
| Реєстрація без підтвердження email і CAPTCHA; захист — rate limit на IP | DR-032, R-09 |
| Хто знає email, може тимчасово (15 хв) заблокувати вхід у чужий акаунт | R-04 |

Рекомендація для розгортання (не помилка коду): у Production завжди задавати `LOCALMIND_ALLOWED_HOSTS`
і налаштувати довірені проксі, інакше rate limit рахуватиметься по IP проксі (DR-033).

## Як повторити

1. Запустити сервер за розділом «Стенд».
2. Зареєструвати `alice`, `bob`, `carol`: `POST $B/auth/register` з `{"email":"…","password":"…"}` (пароль ≥12 символів,
   не містить локальної частини email).
3. Виконати команди з таблиць. Для JWT-підробок (API2) достатньо Python з `hmac`: зібрати заголовок і payload,
   підписати довільним ключем або залишити підпис порожнім для `alg: none`.
4. Для перевірок rate limit рахувати, що вікно — 1 хв: між серіями чекати скидання (відповідь `429` з `Retry-After`).
