# Модель загроз LocalMind

Документ описує, що саме ми захищаємо, від кого і чим. Метод — STRIDE для трьох частин системи: клієнт
(десктопний застосунок і CLI), сервер бекапу та сам бекап. Кожна загроза має захист із посиланням на тест або
записана як **залишковий ризик** (розділ 6). Рішення, на які посилаємось, — у [decisions.md](decisions.md).

STRIDE: **S**poofing (підміна особи), **T**ampering (підміна даних), **R**epudiation (відмова від дій),
**I**nformation disclosure (витік), **D**enial of service (відмова в обслуговуванні), **E**levation of privilege
(підвищення прав).

## 1. Що захищаємо

| Актив | Де лежить | Чим цінний |
|---|---|---|
| Документи користувача (нотатки, PDF, DOCX) | Файли на диску користувача | Приватний вміст |
| Локальна база: тексти фрагментів, embeddings, FTS-індекс | SQLite на пристрої користувача | Містить вміст документів у відкритому вигляді |
| Пароль шифрування бекапу | Голова користувача; на мить — пам'ять клієнта | Єдиний ключ до бекапів, відновити його неможливо (DR-014) |
| Бекап (зашифрована база) | Диск сервера | Копія всієї бібліотеки; захищена шифруванням |
| Пароль облікового запису | Голова користувача; на сервері лише хеш | Доступ до чужих бекапів (шифротексту) |
| Токени сесії (access, refresh) | Пам'ять клієнта; Windows — файл під DPAPI | Доступ до API від імені користувача |
| Ключ підпису JWT | Змінна середовища сервера / user-secrets | Можливість випускати токени за будь-якого користувача |
| Модель embeddings (ONNX) | `models/` на диску | Якість і безпека пошуку |
| Доступність сервера й клієнта | — | Користувач може шукати й відновлюватись |

## 2. Межі довіри й учасники

```
 [Користувач] ── файли ──▶ [Клієнт: App/Cli + Core + Search] ── HTTPS ──▶ [Reverse proxy] ──HTTP──▶ [Сервер]
                                   │                                                              │
                             локальна SQLite                                              SQLite + файли .lmbk
                             модель ONNX                                                  (том Docker)
```

- **Межа 1 — файли користувача → клієнт.** Вміст файлів недовіреного походження (завантажені PDF і DOCX).
- **Межа 2 — клієнт → мережа → сервер.** Мережа ворожа; сервер бекапу не є довіреним для вмісту.
- **Межа 3 — reverse proxy → сервер.** Проксі довіряється лише для IP клієнта й схеми (DR-033).
- **Межа 4 — користувач сервера → дані іншого користувача.** Користувачі сервера не довіряють один одному.

Припущення: ОС і обліковий запис користувача на його пристрої не скомпрометовані; адміністратор сервера бачить
метадані, але не вміст бекапів. Зловмисник, який уже виконує код від імені користувача ОС, поза моделлю (див. R-01).

Учасники загроз: сторонній користувач мережі; інший зареєстрований користувач; шкідливий файл у індексованій
теці; цікавий або зламаний сервер (читання диска); викрадений ноутбук чи копія його диска.

## 3. Клієнт

