using testBdControllers.Api.Contracts;
using testBdControllers.Core.Abstractions;

namespace testBdControllers.Application.Services
{
    public class UserService(IUserRepository repository) : IUserService
    {
        private readonly IUserRepository _repository = repository;
        public async Task<List<UserDto>> GetAllUsersAsync(int limit)
        {
            var entities = await _repository.GetAllAsync(limit);

            return [.. entities.Select(e => new UserDto
            {
                Id = e.Id,
                Name = e.Name,
                Surname = e.Surname
            })];
        }

        public async Task<UserDto> AddUserAsync(CreateUserDto dto)
        {
            var newUser = new UserDto
            {
                Id = Guid.NewGuid().ToString(),
                Name = dto.Name,
                Surname = dto.Surname
            };

            await _repository.AddAsync(newUser);

            return newUser;
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