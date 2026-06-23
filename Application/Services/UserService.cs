using testBdControllers.Api.Contracts;
using testBdControllers.Core.Abstractions;
using testBdControllers.DataAccess.Entities;

namespace testBdControllers.Application.Services
{
    public class UserService(IUserRepository repository) : IUserService
    {
        public async Task<List<UserDto>> GetAllUsersAsync(int? limit, CancellationToken ct)
        {
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
            var newUser = new UserEntity
            {
                Id = Guid.NewGuid().ToString(),
                Name = dto.Name,
                Surname = dto.Surname
            };

            await repository.AddAsync(newUser, ct);

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
                return false;
            }

            return await repository.RemoveAsync(id, ct);
        }
    }
}