| ID | STRIDE | Загроза | Захист | Тест / статус |
|---|---|---|---|---|
| C-01 | T | Шкідливий або пошкоджений PDF/DOCX валить індексацію | Парсери кидають доменний виняток; файл позначається помилкою, решта індексується. PDF більший за 50 МБ відхиляється | `FolderIndexerTests.Index_BrokenFile_DoesNotStopOthersAndIsMarkedAsError`, `Index_PdfLargerThanLimit_IsMarkedAsErrorAndOthersAreIndexed`, `DocxParserTests.ParseAsync_CorruptedFixture_ThrowsDocumentParseException` |
| C-02 | D | DOCX-бомба (стиснений архів, що розпаковується в гігабайти) або гігантський текстовий файл вичерпують пам'ять | Для PDF є ліміт розміру; для DOCX і текстових файлів ліміту немає | **Залишковий ризик R-05** |
| C-03 | T | Ін'єкція в FTS5-запит через пошуковий рядок | Слова запиту очищаються, беруться в лапки й передаються параметром `$match`, а не конкатенацією | `Fts5KeywordIndexTests.Search_FtsSyntaxInQuery_DoesNotThrowAndKeepsTable` |
| C-04 | I | Читання локальної бази чи індексу іншим процесом або після крадіжки диска | Опирається на права ОС і шифрування диска; база не шифрується | **Залишковий ризик R-01** |
| C-05 | T | Підміна файла моделі ONNX на диску (шкідлива модель, зміна результатів пошуку) | SHA-256 перевіряється скриптом завантаження (`scripts/download-models.*`), під час завантаження в `OnnxEmbedder` — ні | **Залишковий ризик R-06** |
| C-06 | I | Токени сесії краде інший користувач ОС або копія файла | Windows: файл токенів під DPAPI (`CurrentUser` + ентропія), без відкритого тексту. Інші ОС: токени лише в пам'яті | `TokenStoreTests.Dpapi_SurvivesRestartAndFileHasNoPlaintext`; Linux/macOS — **R-07** |
| C-07 | I | Секрети потрапляють у логи й повідомлення помилок | `ToString()` DTO з паролями й токенами їх маскує; помилки API показують лише безпечний текст | `DtoTests.ToString_DoesNotRevealSecrets` |
| C-08 | S | Клієнт надсилає токени чи пароль на підроблений сервер або по відкритому HTTP | Адреса сервера лише `https://` (HTTP — тільки localhost), інакше конфігурація не проходить валідацію | `BackupRegistrationTests.AddLocalMindBackup_InsecureUrl_FailsValidation` |
| C-09 | D | Скинутий refresh-токен «воскрешає» сесію після виходу | Вихід і оновлення токена виконуються під одним замком | `BackupApiClientTests.Logout_DuringRefresh_DoesNotResurrectSession` |
| C-10 | D | Невдале відновлення псує локальну базу | Відновлення: розшифрування з перевіркою тегу, перевірка, що це база LocalMind, `File.Replace` зі збереженням попередньої бази, відкат при збої міграції | `BackupRestoreTests.Restore_WrongPassword_KeepsCurrentDatabase`, `Restore_CorruptedBackup_KeepsCurrentDatabase`, `Restore_ValidButForeignSqlite_KeepsCurrentDatabase`, `BackupClientEndToEndTests.Restore_WrongPassword_KeepsDatabase` |

## 4. Сервер бекапу

### 4.1 Автентифікація й сесії

