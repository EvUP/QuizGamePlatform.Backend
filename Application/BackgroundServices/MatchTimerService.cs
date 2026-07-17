using QuizGamePlatform.Backend.Application.Abstractions;

namespace QuizGamePlatform.Backend.Application.BackgroundServices
{   
    // TODO: на несколько инстансов — планирование через Redis
    // раз в секунду двигает матчи у которых истёк вопрос
 
    public class MatchTimerService(
        IServiceScopeFactory scopeFactory,
        ILogger<MatchTimerService> logger) : BackgroundService
    {
        private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TickInterval);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();

                    var matchService = scope.ServiceProvider.GetRequiredService<IMatchService>();

                    await matchService.AdvanceExpiredMatchesAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Match timer tick failed");
                }
            }
        }
    }
}
