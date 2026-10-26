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
                    message: $"Проверьте команды: их от {MatchHelper.MinTeams} до {MatchHelper.MaxTeams}, "
                        + $"названия до {MatchHelper.TeamNameMaxLength} символов и не повторяются, "
                        + $"всего не больше {MatchHelper.MaxTeamGamePlayer} игроков",
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
                    message: "Комната не найдена или вы в ней не играете",
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

        // превью приглашения до входа, без токена
        [HttpGet("invite/{code}")]
        public async Task<IActionResult> GetInvite(string code, CancellationToken ct)
        {
            var invite = MatchHelper.HasControlChars(code) ? null : await roomService.GetInviteAsync(code, ct);

            if (invite is null)
            {
                return NotFound(new CommonErrorResponse(
                    message: "Комната не найдена, проверьте ссылку",
                    method: HttpContext.GetMethodWithPath()));
            }

            return Ok(invite);
        }

        [HttpDelete("{id:guid}")]
        [Authorize(Policy = AuthorizationPolicies.Registered)]
        public async Task<IActionResult> DeleteExistingRoom(Guid id, CancellationToken ct)
        {
            if (!await roomService.IsRoomHostAsync(id, ct))
            {
                return StatusCode(StatusCodes.Status403Forbidden, new CommonErrorResponse(
                    message: "Удалить комнату может только ведущий",
                    method: HttpContext.GetMethodWithPath()));
            }

            var method = HttpContext.GetMethodWithPath();

            return await roomService.DeleteExistingRoomByIdAsync(id, ct) switch
            {
                DeleteRoomResult.Deleted => NoContent(),
                DeleteRoomResult.NotWaiting => Conflict(new CommonErrorResponse(
                    message: "Удалить комнату можно только до начала игры",
                    method: method)),
                // во время отмены игры игроки как раз переходят в неё
                DeleteRoomResult.Busy => Conflict(new CommonErrorResponse(
                    message: "Игроки как раз заходят — попробуйте отменить ещё раз",
                    method: method)),
                _ => NotFound(new CommonErrorResponse(
                    message: "Комната не найдена",
                    method: method)),
            };
        }

        // «Сыграть ещё»: та же ссылка и ссылки команд ведут в новую игру, писем нет
        [HttpPost("{id:guid}/rematch")]
        [Authorize(Policy = AuthorizationPolicies.Registered)]
        public async Task<IActionResult> Rematch(Guid id, CancellationToken ct)
        {
            var (room, refusal) = await roomService.RematchAsync(id, ct);

            if (room is not null)
            {
                return Ok(room);
            }

            var method = HttpContext.GetMethodWithPath();

            return refusal switch
            {
                RematchRefusal.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new CommonErrorResponse(
                    message: "Сыграть ещё может только ведущий этой комнаты",
                    method: method)),
                RematchRefusal.NotFinished => Conflict(new CommonErrorResponse(
                    message: "Сыграть ещё можно только после итогов",
                    method: method)),
                RematchRefusal.Busy => Conflict(new CommonErrorResponse(
                    message: "Кто-то успел изменить комнату раньше вас. Попробуйте ещё раз",
                    method: method)),
                _ => NotFound(new CommonErrorResponse(
                    message: "Комната не найдена",
                    method: method)),
            };
        }

        // с итогов в следующую игру серии: аккаунт или гостевой токен этой комнаты
        [HttpPost("{id:guid}/follow")]
        [Authorize]
        public async Task<IActionResult> Follow(Guid id, CancellationToken ct)
        {
            var (roomPlayer, refusal) = await roomService.FollowAsync(id, ct);

            if (roomPlayer is null)
            {
                return FollowRefused(refusal);
            }

            return Ok(JoinResponse(roomPlayer));
        }

        [HttpPost("join")]
        [OptionalAuthorization]
        public async Task<IActionResult> JoinToRoom([FromBody] JoinToRoomRequest roomRequest, CancellationToken ct)
        {
            if (Request.Headers.Authorization.Count > 0 && !currentUser.IsAuthenticated)
            {
                return Unauthorized(new CommonErrorResponse(
                    message: "Вход устарел — войдите ещё раз",
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
                   message: "Введите ник или войдите с аккаунтом",
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

            if (!currentUser.IsRegistered && roomRequest.Username!.Trim().Length > RoomHelper.GuestNicknameMaxLength)
            {
                return BadRequest(new CommonErrorResponse
               (
                   message: $"Ник длинноват: максимум {RoomHelper.GuestNicknameMaxLength} символов",
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

            return Ok(JoinResponse(roomPlayer));
        }

        [HttpPost("leave")]
        [Authorize]
        public async Task<IActionResult> LeaveRoom([FromBody] LeaveRoomRequest roomRequest, CancellationToken ct)
        {
            // остальные причины ставит сервер: убран ведущим, читерство, конец игры
            if (roomRequest.ExitReason is not (ExitReason.Normal or ExitReason.Disconnected))
            {
                return BadRequest(new CommonErrorResponse(
                    message: "Не получилось выйти. Попробуйте ещё раз",
                    method: HttpContext.GetMethodWithPath()));
            }

            var roomPlayer = await roomService.LeaveRoom(roomRequest.RoomId, roomRequest.ExitReason, ct);

            if (roomPlayer is null)
            {
                return NotFound(new CommonErrorResponse
               (
                   message: "Вы уже вышли из этой комнаты или не были в ней",
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
                "Переводить игроков между командами может только ведущий",
                "Сменить команду можно только до начала игры");
        }

        [HttpPut("{id:guid}/teams/{number:int}")]
        [Authorize]
        public async Task<IActionResult> RenameTeam(Guid id, int number, [FromBody] TeamNameRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > MatchHelper.TeamNameMaxLength)
            {
                return BadRequest(new CommonErrorResponse(
                    message: $"Название команды — от 1 до {MatchHelper.TeamNameMaxLength} символов",
                    method: HttpContext.GetMethodWithPath()));
            }

            if (MatchHelper.HasControlChars(request.Name))
            {
                return InvalidChars("Название команды");
            }

            return LobbyResult(
                await roomService.RenameTeamAsync(id, number, request.Name, ct),
                "Переименовать команду может ведущий или её капитан",
                "Не получилось переименовать: игра уже началась или такое название занято");
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
                "Добавить команду может только ведущий",
                $"Не получилось добавить команду: игра уже идёт, команд уже {MatchHelper.MaxTeams} или такое название занято");
        }

        [HttpDelete("{id:guid}/teams/{number:int}")]
        [Authorize(Policy = AuthorizationPolicies.Registered)]
        public async Task<IActionResult> DeleteTeam(Guid id, int number, CancellationToken ct)
        {
            return LobbyResult(
                await roomService.DeleteTeamAsync(id, number, ct),
                "Удалить команду может только ведущий",
                $"Удалить можно только пустую команду и только до начала игры. Команд должно остаться хотя бы {MatchHelper.MinTeams}");
        }

        // в любой комнате, убранный может вернуться сам
        [HttpDelete("{id:guid}/players/{linkId:guid}")]
        [Authorize(Policy = AuthorizationPolicies.Registered)]
        public async Task<IActionResult> RemovePlayer(Guid id, Guid linkId, CancellationToken ct)
        {
            return LobbyResult(
                await roomService.RemovePlayerAsync(id, linkId, ct),
                "Убрать игрока может только ведущий",
                "Убрать игрока можно только до начала игры. Себя убрать нельзя");
        }

        // аккаунту хватает своего токена, гостю выдаём токен этой комнаты
        private JoinToRoomResponse JoinResponse(RoomResponse roomPlayer)
        {
            if (currentUser.IsRegistered)
            {
                return new JoinToRoomResponse(roomPlayer, null, null);
            }

            var guestToken = guestTokenService.Issue(
                roomPlayer.RoomPlayerLinkId,
                roomPlayer.RoomId,
                roomPlayer.PlayerName);

            return new JoinToRoomResponse(roomPlayer, guestToken.AccessToken, guestToken.ExpiresAtUtc);
        }

        private IActionResult InvalidChars(string what) => BadRequest(new CommonErrorResponse(
            message: $"{what} с переносом строки или табуляцией не подойдёт — уберите их",
            method: HttpContext.GetMethodWithPath()));

        private IActionResult JoinRefused(JoinRefusal refusal, JoinToRoomRequest request)
        {
            var method = HttpContext.GetMethodWithPath();

            return refusal switch
            {
                JoinRefusal.TeamLinkRequired => BadRequest(new CommonErrorResponse(
                    message: "В командную игру входят по ссылке команды — попросите её у ведущего",
                    method: method)),
                // ник занят только у гостя, своё участие токен не нашёл
                JoinRefusal.NicknameTaken => Conflict(new CommonErrorResponse(
                    message: $"Ник {request.Username} уже занят — выберите другой. "
                        + "Если это вы, откройте ссылку в том же браузере, где играли",
                    method: method)),
                JoinRefusal.Full => Conflict(new CommonErrorResponse(
                    message: "Мест больше нет: комната заполнена",
                    method: method)),
                JoinRefusal.Started => Conflict(new CommonErrorResponse(
                    message: "Игра уже началась — новых игроков не пускаем",
                    method: method)),
                JoinRefusal.ReturnExpired => Conflict(new CommonErrorResponse(
                    message: $"Время на возвращение вышло: вернуться в идущую игру можно в течение {RoomHelper.ReconnectWindowSeconds} секунд",
                    method: method)),
                JoinRefusal.Finished => Conflict(new CommonErrorResponse(
                    message: "Эта игра уже закончилась",
                    method: method)),
                JoinRefusal.Busy => Conflict(new CommonErrorResponse(
                    message: "Кто-то успел изменить комнату раньше вас. Попробуйте ещё раз",
                    method: method)),
                _ => NotFound(new CommonErrorResponse(
                    message: "Комната не найдена, проверьте ссылку",
                    method: method)),
            };
        }

        private IActionResult FollowRefused(JoinRefusal refusal)
        {
            var method = HttpContext.GetMethodWithPath();

            return refusal switch
            {
                JoinRefusal.NotParticipant => NotFound(new CommonErrorResponse(
                    message: "Вы не играли в этой комнате",
                    method: method)),
                JoinRefusal.NoNextGame => NotFound(new CommonErrorResponse(
                    message: "Следующей игры пока нет — ведущий её ещё не собрал или отменил",
                    method: method)),
                JoinRefusal.Started => Conflict(new CommonErrorResponse(
                    message: "Следующая игра уже идёт — новых игроков не пускаем",
                    method: method)),
                JoinRefusal.NextFinished => Conflict(new CommonErrorResponse(
                    message: "Следующая игра уже сыграна",
                    method: method)),
                JoinRefusal.ReturnExpired => Conflict(new CommonErrorResponse(
                    message: $"Время на возвращение вышло: вернуться в идущую игру можно в течение {RoomHelper.ReconnectWindowSeconds} секунд",
                    method: method)),
                JoinRefusal.Full => Conflict(new CommonErrorResponse(
                    message: "Мест больше нет: комната заполнена",
                    method: method)),
                // формы нет, заняты ник и все его варианты с номером
                JoinRefusal.NicknameTaken => Conflict(new CommonErrorResponse(
                    message: "Ник занят — откройте ту же ссылку и выберите другой",
                    method: method)),
                JoinRefusal.Busy => Conflict(new CommonErrorResponse(
                    message: "Не получилось перейти — попробуйте ещё раз",
                    method: method)),
                _ => NotFound(new CommonErrorResponse(
                    message: "Комната не найдена",
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
                message: "Кто-то успел изменить комнату раньше вас. Попробуйте ещё раз",
                method: HttpContext.GetMethodWithPath())),
            _ => NotFound(new CommonErrorResponse(
                message: "Не нашли — состав уже изменился. Обновите страницу",
                method: HttpContext.GetMethodWithPath())),
        };
    }
}
