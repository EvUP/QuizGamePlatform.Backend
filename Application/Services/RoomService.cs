using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.Contracts.Room;
using QuizGamePlatform.Backend.Core.Abstractions;

namespace QuizGamePlatform.Backend.Application.Services
{
    public class RoomService(IRoomRepository repository, ILogger<RoomService> logger) : IRoomService
    {
        public async Task<CreateRoomResponse> CreateRoomAsync(CancellationToken ct)
        {
            logger.LogInformation("Creating new Room");

            var newRoom = await repository.CreateRoomAsync(ct);

            logger.LogInformation("Room with id: {RoomId} successfully created", newRoom.Id);

            return new CreateRoomResponse(
                Id: newRoom.Id,
                RoomCode: newRoom.RoomCode,
                Status: newRoom.Status,
                CreatedAt: newRoom.CreatedAt
            );
        }

        public async Task<bool> DeleteExistingRoomByIdAsync(Guid id, CancellationToken ct)
        {

            bool isDeletedRoom = await repository.DeleteExistingRoom(id, ct);

            if (isDeletedRoom)
            {
                logger.LogInformation("Room with {id} sucessfully deleted", id);
            }

            return isDeletedRoom;
        }

        public async Task<List<CreateRoomResponse>> GetAllExistingRoomsAsync(CancellationToken ct)
        {
            logger.LogInformation("Getting all existing rooms...");

            var rooms = await repository.GetAllExistingRoomsAsync(ct);

            if (rooms.Count == 0)
            {
                logger.LogInformation("The list of rooms is empty");
            }

            return rooms.Select(r => new CreateRoomResponse(
                Id: r.Id,
                RoomCode: r.RoomCode,
                Status: r.Status,
                CreatedAt: r.CreatedAt
            )).ToList();
        }

        public async Task<CreateRoomResponse?> GetRoomByIdAsync(Guid id, CancellationToken ct)
        {
            logger.LogInformation("Looking forward for room by {roomId}", id);

            var room = await repository.GetRoomByIdAsync(id, ct);

            if (room is null)
            {
                logger.LogInformation("Room with id: {id} is not found", id);

                return null;
            }

            return new CreateRoomResponse(
               Id: room.Id,
               RoomCode: room.RoomCode,
               Status: room.Status,
               CreatedAt: room.CreatedAt
           );
        }
    }
}