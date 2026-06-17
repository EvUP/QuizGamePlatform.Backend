using testBdControllers.Api.Contracts;

namespace testBdControllers.Core.Abstractions
{
    public interface IUserService
    {
        Task<List<UserDto>> GetAllUsersAsync(int limit);
        Task<UserDto> AddUserAsync(CreateUserDto dto);
        Task<bool> RemoveUserAsync(string id);
    }
}