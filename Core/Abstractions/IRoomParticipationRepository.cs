using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.Core.Abstractions
{
    public interface IRoomParticipationRepository
    {
        Task<RoomPlayerEntity?> GetRoomPlayerById(Guid roomId, Guid playerId, CancellationToken ct);
        Task<RoomPlayerEntity> CreateRoomPlayer(PlayerEntity player, RoomEntity room, CancellationToken ct);
        Task<List<RoomPlayerEntity>> GetParticipationsByRoomId(Guid roomId, CancellationToken ct);
    }
}