using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using QuizGamePlatform.Backend.Application.Services;
using QuizGamePlatform.Backend.Core.Abstractions;

namespace QuizGamePlatform.Backend.Api.Hubs
{
    [Authorize]
    public class GameHub(IRoomParticipationRepository roomParticipationRepository) : Hub
    {
        public const string Path = "/hubs/game";

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

            await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroup(roomId), Context.ConnectionAborted);

            return true;
        }
    }
}
