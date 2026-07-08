using QuizGamePlatform.Backend.Application.Enums;

namespace QuizGamePlatform.Backend.Application.Contracts.Room
{
    public record CreateRoomResponse(
        Guid Id,
        Guid RoomCode,
        RoomStatus Status,
        DateTime CreatedAt);
}