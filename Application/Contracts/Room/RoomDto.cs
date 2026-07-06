using testBdControllers.Application.Enums;

namespace testBdControllers.Application.Contracts.Room
{
    public record CreateRoomResponse(
        Guid Id,
        Guid RoomCode,
        RoomStatus Status,
        DateTime CreatedAt);
}