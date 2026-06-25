using testBdControllers.Api.Contracts;
using testBdControllers.DataAccess.Entities;

namespace testBdControllers.Core.Abstractions
{
    public interface IUserRepository
    {
        /// <summary>
        /// Получает список всех пользователей из базы данных.
        /// </summary>
        Task<List<UserEntity>> GetAllAsync(int? limit, CancellationToken ct);

        /// <summary>
        /// Добавляет нового пользователя в базу данных.
        /// </summary>
        Task<UserEntity> AddAsync(UserEntity entity, CancellationToken ct);

        /// <summary>
        /// Удаляет пользователя по идентификатору.
        /// </summary>
        Task<bool> RemoveAsync(string id, CancellationToken ct);
        /// <summary>
        /// Обновляет пользователя.
        /// </summary>
        Task<UserEntity?> UpdateAsync(string id, UpdateUserDto dto, CancellationToken ct);
    }
}