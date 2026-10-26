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
        private const int CancelAttempts = 5;
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

        public async Task<DeleteRoomResult> DeleteExistingRoomByIdAsync(Guid id, CancellationToken ct)
        {
            var room = await roomRepository.GetRoomByIdAsync(id, ct);

            if (room is null)
            {
                return DeleteRoomResult.NotFound;
            }

            // после старта комната уже история и держит очки рейтинга
            if (room.Status != RoomStatus.Waiting)
            {
                return DeleteRoomResult.NotWaiting;
            }

            // отмена игры N: ссылки возвращаются к итогам прошлой
            if (room.PreviousRoomId is { } previousRoomId)
            {
                return await CancelGameAsync(id, previousRoomId, ct);
            }

            roomRepository.RemoveRoom(room);

            if (!await TrySaveAsync(ct))
            {
                return DeleteRoomResult.Busy;
            }

            logger.LogInformation("Room with {id} sucessfully deleted", id);

            // участники перечитают комнату и получат 404
            notifier.RoomUpdated(id);

            return DeleteRoomResult.Deleted;
        }

        // во время отмены игроки ещё переходят: проигравший гонку за версию повторяет с начала
        private async Task<DeleteRoomResult> CancelGameAsync(Guid id, Guid previousRoomId, CancellationToken ct)
        {
            for (var attempt = 1; ; attempt++)
            {
                var result = DeleteRoomResult.Deleted;

                try
                {
                    await roomRepository.InTransactionAsync(async c =>
                    {
                        roomRepository.ForgetChanges();
                        result = DeleteRoomResult.Deleted;

                        var room = await roomRepository.GetRoomByIdAsync(id, c);

                        if (room is null || room.Status != RoomStatus.Waiting)
                        {
                            result = room is null ? DeleteRoomResult.NotFound : DeleteRoomResult.NotWaiting;

                            return;
                        }

                        var code = room.RoomCode;
                        var teamCodes = (await roomRepository.GetTeams(id, c)).ToDictionary(t => t.Number, t => t.JoinCode);

                        // сначала освобождаем коды, потом отдаём их прошлой игре
                        roomRepository.RemoveRoom(room);
                        await context.SaveChangesAsync(c);

                        if (await roomRepository.GetRoomByIdAsync(previousRoomId, c) is not { } previous)
                        {
                            return;
                        }

                        previous.RoomCode = code;

                        // ссылка команды, удалённой в лобби игры N, пропадает: её кода уже нет
                        foreach (var team in await roomRepository.GetTeams(previousRoomId, c))
                        {
                            if (teamCodes.TryGetValue(team.Number, out var joinCode))
                            {
                                team.JoinCode = joinCode;
                            }
                        }

                        roomRepository.MarkChanged(previous);
                        await context.SaveChangesAsync(c);
                    }, ct);
                }
                catch (DbUpdateException ex)
                {
                    logger.LogInformation(ex, "Cancelling game in room {RoomId} rejected by a concurrent change", id);

                    roomRepository.ForgetChanges();

                    if (attempt == CancelAttempts)
                    {
                        return DeleteRoomResult.Busy;
                    }

                    await Task.Delay(Random.Shared.Next(5, 50), ct);

                    continue;
                }

                if (result != DeleteRoomResult.Deleted)
                {
                    return result;
                }

                logger.LogInformation("Game in room {RoomId} cancelled, links returned to room {PreviousRoomId}", id, previousRoomId);

                // в лобби увидят 404, на итогах прошлой снова «Сыграть ещё»
                notifier.RoomUpdated(id);
                notifier.RoomUpdated(previousRoomId);

                return DeleteRoomResult.Deleted;
            }
        }

        public async Task<(CreateRoomResponse? Room, RematchRefusal Refusal)> RematchAsync(Guid roomId, CancellationToken ct)
        {
            var subject = currentUser.KeycloakSubject!;

            if (await roomRepository.GetRoomByIdAsync(roomId, ct) is null)
            {
                return (null, RematchRefusal.NotFound);
            }

            // переиграть может и ведущий, ушедший посреди игры
            if (!await roomRepository.IsOwner(roomId, subject, ct))
            {
                return (null, RematchRefusal.Forbidden);
            }

            var refusal = RematchRefusal.None;
            RoomEntity? next = null;
            List<RoomTeamEntity> nextTeams = [];

            try
            {
                await roomRepository.InTransactionAsync(async c =>
                {
                    // стратегия может повторить работу целиком
                    roomRepository.ForgetChanges();
                    refusal = RematchRefusal.None;
                    next = null;

                    var old = await roomRepository.GetRoomByIdAsync(roomId, c);

                    if (old is null)
                    {
                        refusal = RematchRefusal.NotFound;

                        return;
                    }

                    // повторное нажатие или вторая вкладка: игра уже собрана
                    if (await roomRepository.GetNextRoomAsync(old.Id, c) is not null)
                    {
                        return;
                    }

                    if (old.Status != RoomStatus.Finished)
                    {
                        refusal = RematchRefusal.NotFinished;

                        return;
                    }

                    // коды ссылок переезжают в новую игру, старой достаются архивные
                    var teams = await roomRepository.GetTeams(old.Id, c);
                    var code = old.RoomCode;
                    var teamCodes = teams.ToDictionary(t => t.Number, t => t.JoinCode);

                    old.RoomCode = await NewCodeAsync(c);

                    foreach (var team in teams)
                    {
                        team.JoinCode = await NewCodeAsync(c);
                    }

                    old.SeriesId ??= old.Id;

                    // проверка версии старой комнаты: вторая вкладка ведущего получит конфликт
                    roomRepository.MarkChanged(old);
                    await context.SaveChangesAsync(c);

                    var host = await GetOrCreateCurrentUserAsync(c);
                    var room = await roomRepository.CreateRoomAsync(code, host, c);
                    room.IsTeamGame = old.IsTeamGame;
                    room.PreviousRoomId = old.Id;
                    room.SeriesId = old.SeriesId;
                    room.GameNumber = old.GameNumber + 1;

                    nextTeams = teams
                        .Select(t => new RoomTeamEntity
                        {
                            RoomId = room.Id,
                            Number = t.Number,
                            Name = t.Name,
                            ExpectedPlayers = t.ExpectedPlayers,
                            JoinCode = teamCodes[t.Number],
                        })
                        .ToList();
                    nextTeams.ForEach(roomRepository.AddTeam);

                    // ведущий снова игрок, в своей прежней команде; капитанство считается заново
                    var hostOld = await roomParticipationRepository.FindParticipant(old.Id, null, subject, c);
                    var hostLink = await roomParticipationRepository.CreateRoomPlayer(room, host.UserName, host, c);
                    hostLink.OriginRoomPlayerId = hostOld is null ? null : hostOld.OriginRoomPlayerId ?? hostOld.Id;

                    if (nextTeams.Count > 0)
                    {
                        MoveToTeam(hostLink, nextTeams.Any(t => t.Number == hostOld?.TeamNumber)
                            ? hostOld!.TeamNumber
                            : nextTeams[0].Number);
                    }

                    await context.SaveChangesAsync(c);

                    next = room;
                }, ct);
            }
            catch (DbUpdateException ex)
            {
                logger.LogInformation(ex, "Rematch of room {RoomId} rejected by a concurrent change", roomId);

                roomRepository.ForgetChanges();

                // проиграли гонку второй вкладке: её игра и есть ответ
                if (await roomRepository.GetNextRoomAsync(roomId, ct) is null)
                {
                    return (null, RematchRefusal.Busy);
                }
            }

            if (refusal != RematchRefusal.None)
            {
                return (null, refusal);
            }

            if (next is not null)
            {
                logger.LogInformation("Room {RoomId} continued with game {GameNumber} in room {NextRoomId}", roomId, next.GameNumber, next.Id);

                // на итогах старой увидят новую игру и перейдут сами; писем не шлём, ссылки прежние
                notifier.RoomUpdated(roomId);

                return (ToRematchResponse(next, nextTeams), RematchRefusal.None);
            }

            // продолжение уже было: отдаём последнюю игру серии
            var latest = await LatestAfterAsync(roomId, ct);

            if (latest is null)
            {
                return (null, RematchRefusal.Busy);
            }

            return (ToRematchResponse(latest, await roomRepository.GetTeams(latest.Id, ct)), RematchRefusal.None);
        }

        // последняя игра серии этой комнаты
        private async Task<RoomEntity?> LatestAfterAsync(Guid roomId, CancellationToken ct)
        {
            var room = await roomRepository.GetRoomByIdAsync(roomId, ct);

            return room?.SeriesId is { } seriesId
                ? await roomRepository.GetLatestInSeriesAsync(seriesId, ct)
                : null;
        }

        private CreateRoomResponse ToRematchResponse(RoomEntity room, List<RoomTeamEntity> teams)
            => room.IsTeamGame
                ? room.ToCreateRoomResponse(null, teams.Select(ToTeamLink).ToList())
                : room.ToCreateRoomResponse(invites.Value.JoinLink(room.RoomCode));

        public async Task<RoomStateResponse?> GetRoomStateAsync(Guid roomId, CancellationToken ct)
        {
            var room = await roomRepository.GetRoomByIdAsync(roomId, ct);
            // комнату видят только её участники
            var me = room is null ? null : await FindMeAsync(roomId, ct);

            if (room is null || me is null)
            {
                return null;
            }

            var players = await roomParticipationRepository.GetParticipationsByRoomId(room.Id, ct);
            var match = await matchRepository.GetMatchByRoomId(room.Id, ct);
            var state = await BuildStateAsync(room, me, players, match, ct) with
            {
                GameNumber = room.GameNumber,
                PreviousRoomId = room.PreviousRoomId,
                // по нему фронт показывает «Вернуться»
                MyCanReturn = !me.IsActive && await CanReturnAsync(me, room, ct),
            };

            // на итогах видно, собрал ли ведущий игру новее
            if (room.Status == RoomStatus.Finished && room.SeriesId is { } seriesId
                && await roomRepository.GetLatestInSeriesAsync(seriesId, ct) is { } latest
                && latest.GameNumber > room.GameNumber)
            {
                state = state with
                {
                    NextRoomId = latest.Id,
                    NextGameNumber = latest.GameNumber,
                    NextRoomStatus = latest.Status,
                };
            }

            // ведущему в лобби игры N>1: сколько перешли и с чем играли в прошлый раз
            var isHost = room.HostUserId is not null && me.UserId == room.HostUserId;

            if (room.Status == RoomStatus.Waiting && room.PreviousRoomId is { } previousRoomId && isHost)
            {
                var previousMatch = await matchRepository.GetMatchByRoomId(previousRoomId, ct);

                state = state with
                {
                    PreviousPlayers = await roomParticipationRepository.CountActiveNonHostAsync(previousRoomId, room.HostUserId, ct),
                    ReturnedPlayers = players.Count(p => p.IsActive && p.UserId != room.HostUserId && p.OriginRoomPlayerId != null),
                    // у матчей до сохранения настроек подставлять нечего
                    PreviousSettings = previousMatch is { RequestedQuestionCount: > 0 }
                        ? new PreviousMatchSettings(previousMatch.CategoryIds, previousMatch.RequestedQuestionCount, previousMatch.RoomMode)
                        : null,
                };
            }

            return state;
        }

        // состояние глазами участника me, по нему же итоги в «Моих комнатах»
        private async Task<RoomStateResponse> BuildStateAsync(
            RoomEntity room,
            RoomPlayerEntity me,
            List<RoomPlayerEntity> players,
            MatchEntity? match,
            CancellationToken ct)
        {
            if (!room.IsTeamGame)
            {
                var (finalPlayers, isDraw) = room.Status == RoomStatus.Finished && match is not null
                    ? await FinalPlayersAsync(room, match, players, ct)
                    : (players.Select(p => p.ToRoomParticipationResponse(room.HostUserId)).ToList(), false);

                return new RoomStateResponse(
                    room.Id,
                    room.RoomCode,
                    room.Status,
                    match?.Id,
                    me.Id,
                    finalPlayers,
                    false,
                    null,
                    IsDraw: isDraw);
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
                .ToTeamResponses(room.Status, players, points, averageTime, t => isHost ? invites.Value.JoinLink(t.JoinCode) : null, match?.StartedAt);
            var captains = teams.Select(t => t.CaptainLinkId).ToHashSet();

            // ничья только на итогах: у двух первых игравших команд поровну очков и времени
            var teamDraw = room.Status == RoomStatus.Finished && match is not null && RoomMapper.LeadersTied(teams
                .Where(t => t.Members > 0)
                .OrderBy(t => t.Place ?? int.MaxValue)
                .Select(t => (t.Points ?? 0, t.AvgCorrectTimeMs))
                .ToList());

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
                teams,
                IsDraw: teamDraw);
        }

        // места без дележа: очки, потерявшие очки выходом в конце, меньше суммарное время верных, больше ответов, кто раньше вошёл;
        // ушедшие и убранные ещё в лобби матч не играли, как в рейтинге
        private async Task<(List<RoomParticipationResponse> Players, bool IsDraw)> FinalPlayersAsync(
            RoomEntity room,
            MatchEntity match,
            List<RoomPlayerEntity> players,
            CancellationToken ct)
        {
            var timings = RoomMapper.AnswerTimings(match, match.Questions, await matchRepository.GetAnswersAsync(match.Id, ct));
            RoomMapper.AnswerTiming Timing(RoomPlayerEntity p) => timings.GetValueOrDefault(p.Id) ?? RoomMapper.AnswerTiming.None;
            bool Keeps(RoomPlayerEntity p) => match.FinishedAt is { } finishedAt ? RoomMapper.KeepsScore(p, finishedAt) : p.IsActive;

            // оборвавшийся в окне возврата к финишу стоит наравне с активными
            var ranked = players
                .Where(p => RoomMapper.PlayedMatch(p, match.StartedAt))
                .OrderByDescending(p => p.Score)
                .ThenByDescending(Keeps)
                .ThenBy(p => Timing(p).CorrectTimeMs ?? 0)
                .ThenByDescending(p => Timing(p).Answered)
                .ThenBy(p => p.JoinedAt)
                .ToList();

            // на первое место претендуют только сохранившие очки: потерявший их выходом ничью не делает
            var leaders = ranked.Take(2).ToList();
            var isDraw = leaders.All(Keeps)
                && RoomMapper.LeadersTied(leaders.Select(p => (p.Score, Timing(p).CorrectTimeMs)).ToList());

            return (ranked
                .Select((p, i) => p.ToRoomParticipationResponse(room.HostUserId, place: i + 1, correctTimeMs: Timing(p).CorrectTimeMs))
                .ToList(), isDraw);
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
                    var players = await roomParticipationRepository.GetParticipationsByRoomId(room.Id, ct);
                    var match = await matchRepository.GetMatchByRoomId(room.Id, ct);
                    var played = match is null || RoomMapper.PlayedMatch(me, match.StartedAt);
                    results[room.Id] = (await BuildStateAsync(room, me, players, match, ct)).ToMyRoomResult(played);
                }
            }

            // после старта только игравшие команды, как на экране итогов
            var started = rooms.Where(r => r.IsTeamGame && r.Status != RoomStatus.Waiting).Select(r => r.Id).ToList();
            var playedTeams = started.Count > 0
                ? await roomParticipationRepository.GetPlayedTeamNumbersAsync(started, ct)
                : new Dictionary<Guid, HashSet<int>>();

            return rooms
                .Select(r => r.ToMyRoomResponse(
                    r.IsTeamGame ? null : invites.Value.JoinLink(r.RoomCode),
                    r.IsTeamGame ? MyRoomTeams(r, r.Status == RoomStatus.Waiting ? null : playedTeams.GetValueOrDefault(r.Id) ?? []) : null,
                    results.GetValueOrDefault(r.Id),
                    IsMyRoom(r)))
                .ToList();
        }

        public async Task<InviteResponse?> GetInviteAsync(string code, CancellationToken ct)
        {
            var invite = await roomRepository.FindInviteAsync(code.Trim().ToUpperInvariant(), ct);

            if (invite is not { Room: var room, Team: var team })
            {
                return null;
            }

            return new InviteResponse(room.IsTeamGame, room.Status, room.HostUser?.UserName, team?.Number, team?.Name, room.GameNumber);
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

            return await EnterRoomAsync(room, teamNumber, nickname, predecessor: null, ct);
        }

        // вход по ссылке или переход из прошлой игры серии (predecessor: участие в ней)
        private async Task<(RoomResponse? Room, JoinRefusal Refusal, bool Conflict)> EnterRoomAsync(
            RoomEntity room,
            int? teamNumber,
            string? nickname,
            RoomPlayerEntity? predecessor,
            CancellationToken ct)
        {
            // команды нужны только в лобби командной комнаты
            var teams = room.IsTeamGame && room.Status == RoomStatus.Waiting
                ? await roomRepository.GetTeams(room.Id, ct)
                : [];

            // своё участие только по токену
            var roomPlayer = await FindMeAsync(room.Id, ct);

            if (roomPlayer != null)
            {
                // без своей команды (убран или её удалили) в лобби возвращаются только по ссылке команды
                if (teams.Count > 0 && teamNumber is null && teams.All(t => t.Number != roomPlayer.TeamNumber))
                {
                    return (null, JoinRefusal.TeamLinkRequired, false);
                }

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

            // из прошлой игры переходят в свою команду, а если её уже нет, в самую маленькую
            if (predecessor is not null && teams.Count > 0)
            {
                teamNumber ??= teams.Any(t => t.Number == predecessor.TeamNumber)
                    ? predecessor.TeamNumber
                    : await SmallestTeamAsync(room.Id, teams, ct);
            }

            // в командную игру новые входят только по ссылке своей команды
            if (room.IsTeamGame && teamNumber is null)
            {
                return (null, JoinRefusal.TeamLinkRequired, false);
            }

            // аккаунт играет под логином, гость под своим ником
            var user = currentUser.KeycloakSubject is not null ? await GetOrCreateCurrentUserAsync(ct) : null;
            var name = user?.UserName
                ?? (nickname is null && predecessor is not null
                    ? await FreeNicknameAsync(room.Id, predecessor.Nickname, ct)
                    : nickname!.Trim());

            // логин аккаунта уникален сам, гость не берёт имя занятое в комнате
            if (name is null || user is null && await roomParticipationRepository.IsNicknameTaken(room.Id, name, ct))
            {
                return (null, JoinRefusal.NicknameTaken, false);
            }

            if (await IsRoomFullAsync(room, ct))
            {
                logger.LogInformation("Room with roomcode {roomcode} is full", room.RoomCode);

                return (null, JoinRefusal.Full, false);
            }

            var link = await roomParticipationRepository.CreateRoomPlayer(room, name, user, ct);

            if (predecessor is not null)
            {
                link.OriginRoomPlayerId = predecessor.OriginRoomPlayerId ?? predecessor.Id;
            }

            MoveToTeam(link, teamNumber);
            roomRepository.MarkChanged(room);

            if (!await TrySaveAsync(ct))
            {
                return (null, JoinRefusal.None, true);
            }

            logger.LogInformation("Player {name} joined room {roomCode}", name, room.RoomCode);

            notifier.RoomUpdated(room.Id);

            return (link.ToJoinRoomResponse(), JoinRefusal.None, false);
        }

        // формы нет, ник берём из прошлой игры; занят, тогда «Петя 2»…«Петя 9», null если заняты все
        private async Task<string?> FreeNicknameAsync(Guid roomId, string nickname, CancellationToken ct)
        {
            if (!await roomParticipationRepository.IsNicknameTaken(roomId, nickname, ct))
            {
                return nickname;
            }

            for (var i = 2; i <= 9; i++)
            {
                var suffix = $" {i}";
                var candidate = nickname[..Math.Min(nickname.Length, RoomHelper.GuestNicknameMaxLength - suffix.Length)] + suffix;

                if (!await roomParticipationRepository.IsNicknameTaken(roomId, candidate, ct))
                {
                    return candidate;
                }
            }

            return null;
        }

        // меньше активных, при равенстве меньший номер
        private async Task<int> SmallestTeamAsync(Guid roomId, List<RoomTeamEntity> teams, CancellationToken ct)
        {
            var players = await roomParticipationRepository.GetParticipationsByRoomId(roomId, ct);

            return teams
                .OrderBy(t => players.Count(p => p.IsActive && p.TeamNumber == t.Number))
                .ThenBy(t => t.Number)
                .First()
                .Number;
        }

        public async Task<(RoomResponse? Room, JoinRefusal Refusal)> FollowAsync(Guid roomId, CancellationToken ct)
        {
            // на итогах переходят разом: проигравший гонку за версию комнаты переходит заново
            for (var attempt = 1; ; attempt++)
            {
                var (room, refusal, conflict) = await TryFollowAsync(roomId, ct);

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

        // переход с итогов игры в последнюю игру серии, ник и команда сохраняются
        private async Task<(RoomResponse? Room, JoinRefusal Refusal, bool Conflict)> TryFollowAsync(Guid roomId, CancellationToken ct)
        {
            var old = await roomRepository.GetRoomByIdAsync(roomId, ct);

            if (old is null)
            {
                return (null, JoinRefusal.NotFound, false);
            }

            var me = await FindMeAsync(old.Id, ct);

            if (me is null)
            {
                return (null, JoinRefusal.NotParticipant, false);
            }

            var latest = old.SeriesId is { } seriesId
                ? await roomRepository.GetLatestInSeriesAsync(seriesId, ct)
                : null;

            if (latest is null || latest.GameNumber <= old.GameNumber)
            {
                return (null, JoinRefusal.NoNextGame, false);
            }

            // гость себя в новой игре находит по первому участию, аккаунт по логину
            var mine = currentUser.KeycloakSubject is not null
                ? await FindMeAsync(latest.Id, ct)
                : await roomParticipationRepository.FindByOriginAsync(latest.Id, me.OriginRoomPlayerId ?? me.Id, ct);

            if (mine is not null)
            {
                if (latest.Status == RoomStatus.Finished)
                {
                    return (mine.ToJoinRoomResponse(), JoinRefusal.None, false);
                }

                var teams = latest.IsTeamGame && latest.Status == RoomStatus.Waiting
                    ? await roomRepository.GetTeams(latest.Id, ct)
                    : [];

                // убранный или из удалённой команды без команды не остаётся
                int? teamNumber = teams.Count > 0 && teams.All(t => t.Number != mine.TeamNumber)
                    ? await SmallestTeamAsync(latest.Id, teams, ct)
                    : null;

                var (rejoined, conflict) = await RejoinAsync(mine, latest, teams, teamNumber, ct);

                // убранному ведущим окно возврата не обещаем: для него это просто идущая без него игра
                var refusal = rejoined is not null || conflict ? JoinRefusal.None
                    : latest.Status == RoomStatus.Waiting ? JoinRefusal.Full
                    : mine.ExitReason == ExitReason.Kicked ? JoinRefusal.Started
                    : JoinRefusal.ReturnExpired;

                return (rejoined, refusal, conflict);
            }

            return latest.Status switch
            {
                RoomStatus.InProgress => (null, JoinRefusal.Started, false),
                RoomStatus.Finished => (null, JoinRefusal.NextFinished, false),
                _ => await EnterRoomAsync(latest, teamNumber: null, nickname: null, predecessor: me, ct),
            };
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
                if (!await CanRejoinAsync(roomPlayer, room, ct))
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

            if (lobby is null || !lobby.Room.IsTeamGame)
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
            // «Команда N» могли занять переименованием: берём следующее свободное, из MaxTeams имён одно свободно всегда
            name = string.IsNullOrWhiteSpace(name)
                ? Enumerable.Range(number, MatchHelper.MaxTeams).Select(MatchHelper.DefaultTeamName).First(n => !lobby.IsNameTaken(n, number))
                : name.Trim();

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

        // ссылки команд только владельцу, участнику названия; played: номера игравших, null в лобби
        private List<TeamLinkResponse> MyRoomTeams(RoomEntity room, HashSet<int>? played)
        {
            var isHost = IsMyRoom(room);

            return room.Teams
                .Where(t => played is null || played.Contains(t.Number))
                .OrderBy(t => t.Number)
                .Select(t => isHost ? ToTeamLink(t) : new TeamLinkResponse(t.Number, t.Name, t.ExpectedPlayers, null))
                .ToList();
        }

        private bool IsMyRoom(RoomEntity room)
            => room.HostUser is not null && room.HostUser.KeycloakSubject == currentUser.KeycloakSubject;

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

        // до старта мешает только полная комната, место владельца за ним, в матче окно реконнекта
        private async Task<bool> CanRejoinAsync(RoomPlayerEntity roomPlayer, RoomEntity room, CancellationToken ct)
        {
            var isHost = room.HostUserId is not null && roomPlayer.UserId == room.HostUserId;

            return room.Status == RoomStatus.Waiting
                ? isHost || !await IsRoomFullAsync(room, ct)
                : await CanReconnectAsync(roomPlayer, room, ct);
        }

        // пустит ли вход неактивного: в лобби командной ещё нужна своя команда, иначе только по ссылке команды
        private async Task<bool> CanReturnAsync(RoomPlayerEntity me, RoomEntity room, CancellationToken ct)
        {
            if (room.Status == RoomStatus.Finished)
            {
                return false;
            }

            if (room.IsTeamGame && room.Status == RoomStatus.Waiting
                && (await roomRepository.GetTeams(room.Id, ct)).All(t => t.Number != me.TeamNumber))
            {
                return false;
            }

            return await CanRejoinAsync(me, room, ct);
        }

        // в идущий матч возвращается вышедший после старта, пока не истекло окно;
        // у оборвавшего связь окно не раньше старта: обрыв перед «Старт» не исключает из игры
        private async Task<bool> CanReconnectAsync(RoomPlayerEntity roomPlayer, RoomEntity room, CancellationToken ct)
        {
            var match = await matchRepository.GetMatchByRoomId(room.Id, ct);

            if (match is null || roomPlayer.FinishedAt is not { } finishedAt)
            {
                return false;
            }

            var disconnected = roomPlayer.ExitReason == ExitReason.Disconnected;

            // без команды в командном матче играть не за кого: её удалили, пока не было связи
            if ((!disconnected && finishedAt < match.StartedAt) || (room.IsTeamGame && roomPlayer.TeamNumber is null))
            {
                return false;
            }

            var windowFrom = disconnected && finishedAt < match.StartedAt ? match.StartedAt : finishedAt;

            return UtcNow - windowFrom <= TimeSpan.FromSeconds(RoomHelper.ReconnectWindowSeconds);
        }
    }
}
