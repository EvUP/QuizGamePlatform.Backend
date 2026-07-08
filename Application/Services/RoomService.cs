using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.Contracts.Room;
using QuizGamePlatform.Backend.Core.Abstractions;

namespace QuizGamePlatform.Backend.Application.Services
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