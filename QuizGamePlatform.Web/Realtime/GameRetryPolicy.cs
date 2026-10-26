using Microsoft.AspNetCore.SignalR.Client;

namespace QuizGamePlatform.Web.Realtime;

/// <summary>
/// Переподключение: быстро в первые секунды (сервер держит место 40 с после 5 с без связи),
/// потом раз в 5 с. Через 2 минуты сдаёмся: соединение закрывается, игрок жмёт «переподключиться».
/// </summary>
public sealed class GameRetryPolicy : IRetryPolicy
{
    private static readonly TimeSpan[] FirstDelays =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(3)
    ];

    private static readonly TimeSpan SteadyDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(2);

    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        if (retryContext.ElapsedTime > GiveUpAfter)
        {
            return null;
        }

        return retryContext.PreviousRetryCount < FirstDelays.Length
            ? FirstDelays[retryContext.PreviousRetryCount]
            : SteadyDelay;
    }
}