| ID | STRIDE | Загроза | Захист | Тест / статус |
|---|---|---|---|---|
| S-01 | S | Підбір пароля до облікового запису | Мінімум 12 символів, перевірка на email у паролі, lockout 5 спроб → 15 хв, rate limit 20 запитів/хв на IP для входу й реєстрації (DR-016, DR-032) | `AuthEndpointsTests.Login_AfterFiveFailures_LockedOutWithSameResponse`, `Register_WeakPassword_Returns400`, `AbuseProtectionTests.Auth_TooManyRequestsFromOneIp_Returns429WithRetryAfter`, `Lockout_IsTemporary` |
| S-02 | I | З'ясувати, чи існує email: за відповіддю входу чи реєстрації | Неправильний пароль, невідомий email і заблокований акаунт дають однакову відповідь; для невідомого й заблокованого пароль усе одно хешується; реєстрація зайнятого email відповідає як нова | `AuthEndpointsTests.Login_WrongPasswordAndUnknownUser_ReturnSameResponse`, `Login_LockedOut_StillHashesPassword`, `Register_ExistingEmail_LooksLikeNewRegistrationAndKeepsOldPassword` |
| S-03 | I | Те саме за часом відповіді реєстрації (insert проти відмови) | Не усунено | **Залишковий ризик R-08** |
| S-04 | S | Підроблений або змінений JWT (чужий ключ, підміна payload, `alg: none`, чужий issuer) | Підпис HS256 з обов'язковою перевіркою issuer, audience і строку; ключ ≥ 32 байти base64, без нього сервер не стартує | `JwtAuthTests.Me_ForgedSignature_Returns401`, `Me_TamperedPayload_Returns401`, `Me_UnsignedToken_Returns401`, `Me_WrongIssuerOrAudience_Returns401`, `Me_ExpiredToken_Returns401`, `Startup_WithoutValidSigningKey_Fails` |
| S-05 | I | Причина відмови токена (`WWW-Authenticate`) допомагає атакувачу в Production | Деталі помилки лише в Development | `JwtAuthTests.Me_ExpiredToken_ErrorDetailsOnlyInDevelopment` |
| S-06 | S | Викрадений refresh-токен | У БД лише SHA-256 токена; ротація при кожному оновленні; повторне пред'явлення використаного токена відкликає всю сесію; сесія живе не довше 30 днів | `RefreshTokenTests.Login_ReturnsRefreshToken_StoredOnlyAsHash`, `Refresh_ReusedToken_RevokesWholeSession`, `Refresh_ConcurrentWithSameToken_OnlyOneSucceeds`, `Refresh_AfterSessionMaxLifetime_Returns401EvenIfRotated` |
| S-07 | S | Сесія переживає зміну пароля чи блокування акаунта | Refresh перевіряє security stamp і lockout та відкликає сесію | `RefreshTokenTests.Refresh_AfterPasswordChange_RevokesSession`, `Refresh_LockedOutUser_RevokesSession`, `RevokeAll_RevokesEverySessionOfUser` |
| S-08 | S | Access-токен чинний після відкликання сесії | Живе до 15 хв (DR-018). Токен видаленого користувача відхиляється | `JwtAuthTests.Me_ValidTokenForDeletedUser_Returns401WithBearerChallenge`; решта — **залишковий ризик R-02** |
| S-09 | R | Неможливо довести, хто що робив | Аудит у БД і лог: входи (успішні й невдалі), refresh, повторне використання токена, вихід, завантаження й видалення бекапів, блокування. Паролі, токени й введений текст не пишуться (DR-034) | `AdminAndAuditTests.Audit_RecordsLoginRefreshLogoutAndBackupDelete_WithoutSecrets`, `Audit_BlockAndUnblock_RecordAdminAsActor`; журнал у тій самій БД — **R-09** |
| S-20 | E | Звичайний користувач звертається до адмін-ендпоінтів | Політика `Admin`: роль у токені й актуальний стан у БД (користувач існує, адмін, не заблокований) | `AdminAndAuditTests.AdminEndpoints_WithoutToken_Returns401`, `AdminEndpoints_AsUser_Return403`, `AdminToken_AfterRoleRemoved_IsForbiddenImmediately` |
| S-21 | E | Захоплення адмін-акаунта через реєстрацію заздалегідь відомого email | Адмін створюється лише на старті з `Admin:Email`/`Admin:Password`; вже зареєстрований email не підвищується | `AdminAndAuditTests.Seed_ExistingRegularAccount_IsNotPromoted`, `Seed_IncompleteOrWeakSettings_StopStartup` |
| S-22 | D | Адмін блокує сам себе або заблокований користувач продовжує працювати | Себе заблокувати не можна; блокування змінює SecurityStamp і відкликає сесії; refresh і вхід відхиляються | `AdminAndAuditTests.Block_Self_Returns409_UnknownUser_Returns404`, `Block_PreventsLoginAndRefresh_UnblockRestores`; access-токен діє до 15 хв — **R-02** |

