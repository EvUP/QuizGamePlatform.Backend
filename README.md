# QuizGamePlatform

Корпоративный квиз: обычная игра (до 10 человек) и командная (2–5 команд, до 30 игроков).
Ведущий входит через Keycloak, игроки — по ссылке, гостю достаточно ника.

| Часть | Что это | Где |
|---|---|---|
| Бэк | ASP.NET Core (.NET 8), EF Core + PostgreSQL, Redis, SignalR | корень репозитория, слои `Api` / `Application` / `Core` / `DataAccess` |
| Фронт | Blazor WebAssembly (standalone, .NET 8) | `QuizGamePlatform.Web/` |
| Инфраструктура | Postgres, Redis, Keycloak (+ своя БД), Mailpit | `docker-compose.yml` или Aspire |
| Оркестрация и наблюдаемость | .NET Aspire 13.6: запуск всего одной командой, дашборд с логами, трассировками и метриками | `QuizGamePlatform.AppHost/`, `QuizGamePlatform.ServiceDefaults/` |
| Тесты | xUnit | `QuizGamePlatform.Backend.Tests/` |

## Требования

- .NET 8 SDK (`dotnet --list-sdks` должен показать `8.0.x`).
- .NET 10 SDK — для сборки решения целиком (`QuizGamePlatform.Backend.sln`): в нём AppHost Aspire, а он собирается только .NET 10 SDK. Бэк, фронт и тесты остаются на .NET 8 и по отдельности собираются 8-м. Ставится рядом с 8-м установщиком с https://dotnet.microsoft.com/download/dotnet/10.0.
- Docker Desktop (запущен).
- HTTPS-сертификат разработчика — один раз:
  ```bash
  dotnet dev-certs https --trust
  ```
  Бэк слушает и `https://localhost:53506`, без сертификата он не стартует. `--trust` нужен, чтобы браузер не ругался на https-адреса (53506, 7200). Если работать только через http (5200 + 53507), доверять сертификату не обязательно.
- `dotnet-ef` **не нужен**: миграции применяются сами при старте бэка.

## Запуск через Aspire (одной командой)

```bash
dotnet run --project QuizGamePlatform.AppHost
```

Поднимает всё: Postgres, Redis, Keycloak (+ его Postgres), Mailpit, бэк и фронт — в правильном порядке (бэк ждёт базы, Redis и Keycloak; фронт — бэк). В консоли появится ссылка на дашборд вида `https://localhost:17057/login?t=...` — открыть её (токен в ссылке и есть вход). Остановить всё — `Ctrl+C`, контейнеры остановятся вместе с AppHost.

Первый запуск дольше: скачиваются образы и Aspire CLI (его ставить вручную не нужно, `dotnet run` скачает сам). Отдельно можно поставить `curl -sSL https://aspire.dev/install.sh | bash` и запускать `aspire run`.

Адреса те же, что без Aspire: фронт http://localhost:5200, API http://localhost:53507 (Swagger — `/swagger`), Keycloak http://localhost:8080, Mailpit http://localhost:8025, Postgres `localhost:5450`. Redis — на случайном порту с паролем (бэк получает адрес от Aspire, руками туда ходить не нужно).

> ⚠️ **Aspire и `docker compose` — что-то одно.** Aspire подключает те же тома баз (`test-project_pgdata`, `test-project_keycloakdata`), поэтому аккаунты и игры общие (у Redis свой том — там только кэш). Два Postgres на одном томе могут испортить данные, поэтому перед Aspire: `docker compose stop` (или `down` — тома останутся), а перед compose — остановить Aspire. Если папка репозитория называется не `test-project`, у compose будут другие тома, а Aspire создаст свои пустые — realm импортируется, сид зальётся сам.

### Где смотреть логи и что происходит в игре

Всё в дашборде Aspire, слева разделы:

| Раздел | Что там | Когда смотреть |
|---|---|---|
| **Ресурсы** | состояние всех сервисов (Running / ошибка), адреса, кнопки перезапуска | что-то не открывается — сразу видно, кто упал |
| **Консоль** | сырой вывод каждого сервиса: бэка, фронта, Keycloak, Postgres, Redis | ошибка старта, логи Keycloak и Postgres |
| **Структурированные журналы** | логи бэка с полями: фильтр по уровню (Warning, Error) и по тексту — например, id матча или комнаты | ход игры: кто вошёл, кто ответил, когда закрылся вопрос, ушли ли письма |
| **Трассировки** | каждый запрос к API по шагам: какие SQL ушли в базу и сколько заняли | «почему медленно» и «что сделал запрос» |
| **Метрики** | время ответа и число запросов, активные соединения SignalR, команды и пул соединений Postgres, GC и память | нагрузка на защите |

У записи лога в колонке «Трассировка» есть ссылка на запрос, в котором она записана, — по ней видно, какие SQL выполнил этот запрос. Чтобы проследить игру: «Структурированные журналы» → ресурс `backend` → в фильтр вставить id матча (`Match <id> started ...`).

