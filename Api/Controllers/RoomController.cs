using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuizGamePlatform.Backend.Api.Swagger;
using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.Auth;
using QuizGamePlatform.Backend.Application.Contracts;
using QuizGamePlatform.Backend.Application.Contracts.Room;
using QuizGamePlatform.Backend.Core.Extensions;

namespace QuizGamePlatform.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RoomController(
        IRoomService roomService,
        ICurrentUserContext currentUser,
        IGuestTokenService guestTokenService) : ControllerBase
    {
        [HttpPost]
        [Authorize(Policy = AuthorizationPolicies.Registered)]
        public async Task<IActionResult> CreateRoom(CancellationToken ct)
        {
            var room = await roomService.CreateRoomAsync(ct);

            return Ok(room);
        }

        [HttpGet("{id:guid}")]
        [Authorize]
        public async Task<IActionResult> GetRoomState(Guid id, CancellationToken ct)
        {
            var room = await roomService.GetRoomStateAsync(id, ct);

            if (room is null)
            {
                return NotFound(new CommonErrorResponse(
                    message: $"Комната {id} не найдена или вы в ней не участвуете",
                    method: HttpContext.GetMethodWithPath()));
            }

            return Ok(room);
        }

        [HttpGet("my")]
        [Authorize(Policy = AuthorizationPolicies.Registered)]
        public async Task<IActionResult> GetMyRooms(CancellationToken ct)
        {
            return Ok(await roomService.GetMyRoomsAsync(ct));
        }

        [HttpDelete("{id:guid}")]
        [Authorize(Policy = AuthorizationPolicies.Registered)]
        public async Task<IActionResult> DeleteExistingRoom(Guid id, CancellationToken ct)
        {
            if (!await roomService.IsRoomHostAsync(id, ct))
            {
                return StatusCode(StatusCodes.Status403Forbidden, new CommonErrorResponse(
                    message: "Удалить комнату может только её владелец",
                    method: HttpContext.GetMethodWithPath()));
            }

            var isDeletedRoom = await roomService.DeleteExistingRoomByIdAsync(id, ct);

            if (!isDeletedRoom)
            {
                return Conflict(new CommonErrorResponse(
                    message: "Удалить комнату можно только до начала игры",
                    method: HttpContext.GetMethodWithPath()));
            }

            return NoContent();
        }

        [HttpPost("join")]
        [OptionalAuthorization]
        public async Task<IActionResult> JoinToRoom([FromBody] JoinToRoomRequest roomRequest, CancellationToken ct)
        {
            if (Request.Headers.Authorization.Count > 0 && !currentUser.IsAuthenticated)
            {
                return Unauthorized(new CommonErrorResponse(
                    message: "Токен недействителен или истёк",
                    method: HttpContext.GetMethodWithPath()));
            }

            // ник вводит только гость
            if (!currentUser.IsRegistered && string.IsNullOrWhiteSpace(roomRequest.Username))
            {
                return BadRequest(new CommonErrorResponse
               (
                   message: $"Username is empty",
                   method: HttpContext.GetMethodWithPath()
               ));
            }

            if (!currentUser.IsRegistered && roomRequest.Username!.Trim().Length > 25)
            {
                return BadRequest(new CommonErrorResponse
               (
                   message: "Ник слишком длинный (максимум 25 символов)",
                   method: HttpContext.GetMethodWithPath()
               ));
            }

            var (roomPlayer, nicknameTaken) = await roomService.JoinToRoomByRoomCodeAsync(
                roomRequest.Username,
                roomRequest.RoomCode,
                ct);

            if (nicknameTaken)
            {
                // ник занят только у гостя, своё участие токен не нашёл
                return Conflict(new CommonErrorResponse(
                    message: $"Ник {roomRequest.Username} занят. "
                        + "Если это ваше участие, войдите с гостевым токеном, выданным при первом входе",
                    method: HttpContext.GetMethodWithPath()));
            }

            if (roomPlayer is null)
            {
                return NotFound(new CommonErrorResponse
                (
                    message: $"Room {roomRequest.RoomCode} hasn't found or already busy",
                    method: HttpContext.GetMethodWithPath()
                ));
            }

            if (currentUser.IsRegistered)
            {
                return Ok(new JoinToRoomResponse(roomPlayer, null, null));
            }

            var guestToken = guestTokenService.Issue(
                roomPlayer.RoomPlayerLinkId,
                roomPlayer.RoomId,
                roomPlayer.PlayerName);

            return Ok(new JoinToRoomResponse(
                roomPlayer,
                guestToken.AccessToken,
                guestToken.ExpiresAtUtc));
        }

        [HttpPost("leave")]
        [Authorize]
        public async Task<IActionResult> LeaveRoom([FromBody] LeaveRoomRequest roomRequest, CancellationToken ct)
        {
            var roomPlayer = await roomService.LeaveRoom(roomRequest.RoomId, roomRequest.ExitReason, ct);

            if (roomPlayer is null)
            {
                return NotFound(new CommonErrorResponse
               (
                   message: $"Вы не участник комнаты {roomRequest.RoomId} или уже покинули её",
                   method: HttpContext.GetMethodWithPath()
               ));
            }

            return Ok(roomPlayer);
        }

        [HttpGet("leaderboard")]
        public async Task<IActionResult> GetGlobalLeaderboard(CancellationToken ct)
        {
            return Ok(await roomService.GetGlobalLeaderboardAsync(ct));
        }
    }
}
