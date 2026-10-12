using QuizGamePlatform.Backend.Application.Contracts.Room;
using QuizGamePlatform.Backend.Application.Enums;
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

        public static RoomResponse ToJoinRoomResponse(this RoomPlayerEntity playerEntity)
        {
            return new RoomResponse(
            RoomPlayerLinkId: playerEntity.Id,
            RoomId: playerEntity.RoomId,
            FinishedAt: playerEntity.FinishedAt,
            RoomCode: playerEntity.Room.RoomCode,
            RoomStatus: playerEntity.Room.Status,
            PlayerName: playerEntity.Nickname,
            JoinedAt: playerEntity.JoinedAt,
            IsActive: playerEntity.IsActive
        );
        }

        public static RoomParticipationResponse ToRoomParticipationResponse(this RoomPlayerEntity playerEntity, Guid? hostUserId)
        {
            return new RoomParticipationResponse(
            RoomPlayerLinkId: playerEntity.Id,
            PlayerName: playerEntity.Nickname,
            IsGuest: playerEntity.UserId is null,
            IsHost: playerEntity.UserId is not null && playerEntity.UserId == hostUserId,
            IsActive: playerEntity.IsActive,
            ExitReason: playerEntity.ExitReason,
            Score: playerEntity.Score
        );
        }

        public static LeaveRoomResponse ToLeaveRoomResponse(this RoomPlayerEntity roomPlayerEntity)
        {
            return new LeaveRoomResponse(
            RoomId: roomPlayerEntity.RoomId,
            RoomPlayerLinkId: roomPlayerEntity.Id,
            FinishedAt: roomPlayerEntity.FinishedAt ?? DateTime.UtcNow,
            ExitReason: roomPlayerEntity.ExitReason ?? ExitReason.Other
            );
        }
    }
}