Что **не** попадает в дашборд:
- данные игры (комнаты, ответы, очки) — они в базе: pgAdmin/DBeaver к `localhost:5450`, `postgres` / `secret`, БД `diplomDB`;
- письма — в Mailpit, http://localhost:8025;
- ошибки самого фронта — фронт работает в браузере, смотреть в DevTools (Cmd+Option+I → Console).

Фоновый таймер матчей ходит в базу каждую секунду; такие SQL без входящего запроса в трассировки не пишутся, чтобы не засорять список (`QuizGamePlatform.ServiceDefaults/Extensions.cs`). Логи таймера («auto-advanced …») остаются.

Без Aspire (`dotnet run` бэка) логи только в консоли: телеметрия отправляется, только если Aspire передал адрес дашборда.

## Запуск без Aspire (docker compose)

Все команды — из корня репозитория.

### 1. Инфраструктура

```bash
docker compose up -d --wait
```

`--wait` ждёт, пока сервисы станут healthy. Keycloak стартует 30–60 секунд, первый раз дольше.

| Сервис | Контейнер | Адрес | Доступ |
|---|---|---|---|
| PostgreSQL (данные игры) | `postgres_db` | `localhost:5450`, БД `diplomDB` | `postgres` / `secret` |
| Redis | `redis_db` | `localhost:6379` | — |
| Keycloak | `keycloak_server` | http://localhost:8080 | админка: `admin` / `admin` |
| БД Keycloak | `keycloak_db` | только внутри Docker | — |
| Mailpit (тестовая почта) | `mailpit` | SMTP `localhost:1025`, веб http://localhost:8025 | — |

Проверить, что realm на месте (должен вернуться JSON):

```bash
curl -s http://localhost:8080/realms/quizgame/.well-known/openid-configuration | head -c 200
```

**Realm `quizgame`** Keycloak импортирует при старте из `keycloak/realm-export.json` (`start-dev --import-realm`, папка `./keycloak` смонтирована в `/opt/keycloak/data/import`). Импорт **пропускается, если realm уже есть** в БД Keycloak. Поэтому правки `realm-export.json` на уже поднятый Keycloak не попадают — см. «Частые проблемы».

### 2. Бэк

```bash
dotnet run
```

- Порты (`Properties/launchSettings.json`): http://localhost:53507 и https://localhost:53506, окружение `Development`.
- Swagger: http://localhost:53507/swagger (кнопка Authorize — вход через Keycloak).
- Проверка живости: http://localhost:53507/health.
- При старте сам **доприменяет недостающие миграции** EF (`MigrateAsync`) и **досевает темы и вопросы** из `Seed/quiz_seed.json`: добавляет недостающие (тема — по названию, вопрос — по тексту), существующие не стираются и не перезаписываются. Тема, которую убрали из сида, удаляется, а если по ней уже играли — скрывается из выбора (история игр цела). Новые вопросы — дописать в сид и перезапустить бэк.

Настройки (`appsettings.json`) уже смотрят на контейнеры из шага 1, менять ничего не нужно:

| Ключ | Значение |
|---|---|
| `ConnectionStrings:DefaultConnection` | `Host=localhost;Port=5450;Database=diplomDB;Username=postgres;Password=secret` |
| `ConnectionStrings:Redis` | `localhost:6379` |
| `Keycloak:Authority` | `http://localhost:8080/realms/quizgame` (клиент `quiz-backend`) |
| `Invites:JoinBaseUrl` | `http://localhost:5200` — адрес фронта в ссылках-приглашениях |
| `Invites:SmtpHost` / `SmtpPort` / `From` | `localhost` / `1025` (Mailpit) / `quiz@corp.local` |

CORS в Development открыт для любого origin, фронт на 5200/7200 проходит.

### 3. Фронт

Во втором терминале:

```bash
dotnet run --project QuizGamePlatform.Web
```

Откроется на http://localhost:5200 (профиль `http`). Вариант с https:

```bash
dotnet run --project QuizGamePlatform.Web --launch-profile https   # https://localhost:7200 и http://localhost:5200
```

Конфиг фронта — `QuizGamePlatform.Web/wwwroot/appsettings.json`: API `http://localhost:53507/`, OIDC `http://localhost:8080/realms/quizgame`, клиент `quiz-backend`. Адреса возврата для 5200 и 7200 уже прописаны в `realm-export.json`.

> После пересборки (`dotnet build` всего решения, смена пакетов, правки фронта) **перезапустите `dotnet run` фронта** и обновите страницу с очисткой кэша (Cmd+Shift+R). Иначе внизу страницы появится «Произошла ошибка»: браузер получает старые файлы, которые не проходят проверку целостности (SRI).

### 4. Аккаунт

Аккаунт нужен только ведущему, чтобы создавать комнаты. Гость играет по ссылке без регистрации. Готовых пользователей в realm нет.