### 4.2 Бекапи на сервері

| ID | STRIDE | Загроза | Захист | Тест / статус |
|---|---|---|---|---|
| S-10 | E | Користувач читає, видаляє чи перелічує чужі бекапи | Кожен запит фільтрується за власником з токена; чужий бекап — 404, як і неіснуючий | `BackupEndpointsTests.OtherUsersBackup_Returns404`, `WithoutToken_Returns401` |
| S-11 | T | Path traversal через ім'я файла | Імена файлів — GUID сервера; шлях додатково перевіряється на вихід за кореневу теку | `BackupEndpointsTests.ResolvePath_Traversal_Throws`, `RootPathWithTrailingSeparator_Works` |
| S-12 | D | Заповнення диска великими або численними завантаженнями | Ліміт 100 МБ на бекап (за `Content-Length` і фактично), квоти 20 бекапів і 1 ГБ на користувача, завантаження одного користувача — по черзі | `BackupEndpointsTests.Upload_TooLarge_Returns413AndStoresNothing`, `Upload_OverCountQuota_Returns409UntilOldDeleted`, `Upload_OverTotalBytesQuota_Returns409AndStoresNothing`, `Upload_ParallelRequests_CannotExceedCountQuota`, `Upload_ParallelRequests_CannotExceedBytesQuota` |
| S-13 | D | Зловмисник навмисно блокує чужий акаунт, вводячи неправильні паролі | Блокування тимчасове (15 хв), ліміт запитів на IP | `AbuseProtectionTests.Lockout_IsTemporary`; повністю не усувається — **R-04** |
| S-14 | D | Перебір і спам з однієї IP | Fixed-window rate limit; для refresh/logout/me окремий м'якший | `AbuseProtectionTests.Auth_TooManyRequestsFromOneIp_Returns429WithRetryAfter`, `RefreshAndMe_HaveSeparateSofterLimit` |
| S-15 | S | Підробка `X-Forwarded-For`, щоб обійти rate limit або зіпсувати чужі лічильники | Заголовки приймаються лише від проксі з `ReverseProxy:KnownProxies/KnownNetworks`, `ForwardLimit = 1` (DR-033) | `ReverseProxyTests.TrustedProxy_RateLimitIsPerForwardedClient`, `UntrustedProxy_SpoofedForwardedForDoesNotBypassRateLimit`, `InvalidProxySettings_StopStartup` |
| S-16 | I | Витік деталей винятків і стеку у відповідях | Єдиний обробник винятків повертає ProblemDetails без деталей, у тому числі в Development | `ServerSkeletonTests.UnhandledException_ReturnsProblemDetailsWithoutStackTrace`, `Auth_MalformedBody_Returns400` |
| S-17 | I | Перехоплення трафіку клієнт ↔ сервер | TLS на reverse proxy, HSTS у Production; клієнт працює лише з HTTPS (C-08). Сервер у контейнері слухає HTTP, тож правильність налаштування проксі лежить на адміністраторі | Конфігурація (`deploy/`, README); перевірка вручну |
| S-18 | I | Секрети в образі Docker чи репозиторії | Ключ JWT тільки зі змінної середовища; `.env` не комітиться; без ключа ні контейнер, ні `docker compose` не стартують | `JwtAuthTests.Startup_WithoutValidSigningKey_Fails`; перевірено `docker compose config` |
| S-19 | I | Диск сервера потрапив до зловмисника: база з email, хешами паролів і refresh-токенів; файли бекапів | Файли бекапів — шифротекст (див. розділ 5); паролі — хеші Identity, refresh — SHA-256. SQLite-база сервера не шифрується | Бекапи — розділ 5; решта — **R-10** |

## 5. Бекап (клієнт → сервер → клієнт)

