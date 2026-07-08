# QuizGamePlatform.Backend

Учебный скелет ASP.NET Core (.NET 8) + EF Core + PostgreSQL. Слои: `Api` / `Application` / `Core` / `DataAccess`.

Строка подключения — в `appsettings.json` (`ConnectionStrings:DefaultConnection`).

## Требования

- .NET 8 SDK
- Docker
- dotnet-ef: `dotnet tool install --global dotnet-ef`

## Запуск

1. Поднять БД:
   ```bash
   docker compose up -d
   ```

2. Применить миграции:
   ```bash
   dotnet ef database update
   ```

3. Запустить:
   ```bash
   dotnet run
   ```

## Эндпоинты

- Swagger: `https://localhost:53506/swagger` (для https может понадобиться `dotnet dev-certs https --trust`)
- Проверка живости: `GET /health`

## Тесты

```bash
dotnet test
```

## CI

GitHub Actions прогоняет build + test на каждый push и PR в `develop`.
