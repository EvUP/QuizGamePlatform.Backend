using Microsoft.AspNetCore.Mvc;
using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.Contracts;
using QuizGamePlatform.Backend.Application.Contracts.Room;
using QuizGamePlatform.Backend.Core.Extensions;

namespace QuizGamePlatform.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RoomController(IRoomService roomService) : ControllerBase
    {
        [HttpPost]
        public async Task<IActionResult> CreateRoom(CancellationToken ct)
        {
            var room = await roomService.CreateRoomAsync(ct);

            return Ok(room);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetRoomById(Guid id, CancellationToken ct)
        {
            var room = await roomService.GetRoomByIdAsync(id, ct);

            if (room is null)
            {
                return NotFound(new CommonErrorResponse(
                    message: $"Room with {id} is not found",
                    method: HttpContext.GetMethodWithPath()));
            }

            return Ok(room);
        }

        [HttpGet("all")]
        public async Task<IActionResult> GetAllExistingRooms(CancellationToken ct)
        {
            return Ok(await roomService.GetAllExistingRoomsAsync(ct));
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteExistingRoom(Guid id, CancellationToken ct)
        {
            var isDeletedRoom = await roomService.DeleteExistingRoomByIdAsync(id, ct);

            if (!isDeletedRoom)
            {
                return NotFound(new CommonErrorResponse(
                    message: $"Room with {id} is not found",
                    method: HttpContext.GetMethodWithPath()));
            }

            return NoContent();
        }

        [HttpPost("join")]
        public async Task<IActionResult> JoinToRoom([FromBody] JoinToRoomRequest roomRequest, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(roomRequest.Username))
            {
                return BadRequest(new CommonErrorResponse
               (
                   message: $"Username is empty",
                   method: HttpContext.GetMethodWithPath()
               ));
            }

            var roomPlayer = await roomService.JoinToRoomByRoomCodeAsync(roomRequest.Username, roomRequest.RoomCode, ct);

            if (roomPlayer is null)
            {
                return NotFound(new CommonErrorResponse
                (
                    message: $"Room {roomRequest.RoomCode} hasn't found or already busy",
                    method: HttpContext.GetMethodWithPath()
                ));
            }

            //TODO Поле будет назначаться после ожидания в 30сек 
            if (roomPlayer.FinishedAt.HasValue
            && roomPlayer.FinishedAt < DateTime.UtcNow)
            {
                return BadRequest(new CommonErrorResponse(
                    message: $"{roomPlayer.PlayerName} has been left room",
                    method: HttpContext.GetMethodWithPath()
                ));
            }

            return Ok(roomPlayer);
        }

        [HttpPost("leave")]
        public async Task<IActionResult> LeaveRoom([FromBody] LeaveRoomRequest roomRequest, CancellationToken ct)
        {
            var roomPlayer = await roomService.LeaveRoom(roomRequest.RoomId, roomRequest.PlayerId, roomRequest.ExitReason, ct);

            if (roomPlayer is null)
            {
                return NotFound(new CommonErrorResponse
               (
                   message: $"Комната с игроком {roomRequest.PlayerId} не найдена или игрок уже покинул комнату {roomRequest.RoomId}",
                   method: HttpContext.GetMethodWithPath()
               ));
            }

            return Ok(roomPlayer);
        }

        [HttpGet("participations/{roomId}")]
        public async Task<IActionResult> GetRoomParticipationsById(Guid roomId, CancellationToken ct)
        {
            return Ok(await roomService.GetRoomParticipationsById(roomId, ct));
        }

        [HttpGet("participations/all")]
        public async Task<IActionResult> GetAllParticipations(CancellationToken ct)
        {
            return Ok(await roomService.GetAllExistingParticipation(ct));
        }
    }
}