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
        /// Владелец ли пользователь комнаты, даже если сам из неё вышел
        /// </summary>
        Task<bool> IsOwner(Guid roomId, string keycloakSubject, CancellationToken ct);
        /// <summary>
        /// Следующая игра серии после этой комнаты, если ведущий нажал «Сыграть ещё»
        /// </summary>
        Task<RoomEntity?> GetNextRoomAsync(Guid roomId, CancellationToken ct);
        /// <summary>
        /// Последняя игра серии (с наибольшим номером)
        /// </summary>
        Task<RoomEntity?> GetLatestInSeriesAsync(Guid seriesId, CancellationToken ct);
        /// <summary>
        /// Выполнить в транзакции; work перезапускаемый: сначала ForgetChanges и перечитать данные
        /// </summary>
        Task InTransactionAsync(Func<CancellationToken, Task> work, CancellationToken ct);
        /// <summary>
        /// Найти комнату по ID
        /// </summary>
        Task<RoomEntity?> GetRoomByIdAsync(Guid Id, CancellationToken ct);
        /// <summary>
        /// Найти комнату по коду
        /// </summary>
        Task<RoomEntity?> GetRoomByRoomCodeAsync(string roomCode, CancellationToken ct);
        /// <summary>
        /// Комнаты, где у аккаунта есть участие, с владельцем, командами и его участием (других участников нет)
        /// </summary>
        Task<List<RoomEntity>> GetRoomsOfUser(string keycloakSubject, CancellationToken ct);
        /// <summary>
        /// Команды комнаты по номеру
        /// </summary>
        Task<List<RoomTeamEntity>> GetTeams(Guid roomId, CancellationToken ct);
        /// <summary>
        /// Найти команду с её комнатой по коду из ссылки
        /// </summary>
        Task<RoomTeamEntity?> GetTeamByJoinCodeAsync(string joinCode, CancellationToken ct);
        /// <summary>
        /// Комната с владельцем по коду из ссылки, команда если код командный
        /// </summary>
        Task<(RoomEntity Room, RoomTeamEntity? Team)?> FindInviteAsync(string code, CancellationToken ct);

        Task<bool> IsCodeTakenAsync(string code, CancellationToken ct);
        /// <summary>
        /// Добавить команду (без сохранения)
        /// </summary>
        void AddTeam(RoomTeamEntity team);
        /// <summary>
        /// Удалить команду (без сохранения)
        /// </summary>
        void RemoveTeam(RoomTeamEntity team);
        /// <summary>
        /// Забыть несохранённые изменения, чтобы повторить действие с чистого листа
        /// </summary>
        void ForgetChanges();
        /// <summary>
        /// Удалить комнату (без сохранения)
        /// </summary>
        void RemoveRoom(RoomEntity room);
        /// <summary>
        /// Состав комнаты меняется: при сохранении растёт её версия и параллельное изменение получит конфликт
        /// </summary>
        void MarkChanged(RoomEntity room);
    }
}