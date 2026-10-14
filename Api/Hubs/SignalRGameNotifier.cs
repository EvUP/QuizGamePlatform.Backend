using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.Contracts.Match;

namespace QuizGamePlatform.Backend.Api.Hubs
{
    // события копятся в очереди и уходят по порядку из фона:
    // игра и таймер не ждут Redis, даже если он недоступен
    public class SignalRGameNotifier(
        IHubContext<GameHub> hub,
        ILogger<SignalRGameNotifier> logger) : BackgroundService, IGameNotifier
    {
        // при долгом сбое старые события отбрасываются, клиент всё равно перечитает состояние
        private readonly Channel<(Guid RoomId, string Method, object Payload)> _queue =
            Channel.CreateBounded<(Guid, string, object)>(new BoundedChannelOptions(10_000)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });

        public void RoomUpdated(Guid roomId)
            => _queue.Writer.TryWrite((roomId, "RoomUpdated", roomId));

        public void MatchUpdated(MatchStateResponse match)
            => _queue.Writer.TryWrite((match.RoomId, "MatchUpdated", match));

        public void PlayerAnswered(PlayerAnsweredEvent answer)
            => _queue.Writer.TryWrite((answer.RoomId, "PlayerAnswered", answer));

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var (roomId, method, payload) in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await hub.Clients.Group(GameHub.RoomGroup(roomId)).SendAsync(method, payload, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // действие уже сохранено, потерянное событие клиент восполнит запросом
                    logger.LogWarning(ex, "Failed to send {Method} to room {RoomId}", method, roomId);
                }
            }
        }
    }
}
