using Microsoft.AspNetCore.Mvc;
using QuizGamePlatform.Backend.Application.Abstractions;

namespace QuizGamePlatform.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RoomsController(IRoomService roomService) : ControllerBase
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
                return NotFound();
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
                return NotFound(new { message = "Room not found" });
            }

            return NoContent();
        }
    }
}