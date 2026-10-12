using QuizGamePlatform.Backend.Application.Contracts.Room;
using QuizGamePlatform.Backend.Application.Enums;

namespace QuizGamePlatform.Backend.Application.Abstractions
{
    public interface IRoomService
    {
        Task<CreateRoomResponse> CreateRoomAsync(CancellationToken ct);
        Task<RoomStateResponse?> GetRoomStateAsync(Guid roomId, CancellationToken ct);
        Task<List<CreateRoomResponse>> GetMyRoomsAsync(CancellationToken ct);
        Task<bool> IsRoomHostAsync(Guid roomId, CancellationToken ct);
        Task<bool> DeleteExistingRoomByIdAsync(Guid id, CancellationToken ct);
        Task<(RoomResponse? Room, bool NicknameTaken)> JoinToRoomByRoomCodeAsync(
            string? nickname,
            string roomCode,
            CancellationToken ct);
        Task<LeaveRoomResponse?> LeaveRoom(Guid roomId, ExitReason exitReason, CancellationToken ct);
        Task<List<GlobalLeaderboardEntry>> GetGlobalLeaderboardAsync(CancellationToken ct);
    };
}
