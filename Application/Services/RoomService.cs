using Microsoft.EntityFrameworkCore;
using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.Contracts.Room;
using QuizGamePlatform.Backend.Application.Enums;
using QuizGamePlatform.Backend.Application.Mappers;
using QuizGamePlatform.Backend.Core.Abstractions;
using QuizGamePlatform.Backend.DataAccess;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.Application.Services
{
    public class RoomService(
        IRoomRepository roomRepository,
        IPlayerRepository playerRepository,
        IRoomHelper roomHelper,
        ApplicationDbContext context,
        ILogger<RoomService> logger) : IRoomService
    {
        public async Task<CreateRoomResponse> CreateRoomAsync(CancellationToken ct)
        {
            logger.LogInformation("Creating new Room");

            var roomCode = roomHelper.GenerateRoomCode();
            var newRoom = await roomRepository.CreateRoomAsync(roomCode, ct);

            logger.LogInformation("Room with id: {RoomId} successfully created", newRoom.Id);

            return newRoom.ToCreateRoomResponse();
        }

        public async Task<bool> DeleteExistingRoomByIdAsync(Guid id, CancellationToken ct)
        {
            bool isDeletedRoom = await roomRepository.DeleteExistingRoom(id, ct);

            if (isDeletedRoom)
            {
                logger.LogInformation("Room with {id} sucessfully deleted", id);
            }

            return isDeletedRoom;
        }

        public async Task<List<CreateRoomResponse>> GetAllExistingRoomsAsync(CancellationToken ct)
        {
            logger.LogInformation("Getting all existing rooms...");

            var rooms = await roomRepository.GetAllExistingRoomsAsync(ct);

            if (rooms.Count == 0)
            {
                logger.LogInformation("The list of rooms is empty");
            }

            return rooms.Select(r => r.ToCreateRoomResponse()).ToList();
        }

        public async Task<CreateRoomResponse?> GetRoomByIdAsync(Guid id, CancellationToken ct)
        {
            logger.LogInformation("Looking forward for room by {roomId}", id);

            var room = await roomRepository.GetRoomByIdAsync(id, ct);

            if (room is null)
            {
                logger.LogInformation("Room with id: {id} is not found", id);

                return null;
            }

            return room.ToCreateRoomResponse();
        }

        public async Task<JoinToRoomResponse?> JoinToRoomByRoomCodeAsync(
        string username, string roomCode, CancellationToken ct)
        {
            var room = await roomRepository.GetRoomByRoomCodeAsync(roomCode, ct);

            if (room == null || room.Status != RoomStatus.Waiting)
            {
                logger.LogInformation("Room with roomcode {roomcode} is not found", roomCode);

                return null;
            }

            var player = await playerRepository.GetOrCreatePlayerAsync(username, ct);

            var roomPlayer = await context.RoomParticipations
                .FirstOrDefaultAsync(rp => rp.PlayerId == player.Id && rp.RoomId == room.Id && rp.IsActive, ct);

            if (roomPlayer != null)
            {
                return roomPlayer.ToJoinRoomResponse();
            }

            var link = new RoomPlayerEntity
            {
                Id = Guid.NewGuid(),

                Player = player,
                PlayerId = player.Id,
                JoinedAt = DateTime.UtcNow,
                IsActive = true,
                Score = 0,

                Room = room,
                RoomId = room.Id,
            };

            await context.RoomParticipations.AddAsync(link, ct);
            await context.SaveChangesAsync(ct);

            logger.LogInformation("Player {username} joined room {roomCode}", username, roomCode);

            return link.ToJoinRoomResponse();
        }
    }
}