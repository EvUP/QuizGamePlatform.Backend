using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.Core.Abstractions
{
    public interface IRoomRepository
    {
        /// <summary>
        /// Создать комнату (без сохранения)
        /// </summary>
        Task<RoomEntity> CreateRoomAsync(string roomCode, UserEntity host, CancellationToken ct);
        /// <summary>
        /// Владелец ли пользователь комнаты и сейчас в ней
        /// </summary>
        Task<bool> IsHost(Guid roomId, string keycloakSubject, CancellationToken ct);
        /// <summary>
        /// Найти комнату по ID
        /// </summary>
        Task<RoomEntity?> GetRoomByIdAsync(Guid Id, CancellationToken ct);
        /// <summary>
        /// Найти комнату по коду
        /// </summary>
        Task<RoomEntity?> GetRoomByRoomCodeAsync(string roomCode, CancellationToken ct);
        /// <summary>
        /// Комнаты, где у аккаунта есть участие
        /// </summary>
        Task<List<RoomEntity>> GetRoomsOfUser(string keycloakSubject, CancellationToken ct);
        /// <summary>
        /// Удалить комнату (без сохранения)
        /// </summary>
        void RemoveRoom(RoomEntity room);
        /// <summary>
        /// Состав комнаты меняется: при сохранении растёт её версия и параллельное изменение получит конфликт
        /// </summary>
        void MarkChanged(RoomEntity room);
    };
}