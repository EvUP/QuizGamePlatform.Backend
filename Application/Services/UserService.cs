using testBdControllers.Api.Contracts;
using testBdControllers.Core.Abstractions;
using testBdControllers.DataAccess.Entities;

namespace testBdControllers.Application.Services
{
    public class UserService(IUserRepository repository) : IUserService
    {
        private readonly IUserRepository _repository = repository;
        public async Task<List<UserDto>> GetAllUsersAsync(int? limit)
        {
            var entities = await _repository.GetAllAsync(limit);

            return entities.Select(el => new UserDto
            {
                Id = el.Id,
                Name = el.Name,
                Surname = el.Surname
            }).ToList();

        }

        public async Task<UserDto> AddUserAsync(CreateUserDto dto)
        {
            var newUser = new UserEntity
            {
                Id = Guid.NewGuid().ToString(),
                Name = dto.Name,
                Surname = dto.Surname
            };

            await _repository.AddAsync(newUser);

            return new UserDto
            {
                Id = newUser.Id,
                Name = newUser.Name,
                Surname = newUser.Surname
            };
        }

        public async Task<bool> RemoveUserAsync(string id)
        {
            if (id == null)
            {
                return false;
            }

            return await _repository.RemoveAsync(id);
        }
    }
}