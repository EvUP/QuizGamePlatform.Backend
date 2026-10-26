using Microsoft.AspNetCore.Components;
using QuizGamePlatform.Web.Infrastructure;

namespace QuizGamePlatform.Web.Components;

/// <summary>
/// Обратный отсчёт до момента с сервера (UTC) по часам сервера. Тикает сам и перерисовывает только себя.
/// </summary>
public abstract class CountdownBase : ComponentBase, IDisposable
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(250);
    private readonly CancellationTokenSource cts = new();

    [Inject]
    private ServerClock Clock { get; set; } = default!;

    [Parameter, EditorRequired]
    public DateTime EndsAtUtc { get; set; }

    protected double RemainingSeconds
        => Math.Max(0, (DateTime.SpecifyKind(EndsAtUtc, DateTimeKind.Utc) - Clock.UtcNow).TotalSeconds);

    protected int SecondsLeft => (int)Math.Ceiling(RemainingSeconds);

    protected override void OnInitialized() => _ = RunAsync();

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(Tick);

        try
        {
            while (await timer.WaitForNextTickAsync(cts.Token))
            {
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException)
        {
            // компонент убран со страницы
        }
    }

    public void Dispose()
    {
        cts.Cancel();
        cts.Dispose();
    }
}
