using testBdControllers.Application.Abstractions;
using testBdControllers.Application.Contracts.Room;
using testBdControllers.Core.Abstractions;

namespace testBdControllers.Application.Services
{
    public class RoomService(IRoomRepository repository, ILogger<RoomService> logger) : IRoomService
    {
        public Task<CreateRoomResponse> CreateRoomAsync(CancellationToken ct)
        {
            logger.LogInformation("Creating");
            throw new NotImplementedException(); //todo
        }

        public Task<CreateRoomResponse> GetRoomByIdAsync(Guid id, CancellationToken ct)
        {
            throw new NotImplementedException(); //todo
        }
    }
}