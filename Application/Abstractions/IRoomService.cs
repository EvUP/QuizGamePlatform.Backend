using testBdControllers.Application.Contracts;
using testBdControllers.Application.Contracts.Room;

namespace testBdControllers.Application.Abstractions
{
    public interface IRoomService
    {
        Task<CreateRoomResponse> CreateRoomAsync(CancellationToken ct);
        Task<CreateRoomResponse> GetRoomByIdAsync(Guid id, CancellationToken ct);
    }
}