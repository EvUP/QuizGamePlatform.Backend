namespace QuizGamePlatform.Web.Infrastructure;

/// <summary>Часы сервера: таймеры игры считаем от них, а не от часов устройства</summary>
public sealed class ServerClock
{
    private TimeSpan offset;

    public DateTime UtcNow => DateTime.UtcNow + offset;

    // задержку ответа не учитываем: таймер отстанет на доли секунды
    public void Sync(DateTime serverTimeUtc)
        => offset = DateTime.SpecifyKind(serverTimeUtc, DateTimeKind.Utc) - DateTime.UtcNow;
}
