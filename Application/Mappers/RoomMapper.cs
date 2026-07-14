using QuizGamePlatform.Backend.Application.Contracts.Room;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.Application.Mappers
{
    public static class RoomMapper
    {
        public static CreateRoomResponse ToCreateRoomResponse(this RoomEntity room)
        {
            return new CreateRoomResponse(
                Id: room.Id,
                RoomCode: room.RoomCode,
                Status: room.Status,
                CreatedAt: room.CreatedAt
            );
        }

        public static JoinToRoomResponse ToJoinRoomResponse(this RoomPlayerEntity playerEntity)
        {
            return new JoinToRoomResponse(
            RoomPlayerLinkId: playerEntity.Id,
            RoomId: playerEntity.RoomId,
            RoomCode: playerEntity.Room.RoomCode,
            RoomStatus: playerEntity.Room.Status,
            PlayerId: playerEntity.PlayerId,
            PlayerName: playerEntity.Player.UserName,
            JoinedAt: playerEntity.JoinedAt,
            IsActive: playerEntity.IsActive
        );
        }
    }
}