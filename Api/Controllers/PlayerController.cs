using Microsoft.AspNetCore.Mvc;
using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.Contracts;
using QuizGamePlatform.Backend.Application.Contracts.Player;
using QuizGamePlatform.Backend.Core.Extensions;

namespace QuizGamePlatform.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PlayerController(IPlayerService playerService) : ControllerBase
    {
        [HttpPost("create")]
        public async Task<IActionResult> CreatePlayer([FromBody] CreatePlayerRequest playerRequest, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(playerRequest.Username))
            {
                return BadRequest(new CommonErrorResponse(
                    message: "Username field is empty",
                    method: HttpContext.GetMethodWithPath()));
            }
            var player = await playerService.CreatePlayer(playerRequest, ct);

            if (player is null)
            {
                return BadRequest(new CommonErrorResponse(
                   message: $"Player {playerRequest.Username} is already exist",
                   method: HttpContext.GetMethodWithPath()));
            }

            return Ok(player);
        }
    }
}