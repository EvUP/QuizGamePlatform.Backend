using QuizGamePlatform.Backend.Application.Contracts.Room;
using QuizGamePlatform.Backend.Application.Enums;

namespace QuizGamePlatform.Backend.Application.Abstractions
{
    public interface IRoomService
    {
        Task<CreateRoomResponse> CreateRoomAsync(CancellationToken ct);
        Task<CreateRoomResponse?> GetRoomByIdAsync(Guid id, CancellationToken ct);
        Task<List<CreateRoomResponse>> GetAllExistingRoomsAsync(CancellationToken ct);
        Task<bool> DeleteExistingRoomByIdAsync(Guid id, CancellationToken ct);
        Task<RoomResponse?> JoinToRoomByRoomCodeAsync(
        string username, string roomCode, CancellationToken ct);
        Task<LeaveRoomResponse?> LeaveRoom(Guid roomId, Guid playerId, ExitReason exitReason, CancellationToken ct);
        Task<List<RoomResponse>> GetRoomParticipationsById(Guid roomId, CancellationToken ct);
    }
}