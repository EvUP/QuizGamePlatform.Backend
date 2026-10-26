var builder = DistributedApplication.CreateBuilder(args);

// Контейнеры повторяют docker-compose.yml: те же образы, порты и пароли.
// Тома баз общие с compose (префикс test-project_: имя папки репозитория), поэтому аккаунты и игры сохраняются,
// но запускать надо что-то одно: Aspire или docker compose.

var postgresUser = builder.AddParameter("postgres-user");
var postgresPassword = builder.AddParameter("postgres-password", secret: true);

// тег до WithDataVolume: том создан Postgres 15, а по умолчанию Aspire ставит новее
var postgres = builder.AddPostgres("postgres", postgresUser, postgresPassword)
    .WithImageTag("15-alpine")
    .WithDataVolume("test-project_pgdata")
    .WithHostPort(5450);

var gameDb = postgres.AddDatabase("game-db", databaseName: "diplomDB");

// Порт хоста не закрепляем: 6379 бывает занят локальным Redis, бэк получит адрес от Aspire.
// Том свой: в Redis только кэш и «кто на связи», а хранит Aspire его иначе (RDB), чем compose (AOF).
// Без TLS, как в compose: иначе бэку нужен доверенный сертификат разработчика.
#pragma warning disable ASPIRECERTIFICATES001
var redis = builder.AddRedis("redis")
    .WithImageTag("7-alpine")
    .WithoutHttpsCertificate()
    .WithDataVolume();
#pragma warning restore ASPIRECERTIFICATES001

// вся почта для тестов: письма бэка и Keycloak, смотреть на http://localhost:8025
var mailpit = builder.AddMailPit("mailpit", httpPort: 8025, smtpPort: 1025)
    .WithImageTag("v1.21");

var keycloakDbUser = builder.AddParameter("keycloak-db-user");
var keycloakDbPassword = builder.AddParameter("keycloak-db-password", secret: true);

var keycloakPostgres = builder.AddPostgres("keycloak-db", keycloakDbUser, keycloakDbPassword)
    .WithImageTag("15-alpine")
    .WithDataVolume("test-project_keycloakdata");

var keycloakDb = keycloakPostgres.AddDatabase("keycloak-database", databaseName: "keycloak");

var keycloakAdmin = builder.AddParameter("keycloak-admin");
var keycloakAdminPassword = builder.AddParameter("keycloak-admin-password", secret: true);

// версия как в compose: более новый Keycloak обновит схему базы, и compose с 26.0 на ней уже не поднимется.
// Пользователи и realm живут в своём Postgres (как в compose), а не во встроенной базе Keycloak.
// Realm импортируется только в пустую базу; адреса фронта и Swagger завязаны на порт 8080.
// Без сертификата: Aspire по умолчанию включает HTTPS, а бэк, фронт и выданные токены ждут http://localhost:8080.
#pragma warning disable ASPIRECERTIFICATES001
var keycloak = builder.AddKeycloak("keycloak", 8080, keycloakAdmin, keycloakAdminPassword)
    .WithImageTag("26.0")
    .WithoutHttpsCertificate()
    .WithRealmImport("../keycloak")
    .WithEnvironment("KC_DB", "postgres")
    .WithEnvironment("KC_DB_URL", "jdbc:postgresql://keycloak-db:5432/keycloak")
    .WithEnvironment("KC_DB_USERNAME", keycloakDbUser)
    .WithEnvironment("KC_DB_PASSWORD", keycloakDbPassword)
    .WaitFor(keycloakDb);
#pragma warning restore ASPIRECERTIFICATES001

// имена подключений те, что бэк читает из ConnectionStrings в appsettings.json
var backend = builder.AddProject<Projects.QuizGamePlatform_Backend>("backend")
    .WithReference(gameDb, connectionName: "DefaultConnection")
    .WithReference(redis, connectionName: "Redis")
    .WaitFor(gameDb)
    .WaitFor(redis)
    .WaitFor(keycloak)
    .WaitFor(mailpit)
    .WithHttpHealthCheck("/health");

// профиль https слушает оба адреса фронта: https://localhost:7200 и http://localhost:5200
builder.AddProject<Projects.QuizGamePlatform_Web>("frontend", launchProfileName: "https")
    .WaitFor(backend);

builder.Build().Run();
