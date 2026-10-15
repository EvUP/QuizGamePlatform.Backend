namespace QuizGamePlatform.Web.Infrastructure;

/// <summary>
/// Важен только последний запрос: новый отменяет предыдущий (смена вкладки, повторная загрузка),
/// Dispose отменяет при уходе со страницы.
/// </summary>
public sealed class LatestRequest : IDisposable
{
    private CancellationTokenSource? cts;

    public CancellationToken Next()
    {
        Cancel();
        cts = new CancellationTokenSource();
        return cts.Token;
    }

    public void Dispose() => Cancel();

    private void Cancel()
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }
}
