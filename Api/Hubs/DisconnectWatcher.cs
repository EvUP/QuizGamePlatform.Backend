using System.Threading.Channels;
using QuizGamePlatform.Backend.Application.Abstractions;

namespace QuizGamePlatform.Backend.Api.Hubs
{
    // закрыл вкладку или пропала сеть: без связи дольше паузы игрок выходит с причиной Disconnected,
    // короткий обрыв не в счёт
    public class DisconnectWatcher(
        IServiceScopeFactory scopes,
        ConnectionPresence presence,
        TimeProvider timeProvider,
        ILogger<DisconnectWatcher> logger) : BackgroundService
    {
        public static readonly TimeSpan Grace = TimeSpan.FromSeconds(5);

        // пауза у всех одна, поэтому очередь идёт по порядку сроков
        private readonly Channel<(Guid RoomId, Guid RoomPlayerId, DateTimeOffset DueAt)> _queue =
            Channel.CreateUnbounded<(Guid, Guid, DateTimeOffset)>(new UnboundedChannelOptions { SingleReader = true });

        public void Watch(Guid roomId, Guid roomPlayerId)
            => _queue.Writer.TryWrite((roomId, roomPlayerId, timeProvider.GetUtcNow() + Grace));

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var (roomId, roomPlayerId, dueAt) in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                var wait = dueAt - timeProvider.GetUtcNow();

                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, timeProvider, stoppingToken);
                }

                try
                {
                    if (await presence.IsOnlineAsync(roomPlayerId))
                    {
                        continue;
                    }

                    using var scope = scopes.CreateScope();
                    var rooms = scope.ServiceProvider.GetRequiredService<IRoomService>();

                    if (await rooms.DisconnectAsync(roomId, roomPlayerId, stoppingToken))
                    {
                        logger.LogInformation("Participant {RoomPlayerId} lost connection to room {RoomId}", roomPlayerId, roomId);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Failed to handle lost connection of participant {RoomPlayerId}", roomPlayerId);
                }
            }
        }
    }
}
