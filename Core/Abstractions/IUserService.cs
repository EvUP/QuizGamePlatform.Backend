using testBdControllers.Api.Contracts;

namespace testBdControllers.Core.Abstractions
{
    public interface IUserService
    {
        Task<List<UserDto>> GetAllUsersAsync(int? limit,CancellationToken ct);
        Task<UserDto> AddUserAsync(CreateUserDto dto,CancellationToken ct);
        Task<bool> RemoveUserAsync(string id,CancellationToken ct);
    }
}