**Вариант А — регистрация (проще).** Саморегистрация включена (`registrationAllowed: true`):

1. На фронте нажать «Войти», на форме Keycloak — ссылка «Register».
2. Заполнить логин, **email**, пароль.
3. В realm включено подтверждение почты (`verifyEmail: true`). Письмо придёт в Mailpit: http://localhost:8025, открыть его и перейти по ссылке.

**Вариант Б — через админку.**

1. http://localhost:8080/admin, войти `admin` / `admin`.
2. Слева вверху переключить realm с `master` на **`quizgame`**.
3. Users → Add user: Username, **Email**, включить **Email verified** → Create.
4. Вкладка Credentials → Set password, выключить **Temporary** → Save.

Email у аккаунта обязателен: на него приходят ссылки команд.

## Почта

Вся почта уходит в **Mailpit**, наружу ничего не отправляется. Смотреть на http://localhost:8025.

| Что | Кто отправляет | Когда |
|---|---|---|
| Подтверждение email, сброс пароля | Keycloak (SMTP `mailpit:1025` из `realm-export.json`) | регистрация, «Forgot password?» |
| Ссылки команд | бэк, `SmtpInviteMailer` (SMTP из `Invites`) | создание **командной** комнаты: ведущему приходят ссылки всех команд; добавление команды в лобби: ещё одно письмо со ссылкой новой команды |

- Письмо уходит на email из токена ведущего (claim `email`). Нет email в Keycloak — нет письма.
- Обычная (не командная) комната писем не шлёт: ссылка показывается в лобби.
- Сбой почты игру не ломает: ссылки команд всё равно видны в лобби, а в логе бэка будет предупреждение `Team links of room ... were not sent`.
- Реальная почта: для Keycloak — Realm settings → Email (хост, порт, SSL, логин/пароль). Бэку хватит смены `Invites:SmtpHost` / `SmtpPort` / `From`, только если SMTP-сервер не требует логина и TLS: `SmtpInviteMailer` их пока не поддерживает.

## Проверка, что всё работает

1. http://localhost:5200 → «Войти» → войти аккаунтом из шага 4.
2. «Создать комнату» → «Обычная» → «Создать комнату».
3. В лобби нажать «Скопировать ссылку на комнату».
4. Открыть ссылку во втором браузере или в окне инкогнито, ввести ник и войти гостем.
5. У ведущего — «Начать игру». Оба видят вопрос и таймер.

Командный режим: на шаге 2 выбрать «Командная», после создания проверить письмо со ссылками команд в Mailpit (http://localhost:8025).

## Частые проблемы

| Симптом | Что делать |
|---|---|
| `address already in use` / `port is already allocated` | Занят порт 5450, 6379, 8080, 1025, 8025, 53506/53507 или 5200/7200. Найти процесс: `lsof -i :8080`. |
| `container name "/postgres_db" is already in use` | Имена контейнеров заданы жёстко. Значит, контейнер с таким именем уже есть от другого проекта — остановите его сами или поменяйте имя у себя локально. |
| Бэк или фронт не может войти, Keycloak не отвечает | Keycloak ещё стартует. `docker compose ps` — ждать `healthy` у `keycloak_server`. |
| `Realm does not exist` / 404 на `/realms/quizgame` | Realm не импортировался. `docker compose logs keycloak \| grep -i import`; проверить, что есть файл `keycloak/realm-export.json`. |
| `Invalid parameter: redirect_uri` при входе с фронта | В Keycloak остался старый realm (импорт не перезаписывает существующий). В админке realm `quizgame` → Clients → `quiz-backend` добавить `http://localhost:5200/authentication/*` и `https://localhost:7200/authentication/*` в Valid redirect URIs и Valid post logout redirect URIs, а `http://localhost:5200`, `https://localhost:7200` — в Web origins. Удалять realm или том `keycloakdata` ради переимпорта не нужно: с ним пропадут все пользователи. |
| «Произошла ошибка» внизу страницы фронта | Перезапустить `dotnet run` фронта, обновить с Cmd+Shift+R (см. шаг 3). |
| Браузер ругается на сертификат на 7200 / 53506 | `dotnet dev-certs https --trust`, перезапустить браузер. Или работать через http://localhost:5200. |
| Бэк падает при старте: `Unable to configure HTTPS endpoint` | Нет dev-сертификата: `dotnet dev-certs https`. |
| Ссылки команд ведут не туда | Адрес в ссылках задаёт `Invites:JoinBaseUrl` (сейчас `http://localhost:5200`). Если фронт открыт на 7200, ссылки всё равно ведут на 5200. Это нормально, если фронт запущен (профиль `https` слушает оба порта). |
| Письмо со ссылками не пришло | У аккаунта нет email в Keycloak, или комната не командная, или не запущен `mailpit` (`docker compose ps`). |

## Тесты и CI

```bash
dotnet test
```

GitHub Actions (`.github/workflows/ci.yml`) прогоняет restore + build + test на каждый push и PR в `develop`.
