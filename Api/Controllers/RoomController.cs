using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using QuizGamePlatform.Backend.Api.Swagger;
using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.Auth;
using QuizGamePlatform.Backend.Application.Contracts;
using QuizGamePlatform.Backend.Application.Contracts.Room;
using QuizGamePlatform.Backend.Application.Enums;
using QuizGamePlatform.Backend.Core.Extensions;
using QuizGamePlatform.Backend.Core.Helpers;

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
        public async Task<IActionResult> CreateRoom(
            [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreateRoomRequest? request,
            CancellationToken ct)
        {
            if (request?.Teams?.Any(t => MatchHelper.HasControlChars(t.Name)) == true)
            {
                return InvalidChars("Название команды");
            }

            var room = await roomService.CreateRoomAsync(request, ct);

            if (room is null)
            {
                return BadRequest(new CommonErrorResponse(
                    message: $"Команд от {MatchHelper.MinTeams} до {MatchHelper.MaxTeams}, "
                        + $"названия до {MatchHelper.TeamNameMaxLength} символов и без повторов, "
                        + $"ожидаемых участников в сумме не больше {MatchHelper.MaxTeamGamePlayer}",
                    method: HttpContext.GetMethodWithPath()));
            }

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

            // гость с токеном возвращается или переходит по ссылке под своим ником
            if (currentUser.IsGuest && string.IsNullOrWhiteSpace(roomRequest.Username))
            {
                roomRequest = roomRequest with { Username = currentUser.Nickname };
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

            if (!currentUser.IsRegistered && MatchHelper.HasControlChars(roomRequest.Username))
            {
                return InvalidChars("Ник");
            }

            // такого кода не бывает, в базу не ходим
            if (MatchHelper.HasControlChars(roomRequest.RoomCode))
            {
                return JoinRefused(JoinRefusal.NotFound, roomRequest);
            }

            if (!currentUser.IsRegistered && roomRequest.Username!.Trim().Length > 25)
            {
                return BadRequest(new CommonErrorResponse
               (
                   message: "Ник слишком длинный (максимум 25 символов)",
                   method: HttpContext.GetMethodWithPath()
               ));
            }

            var (roomPlayer, refusal) = await roomService.JoinToRoomByRoomCodeAsync(
                roomRequest.Username,
                roomRequest.RoomCode,
                ct);

            if (roomPlayer is null)
            {
                return JoinRefused(refusal, roomRequest);
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
            // остальные причины ставит сервер: убран ведущим, читерство, конец игры
            if (roomRequest.ExitReason is not (ExitReason.Normal or ExitReason.Disconnected))
            {
                return BadRequest(new CommonErrorResponse(
                    message: "Причина выхода: Normal или Disconnected",
                    method: HttpContext.GetMethodWithPath()));
            }

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

        // индивидуальные игры
        [HttpGet("leaderboard")]
        public async Task<IActionResult> GetGlobalLeaderboard(CancellationToken ct)
        {
            return Ok(await roomService.GetLeaderboardAsync(teamGames: false, ct));
        }

        // корпоративные: личные очки аккаунтов из командных игр, сами команды не участвуют
        [HttpGet("leaderboard/team-games")]
        public async Task<IActionResult> GetTeamGamesLeaderboard(CancellationToken ct)
        {
            return Ok(await roomService.GetLeaderboardAsync(teamGames: true, ct));
        }

        // лобби командной комнаты, все видят изменения через RoomUpdated

        [HttpPut("{id:guid}/players/{linkId:guid}/team")]
        [Authorize]
        public async Task<IActionResult> ChangeTeam(Guid id, Guid linkId, [FromBody] ChangeTeamRequest request, CancellationToken ct)
        {
            return LobbyResult(
                await roomService.ChangeTeamAsync(id, linkId, request.TeamNumber, ct),
                "Пересаживает только владелец комнаты, игрок сам переходит по ссылке другой команды",
                "Сменить команду можно только до начала игры");
        }

        [HttpPut("{id:guid}/teams/{number:int}")]
        [Authorize]
        public async Task<IActionResult> RenameTeam(Guid id, int number, [FromBody] TeamNameRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > MatchHelper.TeamNameMaxLength)
            {
                return BadRequest(new CommonErrorResponse(
                    message: $"Название команды от 1 до {MatchHelper.TeamNameMaxLength} символов",
                    method: HttpContext.GetMethodWithPath()));
            }

            if (MatchHelper.HasControlChars(request.Name))
            {
                return InvalidChars("Название команды");
            }

            return LobbyResult(
                await roomService.RenameTeamAsync(id, number, request.Name, ct),
                "Переименовать команду может владелец комнаты или капитан этой команды",
                "Переименовать можно только до начала игры, название должно быть свободно");
        }

        [HttpPost("{id:guid}/teams")]
        [Authorize(Policy = AuthorizationPolicies.Registered)]
        public async Task<IActionResult> AddTeam(
            Guid id,
            [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] TeamNameRequest? request,
            CancellationToken ct)
        {
            if (request?.Name?.Trim().Length > MatchHelper.TeamNameMaxLength)
            {
                return BadRequest(new CommonErrorResponse(
                    message: $"Название команды до {MatchHelper.TeamNameMaxLength} символов",
                    method: HttpContext.GetMethodWithPath()));
            }

            if (MatchHelper.HasControlChars(request?.Name))
            {
                return InvalidChars("Название команды");
            }

            return LobbyResult(
                await roomService.AddTeamAsync(id, request?.Name, ct),
                "Добавить команду может только владелец комнаты",
                $"Добавить команду можно только до начала игры, не больше {MatchHelper.MaxTeams}, название должно быть свободно");
        }

        [HttpDelete("{id:guid}/teams/{number:int}")]
        [Authorize(Policy = AuthorizationPolicies.Registered)]
        public async Task<IActionResult> DeleteTeam(Guid id, int number, CancellationToken ct)
        {
            return LobbyResult(
                await roomService.DeleteTeamAsync(id, number, ct),
                "Удалить команду может только владелец комнаты",
                $"Удалить можно только пустую команду до начала игры, команд должно остаться не меньше {MatchHelper.MinTeams}");
        }

        // в любой комнате, убранный может вернуться сам
        [HttpDelete("{id:guid}/players/{linkId:guid}")]
        [Authorize(Policy = AuthorizationPolicies.Registered)]
        public async Task<IActionResult> RemovePlayer(Guid id, Guid linkId, CancellationToken ct)
        {
            return LobbyResult(
                await roomService.RemovePlayerAsync(id, linkId, ct),
                "Убрать игрока может только владелец комнаты",
                "Убрать игрока можно только до начала игры, себя нельзя");
        }

        private IActionResult InvalidChars(string what) => BadRequest(new CommonErrorResponse(
            message: $"{what} содержит недопустимые символы (перевод строки, табуляция и т.п.)",
            method: HttpContext.GetMethodWithPath()));

        private IActionResult JoinRefused(JoinRefusal refusal, JoinToRoomRequest request)
        {
            var method = HttpContext.GetMethodWithPath();

            return refusal switch
            {
                JoinRefusal.TeamLinkRequired => BadRequest(new CommonErrorResponse(
                    message: "В командную игру входят по ссылке своей команды",
                    method: method)),
                // ник занят только у гостя, своё участие токен не нашёл
                JoinRefusal.NicknameTaken => Conflict(new CommonErrorResponse(
                    message: $"Ник {request.Username} занят. "
                        + "Если это ваше участие, войдите с гостевым токеном, выданным при первом входе",
                    method: method)),
                JoinRefusal.Full => Conflict(new CommonErrorResponse(
                    message: "Комната заполнена, свободных мест нет",
                    method: method)),
                JoinRefusal.Started => Conflict(new CommonErrorResponse(
                    message: "Игра уже началась, новые участники не входят",
                    method: method)),
                JoinRefusal.ReturnExpired => Conflict(new CommonErrorResponse(
                    message: $"Игра идёт, вернуться можно в течение {RoomHelper.ReconnectWindowSeconds} с после выхода",
                    method: method)),
                JoinRefusal.Finished => Conflict(new CommonErrorResponse(
                    message: "Игра уже прошла",
                    method: method)),
                JoinRefusal.Busy => Conflict(new CommonErrorResponse(
                    message: "Комната изменилась одновременно с вами, повторите",
                    method: method)),
                _ => NotFound(new CommonErrorResponse(
                    message: "Комната не найдена, проверьте ссылку",
                    method: method)),
            };
        }

        private IActionResult LobbyResult(LobbyChangeResult result, string forbidden, string conflict) => result switch
        {
            LobbyChangeResult.Done => NoContent(),
            LobbyChangeResult.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new CommonErrorResponse(
                message: forbidden,
                method: HttpContext.GetMethodWithPath())),
            LobbyChangeResult.Conflict => Conflict(new CommonErrorResponse(
                message: conflict,
                method: HttpContext.GetMethodWithPath())),
            LobbyChangeResult.Concurrent => Conflict(new CommonErrorResponse(
                message: "Комната изменилась одновременно с вами, повторите",
                method: HttpContext.GetMethodWithPath())),
            _ => NotFound(new CommonErrorResponse(
                message: "Комната, команда или игрок не найдены, либо вы не в комнате",
                method: HttpContext.GetMethodWithPath())),
        };
    }
}
