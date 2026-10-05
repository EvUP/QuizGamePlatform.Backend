using Microsoft.AspNetCore.Mvc;
using QuizGamePlatform.Backend.Api.Swagger;
using QuizGamePlatform.Backend.Application.Abstractions;
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
        [OptionalAuthorization]
        public async Task<IActionResult> JoinToRoom([FromBody] JoinToRoomRequest roomRequest, CancellationToken ct)
        {
            if (Request.Headers.Authorization.Count > 0 && !currentUser.IsAuthenticated)
            {
                return Unauthorized(new CommonErrorResponse(
                    message: "Токен недействителен или истёк",
                    method: HttpContext.GetMethodWithPath()));
            }

            if (string.IsNullOrWhiteSpace(roomRequest.Username))
            {
                return BadRequest(new CommonErrorResponse
               (
                   message: $"Username is empty",
                   method: HttpContext.GetMethodWithPath()
               ));
            }

            if (roomRequest.Username.Length > 25)
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
                currentUser.RoomPlayerLinkId,
                ct);

            if (nicknameTaken)
            {
                // Участие есть, но подтвердить владение нечем: у гостя это делает токен,
                // а аккаунт с участием пока не связан.
                var message = currentUser.IsRegistered
                    ? $"Участие с ником {roomRequest.Username} в этой комнате уже существует. "
                      + "Повторный вход под аккаунтом будет доступен после привязки участия к аккаунту"
                    : $"Ник {roomRequest.Username} в этой комнате уже занят. "
                      + "Чтобы вернуться в своё участие, войдите с гостевым токеном, выданным при первом входе";

                return Conflict(new CommonErrorResponse(
                    message: message,
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
