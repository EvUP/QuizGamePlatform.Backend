using testBdControllers.Api.Contracts;

namespace testBdControllers.Core.Abstractions
{
    public interface IUserService
    {
        Task<List<UserDto>> GetAllUsersAsync();
        Task<UserDto> AddUserAsync(CreateUserDto dto);
        Task<bool> RemoveUserAsync(string id);
    }
}