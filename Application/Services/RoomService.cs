using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.Contracts.Room;
using QuizGamePlatform.Backend.Application.Enums;
using QuizGamePlatform.Backend.Application.Mappers;
using QuizGamePlatform.Backend.Application.Options;
using QuizGamePlatform.Backend.Core.Abstractions;
using QuizGamePlatform.Backend.Core.Helpers;
using QuizGamePlatform.Backend.DataAccess;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.Application.Services
{
    public class RoomService(
        IRoomRepository roomRepository,
        IRoomParticipationRepository roomParticipationRepository,
        IUserRepository userRepository,
        IMatchRepository matchRepository,
        ICurrentUserContext currentUser,
        IGameNotifier notifier,
        IInviteMailer mailer,
        IOptions<InviteOptions> invites,

        IRoomHelper roomHelper,
        ApplicationDbContext context,
        ILogger<RoomService> logger,
        TimeProvider timeProvider) : IRoomService
    {
        private const int JoinAttempts = 20;
        private const int CodeAttempts = 10;
        private const int PresenceAttempts = 3;
        private const int MyResultsLimit = 20;

        private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

        public async Task<CreateRoomResponse?> CreateRoomAsync(CreateRoomRequest? request, CancellationToken ct)
        {
            var newTeams = request is { IsTeamGame: true } ? BuildTeams(request) : [];

            if (newTeams is null)
            {
                return null;
            }

            logger.LogInformation("Creating new Room");

            // владелец сразу игрок под своим логином
            var host = await GetOrCreateCurrentUserAsync(ct);
            var room = await roomRepository.CreateRoomAsync(await NewCodeAsync(ct), host, ct);
            var hostLink = await roomParticipationRepository.CreateRoomPlayer(room, host.UserName, host, ct);

            var teams = new List<RoomTeamEntity>();

            foreach (var (team, i) in newTeams.Select((team, i) => (team, i)))
            {
                teams.Add(new RoomTeamEntity
                {
                    RoomId = room.Id,
                    Number = i + 1,
                    Name = team.Name!,
                    ExpectedPlayers = team.ExpectedPlayers,
                    JoinCode = await NewCodeAsync(ct),
                });
            }

            if (teams.Count > 0)
            {
                room.IsTeamGame = true;
                teams.ForEach(roomRepository.AddTeam);

                // владелец начинает в первой команде
                MoveToTeam(hostLink, 1);
            }

            await context.SaveChangesAsync(ct);

            logger.LogInformation("Room with id: {RoomId} successfully created", room.Id);

            if (!room.IsTeamGame)
            {
                return room.ToCreateRoomResponse(invites.Value.JoinLink(room.RoomCode));
            }

            var links = teams.Select(ToTeamLink).ToList();

            // ведущему письмо со ссылками, чтобы разослать каждой команде свою
            if (currentUser.Email is { } email)
            {
                await mailer.SendTeamLinksAsync(email, room.RoomCode, links, ct);
            }

            return room.ToCreateRoomResponse(null, links);
        }

        public async Task<bool> IsRoomHostAsync(Guid roomId, CancellationToken ct)
            => currentUser.KeycloakSubject is { } subject
                && await roomRepository.IsHost(roomId, subject, ct);

        public async Task<bool> DeleteExistingRoomByIdAsync(Guid id, CancellationToken ct)
        {
            var room = await roomRepository.GetRoomByIdAsync(id, ct);

            // после старта комната уже история и держит очки рейтинга
            if (room is null || room.Status != RoomStatus.Waiting)
            {
                return false;
            }

            roomRepository.RemoveRoom(room);

            if (!await TrySaveAsync(ct))
            {
                return false;
            }

            logger.LogInformation("Room with {id} sucessfully deleted", id);

            // участники перечитают комнату и получат 404
            notifier.RoomUpdated(id);

            return true;
        }

        public async Task<RoomStateResponse?> GetRoomStateAsync(Guid roomId, CancellationToken ct)
        {
            var room = await roomRepository.GetRoomByIdAsync(roomId, ct);
            // комнату видят только её участники
            var me = room is null ? null : await FindMeAsync(roomId, ct);

            if (room is null || me is null)
            {
                return null;
            }

            return await BuildStateAsync(room, me, ct);
        }

        // состояние глазами участника me, по нему же итоги в «Моих комнатах»
        private async Task<RoomStateResponse> BuildStateAsync(RoomEntity room, RoomPlayerEntity me, CancellationToken ct)
        {
            var players = await roomParticipationRepository.GetParticipationsByRoomId(room.Id, ct);
            var match = await matchRepository.GetMatchByRoomId(room.Id, ct);

            if (!room.IsTeamGame)
            {
                return new RoomStateResponse(
                    room.Id,
                    room.RoomCode,
                    room.Status,
                    match?.Id,
                    me.Id,
                    room.Status == RoomStatus.Finished && match is not null
                        ? await FinalPlayersAsync(room, match, players, ct)
                        : players.Select(p => p.ToRoomParticipationResponse(room.HostUserId)).ToList(),
                    false,
                    null);
            }

            // таблица обновляется после каждого вопроса, открытый не считаем, иначе он выдал бы верность ответов
            Func<List<RoomPlayerEntity>, int>? points = null;
            Func<List<RoomPlayerEntity>, long?>? averageTime = null;

            if (room.Status != RoomStatus.Waiting && match is not null)
            {
                var answers = await matchRepository.GetAnswersAsync(match.Id, ct);
                var questions = room.Status == RoomStatus.Finished
                    ? match.Questions
                    : match.Questions.Where(q => q.ClosedAt is not null).ToList();
                var timings = RoomMapper.AnswerTimings(match, questions, answers);
                points = members => RoomMapper.TeamPoints(members, questions, answers);
                averageTime = members => RoomMapper.AverageCorrectTime(members, timings);
            }

            // ссылки команд видит только владелец
            var isHost = room.HostUserId is not null && me.UserId == room.HostUserId;
            var teams = (await roomRepository.GetTeams(room.Id, ct))
                .ToTeamResponses(room.Status, players, points, averageTime, t => isHost ? invites.Value.JoinLink(t.JoinCode) : null);
            var captains = teams.Select(t => t.CaptainLinkId).ToHashSet();

            return new RoomStateResponse(
                room.Id,
                room.RoomCode,
                room.Status,
                match?.Id,
                me.Id,
                // в командной игре личные очки не показываем, итоги только по командам
                players
                    .OrderBy(p => p.JoinedAt)
                    .Select(p => p.ToRoomParticipationResponse(room.HostUserId, captains.Contains(p.Id), showScore: false))
                    .ToList(),
                true,
                teams);
        }

        // места без дележа: очки, вышедшие до финала в конце, меньше суммарное время верных, больше ответов, кто раньше вошёл
        private async Task<List<RoomParticipationResponse>> FinalPlayersAsync(
            RoomEntity room,
            MatchEntity match,
            List<RoomPlayerEntity> players,
            CancellationToken ct)
        {
            var timings = RoomMapper.AnswerTimings(match, match.Questions, await matchRepository.GetAnswersAsync(match.Id, ct));
            RoomMapper.AnswerTiming Timing(RoomPlayerEntity p) => timings.GetValueOrDefault(p.Id) ?? RoomMapper.AnswerTiming.None;

            return players
                .OrderByDescending(p => p.Score)
                .ThenByDescending(p => p.IsActive)
                .ThenBy(p => Timing(p).CorrectTimeMs ?? 0)
                .ThenByDescending(p => Timing(p).Answered)
                .ThenBy(p => p.JoinedAt)
                .Select((p, i) => p.ToRoomParticipationResponse(room.HostUserId, place: i + 1, correctTimeMs: Timing(p).CorrectTimeMs))
                .ToList();
        }

        public async Task<List<MyRoomResponse>> GetMyRoomsAsync(CancellationToken ct)
        {
            var rooms = await roomRepository.GetRoomsOfUser(currentUser.KeycloakSubject!, ct);
            var results = new Dictionary<Guid, MyRoomResult>();

            // итоги считаем как на экране итогов, но только для последних завершённых, иначе список тормозит
            foreach (var room in rooms.Where(r => r.Status == RoomStatus.Finished).Take(MyResultsLimit))
            {
                // в Players репозиторий кладёт только моё участие
                if (room.Players.FirstOrDefault() is { } me)
                {
                    results[room.Id] = (await BuildStateAsync(room, me, ct)).ToMyRoomResult();
                }
            }

            return rooms
                .Select(r => r.ToMyRoomResponse(
                    r.IsTeamGame ? null : invites.Value.JoinLink(r.RoomCode),
                    r.IsTeamGame ? MyRoomTeams(r) : null,
                    results.GetValueOrDefault(r.Id)))
                .ToList();
        }

        public async Task<InviteResponse?> GetInviteAsync(string code, CancellationToken ct)
        {
            var invite = await roomRepository.FindInviteAsync(code.Trim().ToUpperInvariant(), ct);

            if (invite is not { Room: var room, Team: var team })
            {
                return null;
            }

            return new InviteResponse(room.IsTeamGame, room.Status, room.HostUser?.UserName, team?.Number, team?.Name);
        }

        public async Task<(RoomResponse? Room, JoinRefusal Refusal)> JoinToRoomByRoomCodeAsync(
            string? nickname,
            string roomCode,
            CancellationToken ct)
        {
            // ссылку открывают разом: проигравший гонку за версию комнаты входит заново
            for (var attempt = 1; ; attempt++)
            {
                var (room, refusal, conflict) = await TryJoinAsync(nickname, roomCode, ct);

                if (!conflict)
                {
                    return (room, refusal);
                }

                if (attempt == JoinAttempts)
                {
                    return (null, JoinRefusal.Busy);
                }

                roomRepository.ForgetChanges();
                await Task.Delay(Random.Shared.Next(5, 50), ct);
            }
        }

        private async Task<(RoomResponse? Room, JoinRefusal Refusal, bool Conflict)> TryJoinAsync(
            string? nickname,
            string roomCode,
            CancellationToken ct)
        {
            // в ссылке код комнаты или код команды
            var code = roomCode.Trim().ToUpperInvariant();
            var room = await roomRepository.GetRoomByRoomCodeAsync(code, ct);
            var team = room is null ? await roomRepository.GetTeamByJoinCodeAsync(code, ct) : null;
            room ??= team?.Room;
            var teamNumber = team?.Number;

            if (room == null || room.Status == RoomStatus.Finished)
            {
                logger.LogInformation("Room with roomcode {roomcode} is not found or finished", roomCode);

                return (null, room is null ? JoinRefusal.NotFound : JoinRefusal.Finished, false);
            }

            // команды нужны только в лобби командной комнаты
            var teams = room.IsTeamGame && room.Status == RoomStatus.Waiting
                ? await roomRepository.GetTeams(room.Id, ct)
                : [];

            // своё участие только по токену
            var roomPlayer = await FindMeAsync(room.Id, ct);

            // без своей команды (убран или её удалили) в лобби возвращаются только по ссылке команды
            if (roomPlayer != null && teams.Count > 0 && teamNumber is null && teams.All(t => t.Number != roomPlayer.TeamNumber))
            {
                return (null, JoinRefusal.TeamLinkRequired, false);
            }

            if (roomPlayer != null)
            {
                var (rejoined, conflict) = await RejoinAsync(roomPlayer, room, teams, teamNumber, ct);

                // в лобби не пускает только полная комната, в матче истёкшее окно возврата
                var refusal = rejoined is not null || conflict ? JoinRefusal.None
                    : room.Status == RoomStatus.Waiting ? JoinRefusal.Full
                    : JoinRefusal.ReturnExpired;

                return (rejoined, refusal, conflict);
            }

            // новый игрок заходит только до старта матча
            if (room.Status != RoomStatus.Waiting)
            {
                return (null, JoinRefusal.Started, false);
            }

            // в командную игру новые входят только по ссылке своей команды
            if (room.IsTeamGame && teamNumber is null)
            {
                return (null, JoinRefusal.TeamLinkRequired, false);
            }

            // аккаунт играет под логином, гость под своим ником
            var user = currentUser.KeycloakSubject is not null ? await GetOrCreateCurrentUserAsync(ct) : null;
            var name = user?.UserName ?? nickname!.Trim();

            // логин аккаунта уникален сам, гость не берёт имя занятое в комнате
            if (user is null && await roomParticipationRepository.IsNicknameTaken(room.Id, name, ct))
            {
                return (null, JoinRefusal.NicknameTaken, false);
            }

            if (await IsRoomFullAsync(room, ct))
            {
                logger.LogInformation("Room with roomcode {roomcode} is full", roomCode);

                return (null, JoinRefusal.Full, false);
            }

            var link = await roomParticipationRepository.CreateRoomPlayer(room, name, user, ct);

            MoveToTeam(link, teamNumber);
            roomRepository.MarkChanged(room);

            if (!await TrySaveAsync(ct))
            {
                return (null, JoinRefusal.None, true);
            }

            logger.LogInformation("Player {name} joined room {roomCode}", name, roomCode);

            notifier.RoomUpdated(room.Id);

            return (link.ToJoinRoomResponse(), JoinRefusal.None, false);
        }

        private async Task<(RoomResponse? Room, bool Conflict)> RejoinAsync(
            RoomPlayerEntity roomPlayer,
            RoomEntity room,
            List<RoomTeamEntity> teams,
            int? teamNumber,
            CancellationToken ct)
        {
            if (roomPlayer.IsActive)
            {
                // в лобби ссылка другой команды пересаживает, в матче состав не меняется
                if (teams.Count == 0 || teamNumber is null || teamNumber == roomPlayer.TeamNumber)
                {
                    return (roomPlayer.ToJoinRoomResponse(), false);
                }

                MoveToTeam(roomPlayer, teamNumber);
            }
            else
            {
                // до старта мешает только полная комната, место владельца за ним, в матче окно реконнекта
                var isHost = room.HostUserId is not null && roomPlayer.UserId == room.HostUserId;
                var canReturn = room.Status == RoomStatus.Waiting
                    ? isHost || !await IsRoomFullAsync(room, ct)
                    : await CanReconnectAsync(roomPlayer, room.Id, ct);

                if (!canReturn)
                {
                    logger.LogInformation("Participant {RoomPlayerId} can't return to room {RoomCode}", roomPlayer.Id, room.RoomCode);

                    return (null, false);
                }

                // в лобби возвращается в свою команду или по ссылке другой
                if (teams.Count > 0)
                {
                    MoveToTeam(roomPlayer, teamNumber ?? roomPlayer.TeamNumber);
                }

                roomPlayer.IsActive = true;
                roomPlayer.FinishedAt = null;
                roomPlayer.ExitReason = null;
            }

            roomRepository.MarkChanged(room);

            if (!await TrySaveAsync(ct))
            {
                return (null, true);
            }

            notifier.RoomUpdated(room.Id);

            return (roomPlayer.ToJoinRoomResponse(), false);
        }

        public async Task<LeaveRoomResponse?> LeaveRoom(Guid roomId, ExitReason exitReason, CancellationToken ct)
        {
            var room = await roomRepository.GetRoomByIdAsync(roomId, ct);

            // после матча комната история, итоговые очки не меняются
            if (room is null || room.Status == RoomStatus.Finished)
            {
                return null;
            }

            var roomPlayer = await FindMeAsync(roomId, ct);

            if (roomPlayer == null)
            {
                logger.LogWarning("Attempted to leave room {RoomId} by user who is not in it", roomId);

                return null;
            }

            return await LeaveAsync(room, roomPlayer, exitReason, ct);
        }

        // связь оборвалась и за паузу не вернулась: выходим за игрока, как будто он нажал «выйти»
        public async Task<bool> DisconnectAsync(Guid roomId, Guid roomPlayerId, CancellationToken ct)
        {
            for (var attempt = 1; attempt <= PresenceAttempts; attempt++)
            {
                var room = await roomRepository.GetRoomByIdAsync(roomId, ct);
                var roomPlayer = await roomParticipationRepository.FindParticipant(roomId, roomPlayerId, null, ct);

                if (room is null || room.Status == RoomStatus.Finished || roomPlayer is not { IsActive: true })
                {
                    return false;
                }

                if (await LeaveAsync(room, roomPlayer, ExitReason.Disconnected, ct) is not null)
                {
                    return true;
                }

                roomRepository.ForgetChanges();
            }

            return false;
        }

        // связь вернулась: возвращаем в игру сам, пока не истекло окно, ссылку открывать не нужно
        public async Task<bool> ReconnectAsync(Guid roomId, Guid roomPlayerId, CancellationToken ct)
        {
            for (var attempt = 1; attempt <= PresenceAttempts; attempt++)
            {
                var room = await roomRepository.GetRoomByIdAsync(roomId, ct);
                var roomPlayer = await roomParticipationRepository.FindParticipant(roomId, roomPlayerId, null, ct);

                // вышедший сам или убранный возвращается только по ссылке
                if (room is null || room.Status == RoomStatus.Finished || roomPlayer is null
                    || (!roomPlayer.IsActive && roomPlayer.ExitReason != ExitReason.Disconnected))
                {
                    return false;
                }

                if (roomPlayer.IsActive)
                {
                    return true;
                }

                var teams = room.IsTeamGame && room.Status == RoomStatus.Waiting
                    ? await roomRepository.GetTeams(room.Id, ct)
                    : [];

                // его команду удалили, пока не было связи
                if (teams.Count > 0 && teams.All(t => t.Number != roomPlayer.TeamNumber))
                {
                    return false;
                }

                var (rejoined, conflict) = await RejoinAsync(roomPlayer, room, teams, null, ct);

                if (!conflict)
                {
                    return rejoined is not null;
                }

                roomRepository.ForgetChanges();
            }

            return false;
        }

        private async Task<LeaveRoomResponse?> LeaveAsync(RoomEntity room, RoomPlayerEntity roomPlayer, ExitReason exitReason, CancellationToken ct)
        {
            var roomId = room.Id;

            if (!roomPlayer.IsActive)
            {
                logger.LogInformation("Participant {RoomPlayerId} is already inactive in room {RoomId}", roomPlayer.Id, roomId);

                return null;
            }

            roomPlayer.FinishedAt = UtcNow;
            roomPlayer.IsActive = false;
            roomPlayer.ExitReason = exitReason;
            roomRepository.MarkChanged(room);

            if (!await TrySaveAsync(ct))
            {
                return null;
            }

            logger.LogInformation(
            "Participant {RoomPlayerId} left room {RoomId} due to {ExitReason}",
            roomPlayer.Id, roomId, exitReason);

            notifier.RoomUpdated(roomId);

            return roomPlayer.ToLeaveRoomResponse();
        }

        public async Task<List<GlobalLeaderboardEntry>> GetLeaderboardAsync(bool teamGames, CancellationToken ct)
            => await roomParticipationRepository.GetLeaderboard(teamGames, ct);

        // лобби командной комнаты

        public async Task<LobbyChangeResult> ChangeTeamAsync(Guid roomId, Guid roomPlayerLinkId, int teamNumber, CancellationToken ct)
        {
            var lobby = await GetLobbyAsync(roomId, ct);

            if (lobby is null)
            {
                return LobbyChangeResult.NotFound;
            }

            var player = lobby.Players.FirstOrDefault(p => p.Id == roomPlayerLinkId);

            if (player is null || lobby.Team(teamNumber) is null)
            {
                return LobbyChangeResult.NotFound;
            }

            // пересаживает только владелец, игрок сам переходит лишь по ссылке команды
            if (!lobby.IsHost)
            {
                return LobbyChangeResult.Forbidden;
            }

            if (lobby.Room.Status != RoomStatus.Waiting)
            {
                return LobbyChangeResult.Conflict;
            }

            MoveToTeam(player, teamNumber);

            return await SaveLobbyAsync(lobby.Room, ct);
        }

        public async Task<LobbyChangeResult> RenameTeamAsync(Guid roomId, int teamNumber, string name, CancellationToken ct)
        {
            var lobby = await GetLobbyAsync(roomId, ct);
            var team = lobby?.Team(teamNumber);

            if (lobby is null || team is null)
            {
                return LobbyChangeResult.NotFound;
            }

            // владелец любую, капитан свою
            if (!lobby.IsHost && lobby.CaptainOf(teamNumber)?.Id != lobby.Me.Id)
            {
                return LobbyChangeResult.Forbidden;
            }

            name = name.Trim();

            if (lobby.Room.Status != RoomStatus.Waiting || lobby.IsNameTaken(name, teamNumber))
            {
                return LobbyChangeResult.Conflict;
            }

            team.Name = name;

            return await SaveLobbyAsync(lobby.Room, ct);
        }

        public async Task<LobbyChangeResult> AddTeamAsync(Guid roomId, string? name, CancellationToken ct)
        {
            var lobby = await GetLobbyAsync(roomId, ct);

            if (lobby is null)
            {
                return LobbyChangeResult.NotFound;
            }

            if (!lobby.Room.IsTeamGame)
            {
                return LobbyChangeResult.NotFound;
            }

            if (!lobby.IsHost)
            {
                return LobbyChangeResult.Forbidden;
            }

            if (lobby.Room.Status != RoomStatus.Waiting || lobby.Teams.Count >= MatchHelper.MaxTeams)
            {
                return LobbyChangeResult.Conflict;
            }

            // занимает первый свободный номер
            var number = Enumerable.Range(1, MatchHelper.MaxTeams).First(n => lobby.Team(n) is null);
            name = string.IsNullOrWhiteSpace(name) ? MatchHelper.DefaultTeamName(number) : name.Trim();

            if (lobby.IsNameTaken(name, number))
            {
                return LobbyChangeResult.Conflict;
            }

            var team = new RoomTeamEntity
            {
                RoomId = roomId,
                Number = number,
                Name = name,
                JoinCode = await NewCodeAsync(ct),
            };
            roomRepository.AddTeam(team);

            var result = await SaveLobbyAsync(lobby.Room, ct);

            // ссылку новой команды ведущий тоже получает письмом
            if (result == LobbyChangeResult.Done && currentUser.Email is { } email)
            {
                await mailer.SendTeamLinksAsync(email, lobby.Room.RoomCode, [ToTeamLink(team)], ct);
            }

            return result;
        }

        public async Task<LobbyChangeResult> DeleteTeamAsync(Guid roomId, int teamNumber, CancellationToken ct)
        {
            var lobby = await GetLobbyAsync(roomId, ct);
            var team = lobby?.Team(teamNumber);

            if (lobby is null || team is null)
            {
                return LobbyChangeResult.NotFound;
            }

            if (!lobby.IsHost)
            {
                return LobbyChangeResult.Forbidden;
            }

            // только пустую и пока команд больше минимума
            var isEmpty = !lobby.Players.Any(p => p.IsActive && p.TeamNumber == teamNumber);

            if (lobby.Room.Status != RoomStatus.Waiting || !isEmpty || lobby.Teams.Count <= MatchHelper.MinTeams)
            {
                return LobbyChangeResult.Conflict;
            }

            // вышедшие из неё при возврате попадут в самую маленькую
            foreach (var player in lobby.Players.Where(p => p.TeamNumber == teamNumber))
            {
                player.TeamNumber = null;
            }

            roomRepository.RemoveTeam(team);

            return await SaveLobbyAsync(lobby.Room, ct);
        }

        // владелец убирает из лобби того, кто закрыл вкладку и не играет
        public async Task<LobbyChangeResult> RemovePlayerAsync(Guid roomId, Guid roomPlayerLinkId, CancellationToken ct)
        {
            var lobby = await GetLobbyAsync(roomId, ct);
            var player = lobby?.Players.FirstOrDefault(p => p.Id == roomPlayerLinkId && p.IsActive);

            if (lobby is null || player is null)
            {
                return LobbyChangeResult.NotFound;
            }

            if (!lobby.IsHost)
            {
                return LobbyChangeResult.Forbidden;
            }

            // себя не убирает, для этого есть выход
            if (lobby.Room.Status != RoomStatus.Waiting || player.Id == lobby.Me.Id)
            {
                return LobbyChangeResult.Conflict;
            }

            player.IsActive = false;
            player.FinishedAt = UtcNow;
            player.ExitReason = ExitReason.Kicked;
            // вернётся новичком в команде и без капитанства
            player.TeamNumber = null;

            return await SaveLobbyAsync(lobby.Room, ct);
        }

        private sealed record Lobby(RoomEntity Room, RoomPlayerEntity Me, List<RoomTeamEntity> Teams, List<RoomPlayerEntity> Players)
        {
            public bool IsHost => Room.HostUserId is not null && Me.UserId == Room.HostUserId;

            public RoomTeamEntity? Team(int number) => Teams.FirstOrDefault(t => t.Number == number);

            // капитан первый вошедший в команду из активных, гость тоже
            public RoomPlayerEntity? CaptainOf(int number) => Players
                .Where(p => p.IsActive && p.TeamNumber == number)
                .MinBy(p => p.TeamJoinedAt);

            public bool IsNameTaken(string name, int exceptNumber) => Teams
                .Any(t => t.Number != exceptNumber && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        // управлять лобби может только активный участник, в обычной комнате команд нет
        private async Task<Lobby?> GetLobbyAsync(Guid roomId, CancellationToken ct)
        {
            var room = await roomRepository.GetRoomByIdAsync(roomId, ct);
            var me = room is null ? null : await FindMeAsync(roomId, ct);

            if (room is null || me is not { IsActive: true })
            {
                return null;
            }

            return new Lobby(
                room,
                me,
                await roomRepository.GetTeams(roomId, ct),
                await roomParticipationRepository.GetParticipationsByRoomId(roomId, ct));
        }

        private async Task<LobbyChangeResult> SaveLobbyAsync(RoomEntity room, CancellationToken ct)
        {
            roomRepository.MarkChanged(room);

            if (!await TrySaveAsync(ct))
            {
                return LobbyChangeResult.Concurrent;
            }

            notifier.RoomUpdated(room.Id);

            return LobbyChangeResult.Done;
        }

        private void MoveToTeam(RoomPlayerEntity player, int? teamNumber)
        {
            if (player.TeamNumber != teamNumber)
            {
                player.TeamNumber = teamNumber;
                player.TeamJoinedAt = UtcNow;
            }
        }

        // null если команд неверное число, название длинное или повторяется, ожидаемых больше лимита
        private static List<NewTeamRequest>? BuildTeams(CreateRoomRequest request)
        {
            var teams = (request.Teams ?? Enumerable.Repeat(new NewTeamRequest(null), MatchHelper.MinTeams))
                .Select((team, i) => team with
                {
                    Name = team.Name?.Trim() is { Length: > 0 } name ? name : MatchHelper.DefaultTeamName(i + 1)
                })
                .ToList();

            var valid = teams.Count >= MatchHelper.MinTeams
                && teams.Count <= MatchHelper.MaxTeams
                && teams.All(t => t.Name!.Length <= MatchHelper.TeamNameMaxLength && t.ExpectedPlayers is null or > 0)
                && teams.Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == teams.Count
                && teams.Sum(t => t.ExpectedPlayers ?? 0) <= MatchHelper.MaxTeamGamePlayer;

            return valid ? teams : null;
        }

        // код не должен совпасть ни с комнатой, ни с командой, иначе ссылка поведёт не туда
        private async Task<string> NewCodeAsync(CancellationToken ct)
        {
            var code = roomHelper.GenerateRoomCode();

            for (var attempt = 1; attempt < CodeAttempts && await roomRepository.IsCodeTakenAsync(code, ct); attempt++)
            {
                code = roomHelper.GenerateRoomCode();
            }

            return code;
        }

        // ссылки команд только владельцу, участнику названия
        private List<TeamLinkResponse> MyRoomTeams(RoomEntity room)
        {
            var isHost = room.HostUser is not null && room.HostUser.KeycloakSubject == currentUser.KeycloakSubject;

            return room.Teams
                .OrderBy(t => t.Number)
                .Select(t => isHost ? ToTeamLink(t) : new TeamLinkResponse(t.Number, t.Name, t.ExpectedPlayers, null))
                .ToList();
        }

        private TeamLinkResponse ToTeamLink(RoomTeamEntity team)
            => new(team.Number, team.Name, team.ExpectedPlayers, invites.Value.JoinLink(team.JoinCode));

        // параллельный вход, выход, старт или удаление той же комнаты отклоняет база
        private async Task<bool> TrySaveAsync(CancellationToken ct)
        {
            try
            {
                await context.SaveChangesAsync(ct);

                return true;
            }
            catch (DbUpdateException ex)
            {
                logger.LogInformation(ex, "Room change rejected by a concurrent change");

                return false;
            }
        }

        private Task<RoomPlayerEntity?> FindMeAsync(Guid roomId, CancellationToken ct)
            => roomParticipationRepository.FindParticipant(
                roomId, currentUser.RoomPlayerLinkId, currentUser.KeycloakSubject, ct);

        private Task<UserEntity> GetOrCreateCurrentUserAsync(CancellationToken ct)
            => userRepository.GetOrCreateUserAsync(currentUser.KeycloakSubject!, currentUser.DisplayName!, ct);

        // слоты держат активные и владелец, даже если вышел
        private async Task<bool> IsRoomFullAsync(RoomEntity room, CancellationToken ct)
            => await roomParticipationRepository.CountTakenSlots(room.Id, ct)
                >= (room.IsTeamGame ? MatchHelper.MaxTeamGamePlayer : MatchHelper.MaxMatchPlayer);

        // в идущий матч возвращается только вышедший после старта и пока не истекло окно
        private async Task<bool> CanReconnectAsync(RoomPlayerEntity roomPlayer, Guid roomId, CancellationToken ct)
        {
            var match = await matchRepository.GetMatchByRoomId(roomId, ct);

            return match is not null
                && roomPlayer.FinishedAt is { } finishedAt
                && finishedAt >= match.StartedAt
                && UtcNow - finishedAt <= TimeSpan.FromSeconds(RoomHelper.ReconnectWindowSeconds);
        }
    };
}
