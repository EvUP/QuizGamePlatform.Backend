using QuizGamePlatform.Backend.Application.Abstractions;

namespace QuizGamePlatform.Backend.Application.BackgroundServices
{
    // раз в секунду двигает матчи у которых истёк вопрос
    // при нескольких инстансах двойной сдвиг отсекает версия строки матча

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
                    foreach (var matchId in await GetExpiredMatchIdsAsync(stoppingToken))
                    {
                        await AdvanceMatchAsync(matchId, stoppingToken);
                    }
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

        private async Task<List<Guid>> GetExpiredMatchIdsAsync(CancellationToken ct)
        {
            using var scope = scopeFactory.CreateScope();

            return await scope.ServiceProvider.GetRequiredService<IMatchService>().GetExpiredMatchIdsAsync(ct);
        }

        // свой scope на матч, чтобы контекст не копил сущности всего тика и сбой одного матча не ронял остальные
        private async Task AdvanceMatchAsync(Guid matchId, CancellationToken ct)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();

                await scope.ServiceProvider.GetRequiredService<IMatchService>().AdvanceExpiredMatchAsync(matchId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to advance match {MatchId}", matchId);
            }
        }
    }
}
