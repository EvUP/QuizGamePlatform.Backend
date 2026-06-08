using testBdControllers.Models;

namespace testBdControllers.Services.Interfaces
{
    public interface IUserService
    {
        Task<List<UserDto>> GetAllUsersAsync();
        Task<UserDto> AddUserAsync(CreateUserDto dto);
        Task<bool> RemoveUserAsync(string id);
    }
}