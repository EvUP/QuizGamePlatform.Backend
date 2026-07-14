using QuizGamePlatform.Backend.Application.Contracts.Room;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.Application.Abstractions
{
    public interface IRoomService
    {
        Task<CreateRoomResponse> CreateRoomAsync(CancellationToken ct);
        Task<CreateRoomResponse?> GetRoomByIdAsync(Guid id, CancellationToken ct);
        Task<List<CreateRoomResponse>> GetAllExistingRoomsAsync(CancellationToken ct);
        Task<bool> DeleteExistingRoomByIdAsync(Guid id, CancellationToken ct);
        Task<JoinToRoomResponse?> JoinToRoomByRoomCodeAsync(
        string username, string roomCode, CancellationToken ct);
    }
}