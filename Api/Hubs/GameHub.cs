using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.Enums;
using QuizGamePlatform.Backend.Application.Services;
using QuizGamePlatform.Backend.Core.Abstractions;

namespace QuizGamePlatform.Backend.Api.Hubs
{
    [Authorize]
    public class GameHub(
        IRoomParticipationRepository roomParticipationRepository,
        IRoomService roomService,
        ConnectionPresence presence,
        DisconnectWatcher disconnectWatcher) : Hub
    {
        public const string Path = "/hubs/game";

        private const string WatchedKey = "watched";

        public static string RoomGroup(Guid roomId) => $"room:{roomId}";

        // подписка на события комнаты только для её участника;
        // после переподключения группы теряются, клиент снова вызывает JoinRoom и перечитывает состояние
        public async Task<bool> JoinRoom(Guid roomId)
        {
            var currentUser = CurrentUserContext.For(Context.User);

            var participant = await roomParticipationRepository.FindParticipant(
                roomId, currentUser.RoomPlayerLinkId, currentUser.KeycloakSubject, Context.ConnectionAborted);

            if (participant is null)
            {
                return false;
            }

            // связь вернулась после обрыва дольше паузы: сервер сам возвращает в игру, пока не истекло окно
            if (!participant.IsActive && participant.ExitReason == ExitReason.Disconnected)
            {
                await roomService.ReconnectAsync(roomId, participant.Id, Context.ConnectionAborted);
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroup(roomId), Context.ConnectionAborted);

            // следим за связью, чтобы закрытая вкладка не держала игрока в игре
            Watched().Add((roomId, participant.Id));
            await presence.AddAsync(participant.Id, Context.ConnectionId);

            return true;
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            foreach (var (roomId, roomPlayerId) in Watched())
            {
                await presence.RemoveAsync(roomPlayerId, Context.ConnectionId);
                disconnectWatcher.Watch(roomId, roomPlayerId);
            }

            await base.OnDisconnectedAsync(exception);
        }

        // комнаты этого соединения, обрыв приходит на тот же инстанс
        private HashSet<(Guid RoomId, Guid RoomPlayerId)> Watched()
        {
            if (Context.Items[WatchedKey] is not HashSet<(Guid, Guid)> watched)
            {
                watched = [];
                Context.Items[WatchedKey] = watched;
            }

            return watched;
        }
    }
}
