using testBdControllers.Api.Contracts;
using testBdControllers.Core.Abstractions;
using testBdControllers.DataAccess.Entities;

namespace testBdControllers.Application.Services
{
    public class UserService(IUserRepository repository, ILogger<UserService> logger) : IUserService
    {
        public async Task<List<UserDto>> GetAllUsersAsync(int? limit, CancellationToken ct)
        {
            logger.LogInformation("Получаем список пользователей. Limit: {Limit}", limit);

            var entities = await repository.GetAllAsync(limit, ct);

            return entities.Select(el => new UserDto
            {
                Id = el.Id,
                Name = el.Name,
                Surname = el.Surname
            }).ToList();
        }

        public async Task<UserDto> AddUserAsync(CreateUserDto dto, CancellationToken ct)
        {
            logger.LogDebug("Добавляем пользователя: Name={Name}, Surname={Surname}", dto.Name, dto.Surname);

            var newUser = new UserEntity
            {
                Id = Guid.NewGuid().ToString(),
                Name = dto.Name,
                Surname = dto.Surname
            };

            await repository.AddAsync(newUser, ct);

            logger.LogInformation("Пользователь успешно добавлен. Id={Id}", newUser.Id);

            return new UserDto
            {
                Id = newUser.Id,
                Name = newUser.Name,
                Surname = newUser.Surname
            };
        }

        public async Task<bool> RemoveUserAsync(string id, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(id))
            {
                logger.LogWarning("Попытка удалить пользователя с пустым ID");
                return false;
            }

            logger.LogInformation("Удаляем пользователя с ID={Id}", id);

            return await repository.RemoveAsync(id, ct);
        }

        public async Task<UserDto?> UpdateUserAsync(string id, UpdateUserDto userDto, CancellationToken ct)
        {
            logger.LogDebug("Обновляем пользователя. Id={Id}, Name={Name}", id, userDto.Name);

            var entity = await repository.UpdateAsync(id, userDto, ct);

            if (entity == null)
            {
                logger.LogWarning("Не удалось обновить пользователя. ID={Id} не найден", id);

                return null;
            }

            return new UserDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Surname = entity.Surname
            };
        }
    }
}