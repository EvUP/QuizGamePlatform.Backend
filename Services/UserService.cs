using testBdControllers.Models;
using testBdControllers.Services.Interfaces;

namespace testBdControllers.Services
{
    public class UserService : IUserService
    {
        private readonly List<UserDto> _users = [];

        private int _nextId = 0;

        public Task<List<UserDto>> GetAllUsersAsync()
        {
            return Task.FromResult(_users.ToList());
        }

        public Task<UserDto> AddUserAsync(CreateUserDto dto)
        {
            var newUser = new UserDto
            {
                Id = _nextId.ToString(),
                Name = dto.Name,
                Surname = dto.Surname
            };

            _nextId++;
            _users.Add(newUser);

            return Task.FromResult(newUser);
        }

        public Task<bool> RemoveUserAsync(string id)
        {
            var user = _users.FirstOrDefault(u => u.Id == id);

            if (user == null)
            {
                return Task.FromResult(false);
            }

            _users.Remove(user);
            return Task.FromResult(true);
        }
    }
}