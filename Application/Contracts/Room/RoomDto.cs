using QuizGamePlatform.Backend.Application.Enums;

namespace QuizGamePlatform.Backend.Application.Contracts.Room
{
    public record CreateRoomResponse(
        Guid Id,
        string RoomCode,
        RoomStatus Status,
        DateTime CreatedAt);

    public record JoinToRoomRequest(string Username, string RoomCode);

    public record RoomResponse(
    Guid RoomPlayerLinkId,   // Это Id самой связи (RoomPlayerEntity.Id)
    Guid RoomId,             // ID комнаты
    string RoomCode,          // Код комнаты (удобно для UI)
    RoomStatus RoomStatus,   // Текущий статус комнаты
    Guid PlayerId,            // ID игрока
    string PlayerName,         // Имя игрока (UserName)
    DateTime JoinedAt,        // Время присоединения
    bool IsActive);           // Активен ли игрок в комнате

    public record LeaveRoomResponse(
        Guid RoomId,
        Guid PlayerId,
        DateTime FinishedAt,
        ExitReason ExitReason
    );

    public record LeaveRoomRequest(
        Guid RoomId,
        Guid PlayerId,
        ExitReason ExitReason
    );
}