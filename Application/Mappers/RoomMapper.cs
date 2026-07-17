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
            RoomCode: playerEntity.Room.RoomCode,
            RoomStatus: playerEntity.Room.Status,
            PlayerId: playerEntity.PlayerId,
            PlayerName: playerEntity.Player.UserName,
            JoinedAt: playerEntity.JoinedAt,
            IsActive: playerEntity.IsActive
        );
        }

        public static RoomParticipationResponse ToRoomParticipationResponse(this RoomPlayerEntity playerEntity)
        {
            return new RoomParticipationResponse(
            RoomId: playerEntity.RoomId,
            FinishedAt: playerEntity.FinishedAt,
            ExitReason: playerEntity.ExitReason,
            PlayerId: playerEntity.PlayerId,
            PlayerName: playerEntity.Player.UserName,
            JoinedAt: playerEntity.JoinedAt,
            IsActive: playerEntity.IsActive,
            Score: playerEntity.Score
        );
        }

        public static LeaveRoomResponse ToLeaveRoomResponse(this RoomPlayerEntity roomPlayerEntity)
        {
            return new LeaveRoomResponse(
            RoomId: roomPlayerEntity.RoomId,
            PlayerId: roomPlayerEntity.PlayerId,
            FinishedAt: roomPlayerEntity.FinishedAt ?? DateTime.UtcNow,
            ExitReason: roomPlayerEntity.ExitReason ?? ExitReason.Other
            );
        }
    }
}