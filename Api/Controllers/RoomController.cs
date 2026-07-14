using Microsoft.AspNetCore.Mvc;
using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.Contracts;
using QuizGamePlatform.Backend.Application.Contracts.Room;
using QuizGamePlatform.Backend.Application.Mappers;
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
            var roomPlayer = await roomService.JoinToRoomByRoomCodeAsync(roomRequest.Username, roomRequest.RoomCode, ct);

            if (roomPlayer is null)
            {
                return BadRequest(new CommonErrorResponse
                (
                    Message: $"Комната {roomRequest.RoomCode} не найдена или уже занята",
                    Method: HttpContext.GetMethodWithPath()
                ));
            }

            return Ok(roomPlayer);
        }
    }
}