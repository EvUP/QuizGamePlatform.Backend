using QuizGamePlatform.Backend.Application.Contracts.Room;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.Core.Abstractions
{
    public interface IRoomParticipationRepository
    {
        /// <summary>
        /// Участие текущего пользователя в комнате: гостя по ID участия, зарегистрированного по sub
        /// </summary>
        Task<RoomPlayerEntity?> FindParticipant(Guid roomId, Guid? roomPlayerId, string? keycloakSubject, CancellationToken ct);
        /// <summary>
        /// Участие в комнате, пришедшее из прошлой игры серии по первому участию человека
        /// </summary>
        Task<RoomPlayerEntity?> FindByOriginAsync(Guid roomId, Guid originRoomPlayerId, CancellationToken ct);
        /// <summary>
        /// Активные участники комнаты без ведущего
        /// </summary>
        Task<int> CountActiveNonHostAsync(Guid roomId, Guid? hostUserId, CancellationToken ct);
        Task<bool> IsNicknameTaken(Guid roomId, string nickname, CancellationToken ct);
        Task<int> CountActivePlayers(Guid roomId, CancellationToken ct);
        /// <summary>
        /// Занятые места: активные участники и владелец, даже вышедший
        /// </summary>
        Task<int> CountTakenSlots(Guid roomId, CancellationToken ct);
        Task<RoomPlayerEntity> CreateRoomPlayer(RoomEntity room, string nickname, UserEntity? user, CancellationToken ct);
        Task<List<RoomPlayerEntity>> GetParticipationsByRoomId(Guid roomId, CancellationToken ct);
        /// <summary>
        /// Номера команд, игравших матч: в составе есть активный или вышедший не раньше старта
        /// </summary>
        Task<Dictionary<Guid, HashSet<int>>> GetPlayedTeamNumbersAsync(IReadOnlyCollection<Guid> roomIds, CancellationToken ct);
        /// <summary>
        /// Сумма личных очков аккаунтов по завершённым комнатам, гостей нет; командные игры в своём рейтинге.
        /// Порядок без общих мест: очки, точность, кто набрал раньше
        /// </summary>
        Task<List<GlobalLeaderboardEntry>> GetLeaderboard(bool teamGames, CancellationToken ct);
    }
}
