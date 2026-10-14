using QuizGamePlatform.Backend.Application.Contracts.Room;
using QuizGamePlatform.Backend.Application.Enums;

namespace QuizGamePlatform.Backend.Application.Abstractions
{
    public interface IRoomService
    {
        Task<CreateRoomResponse?> CreateRoomAsync(CreateRoomRequest? request, CancellationToken ct);
        Task<RoomStateResponse?> GetRoomStateAsync(Guid roomId, CancellationToken ct);
        Task<List<CreateRoomResponse>> GetMyRoomsAsync(CancellationToken ct);
        Task<bool> IsRoomHostAsync(Guid roomId, CancellationToken ct);
        Task<bool> DeleteExistingRoomByIdAsync(Guid id, CancellationToken ct);
        Task<(RoomResponse? Room, JoinRefusal Refusal)> JoinToRoomByRoomCodeAsync(
            string? nickname,
            string roomCode,
            CancellationToken ct);
        Task<LeaveRoomResponse?> LeaveRoom(Guid roomId, ExitReason exitReason, CancellationToken ct);
        Task<bool> DisconnectAsync(Guid roomId, Guid roomPlayerId, CancellationToken ct);
        Task<bool> ReconnectAsync(Guid roomId, Guid roomPlayerId, CancellationToken ct);
        Task<List<GlobalLeaderboardEntry>> GetLeaderboardAsync(bool teamGames, CancellationToken ct);
        Task<LobbyChangeResult> ChangeTeamAsync(Guid roomId, Guid roomPlayerLinkId, int teamNumber, CancellationToken ct);
        Task<LobbyChangeResult> RenameTeamAsync(Guid roomId, int teamNumber, string name, CancellationToken ct);
        Task<LobbyChangeResult> AddTeamAsync(Guid roomId, string? name, CancellationToken ct);
        Task<LobbyChangeResult> DeleteTeamAsync(Guid roomId, int teamNumber, CancellationToken ct);
        Task<LobbyChangeResult> RemovePlayerAsync(Guid roomId, Guid roomPlayerLinkId, CancellationToken ct);
    };
}
