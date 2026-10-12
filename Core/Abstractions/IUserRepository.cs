using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.Core.Abstractions
{
    public interface IUserRepository
    {
        /// <summary>
        /// Найти пользователя по sub из Keycloak или создать (без сохранения)
        /// </summary>
        Task<UserEntity> GetOrCreateUserAsync(string keycloakSubject, string userName, CancellationToken ct);
        /// <summary>
        /// Занято ли имя аккаунтом
        /// </summary>
        Task<bool> IsUserNameTaken(string userName, CancellationToken ct);
    };
}