Модель: сервер не бачить пароля шифрування (zero-knowledge, DR-007, DR-014). Шифрування на клієнті: PBKDF2-HMAC-SHA256
(600 000 ітерацій, сіль 16 байт) і AES-256-GCM; заголовок конверта входить до автентифікованих даних.

| ID | STRIDE | Загроза | Захист | Тест / статус |
|---|---|---|---|---|
| B-01 | I | Сервер або його адміністратор читає вміст бекапу | Шифротекст; ключ виводиться з пароля, який на сервер не передається; у файлі немає відкритого тексту | `AesGcmCipherTests.Encrypt_CiphertextDoesNotContainPlaintext`, `BackupRestoreTests.Snapshot_IsEncryptedAndLeavesNoTempFiles` |
| B-02 | T | Підміна чи псування шифротексту на сервері або в дорозі | AES-GCM: тег перевіряється до запису будь-чого; зміна будь-якого байта заголовка чи даних → відмова; відновлення не змінює поточну базу | `AesGcmCipherTests.Decrypt_OneByteChanged_Throws`, `Parse_AnyTruncation_ThrowsBackupDecryptionException`, `DecryptAsync_WrongPassword_WritesNothing`, `BackupRestoreTests.Restore_CorruptedBackup_KeepsCurrentDatabase` |
| B-03 | T | Сервер підсовує старий, але справжній бекап (rollback) | Тег підтверджує лише, що бекап створений за цим паролем, а не що він найновіший | **Залишковий ризик R-03** |
| B-04 | I | Офлайн-підбір пароля шифрування до викраденого шифротексту | PBKDF2 600 000 ітерацій (мінімум OWASP), унікальні сіль і nonce на кожен бекап; пароль нормалізується в NFC. Складність пароля бекапу не вимагається | `KeyDerivationTests.Parameters_MeetOwaspMinimum`, `AesGcmCipherTests.Encrypt_SameInput_UsesUniqueNonceAndSalt`, `KeyDerivationTests.DeriveKey_NfcAndNfdPassword_ReturnSameKey`; слабкий пароль — **R-11** |
| B-05 | I | Відкритий текст бази з'являється в пам'яті й на диску під час бекапу й відновлення | Тимчасовий знімок (`VACUUM INTO`) видаляється одразу, буфери з відкритим текстом затираються | `BackupRestoreTests.Snapshot_IsEncryptedAndLeavesNoTempFiles`, `Restore_Success_LeavesNoBackupFiles`; решта — **R-12** |
| B-06 | D | Користувач втрачає пароль шифрування | Відновити бекап без нього неможливо (свідомо, DR-014) | Прийнятий ризик **R-13**; пароль потрібно повідомляти користувачу при створенні бекапу |
| B-07 | T | Бекап зроблено новішою чи іншою версією, що ламає базу при відновленні | Міграції після відновлення; при збої попередня база повертається; приймається лише база LocalMind | `BackupRestoreTests.Restore_ValidButForeignSqlite_KeepsCurrentDatabase`, `BackupClientEndToEndTests.BackupThenRestore_RoundTripsThroughServer` |

## 6. Залишкові ризики

Ці ризики ми усвідомлено лишаємо. Для кожного — чому це прийнятно й що могло б його зменшити.

