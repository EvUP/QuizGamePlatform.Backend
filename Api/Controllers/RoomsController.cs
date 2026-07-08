using Microsoft.AspNetCore.Mvc;

namespace QuizGamePlatform.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RoomsController() : ControllerBase
    {
        [HttpPost]
        public async Task<IActionResult> CreateRoom()
        {
            return Ok(); //todo
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetRoomById(Guid id)
        {
            return Ok(); //todo
        }
    }
}