| ID | Ризик | Чому прийнятно | Що зменшило б |
|---|---|---|---|
| R-01 | Локальна база й індекс не шифруються: хто має доступ до облікового запису ОС або диска, читає вміст документів | Модель довіри — власний пристрій; вміст і так лежить у тих самих файлах | Повнодисковe шифрування (BitLocker, FileVault) — порада користувачу; SQLCipher |
| R-02 | Access-токен діє до 15 хв після відкликання сесії чи зміни пароля (DR-018) | JWT перевіряється без звернення до БД; короткий строк обмежує вікно | Зменшити строк або перевіряти security stamp на кожен запит |
| R-03 | Сервер може віддати старіший справжній бекап (rollback); AES-GCM цього не виявляє | Сервер бекапу не підтверджує свіжість; користувач бачить дату створення в списку | Підписаний лічильник версій у метаданих, який клієнт зберігає локально |
| R-04 | Хто знає email, може тимчасово (15 хв) заблокувати вхід у чужий акаунт | Альтернатива — безмежне блокування чи вхід без обмежень гірша; бекапи від цього не страждають | CAPTCHA, лімітування за IP+акаунтом, сповіщення власника |
| R-05 | DOCX-бомба або дуже великий текстовий файл можуть вичерпати пам'ять при індексації | Файли у власних теках користувача, а не з мережі; PDF уже обмежено | Ліміт розміру файла й розпакованого вмісту для DOCX і тексту |
| R-06 | Модель ONNX перед завантаженням у `OnnxEmbedder` не перевіряється за хешем; хеш звіряється лише під час завантаження скриптом | Для підміни потрібен запис у теку `models/` (див. R-01) | Звіряти SHA-256 при першому завантаженні моделі |
| R-07 | Linux і macOS: токени лише в пам'яті, а після перезапуску потрібен новий вхід; на Windows DPAPI захищає від інших користувачів, але не від коду того самого користувача | Keychain і libsecret потребують нативних залежностей (DR-031) | Keychain/libsecret |
| R-08 | Час відповіді реєстрації для зайнятого й нового email різниться | Реєстрація обмежена rate limit; відповідь за змістом однакова | Однаковий час (фіктивне хешування) або підтвердження через лист |
| R-09 | Журнал аудиту лежить у тій самій БД, що й дані: той, хто має доступ до БД, може його змінити | Для цього потрібен доступ до диска сервера (див. R-10); події ще й іде в лог | Відправляти лог у зовнішнє сховище, лише для додавання |
| R-10 | База сервера (SQLite) не шифрується: на диску email-и, хеші паролів і refresh-токенів | Паролі й токени зберігаються лише як хеші; бекапи вже шифротекст | Шифрування тому, SQLCipher |
| R-11 | Складність пароля шифрування бекапу не перевіряється | Користувач сам обирає пароль, і навіть його ми не можемо відновити | Оцінка складності й попередження при створенні бекапу |
| R-12 | Відкритий текст бази лежить у пам'яті клієнта під час шифрування й розшифрування; тимчасовий знімок кілька секунд лежить на диску поруч із базою без безпечного стирання | Для читання потрібен доступ до процесу чи диска, див. R-01 | Шифрувати потоково без проміжного файла; безпечне стирання |
| R-13 | Втрата пароля шифрування означає втрату бекапів | Це властивість zero-knowledge | Ключ відновлення, який користувач зберігає окремо |
| R-14 | Ланцюг постачання: вразливі NuGet-пакети, шкідливі оновлення, помилки в нашому коді, які знаходить статичний аналіз | Версії централізовано в `Directory.Packages.props`; у CI є job `vulnerable packages` (`dotnet list package --vulnerable --include-transitive`, вразливий пакет ламає збірку), Dependabot щотижня, CodeQL для C# на push у main і раз на тиждень. Це не захищає від ще невідомих вразливостей і навмисно шкідливого пакета | Фіксація хешів пакетів (lock-файли), перевірка підписів |
| R-15 | TLS завершується reverse proxy, тож його неправильне налаштування (HTTP назовні, широка довірена мережа) лишає сервер вразливим | Конфігурація задокументована в README; стандартне значення довіряє лише приватній мережі Docker | Звузити `ReverseProxy:KnownNetworks` до адреси проксі |

## 7. Як підтримувати документ

- Нова функція, що працює з мережею, файлами чи секретами, додає рядок у відповідну таблицю з тестом або ризиком.
- Назви тестів у таблицях — реальні; якщо тест перейменовано чи видалено, рядок треба оновити.
- Ризик, який усунуто, переходить з розділу 6 у таблицю захистів з посиланням на тест.
