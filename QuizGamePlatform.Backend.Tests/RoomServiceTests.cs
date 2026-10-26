using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Claims;
using QuizGamePlatform.Backend.Api.Controllers;
using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.Auth;
using QuizGamePlatform.Backend.Application.Contracts.Enums;
using QuizGamePlatform.Backend.Application.Contracts.Room;
using QuizGamePlatform.Backend.Application.Enums;
using QuizGamePlatform.Backend.Application.Mappers;
using QuizGamePlatform.Backend.Application.Options;
using QuizGamePlatform.Backend.Application.Services;
using QuizGamePlatform.Backend.Core.Abstractions;
using QuizGamePlatform.Backend.DataAccess;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.Tests
{
    public class RoomServiceTests
    {
        private readonly Mock<IRoomRepository> _roomRepo = new();
        private readonly Mock<IRoomParticipationRepository> _participationRepo = new();
        private readonly Mock<IUserRepository> _userRepo = new();
        private readonly Mock<IMatchRepository> _matchRepo = new();
        private readonly Mock<IRoomHelper> _roomHelper = new();
        private readonly Mock<IGameNotifier> _notifier = new();
        private readonly Mock<IInviteMailer> _mailer = new();
        private readonly FakeTimeProvider _time = new();
        private readonly HttpContextAccessor _http = new() { HttpContext = new DefaultHttpContext() };
        private readonly CurrentUserContext _currentUser;
        private readonly RoomService _sut;
        private readonly Mock<ApplicationDbContext> _context;

        public RoomServiceTests()
        {
            // сохранение без БД
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().Options;
            _context = new Mock<ApplicationDbContext>(options);
            _context.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            // сервис и контроллер видят одного пользователя
            _currentUser = new CurrentUserContext(_http);

            _sut = new RoomService(
                _roomRepo.Object,
                _participationRepo.Object,
                _userRepo.Object,
                _matchRepo.Object,
                _currentUser,
                _notifier.Object,
                _mailer.Object,
                Options.Create(new InviteOptions { JoinBaseUrl = "http://quiz/", SmtpHost = "smtp", From = "quiz@corp" }),
                _roomHelper.Object,
                _context.Object,
                Mock.Of<ILogger<RoomService>>(),
                _time);
        }

        private DateTime Now => _time.GetUtcNow().UtcDateTime;

        private RoomController CreateController(Mock<IGuestTokenService> tokens) =>
            new(_sut, _currentUser, tokens.Object)
            {
                ControllerContext = new ControllerContext { HttpContext = _http.HttpContext! },
            };

        private void SignIn(string? authenticationType, string subject, string? userName = null)
        {
            var claims = new List<Claim> { new("sub", subject) };
            if (userName is not null)
            {
                claims.Add(new Claim("preferred_username", userName));
            }

            _http.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType)),
            };
        }

        private void AsGuest(Guid roomPlayerId) => SignIn(AuthenticationSchemes.Guest, roomPlayerId.ToString());

        // аккаунт с логином alice
        private UserEntity AsRegistered(string subject = "alice-subject")
        {
            SignIn("Bearer", subject, "alice");
            var user = new UserEntity { Id = Guid.NewGuid(), KeycloakSubject = subject, UserName = "alice" };
            _userRepo.Setup(r => r.GetOrCreateUserAsync(subject, "alice", It.IsAny<CancellationToken>())).ReturnsAsync(user);

            return user;
        }

        private static RoomPlayerEntity Participant(RoomEntity room, bool isActive, DateTime? finishedAt = null)
            => new()
            {
                Id = Guid.NewGuid(),
                RoomId = room.Id,
                Room = room,
                Nickname = "bob",
                IsActive = isActive,
                FinishedAt = finishedAt
            };

        private RoomEntity Setup(RoomStatus status)
        {
            var room = new RoomEntity { Id = Guid.NewGuid(), RoomCode = "CODE", Status = status };

            _roomRepo.Setup(r => r.GetRoomByRoomCodeAsync("CODE", It.IsAny<CancellationToken>())).ReturnsAsync(room);
            _roomRepo.Setup(r => r.GetRoomByIdAsync(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(room);

            return room;
        }

        private void MatchStarted(RoomEntity room, int secondsAgo)
            => _matchRepo.Setup(r => r.GetMatchByRoomId(room.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MatchEntity { Id = Guid.NewGuid(), RoomId = room.Id, StartedAt = Now.AddSeconds(-secondsAgo) });

        private void Mine(RoomEntity room, RoomPlayerEntity participant)
            => _participationRepo.Setup(r => r.FindParticipant(room.Id, participant.Id, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(participant);

        // создание комнаты

        [Fact]
        public async Task CreateRoom_HostBecomesPlayerUnderLogin()
        {
            var user = AsRegistered();
            var room = new RoomEntity { Id = Guid.NewGuid(), RoomCode = "CODE" };
            _roomHelper.Setup(h => h.GenerateRoomCode(It.IsAny<int>())).Returns("CODE");
            _roomRepo.Setup(r => r.CreateRoomAsync("CODE", user, It.IsAny<CancellationToken>())).ReturnsAsync(room);

            var result = await _sut.CreateRoomAsync(null, CancellationToken.None);

            Assert.Equal("CODE", result.RoomCode);
            _participationRepo.Verify(r => r.CreateRoomPlayer(room, "alice", user, It.IsAny<CancellationToken>()), Times.Once);
        }

        // удаление

        [Fact]
        public async Task DeleteRoom_NotHost_ForbiddenAndNotDeleted()
        {
            var room = Setup(RoomStatus.Waiting);
            AsRegistered("not-host");
            var controller = CreateController(new Mock<IGuestTokenService>());

            var result = await controller.DeleteExistingRoom(room.Id, CancellationToken.None);

            Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
            _roomRepo.Verify(r => r.RemoveRoom(It.IsAny<RoomEntity>()), Times.Never);
        }

        [Theory]
        [InlineData(RoomStatus.Waiting, true)]
        [InlineData(RoomStatus.InProgress, false)]
        [InlineData(RoomStatus.Finished, false)]
        public async Task DeleteRoom_Host_OnlyBeforeStart(RoomStatus status, bool deleted)
        {
            var room = Setup(status);
            AsRegistered("host");
            _roomRepo.Setup(r => r.IsHost(room.Id, "host", It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var controller = CreateController(new Mock<IGuestTokenService>());

            var result = await controller.DeleteExistingRoom(room.Id, CancellationToken.None);

            if (deleted)
            {
                Assert.IsType<NoContentResult>(result);
                _roomRepo.Verify(r => r.RemoveRoom(room), Times.Once);
                _notifier.Verify(n => n.RoomUpdated(room.Id), Times.Once);
            }
            else
            {
                Assert.IsType<ConflictObjectResult>(result);
                _roomRepo.Verify(r => r.RemoveRoom(It.IsAny<RoomEntity>()), Times.Never);
                _notifier.Verify(n => n.RoomUpdated(It.IsAny<Guid>()), Times.Never);
            }
        }

        // комнату успели запустить или изменить параллельно
        [Fact]
        public async Task DeleteRoom_ConcurrentChange_Conflict()
        {
            var room = Setup(RoomStatus.Waiting);
            AsRegistered("host");
            _roomRepo.Setup(r => r.IsHost(room.Id, "host", It.IsAny<CancellationToken>())).ReturnsAsync(true);
            _context.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new DbUpdateConcurrencyException());
            var controller = CreateController(new Mock<IGuestTokenService>());

            var result = await controller.DeleteExistingRoom(room.Id, CancellationToken.None);

            Assert.IsType<ConflictObjectResult>(result);
            _notifier.Verify(n => n.RoomUpdated(It.IsAny<Guid>()), Times.Never);
        }

        // состояние комнаты

        [Fact]
        public async Task RoomState_Member_SeesCodeMatchAndPlayers()
        {
            var room = Setup(RoomStatus.InProgress);
            room.HostUserId = Guid.NewGuid();
            var host = new RoomPlayerEntity { Id = Guid.NewGuid(), RoomId = room.Id, Nickname = "alice", UserId = room.HostUserId };
            var me = Participant(room, isActive: true);
            AsGuest(me.Id);
            Mine(room, me);
            _participationRepo.Setup(r => r.GetParticipationsByRoomId(room.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RoomPlayerEntity> { host, me });
            MatchStarted(room, secondsAgo: 10);

            var state = await _sut.GetRoomStateAsync(room.Id, CancellationToken.None);

            Assert.NotNull(state);
            Assert.Equal("CODE", state!.RoomCode);
            Assert.NotNull(state.MatchId);
            Assert.Equal(me.Id, state.MyRoomPlayerLinkId);
            Assert.True(state.Players[0].IsHost);
            Assert.False(state.Players[0].IsGuest);
            Assert.True(state.Players[1].IsGuest);
            Assert.False(state.IsTeamGame);
            Assert.Null(state.Teams);
        }

        [Fact]
        public async Task RoomState_NotMember_ReturnsNull()
        {
            var room = Setup(RoomStatus.Waiting);
            AsGuest(Guid.NewGuid());

            Assert.Null(await _sut.GetRoomStateAsync(room.Id, CancellationToken.None));
        }

        // вход нового игрока

        [Theory]
        [InlineData(null, JoinRefusal.NotFound)] // комнаты нет
        [InlineData(RoomStatus.Finished, JoinRefusal.Finished)]
        public async Task Join_RoomNotFoundOrFinished_Refused(RoomStatus? status, JoinRefusal expected)
        {
            if (status is { } existing)
            {
                Setup(existing);
            }
            else
            {
                _roomRepo.Setup(r => r.GetRoomByRoomCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((RoomEntity?)null);
            }

            var (result, refusal) = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", CancellationToken.None);

            Assert.Null(result);
            Assert.Equal(expected, refusal);
        }

        // ник без пробелов по краям, регистр как ввёл гость
        [Fact]
        public async Task Join_NewGuest_NicknameTrimmedCaseKept()
        {
            var room = Setup(RoomStatus.Waiting);
            var link = Participant(room, isActive: true);
            _participationRepo.Setup(r => r.CreateRoomPlayer(room, "Bob", null, It.IsAny<CancellationToken>())).ReturnsAsync(link);

            var (result, _) = await _sut.JoinToRoomByRoomCodeAsync("  Bob ", " code ", CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(link.Id, result!.RoomPlayerLinkId);
            _userRepo.Verify(r => r.GetOrCreateUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _notifier.Verify(n => n.RoomUpdated(room.Id), Times.Once);
        }

        [Fact]
        public async Task Join_NewRegistered_PlaysUnderLoginWithoutNickname()
        {
            var room = Setup(RoomStatus.Waiting);
            var user = AsRegistered();
            _participationRepo.Setup(r => r.CreateRoomPlayer(room, "alice", user, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Participant(room, isActive: true));

            var (result, _) = await _sut.JoinToRoomByRoomCodeAsync(null, "CODE", CancellationToken.None);

            Assert.NotNull(result);
            _participationRepo.Verify(r => r.CreateRoomPlayer(room, "alice", user, It.IsAny<CancellationToken>()), Times.Once);
            _notifier.Verify(n => n.RoomUpdated(room.Id), Times.Once);
        }

        [Fact]
        public async Task Join_NewPlayer_MatchInProgress_StartedEvenIfNicknameTaken()
        {
            var room = Setup(RoomStatus.InProgress);
            _participationRepo.Setup(r => r.IsNicknameTaken(room.Id, "bob", It.IsAny<CancellationToken>())).ReturnsAsync(true);

            var (result, refusal) = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", CancellationToken.None);

            Assert.Null(result);
            Assert.Equal(JoinRefusal.Started, refusal);
        }

        [Theory]
        [InlineData(10, false)]
        [InlineData(9, true)]
        public async Task Join_NewPlayer_RespectsSlotLimit(int takenSlots, bool shouldJoin)
        {
            var room = Setup(RoomStatus.Waiting);
            _participationRepo.Setup(r => r.CountTakenSlots(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(takenSlots);
            _participationRepo.Setup(r => r.CreateRoomPlayer(room, "bob", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Participant(room, isActive: true));

            var (result, _) = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", CancellationToken.None);

            Assert.Equal(shouldJoin, result is not null);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Join_NewParticipant_IssuesTokenOnlyToGuest(bool registered)
        {
            var room = Setup(RoomStatus.Waiting);
            var link = Participant(room, isActive: true);
            _participationRepo.Setup(r => r.CreateRoomPlayer(room, It.IsAny<string>(), It.IsAny<UserEntity?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(link);
            if (registered)
            {
                AsRegistered();
            }
            var tokens = new Mock<IGuestTokenService>(MockBehavior.Strict);
            if (!registered)
            {
                tokens.Setup(t => t.Issue(link.Id, room.Id, "bob"))
                    .Returns(new GuestTokenResult("guest-token", Now.AddHours(4)));
            }
            var controller = CreateController(tokens);

            var result = await controller.JoinToRoom(new(registered ? null : "bob", "CODE"), CancellationToken.None);

            var response = Assert.IsType<JoinToRoomResponse>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal(link.Id, response.Room.RoomPlayerLinkId);
            Assert.Equal(registered ? null : "guest-token", response.GuestAccessToken);
            Assert.Equal(registered ? (DateTime?)null : Now.AddHours(4), response.GuestTokenExpiresAtUtc);
        }

        [Fact]
        public async Task Join_InvalidProvidedToken_ReturnsUnauthorized()
        {
            _http.HttpContext!.Request.Headers.Authorization = "Bearer expired-token";
            var tokens = new Mock<IGuestTokenService>(MockBehavior.Strict);
            var controller = CreateController(tokens);

            var result = await controller.JoinToRoom(new("bob", "CODE"), CancellationToken.None);

            Assert.IsType<UnauthorizedObjectResult>(result);
            _roomRepo.Verify(
                r => r.GetRoomByRoomCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // чужое имя не даёт гостю участие
        [Fact]
        public async Task Join_GuestNameTakenInRoom_ConflictWithoutIssuingToken()
        {
            var room = Setup(RoomStatus.Waiting);
            _participationRepo.Setup(r => r.IsNicknameTaken(room.Id, "bob", It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var tokens = new Mock<IGuestTokenService>(MockBehavior.Strict);
            var controller = CreateController(tokens);

            var result = await controller.JoinToRoom(new("bob", "CODE"), CancellationToken.None);

            Assert.IsType<ConflictObjectResult>(result);
            _participationRepo.Verify(
                r => r.CreateRoomPlayer(It.IsAny<RoomEntity>(), It.IsAny<string>(), It.IsAny<UserEntity?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // реконнект

        [Fact]
        public async Task Join_OwnGuestParticipation_ReconnectsAndIssuesTokenForSameParticipation()
        {
            var room = Setup(RoomStatus.Waiting);
            var existing = Participant(room, isActive: false, finishedAt: Now.AddSeconds(-20));
            AsGuest(existing.Id);
            Mine(room, existing);
            // ник занят им же
            _participationRepo.Setup(r => r.IsNicknameTaken(room.Id, "bob", It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var tokens = new Mock<IGuestTokenService>(MockBehavior.Strict);
            tokens.Setup(t => t.Issue(existing.Id, room.Id, "bob"))
                .Returns(new GuestTokenResult("guest-token", Now.AddHours(4)));
            var controller = CreateController(tokens);

            var result = await controller.JoinToRoom(new("bob", "CODE"), CancellationToken.None);

            var response = Assert.IsType<JoinToRoomResponse>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal(existing.Id, response.Room.RoomPlayerLinkId);
            Assert.Equal("guest-token", response.GuestAccessToken);
            Assert.True(existing.IsActive);
            Assert.Null(existing.FinishedAt);
        }

        [Fact]
        public async Task Join_RegisteredOwnParticipation_InProgress_WithinWindow_Reconnects()
        {
            var room = Setup(RoomStatus.InProgress);
            MatchStarted(room, secondsAgo: 60);
            var left = Participant(room, isActive: false, finishedAt: Now.AddSeconds(-20));
            AsRegistered("registered-subject");
            _participationRepo.Setup(r => r.FindParticipant(room.Id, null, "registered-subject", It.IsAny<CancellationToken>()))
                .ReturnsAsync(left);

            var (result, _) = await _sut.JoinToRoomByRoomCodeAsync(null, "CODE", CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(left.Id, result!.RoomPlayerLinkId);
            Assert.True(left.IsActive);
        }

        [Fact]
        public async Task Rejoin_AlreadyActive_ReturnsResponse()
        {
            var room = Setup(RoomStatus.InProgress);
            var existing = Participant(room, isActive: true);
            AsGuest(existing.Id);
            Mine(room, existing);

            var (result, _) = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", CancellationToken.None);

            Assert.NotNull(result);
            Assert.True(result!.IsActive);
            // состав не изменился, рассылать нечего
            _notifier.Verify(n => n.RoomUpdated(It.IsAny<Guid>()), Times.Never);
        }

        // вышедший возвращается, только если вышел после старта и окно не истекло;
        // у оборвавшего связь окно возврата не раньше старта
        [Theory]
        [InlineData(null, 60, -20, true)]                    // вышел в матче, ещё в окне
        [InlineData(null, 60, -41, false)]                   // вышел в матче, окно истекло
        [InlineData(null, 10, -20, false)]                   // вышел ещё до старта
        [InlineData(ExitReason.Disconnected, 10, -20, true)]  // связь пропала за 10 с до старта, со старта прошло 10 с
        [InlineData(ExitReason.Disconnected, 41, -50, false)] // со старта прошло больше окна
        [InlineData(ExitReason.Disconnected, 60, -20, true)]  // обрыв уже в матче, окно от обрыва
        public async Task Rejoin_InProgress_WindowByExitReason(ExitReason? exitReason, int startedAgo, int leftAt, bool returns)
        {
            var room = Setup(RoomStatus.InProgress);
            MatchStarted(room, startedAgo);
            var left = Participant(room, isActive: false, finishedAt: Now.AddSeconds(leftAt));
            left.ExitReason = exitReason;
            AsGuest(left.Id);
            Mine(room, left);

            var (result, _) = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", CancellationToken.None);

            Assert.Equal(returns, result is not null);
            Assert.Equal(returns, left.IsActive);
            _notifier.Verify(n => n.RoomUpdated(room.Id), returns ? Times.Once() : Times.Never());
        }

        // команду удалили, пока не было связи: в командный матч играть не за кого
        [Fact]
        public async Task Reconnect_TeamMatch_DisconnectedBeforeStartWithoutTeam_NotReturned()
        {
            var (room, _, players) = TeamRoom(RoomStatus.InProgress);
            MatchStarted(room, secondsAgo: 5);
            var lost = InTeam(room, players, null, isActive: false);
            lost.ExitReason = ExitReason.Disconnected;
            lost.FinishedAt = Now.AddSeconds(-10);
            _participationRepo.Setup(r => r.FindParticipant(room.Id, lost.Id, null, It.IsAny<CancellationToken>())).ReturnsAsync(lost);

            Assert.False(await _sut.ReconnectAsync(room.Id, lost.Id, CancellationToken.None));
            Assert.False(lost.IsActive);
        }

        [Theory]
        [InlineData(3, true)]
        [InlineData(10, false)] // пока его не было комнату заполнили
        public async Task Rejoin_WaitingRoom_IgnoresWindowButRespectsLimit(int takenSlots, bool returns)
        {
            var room = Setup(RoomStatus.Waiting);
            var left = Participant(room, isActive: false, finishedAt: Now.AddSeconds(-500));
            AsGuest(left.Id);
            Mine(room, left);
            _participationRepo.Setup(r => r.CountTakenSlots(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(takenSlots);

            var (result, _) = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", CancellationToken.None);

            Assert.Equal(returns, result is not null);
            Assert.Equal(returns, left.IsActive);
            _notifier.Verify(n => n.RoomUpdated(room.Id), returns ? Times.Once() : Times.Never());
        }

        [Fact]
        public async Task Rejoin_HostKeepsSlotInFullWaitingRoom()
        {
            var room = Setup(RoomStatus.Waiting);
            var user = AsRegistered();
            room.HostUserId = user.Id;
            var host = Participant(room, isActive: false, finishedAt: Now.AddSeconds(-30));
            host.UserId = user.Id;
            _participationRepo.Setup(r => r.FindParticipant(room.Id, null, "alice-subject", It.IsAny<CancellationToken>())).ReturnsAsync(host);
            _participationRepo.Setup(r => r.CountTakenSlots(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(5);

            var (result, _) = await _sut.JoinToRoomByRoomCodeAsync(null, "CODE", CancellationToken.None);

            Assert.NotNull(result);
            Assert.True(host.IsActive);
        }

        [Fact]
        public async Task Join_AccountEntersUnderLoginEvenIfGuestTookSameName()
        {
            var room = Setup(RoomStatus.Waiting);
            var user = AsRegistered();
            _participationRepo.Setup(r => r.IsNicknameTaken(room.Id, "alice", It.IsAny<CancellationToken>())).ReturnsAsync(true);
            _participationRepo.Setup(r => r.CreateRoomPlayer(room, "alice", user, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Participant(room, isActive: true));

            var (result, _) = await _sut.JoinToRoomByRoomCodeAsync(null, "CODE", CancellationToken.None);

            Assert.NotNull(result);
            _participationRepo.Verify(
                r => r.IsNicknameTaken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // параллельный вход изменил комнату: вход повторяется с чистого листа
        [Fact]
        public async Task Join_ConcurrentRoomChange_RetriesAndJoins()
        {
            var room = Setup(RoomStatus.Waiting);
            _participationRepo.Setup(r => r.CreateRoomPlayer(room, "bob", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Participant(room, isActive: true));
            _context.SetupSequence(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DbUpdateConcurrencyException())
                .ReturnsAsync(1);

            var (result, _) = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", CancellationToken.None);

            Assert.NotNull(result);
            _roomRepo.Verify(r => r.ForgetChanges(), Times.Once);
            _notifier.Verify(n => n.RoomUpdated(room.Id), Times.Once);
        }

        // комнату так и не удалось занять или её удалили
        [Fact]
        public async Task Join_ConflictEveryTime_NotJoinedWithoutToken()
        {
            var room = Setup(RoomStatus.Waiting);
            _participationRepo.Setup(r => r.CreateRoomPlayer(room, "bob", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Participant(room, isActive: true));
            _context.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new DbUpdateConcurrencyException());
            var tokens = new Mock<IGuestTokenService>(MockBehavior.Strict);
            var controller = CreateController(tokens);

            var result = await controller.JoinToRoom(new("bob", "CODE"), CancellationToken.None);

            Assert.IsType<ConflictObjectResult>(result);
            _roomRepo.Verify(r => r.MarkChanged(room), Times.Exactly(20));
            _notifier.Verify(n => n.RoomUpdated(It.IsAny<Guid>()), Times.Never);
        }

        // рейтинги

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Leaderboard_IndividualAndTeamGamesSeparately(bool teamGames)
        {
            var controller = CreateController(new Mock<IGuestTokenService>());
            var entry = new GlobalLeaderboardEntry("alice", 3, 1, 100);
            _participationRepo.Setup(r => r.GetLeaderboard(teamGames, It.IsAny<CancellationToken>())).ReturnsAsync([entry]);

            var result = teamGames
                ? await controller.GetTeamGamesLeaderboard(CancellationToken.None)
                : await controller.GetGlobalLeaderboard(CancellationToken.None);

            Assert.Equal([entry], Assert.IsType<List<GlobalLeaderboardEntry>>(Assert.IsType<OkObjectResult>(result).Value));
        }

        // выход из комнаты

        [Fact]
        public async Task Leave_NotParticipant_ReturnsNull()
        {
            AsGuest(Guid.NewGuid());

            var result = await _sut.LeaveRoom(Guid.NewGuid(), ExitReason.Normal, CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task Leave_AlreadyInactive_ReturnsNull()
        {
            var room = Setup(RoomStatus.InProgress);
            var left = Participant(room, isActive: false, finishedAt: Now);
            AsGuest(left.Id);
            Mine(room, left);

            var result = await _sut.LeaveRoom(room.Id, ExitReason.Normal, CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task Leave_ActivePlayer_SetsFinishedAtNow_AndInactive()
        {
            var room = Setup(RoomStatus.InProgress);
            var active = Participant(room, isActive: true);
            AsGuest(active.Id);
            Mine(room, active);

            var result = await _sut.LeaveRoom(room.Id, ExitReason.Disconnected, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(active.Id, result!.RoomPlayerLinkId);
            Assert.False(active.IsActive);
            Assert.Equal(Now, active.FinishedAt);
            Assert.Equal(ExitReason.Disconnected, active.ExitReason);
            _roomRepo.Verify(r => r.MarkChanged(room), Times.Once);
            _notifier.Verify(n => n.RoomUpdated(room.Id), Times.Once);
        }

        // после матча очки и состав уже итоговые
        [Fact]
        public async Task Leave_FinishedRoom_ReturnsNullAndKeepsActive()
        {
            var room = Setup(RoomStatus.Finished);
            var active = Participant(room, isActive: true);
            AsGuest(active.Id);
            Mine(room, active);

            var result = await _sut.LeaveRoom(room.Id, ExitReason.Normal, CancellationToken.None);

            Assert.Null(result);
            Assert.True(active.IsActive);
            _notifier.Verify(n => n.RoomUpdated(It.IsAny<Guid>()), Times.Never);
        }

        // командный режим

        // у команды n код ссылки TEAMn
        private (RoomEntity Room, List<RoomTeamEntity> Teams, List<RoomPlayerEntity> Players) TeamRoom(
            RoomStatus status = RoomStatus.Waiting,
            int teamCount = 2)
        {
            var room = Setup(status);
            room.IsTeamGame = true;
            room.HostUserId = Guid.NewGuid();
            var teams = Enumerable.Range(1, teamCount)
                .Select(n => new RoomTeamEntity { RoomId = room.Id, Room = room, Number = n, Name = $"Команда {n}", JoinCode = $"TEAM{n}" })
                .ToList();
            var players = new List<RoomPlayerEntity>();
            _roomRepo.Setup(r => r.GetTeams(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(teams);
            _roomRepo.Setup(r => r.GetTeamByJoinCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string code, CancellationToken _) => teams.FirstOrDefault(t => t.JoinCode == code));
            _participationRepo.Setup(r => r.GetParticipationsByRoomId(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(players);

            return (room, teams, players);
        }

        private RoomPlayerEntity InTeam(
            RoomEntity room,
            List<RoomPlayerEntity> players,
            int? team,
            bool isActive = true,
            Guid? userId = null,
            int joinedAgo = 0)
        {
            var player = Participant(room, isActive);
            player.TeamNumber = team;
            player.UserId = userId;
            player.JoinedAt = Now.AddSeconds(-joinedAgo);
            player.TeamJoinedAt = team is null ? null : player.JoinedAt;
            players.Add(player);

            return player;
        }

        // текущий пользователь аккаунт alice в этой команде
        private RoomPlayerEntity MeAccount(RoomEntity room, List<RoomPlayerEntity> players, int team, bool isHost = false, int joinedAgo = 0)
        {
            var user = AsRegistered();
            if (isHost)
            {
                room.HostUserId = user.Id;
            }
            var me = InTeam(room, players, team, userId: user.Id, joinedAgo: joinedAgo);
            _participationRepo.Setup(r => r.FindParticipant(room.Id, null, "alice-subject", It.IsAny<CancellationToken>())).ReturnsAsync(me);

            return me;
        }

        private RoomPlayerEntity MeGuest(RoomEntity room, List<RoomPlayerEntity> players, int team, int joinedAgo = 0)
        {
            var me = InTeam(room, players, team, joinedAgo: joinedAgo);
            AsGuest(me.Id);
            Mine(room, me);

            return me;
        }

        private void LobbySaved(RoomEntity room, bool saved = true)
        {
            _roomRepo.Verify(r => r.MarkChanged(room), saved ? Times.Once() : Times.Never());
            _notifier.Verify(n => n.RoomUpdated(room.Id), saved ? Times.Once() : Times.Never());
        }

        // создание

        [Fact]
        public async Task CreateRoom_TeamGame_TeamLinksInResponseAndMailToHost()
        {
            var user = AsRegistered();
            ((ClaimsIdentity)_http.HttpContext!.User.Identity!).AddClaim(new Claim("email", "alice@corp.local"));
            var room = new RoomEntity { Id = Guid.NewGuid(), RoomCode = "CODE" };
            var hostLink = new RoomPlayerEntity { Id = Guid.NewGuid(), Room = room };
            _roomHelper.SetupSequence(h => h.GenerateRoomCode(It.IsAny<int>())).Returns("CODE").Returns("T1").Returns("T2").Returns("T3");
            _roomRepo.Setup(r => r.CreateRoomAsync("CODE", user, It.IsAny<CancellationToken>())).ReturnsAsync(room);
            _participationRepo.Setup(r => r.CreateRoomPlayer(room, "alice", user, It.IsAny<CancellationToken>())).ReturnsAsync(hostLink);
            List<TeamLinkResponse>? mailed = null;
            _mailer.Setup(m => m.SendTeamLinksAsync("alice@corp.local", "CODE", It.IsAny<List<TeamLinkResponse>>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, List<TeamLinkResponse>, CancellationToken>((_, _, links, _) => mailed = links);

            var result = await _sut.CreateRoomAsync(
                new CreateRoomRequest(true, [new(" Бухгалтерия ", 8), new(null), new("IT", 5)]),
                CancellationToken.None);

            Assert.True(result!.IsTeamGame);
            Assert.Null(result.JoinLink); // общей ссылки у командной игры нет
            var expected = new[]
            {
                new TeamLinkResponse(1, "Бухгалтерия", 8, "http://quiz/join/T1"),
                new TeamLinkResponse(2, "Команда 2", null, "http://quiz/join/T2"),
                new TeamLinkResponse(3, "IT", 5, "http://quiz/join/T3"),
            };
            Assert.Equal(expected, result.Teams);
            Assert.Equal(expected, mailed);
            Assert.Equal(1, hostLink.TeamNumber);
        }

        [Fact]
        public async Task CreateRoom_Individual_RoomLinkWithoutMail()
        {
            var user = AsRegistered();
            var room = new RoomEntity { Id = Guid.NewGuid(), RoomCode = "CODE" };
            _roomHelper.Setup(h => h.GenerateRoomCode(It.IsAny<int>())).Returns("CODE");
            _roomRepo.Setup(r => r.CreateRoomAsync("CODE", user, It.IsAny<CancellationToken>())).ReturnsAsync(room);

            var result = await _sut.CreateRoomAsync(null, CancellationToken.None);

            Assert.Equal("http://quiz/join/CODE", result!.JoinLink);
            Assert.Null(result.Teams);
            _mailer.VerifyNoOtherCalls();
        }

        public static TheoryData<List<NewTeamRequest>> InvalidTeams => new()
        {
            new List<NewTeamRequest> { new("A") },
            Enumerable.Range(1, 6).Select(i => new NewTeamRequest($"T{i}")).ToList(),
            new List<NewTeamRequest> { new("IT"), new("it") },                 // повтор без учёта регистра
            new List<NewTeamRequest> { new(null), new("Команда 1") },          // совпало с названием по умолчанию
            new List<NewTeamRequest> { new(new string('x', 31)), new("B") },
            new List<NewTeamRequest> { new("A", 0), new("B") },
            new List<NewTeamRequest> { new("A", 20), new("B", 11) },           // ждут больше 30
        };

        [Theory]
        [MemberData(nameof(InvalidTeams))]
        public async Task CreateRoom_InvalidTeams_NotCreated(List<NewTeamRequest> teams)
        {
            AsRegistered();

            var result = await _sut.CreateRoomAsync(new CreateRoomRequest(true, teams), CancellationToken.None);

            Assert.Null(result);
            _roomRepo.Verify(r => r.CreateRoomAsync(It.IsAny<string>(), It.IsAny<UserEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        // вход по ссылке

        [Fact]
        public async Task Join_TeamLink_JoinsThatTeam()
        {
            var (room, _, _) = TeamRoom();
            var link = Participant(room, isActive: true);
            _participationRepo.Setup(r => r.CreateRoomPlayer(room, "bob", null, It.IsAny<CancellationToken>())).ReturnsAsync(link);

            var (result, _) = await _sut.JoinToRoomByRoomCodeAsync("bob", " team2 ", CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(2, result!.TeamNumber);
            Assert.Equal(Now, link.TeamJoinedAt);
            LobbySaved(room);
        }

        // ник не вводит заново, он есть в токене
        [Fact]
        public async Task Join_GuestWithTokenWithoutNickname_FollowsOtherTeamLink()
        {
            var (room, _, players) = TeamRoom();
            var me = MeGuest(room, players, 1);
            _http.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim("sub", me.Id.ToString()), new Claim("nickname", "bob")], AuthenticationSchemes.Guest)),
            };
            var tokens = new Mock<IGuestTokenService>();
            tokens.Setup(t => t.Issue(me.Id, room.Id, "bob")).Returns(new GuestTokenResult("guest-token", Now.AddHours(4)));

            var result = await CreateController(tokens).JoinToRoom(new(null, "TEAM2"), CancellationToken.None);

            var response = Assert.IsType<JoinToRoomResponse>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal(2, response.Room.TeamNumber);
        }

        [Fact]
        public async Task Join_TeamRoomByRoomCode_NewPlayerNeedsTeamLink()
        {
            TeamRoom();
            var controller = CreateController(new Mock<IGuestTokenService>(MockBehavior.Strict));

            var result = await controller.JoinToRoom(new("bob", "CODE"), CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
            _participationRepo.Verify(
                r => r.CreateRoomPlayer(It.IsAny<RoomEntity>(), It.IsAny<string>(), It.IsAny<UserEntity?>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Theory]
        [InlineData(30, false)]
        [InlineData(29, true)]
        public async Task Join_TeamRoom_LimitIs30(int takenSlots, bool shouldJoin)
        {
            var (room, _, _) = TeamRoom();
            _participationRepo.Setup(r => r.CountTakenSlots(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(takenSlots);
            _participationRepo.Setup(r => r.CreateRoomPlayer(room, "bob", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Participant(room, isActive: true));

            var (result, _) = await _sut.JoinToRoomByRoomCodeAsync("bob", "TEAM1", CancellationToken.None);

            Assert.Equal(shouldJoin, result is not null);
        }

        [Theory]
        [InlineData(2, "CODE", 2)]  // вернулся в свою
        [InlineData(2, "TEAM1", 1)] // по ссылке другой команды
        [InlineData(null, "TEAM1", 1)] // убран или команду удалили, вернулся по ссылке
        public async Task Rejoin_TeamLobby_OwnOrLinkedTeam(int? ownTeam, string code, int expected)
        {
            var (room, _, players) = TeamRoom();
            InTeam(room, players, 2);
            var left = InTeam(room, players, ownTeam, isActive: false);
            AsGuest(left.Id);
            Mine(room, left);

            var (result, _) = await _sut.JoinToRoomByRoomCodeAsync("bob", code, CancellationToken.None);

            Assert.True(left.IsActive);
            Assert.Equal(expected, result!.TeamNumber);
        }

        // автораспределения нет: без своей команды только по ссылке
        [Fact]
        public async Task Rejoin_TeamLobbyWithoutOwnTeam_ByRoomCode_NeedsTeamLink()
        {
            var (room, _, players) = TeamRoom();
            var kicked = InTeam(room, players, null, isActive: false);
            AsGuest(kicked.Id);
            Mine(room, kicked);

            var (result, refusal) = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", CancellationToken.None);

            Assert.Null(result);
            Assert.Equal(JoinRefusal.TeamLinkRequired, refusal);
            Assert.False(kicked.IsActive);
        }

        [Theory]
        [InlineData("TEAM2", 2, true)]  // ссылка другой команды пересаживает
        [InlineData("CODE", 1, false)]
        [InlineData("TEAM1", 1, false)]
        public async Task Rejoin_ActiveInLobby_OtherTeamLinkMoves(string code, int expected, bool saved)
        {
            var (room, _, players) = TeamRoom();
            MeGuest(room, players, 1);

            var (result, _) = await _sut.JoinToRoomByRoomCodeAsync("bob", code, CancellationToken.None);

            Assert.Equal(expected, result!.TeamNumber);
            LobbySaved(room, saved);
        }

        // в матче состав не меняется
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Rejoin_TeamMatch_LinkDoesNotChangeTeam(bool isActive)
        {
            var (room, _, players) = TeamRoom(RoomStatus.InProgress);
            MatchStarted(room, secondsAgo: 60);
            var me = MeGuest(room, players, 2);
            me.IsActive = isActive;
            me.FinishedAt = isActive ? null : Now.AddSeconds(-10);

            var (result, _) = await _sut.JoinToRoomByRoomCodeAsync("bob", "TEAM1", CancellationToken.None);

            Assert.Equal(2, result!.TeamNumber);
            Assert.True(me.IsActive);
        }

        // пересадка

        [Theory]
        [InlineData(true, false, LobbyChangeResult.Done)]
        [InlineData(false, true, LobbyChangeResult.Forbidden)]  // сам игрок только по ссылке
        [InlineData(false, false, LobbyChangeResult.Forbidden)]
        public async Task ChangeTeam_OnlyHost(bool isHost, bool self, LobbyChangeResult expected)
        {
            var (room, _, players) = TeamRoom();
            var me = MeAccount(room, players, 1, isHost);
            var target = self ? me : InTeam(room, players, 1);

            var result = await _sut.ChangeTeamAsync(room.Id, target.Id, 2, CancellationToken.None);

            Assert.Equal(expected, result);
            Assert.Equal(expected == LobbyChangeResult.Done ? 2 : 1, target.TeamNumber);
            LobbySaved(room, expected == LobbyChangeResult.Done);
        }

        [Fact]
        public async Task ChangeTeam_AfterStart_Conflict()
        {
            var (room, _, players) = TeamRoom(RoomStatus.InProgress);
            MeAccount(room, players, 1, isHost: true);
            var other = InTeam(room, players, 1);

            Assert.Equal(LobbyChangeResult.Conflict, await _sut.ChangeTeamAsync(room.Id, other.Id, 2, CancellationToken.None));
            Assert.Equal(1, other.TeamNumber);
            LobbySaved(room, false);
        }

        [Fact]
        public async Task ChangeTeam_UnknownTeamOrNormalRoom_NotFound()
        {
            var (room, teams, players) = TeamRoom();
            MeAccount(room, players, 1, isHost: true);
            var other = InTeam(room, players, 1);

            Assert.Equal(LobbyChangeResult.NotFound, await _sut.ChangeTeamAsync(room.Id, other.Id, 3, CancellationToken.None));

            // в обычной комнате команд нет
            room.IsTeamGame = false;
            teams.Clear();

            Assert.Equal(LobbyChangeResult.NotFound, await _sut.ChangeTeamAsync(room.Id, other.Id, 2, CancellationToken.None));
        }

        // параллельно стартовали или изменили лобби
        [Fact]
        public async Task ChangeTeam_ConcurrentChange_RetryWithoutNotify()
        {
            var (room, _, players) = TeamRoom();
            MeAccount(room, players, 1, isHost: true);
            var other = InTeam(room, players, 1);
            _context.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new DbUpdateConcurrencyException());

            Assert.Equal(LobbyChangeResult.Concurrent, await _sut.ChangeTeamAsync(room.Id, other.Id, 2, CancellationToken.None));
            _notifier.Verify(n => n.RoomUpdated(It.IsAny<Guid>()), Times.Never);
        }

        // капитан и название

        [Theory]
        [InlineData(2, LobbyChangeResult.Done)]      // капитан свою
        [InlineData(1, LobbyChangeResult.Forbidden)] // чужую нельзя
        public async Task RenameTeam_GuestCaptain_OnlyOwnTeam(int teamNumber, LobbyChangeResult expected)
        {
            var (room, teams, players) = TeamRoom();
            MeGuest(room, players, 2, joinedAgo: 10);
            InTeam(room, players, 2, userId: Guid.NewGuid(), joinedAgo: 5);

            var result = await _sut.RenameTeamAsync(room.Id, teamNumber, " Отдел ", CancellationToken.None);

            Assert.Equal(expected, result);
            Assert.Equal(expected == LobbyChangeResult.Done ? "Отдел" : $"Команда {teamNumber}", teams[teamNumber - 1].Name);
        }

        [Fact]
        public async Task RenameTeam_NotCaptain_Forbidden()
        {
            var (room, _, players) = TeamRoom();
            InTeam(room, players, 2, joinedAgo: 10);
            MeAccount(room, players, 2);

            Assert.Equal(LobbyChangeResult.Forbidden, await _sut.RenameTeamAsync(room.Id, 2, "Отдел", CancellationToken.None));
        }

        // капитан по входу в команду, а не в комнату
        [Fact]
        public async Task TeamLink_EarlyRoomJoinerDoesNotTakeCaptaincy()
        {
            var (room, _, players) = TeamRoom();
            InTeam(room, players, 2, joinedAgo: 10);
            var me = MeGuest(room, players, 1, joinedAgo: 100);

            await _sut.JoinToRoomByRoomCodeAsync("bob", "TEAM2", CancellationToken.None);

            Assert.Equal(Now, me.TeamJoinedAt);
            Assert.Equal(LobbyChangeResult.Forbidden, await _sut.RenameTeamAsync(room.Id, 2, "Отдел", CancellationToken.None));
        }

        [Theory]
        [InlineData("Отдел", LobbyChangeResult.Done)]
        [InlineData("команда 2", LobbyChangeResult.Conflict)] // занято другой командой
        [InlineData("КОМАНДА 1", LobbyChangeResult.Done)]     // своё же другим регистром
        public async Task RenameTeam_HostAnyTeam_NameMustBeFree(string name, LobbyChangeResult expected)
        {
            var (room, _, players) = TeamRoom();
            MeAccount(room, players, 2, isHost: true);

            Assert.Equal(expected, await _sut.RenameTeamAsync(room.Id, 1, name, CancellationToken.None));
        }

        [Fact]
        public async Task RenameTeam_TooLongName_BadRequest()
        {
            var controller = CreateController(new Mock<IGuestTokenService>());

            var result = await controller.RenameTeam(Guid.NewGuid(), 1, new TeamNameRequest(new string('x', 31)), CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        // команды

        [Fact]
        public async Task AddTeam_Host_FirstFreeNumberDefaultNameAndOwnLink()
        {
            var (room, teams, players) = TeamRoom(teamCount: 3);
            teams.RemoveAt(1); // остались 1 и 3
            MeAccount(room, players, 1, isHost: true);
            ((ClaimsIdentity)_http.HttpContext!.User.Identity!).AddClaim(new Claim("email", "alice@corp.local"));
            _roomHelper.Setup(h => h.GenerateRoomCode(It.IsAny<int>())).Returns("NEWCODE");
            RoomTeamEntity? added = null;
            _roomRepo.Setup(r => r.AddTeam(It.IsAny<RoomTeamEntity>())).Callback<RoomTeamEntity>(t => added = t);

            var result = await _sut.AddTeamAsync(room.Id, null, CancellationToken.None);

            Assert.Equal(LobbyChangeResult.Done, result);
            Assert.Equal((2, "Команда 2", "NEWCODE"), (added!.Number, added.Name, added.JoinCode));
            LobbySaved(room);
            _mailer.Verify(m => m.SendTeamLinksAsync("alice@corp.local", "CODE",
                It.Is<List<TeamLinkResponse>>(l => l.Single().JoinLink == "http://quiz/join/NEWCODE"), It.IsAny<CancellationToken>()), Times.Once);
        }

        // «Команда 3» заняли переименованием: новая без названия получает следующее свободное
        [Fact]
        public async Task AddTeam_DefaultNameTaken_TakesNextFreeDefault()
        {
            var (room, teams, players) = TeamRoom(teamCount: 2);
            teams[0].Name = "команда 3";
            MeAccount(room, players, 1, isHost: true);
            RoomTeamEntity? added = null;
            _roomRepo.Setup(r => r.AddTeam(It.IsAny<RoomTeamEntity>())).Callback<RoomTeamEntity>(t => added = t);

            var result = await _sut.AddTeamAsync(room.Id, null, CancellationToken.None);

            Assert.Equal(LobbyChangeResult.Done, result);
            Assert.Equal((3, "Команда 4"), (added!.Number, added.Name));
        }

        [Theory]
        [InlineData(true, 5, LobbyChangeResult.Conflict)]
        [InlineData(false, 2, LobbyChangeResult.Forbidden)]
        public async Task AddTeam_LimitAndOnlyHost(bool isHost, int teamCount, LobbyChangeResult expected)
        {
            var (room, _, players) = TeamRoom(teamCount: teamCount);
            MeAccount(room, players, 1, isHost);

            Assert.Equal(expected, await _sut.AddTeamAsync(room.Id, "Новая", CancellationToken.None));
            _roomRepo.Verify(r => r.AddTeam(It.IsAny<RoomTeamEntity>()), Times.Never);
        }

        [Fact]
        public async Task AddTeam_NormalRoom_NotFound()
        {
            var (room, teams, players) = TeamRoom();
            room.IsTeamGame = false;
            teams.Clear();
            MeAccount(room, players, 1, isHost: true);

            Assert.Equal(LobbyChangeResult.NotFound, await _sut.AddTeamAsync(room.Id, null, CancellationToken.None));
            _roomRepo.Verify(r => r.AddTeam(It.IsAny<RoomTeamEntity>()), Times.Never);
        }

        [Fact]
        public async Task DeleteTeam_EmptyTeam_RemovedAndLeftPlayersLoseIt()
        {
            var (room, teams, players) = TeamRoom(teamCount: 3);
            MeAccount(room, players, 1, isHost: true);
            var left = InTeam(room, players, 3, isActive: false);

            var result = await _sut.DeleteTeamAsync(room.Id, 3, CancellationToken.None);

            Assert.Equal(LobbyChangeResult.Done, result);
            Assert.Null(left.TeamNumber);
            _roomRepo.Verify(r => r.RemoveTeam(teams[2]), Times.Once);
            LobbySaved(room);
        }

        [Theory]
        [InlineData(3, true)]  // в команде есть игрок
        [InlineData(2, false)] // команд останется меньше двух
        public async Task DeleteTeam_NonEmptyOrLastTwo_Conflict(int teamCount, bool hasPlayer)
        {
            var (room, _, players) = TeamRoom(teamCount: teamCount);
            MeAccount(room, players, 1, isHost: true);
            if (hasPlayer)
            {
                InTeam(room, players, 2);
            }

            Assert.Equal(LobbyChangeResult.Conflict, await _sut.DeleteTeamAsync(room.Id, 2, CancellationToken.None));
            _roomRepo.Verify(r => r.RemoveTeam(It.IsAny<RoomTeamEntity>()), Times.Never);
        }

        [Fact]
        public async Task DeleteTeam_NotHost_Forbidden()
        {
            var (room, _, players) = TeamRoom(teamCount: 3);
            MeAccount(room, players, 1);

            Assert.Equal(LobbyChangeResult.Forbidden, await _sut.DeleteTeamAsync(room.Id, 3, CancellationToken.None));
        }

        // владелец убирает игрока из лобби

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task RemovePlayer_Host_PlayerInactiveAndKicked(bool isTeamGame)
        {
            var (room, _, players) = TeamRoom();
            room.IsTeamGame = isTeamGame;
            MeAccount(room, players, 1, isHost: true);
            var ghost = InTeam(room, players, 2);

            var result = await _sut.RemovePlayerAsync(room.Id, ghost.Id, CancellationToken.None);

            Assert.Equal(LobbyChangeResult.Done, result);
            Assert.False(ghost.IsActive);
            Assert.Equal(ExitReason.Kicked, ghost.ExitReason);
            Assert.Equal(Now, ghost.FinishedAt);
            Assert.Null(ghost.TeamNumber); // вернётся без капитанства
            LobbySaved(room);
        }

        [Theory]
        [InlineData(false, RoomStatus.Waiting, false, LobbyChangeResult.Forbidden)]
        [InlineData(true, RoomStatus.Waiting, true, LobbyChangeResult.Conflict)]     // себя нельзя
        [InlineData(true, RoomStatus.InProgress, false, LobbyChangeResult.Conflict)] // в матче нельзя
        public async Task RemovePlayer_NotAllowed(bool isHost, RoomStatus status, bool self, LobbyChangeResult expected)
        {
            var (room, _, players) = TeamRoom(status);
            var me = MeAccount(room, players, 1, isHost);
            var target = self ? me : InTeam(room, players, 2);

            Assert.Equal(expected, await _sut.RemovePlayerAsync(room.Id, target.Id, CancellationToken.None));
            Assert.True(target.IsActive);
            LobbySaved(room, false);
        }

        // состояние и итоги

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task RoomState_TeamLobby_LinksOnlyForHost(bool isHost)
        {
            var (room, _, players) = TeamRoom();
            MeAccount(room, players, 1, isHost);

            var state = await _sut.GetRoomStateAsync(room.Id, CancellationToken.None);

            Assert.Equal(
                isHost ? new[] { "http://quiz/join/TEAM1", "http://quiz/join/TEAM2" } : new string?[] { null, null },
                state!.Teams!.Select(t => t.JoinLink));
        }

        // 3 вопроса: IT 50+100+100, бухгалтерия 67+100+50, ушедший после первого её не тянет
        [Fact]
        public async Task RoomState_TeamGameFinished_PointsPerQuestionAndPlaces()
        {
            var (room, _, players) = TeamRoom(RoomStatus.Finished, teamCount: 3);
            var questions = Enumerable.Range(0, 3)
                .Select(i => new MatchQuestionEntity { Id = Guid.NewGuid(), Order = i, ClosedAt = Now.AddSeconds(-30 + i * 10) })
                .ToList();
            var match = new MatchEntity { Id = Guid.NewGuid(), RoomId = room.Id, Questions = questions };
            _matchRepo.Setup(r => r.GetMatchByRoomId(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(match);
            var host = MeGuest(room, players, 1);
            var petya = InTeam(room, players, 1);
            var leaver = InTeam(room, players, 1, isActive: false);
            leaver.FinishedAt = Now.AddSeconds(-25); // после первого вопроса
            var vasya = InTeam(room, players, 2);
            var b1 = InTeam(room, players, 2);
            InTeam(room, players, null, isActive: false); // ушёл до старта

            var answers = new List<PlayerAnswerEntity>();
            void Answer(RoomPlayerEntity p, int q, bool correct) => answers.Add(new PlayerAnswerEntity
            {
                MatchQuestionId = questions[q].Id,
                RoomPlayerId = p.Id,
                SelectedOption = new AnswerOptionEntity { IsCorrect = correct },
            });
            Answer(host, 0, true); Answer(leaver, 0, true); Answer(petya, 0, false);
            Answer(host, 1, true); Answer(petya, 1, true);
            Answer(host, 2, false); Answer(petya, 2, true);
            Answer(vasya, 0, true); Answer(b1, 0, false);
            Answer(vasya, 1, true); Answer(b1, 1, true);
            Answer(vasya, 2, true); Answer(b1, 2, true);
            _matchRepo.Setup(r => r.GetAnswersAsync(match.Id, It.IsAny<CancellationToken>())).ReturnsAsync(answers);
            // ушедший после первого вопроса на закрытии следующих уже не в игре
            questions[0].InGamePlayerIds = [host.Id, petya.Id, leaver.Id, vasya.Id, b1.Id];
            questions[1].InGamePlayerIds = [host.Id, petya.Id, vasya.Id, b1.Id];
            questions[2].InGamePlayerIds = [host.Id, petya.Id, vasya.Id, b1.Id];

            var state = await _sut.GetRoomStateAsync(room.Id, CancellationToken.None);

            // пустая команда 3 не играла
            Assert.Equal(
                new[] { (2, 2, 250, 1), (1, 3, 217, 2) },
                state!.Teams!.Select(t => (t.Number, t.Members, t.Points!.Value, t.Place!.Value)));
        }

        // в игре ответившие и активные на закрытии; вернувшийся позже не тянет команду на пропущенном
        [Fact]
        public void TeamPoints_InGameAtCloseOrAnswered()
        {
            var silentButPresent = new RoomPlayerEntity { Id = Guid.NewGuid(), IsActive = true };
            var answeredThenLeft = new RoomPlayerEntity { Id = Guid.NewGuid(), IsActive = false };
            var awayAtCloseThenReturned = new RoomPlayerEntity { Id = Guid.NewGuid(), IsActive = true };
            var right = new RoomPlayerEntity { Id = Guid.NewGuid(), IsActive = true };
            var question = new MatchQuestionEntity { Id = Guid.NewGuid(), InGamePlayerIds = [silentButPresent.Id, right.Id] };
            var answers = new List<PlayerAnswerEntity>
            {
                new() { MatchQuestionId = question.Id, RoomPlayerId = right.Id, SelectedOption = new() { IsCorrect = true } },
                new() { MatchQuestionId = question.Id, RoomPlayerId = answeredThenLeft.Id, SelectedOption = new() { IsCorrect = true } },
            };

            var points = RoomMapper.TeamPoints([silentButPresent, answeredThenLeft, awayAtCloseThenReturned, right], [question], answers);

            Assert.Equal(67, points); // 2 верных из 3 в игре
        }

        // место не делим: при равных очках выше команда с меньшим средним временем верного, без верных ниже, дальше номер
        [Fact]
        public void TeamResults_SamePoints_FasterAverageAbove()
        {
            var room = new RoomEntity { Id = Guid.NewGuid() };
            var players = new List<RoomPlayerEntity>();
            for (var n = 1; n <= 5; n++)
            {
                InTeam(room, players, n);
            }
            var teams = Enumerable.Range(1, 5).Select(n => new RoomTeamEntity { Number = n, Name = $"T{n}" }).ToList();
            var points = new Dictionary<int, int> { [1] = 200, [2] = 200, [3] = 100, [4] = 200, [5] = 200 };
            var times = new Dictionary<int, long?> { [1] = 9000, [2] = 4000, [3] = 1000, [4] = null, [5] = 9000 };

            var result = teams.ToTeamResponses(
                RoomStatus.Finished, players, m => points[m[0].TeamNumber!.Value], m => times[m[0].TeamNumber!.Value], _ => null);

            Assert.Equal(
                new[] { (2, 1, 4000L), (1, 2, 9000L), (5, 3, 9000L), (4, 4, -1L), (3, 5, 1000L) },
                result.Select(t => (t.Number, t.Place!.Value, t.AvgCorrectTimeMs ?? -1)));
        }

        // время от открытия вопроса; у старого матча без OpenedAt второй открыт после показа ответа на первый
        [Fact]
        public void AnswerTimings_FromOpenedAt_OldMatchApproximated()
        {
            var first = new MatchQuestionEntity { Id = Guid.NewGuid(), Order = 0, ClosedAt = Now.AddSeconds(20) };
            var second = new MatchQuestionEntity { Id = Guid.NewGuid(), Order = 1 };
            var match = new MatchEntity { StartedAt = Now, Questions = [first, second] };
            var player = Guid.NewGuid();
            var answers = new List<PlayerAnswerEntity>
            {
                new() { MatchQuestionId = first.Id, RoomPlayerId = player, AnsweredAt = Now.AddSeconds(4), SelectedOption = new() { IsCorrect = true } },
                new() { MatchQuestionId = second.Id, RoomPlayerId = player, AnsweredAt = Now.AddSeconds(25.5), SelectedOption = new() { IsCorrect = true } },
            };

            var timing = RoomMapper.AnswerTimings(match, match.Questions, answers)[player];

            Assert.Equal(new long[] { 4000, 2500 }, timing.CorrectMs); // второй открыт на 23-й секунде
            Assert.Equal(6500, timing.CorrectTimeMs);

            second.OpenedAt = Now.AddSeconds(21);
            Assert.Equal(4500, RoomMapper.AnswerTimings(match, match.Questions, answers)[player].CorrectMs[1]);
        }

        // обычная игра: очки, вышедшие в конце, меньше сумма времени верных, больше ответов, кто раньше вошёл;
        // ушедшего и убранного в лобби в итогах нет
        [Fact]
        public async Task RoomState_SimpleGameFinished_TiesBrokenByTimeThenAnswersThenJoin()
        {
            var room = Setup(RoomStatus.Finished);
            var question = new MatchQuestionEntity { Id = Guid.NewGuid(), Order = 0, OpenedAt = Now.AddSeconds(-60) };
            var other = new MatchQuestionEntity { Id = Guid.NewGuid(), Order = 1, OpenedAt = Now.AddSeconds(-30) };
            var match = new MatchEntity { Id = Guid.NewGuid(), RoomId = room.Id, StartedAt = Now.AddSeconds(-60), Questions = [question, other] };
            _matchRepo.Setup(r => r.GetMatchByRoomId(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(match);
            var players = new List<RoomPlayerEntity>();
            var answers = new List<PlayerAnswerEntity>();
            RoomPlayerEntity Player(string name, int score, int joinedAgo, bool isActive = true)
            {
                var player = Participant(room, isActive);
                player.Nickname = name;
                player.Score = score;
                player.JoinedAt = Now.AddSeconds(-joinedAgo);
                players.Add(player);

                return player;
            }
            void Answer(RoomPlayerEntity p, MatchQuestionEntity q, double seconds, bool correct) => answers.Add(new PlayerAnswerEntity
            {
                MatchQuestionId = q.Id,
                RoomPlayerId = p.Id,
                AnsweredAt = q.OpenedAt!.Value.AddSeconds(seconds),
                SelectedOption = new AnswerOptionEntity { IsCorrect = correct },
            });
            var slow = Player("slow", 2, 100);
            Answer(slow, question, 3, true); Answer(slow, other, 4, true);
            var fast = Player("fast", 2, 50);
            Answer(fast, question, 2, true); Answer(fast, other, 3, true);
            var moreAnswers = Player("moreAnswers", 1, 10);
            Answer(moreAnswers, question, 10, true); Answer(moreAnswers, other, 1, false);
            var later = Player("later", 1, 70);
            Answer(later, question, 10, true);
            var earlier = Player("earlier", 1, 80);
            Answer(earlier, question, 10, true);
            var leaver = Player("leaver", 0, 90, isActive: false);
            leaver.FinishedAt = Now.AddSeconds(-50); // вышел во время игры
            Answer(leaver, question, 1, true);
            var silent = Player("silent", 0, 5);
            Player("leftLobby", 0, 95, isActive: false).FinishedAt = match.StartedAt.AddSeconds(-5);
            var kicked = Player("kicked", 0, 96, isActive: false);
            kicked.FinishedAt = match.StartedAt.AddSeconds(-1);
            kicked.ExitReason = ExitReason.Kicked;
            var me = slow;
            AsGuest(me.Id);
            Mine(room, me);
            _participationRepo.Setup(r => r.GetParticipationsByRoomId(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(players);
            _matchRepo.Setup(r => r.GetAnswersAsync(match.Id, It.IsAny<CancellationToken>())).ReturnsAsync(answers);

            var state = await _sut.GetRoomStateAsync(room.Id, CancellationToken.None);

            Assert.Equal(
                new[] { ("fast", 1, 5000L), ("slow", 2, 7000L), ("moreAnswers", 3, 10000L), ("earlier", 4, 10000L), ("later", 5, 10000L), ("silent", 6, -1L), ("leaver", 7, 1000L) },
                state!.Players.Select(p => (p.PlayerName, p.Place!.Value, p.CorrectTimeMs ?? -1)));
        }

        // очки сохранил выходом в окне возврата к финишу: при равных очках решает время, а не то, что вышел
        [Fact]
        public async Task RoomState_SimpleGameFinished_LeftWithinWindow_RankedLikeActive()
        {
            var room = Setup(RoomStatus.Finished);
            var question = new MatchQuestionEntity { Id = Guid.NewGuid(), Order = 0, OpenedAt = Now.AddSeconds(-60) };
            var match = new MatchEntity { Id = Guid.NewGuid(), RoomId = room.Id, StartedAt = Now.AddSeconds(-60), FinishedAt = Now, Questions = [question] };
            _matchRepo.Setup(r => r.GetMatchByRoomId(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(match);
            var stayed = Participant(room, isActive: true);
            stayed.Nickname = "stayed";
            stayed.Score = 1;
            var dropped = Participant(room, isActive: false, finishedAt: Now.AddSeconds(-10));
            dropped.Nickname = "dropped";
            dropped.Score = 1;
            var expired = Participant(room, isActive: false, finishedAt: Now.AddSeconds(-50));
            expired.Nickname = "expired";
            AsGuest(stayed.Id);
            Mine(room, stayed);
            _participationRepo.Setup(r => r.GetParticipationsByRoomId(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync([stayed, dropped, expired]);
            _matchRepo.Setup(r => r.GetAnswersAsync(match.Id, It.IsAny<CancellationToken>())).ReturnsAsync(
            [
                new() { MatchQuestionId = question.Id, RoomPlayerId = stayed.Id, AnsweredAt = question.OpenedAt!.Value.AddSeconds(9), SelectedOption = new() { IsCorrect = true } },
                new() { MatchQuestionId = question.Id, RoomPlayerId = dropped.Id, AnsweredAt = question.OpenedAt!.Value.AddSeconds(3), SelectedOption = new() { IsCorrect = true } },
            ]);

            var state = await _sut.GetRoomStateAsync(room.Id, CancellationToken.None);

            Assert.Equal(new[] { "dropped", "stayed", "expired" }, state!.Players.Select(p => p.PlayerName));
        }

        // командная: равные очки, выше команда, которая в среднем быстрее отвечала верно
        [Fact]
        public async Task RoomState_TeamGameFinished_SamePoints_FasterTeamFirst()
        {
            var (room, _, players) = TeamRoom(RoomStatus.Finished);
            var question = new MatchQuestionEntity { Id = Guid.NewGuid(), Order = 0, OpenedAt = Now.AddSeconds(-30), ClosedAt = Now };
            var match = new MatchEntity { Id = Guid.NewGuid(), RoomId = room.Id, Questions = [question] };
            _matchRepo.Setup(r => r.GetMatchByRoomId(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(match);
            var me = MeGuest(room, players, 1);
            var mate = InTeam(room, players, 1);
            var rival = InTeam(room, players, 2);
            question.InGamePlayerIds = [me.Id, mate.Id, rival.Id];
            PlayerAnswerEntity Correct(RoomPlayerEntity p, int seconds) => new()
            {
                MatchQuestionId = question.Id,
                RoomPlayerId = p.Id,
                AnsweredAt = Now.AddSeconds(-30 + seconds),
                SelectedOption = new AnswerOptionEntity { IsCorrect = true },
            };
            _matchRepo.Setup(r => r.GetAnswersAsync(match.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync([Correct(me, 4), Correct(mate, 12), Correct(rival, 6)]);

            var state = await _sut.GetRoomStateAsync(room.Id, CancellationToken.None);

            // у обеих по 100: в среднем 8 с против 6 с
            Assert.Equal(
                new[] { (2, 100, 1, 6000L), (1, 100, 2, 8000L) },
                state!.Teams!.Select(t => (t.Number, t.Points!.Value, t.Place!.Value, t.AvgCorrectTimeMs!.Value)));
        }

        // таблица по ходу игры: закрытый вопрос считается, открытый нет
        [Fact]
        public async Task RoomState_TeamGameInProgress_PointsOnlyForClosedQuestions()
        {
            var (room, _, players) = TeamRoom(RoomStatus.InProgress);
            var closed = new MatchQuestionEntity { Id = Guid.NewGuid(), Order = 0, ClosedAt = Now.AddSeconds(-5) };
            var open = new MatchQuestionEntity { Id = Guid.NewGuid(), Order = 1 };
            var match = new MatchEntity { Id = Guid.NewGuid(), RoomId = room.Id, Questions = [closed, open] };
            _matchRepo.Setup(r => r.GetMatchByRoomId(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(match);
            var host = MeGuest(room, players, 1);
            var guest = InTeam(room, players, 2);
            closed.InGamePlayerIds = [host.Id, guest.Id];

            var answers = new List<PlayerAnswerEntity>
            {
                new() { MatchQuestionId = closed.Id, RoomPlayerId = host.Id, SelectedOption = new AnswerOptionEntity { IsCorrect = false } },
                new() { MatchQuestionId = closed.Id, RoomPlayerId = guest.Id, SelectedOption = new AnswerOptionEntity { IsCorrect = true } },
                new() { MatchQuestionId = open.Id, RoomPlayerId = host.Id, SelectedOption = new AnswerOptionEntity { IsCorrect = true } },
            };
            _matchRepo.Setup(r => r.GetAnswersAsync(match.Id, It.IsAny<CancellationToken>())).ReturnsAsync(answers);

            var state = await _sut.GetRoomStateAsync(room.Id, CancellationToken.None);

            Assert.Equal(
                new[] { (2, 100, 1), (1, 0, 2) },
                state!.Teams!.Select(t => (t.Number, t.Points!.Value, t.Place!.Value)));
            Assert.All(state.Players, p => Assert.Null(p.Score)); // личных очков в командной игре не видно
            Assert.True(state.Players.Single(p => p.TeamNumber == 2).IsCaptain); // гость тоже капитан
        }

        // оборвавший связь перед стартом сохранил команду, но в составе только если вернулся
        [Fact]
        public async Task RoomState_TeamGameInProgress_DisconnectedBeforeStart_NotMemberUntilReturned()
        {
            var (room, _, players) = TeamRoom(RoomStatus.InProgress);
            MatchStarted(room, secondsAgo: 30);
            MeGuest(room, players, 1);
            InTeam(room, players, 2);
            var lost = InTeam(room, players, 2, isActive: false);
            lost.ExitReason = ExitReason.Disconnected;
            lost.FinishedAt = Now.AddSeconds(-35);
            var leftInGame = InTeam(room, players, 2, isActive: false);
            leftInGame.FinishedAt = Now.AddSeconds(-10);
            _matchRepo.Setup(r => r.GetAnswersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

            var state = await _sut.GetRoomStateAsync(room.Id, CancellationToken.None);

            Assert.Equal(new[] { (1, 1), (2, 2) }, state!.Teams!.OrderBy(t => t.Number).Select(t => (t.Number, t.Members)));
        }

        // «Вернуться» только неактивному и только когда вход его пустит
        [Theory]
        [InlineData(RoomStatus.Waiting, true, -500, 3, false)]     // активному возвращаться некуда
        [InlineData(RoomStatus.Waiting, false, -500, 3, true)]     // лобби: окно не важно
        [InlineData(RoomStatus.Waiting, false, -500, 10, false)]   // лобби заполнили
        [InlineData(RoomStatus.InProgress, false, -20, 3, true)]   // матч: окно ещё идёт
        [InlineData(RoomStatus.InProgress, false, -41, 3, false)]  // матч: окно истекло
        [InlineData(RoomStatus.Finished, false, -5, 3, false)]
        public async Task RoomState_MyCanReturn_SameRulesAsJoin(RoomStatus status, bool isActive, int leftAt, int takenSlots, bool expected)
        {
            var room = Setup(status);
            MatchStarted(room, secondsAgo: 60);
            var me = Participant(room, isActive, finishedAt: isActive ? null : Now.AddSeconds(leftAt));
            AsGuest(me.Id);
            Mine(room, me);
            _participationRepo.Setup(r => r.GetParticipationsByRoomId(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync([me]);
            _participationRepo.Setup(r => r.CountTakenSlots(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(takenSlots);
            _matchRepo.Setup(r => r.GetAnswersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

            var state = await _sut.GetRoomStateAsync(room.Id, CancellationToken.None);

            Assert.Equal(expected, state!.MyCanReturn);
        }

        // в лобби командной без своей команды (убран) вход по ссылке комнаты не пустит
        [Theory]
        [InlineData(1, true)]
        [InlineData(null, false)]
        public async Task RoomState_TeamLobby_MyCanReturnNeedsOwnTeam(int? team, bool expected)
        {
            var (room, _, players) = TeamRoom();
            var me = MeGuest(room, players, 1);
            me.IsActive = false;
            me.FinishedAt = Now.AddSeconds(-30);
            me.TeamNumber = team;

            var state = await _sut.GetRoomStateAsync(room.Id, CancellationToken.None);

            Assert.Equal(expected, state!.MyCanReturn);
        }

        // мои комнаты и приглашение

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task MyRooms_TeamNames_LinksOnlyForHost(bool isHost)
        {
            var (room, teams, _) = TeamRoom();
            room.Teams = teams;
            room.HostUser = new UserEntity { KeycloakSubject = isHost ? "alice-subject" : "host-subject", UserName = "host" };
            AsRegistered();
            _roomRepo.Setup(r => r.GetRoomsOfUser("alice-subject", It.IsAny<CancellationToken>())).ReturnsAsync([room]);

            var rooms = await _sut.GetMyRoomsAsync(CancellationToken.None);

            var links = Assert.Single(rooms).Teams!;
            Assert.Equal(new[] { "Команда 1", "Команда 2" }, links.Select(t => t.Name));
            Assert.Equal(
                isHost ? new[] { "http://quiz/join/TEAM1", "http://quiz/join/TEAM2" } : new string?[] { null, null },
                links.Select(t => t.JoinLink));
            Assert.Null(rooms[0].Result); // итог только у завершённой
        }

        // обычная игра: место как в итогах, равные очки разбивает время верных, победитель один
        [Fact]
        public async Task MyRooms_SimpleGameFinished_MyPlaceAndWinner()
        {
            var user = AsRegistered();
            var room = Setup(RoomStatus.Finished);
            var players = new List<RoomPlayerEntity>();
            RoomPlayerEntity Player(string name, int score, Guid? userId = null)
            {
                var player = Participant(room, isActive: true);
                player.Nickname = name;
                player.Score = score;
                player.UserId = userId;
                players.Add(player);

                return player;
            }
            var boris = Player("Борис", 3);
            var me = Player("alice", 2, user.Id);
            var anna = Player("Анна", 3);
            Player("Дима", 1);
            room.Players = [me];
            var waiting = new RoomEntity { Id = Guid.NewGuid(), RoomCode = "WAIT", Status = RoomStatus.Waiting };
            _participationRepo.Setup(r => r.GetParticipationsByRoomId(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(players);
            // у Анны и Бориса по 3, Анна отвечала верно быстрее
            var question = new MatchQuestionEntity { Id = Guid.NewGuid(), Order = 0, OpenedAt = Now };
            var match = new MatchEntity { Id = Guid.NewGuid(), RoomId = room.Id, StartedAt = Now, Questions = [question] };
            _matchRepo.Setup(r => r.GetMatchByRoomId(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(match);
            _matchRepo.Setup(r => r.GetAnswersAsync(match.Id, It.IsAny<CancellationToken>())).ReturnsAsync(
            [
                new() { MatchQuestionId = question.Id, RoomPlayerId = boris.Id, AnsweredAt = Now.AddSeconds(9), SelectedOption = new() { IsCorrect = true } },
                new() { MatchQuestionId = question.Id, RoomPlayerId = anna.Id, AnsweredAt = Now.AddSeconds(5), SelectedOption = new() { IsCorrect = true } },
            ]);
            _roomRepo.Setup(r => r.GetRoomsOfUser("alice-subject", It.IsAny<CancellationToken>())).ReturnsAsync([waiting, room]);

            var rooms = await _sut.GetMyRoomsAsync(CancellationToken.None);

            Assert.Null(rooms[0].Result);
            var result = rooms[1].Result!;
            Assert.Equal(3, result.MyPlace); // двое выше с тремя
            Assert.Equal(2, result.MyPoints);
            Assert.Equal("Анна", result.WinnerName);
            Assert.False(result.IsDraw);
            Assert.Equal("http://quiz/join/CODE", rooms[1].JoinLink);
        }

        // обычная игра, все по 0: сервер места не делит, но победителя нет; ушедшему из лобби места нет
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task MyRooms_SimpleGameFinished_AllZero_DrawWithoutWinner(bool iPlayed)
        {
            var user = AsRegistered();
            var room = Setup(RoomStatus.Finished);
            var match = new MatchEntity { Id = Guid.NewGuid(), RoomId = room.Id, StartedAt = Now.AddSeconds(-60), FinishedAt = Now };
            _matchRepo.Setup(r => r.GetMatchByRoomId(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(match);
            _matchRepo.Setup(r => r.GetAnswersAsync(match.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
            var me = Participant(room, isActive: iPlayed, finishedAt: iPlayed ? null : match.StartedAt.AddSeconds(-5));
            me.UserId = user.Id;
            room.Players = [me];
            _participationRepo.Setup(r => r.GetParticipationsByRoomId(room.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync([me, Participant(room, isActive: true), Participant(room, isActive: true)]);
            _roomRepo.Setup(r => r.GetRoomsOfUser("alice-subject", It.IsAny<CancellationToken>())).ReturnsAsync([room]);

            var result = Assert.Single(await _sut.GetMyRoomsAsync(CancellationToken.None)).Result!;

            Assert.True(result.IsDraw);
            Assert.Null(result.WinnerName);
            Assert.Equal(iPlayed ? 1 : (int?)null, result.MyPlace);
        }

        // обычная игра 0:0, второй вышел: ничья, только если к финишу его окно возврата не истекло
        [Theory]
        [InlineData(50, false)]
        [InlineData(10, true)]
        public async Task MyRooms_SimpleGameFinished_ZeroZero_LeftPlayerDrawsOnlyInsideWindow(int leftSecondsBeforeFinish, bool draw)
        {
            var user = AsRegistered();
            var room = Setup(RoomStatus.Finished);
            var match = new MatchEntity { Id = Guid.NewGuid(), RoomId = room.Id, StartedAt = Now.AddSeconds(-60), FinishedAt = Now };
            _matchRepo.Setup(r => r.GetMatchByRoomId(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(match);
            _matchRepo.Setup(r => r.GetAnswersAsync(match.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
            var me = Participant(room, isActive: true);
            me.UserId = user.Id;
            me.Nickname = "alice";
            room.Players = [me];
            var left = Participant(room, isActive: false, finishedAt: match.FinishedAt.Value.AddSeconds(-leftSecondsBeforeFinish));
            _participationRepo.Setup(r => r.GetParticipationsByRoomId(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync([me, left]);
            _roomRepo.Setup(r => r.GetRoomsOfUser("alice-subject", It.IsAny<CancellationToken>())).ReturnsAsync([room]);

            var result = Assert.Single(await _sut.GetMyRoomsAsync(CancellationToken.None)).Result!;

            Assert.Equal(draw, result.IsDraw);
            Assert.Equal(draw ? null : "alice", result.WinnerName);
            Assert.Equal(1, result.MyPlace);
        }

        // командная: место и очки моей команды, победитель команда
        [Fact]
        public async Task MyRooms_TeamGameFinished_MyTeamPlaceAndWinner()
        {
            var (room, teams, players) = TeamRoom(RoomStatus.Finished);
            room.Teams = teams;
            var me = MeAccount(room, players, 1);
            var vasya = InTeam(room, players, 2);
            room.Players = [me];
            var question = new MatchQuestionEntity { Id = Guid.NewGuid(), ClosedAt = Now, InGamePlayerIds = [me.Id, vasya.Id] };
            var match = new MatchEntity { Id = Guid.NewGuid(), RoomId = room.Id, Questions = [question] };
            _matchRepo.Setup(r => r.GetMatchByRoomId(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(match);
            _matchRepo.Setup(r => r.GetAnswersAsync(match.Id, It.IsAny<CancellationToken>())).ReturnsAsync(
            [
                new() { MatchQuestionId = question.Id, RoomPlayerId = me.Id, SelectedOption = new AnswerOptionEntity { IsCorrect = false } },
                new() { MatchQuestionId = question.Id, RoomPlayerId = vasya.Id, SelectedOption = new AnswerOptionEntity { IsCorrect = true } },
            ]);
            _roomRepo.Setup(r => r.GetRoomsOfUser("alice-subject", It.IsAny<CancellationToken>())).ReturnsAsync([room]);
            PlayedTeams(room, 1, 2);

            var result = Assert.Single(await _sut.GetMyRoomsAsync(CancellationToken.None)).Result!;

            Assert.Equal(2, result.MyPlace);
            Assert.Equal(0, result.MyPoints);
            Assert.Equal("Команда 2", result.WinnerName);
            Assert.False(result.IsDraw);
        }

        private void PlayedTeams(RoomEntity room, params int[] numbers)
            => _participationRepo.Setup(r => r.GetPlayedTeamNumbersAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(room.Id)), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, HashSet<int>> { [room.Id] = numbers.ToHashSet() });

        // командная: у лидеров поровну очков и времени, победителя нет; «из N» без пустой команды;
        // оборвавший связь перед стартом и не вернувшийся места не получает
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task MyRooms_TeamGameFinished_TiedLeaders_DrawAndOnlyPlayedTeams(bool iPlayed)
        {
            var (room, teams, players) = TeamRoom(RoomStatus.Finished, teamCount: 3);
            room.Teams = teams;
            var me = MeAccount(room, players, 1);
            if (!iPlayed)
            {
                me.IsActive = false;
                me.ExitReason = ExitReason.Disconnected;
                me.FinishedAt = Now.AddSeconds(-70);
            }
            InTeam(room, players, 1);
            InTeam(room, players, 2);
            room.Players = [me];
            var match = new MatchEntity { Id = Guid.NewGuid(), RoomId = room.Id, StartedAt = Now.AddSeconds(-60), FinishedAt = Now };
            _matchRepo.Setup(r => r.GetMatchByRoomId(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(match);
            _matchRepo.Setup(r => r.GetAnswersAsync(match.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
            _roomRepo.Setup(r => r.GetRoomsOfUser("alice-subject", It.IsAny<CancellationToken>())).ReturnsAsync([room]);
            PlayedTeams(room, 1, 2);

            var myRoom = Assert.Single(await _sut.GetMyRoomsAsync(CancellationToken.None));

            Assert.True(myRoom.Result!.IsDraw);
            Assert.Null(myRoom.Result.WinnerName);
            Assert.Equal(iPlayed ? 1 : (int?)null, myRoom.Result.MyPlace);
            Assert.Equal(new[] { 1, 2 }, myRoom.Teams!.Select(t => t.Number)); // команда 3 не играла
        }

        // идущая командная игра: только игравшие команды, в лобби все
        [Theory]
        [InlineData(RoomStatus.InProgress, new[] { 2, 3 })]
        [InlineData(RoomStatus.Waiting, new[] { 1, 2, 3 })]
        public async Task MyRooms_TeamGame_TeamsAfterStartOnlyPlayed(RoomStatus status, int[] expected)
        {
            var (room, teams, _) = TeamRoom(status, teamCount: 3);
            room.Teams = teams;
            AsRegistered();
            _roomRepo.Setup(r => r.GetRoomsOfUser("alice-subject", It.IsAny<CancellationToken>())).ReturnsAsync([room]);
            PlayedTeams(room, 2, 3);

            var myRoom = Assert.Single(await _sut.GetMyRoomsAsync(CancellationToken.None));

            Assert.Equal(expected, myRoom.Teams!.Select(t => t.Number));
        }

        // итоги считаем только для последних завершённых, чтобы список не тормозил
        [Fact]
        public async Task MyRooms_ResultsOnlyForLatestTwentyFinished()
        {
            AsRegistered();
            var rooms = Enumerable.Range(0, 21)
                .Select(_ =>
                {
                    var room = new RoomEntity { Id = Guid.NewGuid(), RoomCode = "CODE", Status = RoomStatus.Finished };
                    room.Players = [Participant(room, isActive: true)];

                    return room;
                })
                .ToList();
            _participationRepo.Setup(r => r.GetParticipationsByRoomId(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            _roomRepo.Setup(r => r.GetRoomsOfUser("alice-subject", It.IsAny<CancellationToken>())).ReturnsAsync(rooms);

            var result = await _sut.GetMyRoomsAsync(CancellationToken.None);

            Assert.All(result.Take(20), r => Assert.NotNull(r.Result));
            Assert.Null(result[20].Result);
            _participationRepo.Verify(r => r.GetParticipationsByRoomId(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Exactly(20));
        }

        [Fact]
        public async Task Invite_TeamCode_ShowsTeamAndHost()
        {
            var controller = CreateController(new Mock<IGuestTokenService>());
            var (room, teams, _) = TeamRoom();
            room.HostUser = new UserEntity { UserName = "alice" };
            _roomRepo.Setup(r => r.FindInviteAsync("TEAM2", It.IsAny<CancellationToken>())).ReturnsAsync((room, teams[1]));

            var result = await controller.GetInvite(" team2 ", CancellationToken.None);

            var invite = Assert.IsType<InviteResponse>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal(new InviteResponse(true, RoomStatus.Waiting, "alice", 2, "Команда 2"), invite);
        }

        [Fact]
        public async Task Invite_UnknownCode_NotFound()
        {
            var controller = CreateController(new Mock<IGuestTokenService>());

            var result = await controller.GetInvite("NOPE", CancellationToken.None);

            Assert.IsType<NotFoundObjectResult>(result);
        }
        // «Сыграть ещё»

        // ведущий alice на итогах прошлой игры; транзакция в тестах просто выполняет работу
        private (UserEntity Host, RoomPlayerEntity HostOld) RematchHost(RoomEntity old, List<RoomPlayerEntity> players, int? team = null)
        {
            var user = AsRegistered();
            old.HostUserId = user.Id;
            var hostOld = InTeam(old, players, team, userId: user.Id);
            _participationRepo.Setup(r => r.FindParticipant(old.Id, null, "alice-subject", It.IsAny<CancellationToken>())).ReturnsAsync(hostOld);
            _roomRepo.Setup(r => r.IsOwner(old.Id, "alice-subject", It.IsAny<CancellationToken>())).ReturnsAsync(true);
            InTransaction();

            return (user, hostOld);
        }

        private void InTransaction()
            => _roomRepo.Setup(r => r.InTransactionAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
                .Returns((Func<CancellationToken, Task> work, CancellationToken ct) => work(ct));

        // новая комната игры N+1 под кодом code
        private (RoomEntity Next, RoomPlayerEntity HostLink, List<RoomTeamEntity> AddedTeams) NextCreated(UserEntity host, string code)
        {
            var next = new RoomEntity { Id = Guid.NewGuid(), RoomCode = code, HostUserId = host.Id };
            var hostLink = new RoomPlayerEntity { Id = Guid.NewGuid(), Room = next, RoomId = next.Id };
            var added = new List<RoomTeamEntity>();
            var archived = 0;
            _roomHelper.Setup(h => h.GenerateRoomCode(It.IsAny<int>())).Returns(() => $"ARCH{++archived}");
            _roomRepo.Setup(r => r.CreateRoomAsync(code, host, It.IsAny<CancellationToken>())).ReturnsAsync(next);
            _roomRepo.Setup(r => r.AddTeam(It.IsAny<RoomTeamEntity>())).Callback<RoomTeamEntity>(added.Add);
            _participationRepo.Setup(r => r.CreateRoomPlayer(next, "alice", host, It.IsAny<CancellationToken>())).ReturnsAsync(hostLink);

            return (next, hostLink, added);
        }

        // прошлая игра на итогах и последняя игра серии
        private RoomEntity LatestGame(RoomEntity old, RoomStatus status, int gameNumber = 2, bool isTeamGame = false)
        {
            old.SeriesId ??= old.Id;
            var latest = new RoomEntity
            {
                Id = Guid.NewGuid(),
                RoomCode = "CODE",
                Status = status,
                IsTeamGame = isTeamGame,
                SeriesId = old.SeriesId,
                GameNumber = gameNumber,
            };
            _roomRepo.Setup(r => r.GetRoomByIdAsync(latest.Id, It.IsAny<CancellationToken>())).ReturnsAsync(latest);
            _roomRepo.Setup(r => r.GetLatestInSeriesAsync(old.SeriesId.Value, It.IsAny<CancellationToken>())).ReturnsAsync(latest);

            return latest;
        }

        [Fact]
        public async Task Rematch_CopiesTeamsAndMovesCodes_WithoutMail()
        {
            var (old, oldTeams, players) = TeamRoom(RoomStatus.Finished);
            var (host, hostOld) = RematchHost(old, players, team: 2);
            var (next, hostLink, added) = NextCreated(host, "CODE");

            var (result, refusal) = await _sut.RematchAsync(old.Id, CancellationToken.None);

            Assert.Equal(RematchRefusal.None, refusal);
            Assert.Equal(next.Id, result!.Id);
            Assert.Equal((2, old.Id, old.Id), (next.GameNumber, next.SeriesId!.Value, next.PreviousRoomId!.Value));
            Assert.Equal(old.Id, old.SeriesId);
            Assert.True(next.IsTeamGame);
            // ссылки переехали в новую игру, у старой архивные коды
            Assert.Equal(new[] { (1, "TEAM1"), (2, "TEAM2") }, added.Select(t => (t.Number, t.JoinCode)));
            Assert.DoesNotContain(oldTeams, t => t.JoinCode.StartsWith("TEAM"));
            Assert.StartsWith("ARCH", old.RoomCode);
            Assert.Equal(new[] { "http://quiz/join/TEAM1", "http://quiz/join/TEAM2" }, result.Teams!.Select(t => t.JoinLink));
            // ведущий в прежней команде, капитанство заново
            Assert.Equal(2, hostLink.TeamNumber);
            Assert.Equal(hostOld.Id, hostLink.OriginRoomPlayerId);
            _notifier.Verify(n => n.RoomUpdated(old.Id), Times.Once);
            _mailer.Verify(m => m.SendTeamLinksAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<TeamLinkResponse>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Rematch_Again_ReturnsSameNextGame()
        {
            var old = Setup(RoomStatus.Finished);
            RematchHost(old, []);
            var next = LatestGame(old, RoomStatus.Waiting);
            _roomRepo.Setup(r => r.GetNextRoomAsync(old.Id, It.IsAny<CancellationToken>())).ReturnsAsync(next);

            var (result, refusal) = await _sut.RematchAsync(old.Id, CancellationToken.None);

            Assert.Equal(RematchRefusal.None, refusal);
            Assert.Equal(next.Id, result!.Id);
            _roomRepo.Verify(r => r.CreateRoomAsync(It.IsAny<string>(), It.IsAny<UserEntity>(), It.IsAny<CancellationToken>()), Times.Never);
            _notifier.Verify(n => n.RoomUpdated(It.IsAny<Guid>()), Times.Never);
        }

        [Theory]
        [InlineData(false, RoomStatus.Finished, RematchRefusal.Forbidden)]
        [InlineData(true, RoomStatus.Waiting, RematchRefusal.NotFinished)]
        public async Task Rematch_NotOwnerOrNotFinished_Refused(bool isOwner, RoomStatus status, RematchRefusal expected)
        {
            var old = Setup(status);
            RematchHost(old, []);
            _roomRepo.Setup(r => r.IsOwner(old.Id, "alice-subject", It.IsAny<CancellationToken>())).ReturnsAsync(isOwner);

            var (result, refusal) = await _sut.RematchAsync(old.Id, CancellationToken.None);

            Assert.Null(result);
            Assert.Equal(expected, refusal);
            _roomRepo.Verify(r => r.CreateRoomAsync(It.IsAny<string>(), It.IsAny<UserEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        // переход с итогов

        [Fact]
        public async Task Follow_Guest_KeepsNicknameAndTeam()
        {
            var (old, teams, players) = TeamRoom(RoomStatus.Finished);
            var me = MeGuest(old, players, 2, joinedAgo: 600);
            var latest = LatestGame(old, RoomStatus.Waiting, isTeamGame: true);
            _roomRepo.Setup(r => r.GetTeams(latest.Id, It.IsAny<CancellationToken>())).ReturnsAsync(teams);
            _participationRepo.Setup(r => r.GetParticipationsByRoomId(latest.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
            var link = new RoomPlayerEntity { Id = Guid.NewGuid(), Room = latest, RoomId = latest.Id, Nickname = "bob" };
            _participationRepo.Setup(r => r.CreateRoomPlayer(latest, "bob", null, It.IsAny<CancellationToken>())).ReturnsAsync(link);

            var (result, refusal) = await _sut.FollowAsync(old.Id, CancellationToken.None);

            Assert.Equal(JoinRefusal.None, refusal);
            Assert.Equal((link.Id, 2), (result!.RoomPlayerLinkId, result.TeamNumber));
            Assert.Equal(me.Id, link.OriginRoomPlayerId);
            // капитанство не переносится: в новой команде время входа своё
            Assert.Equal(Now, link.TeamJoinedAt);
            Assert.NotEqual(me.TeamJoinedAt, link.TeamJoinedAt);
            LobbySaved(latest);
        }

        [Fact]
        public async Task Follow_Again_ReturnsSameParticipation()
        {
            var old = Setup(RoomStatus.Finished);
            var me = Participant(old, isActive: true);
            AsGuest(me.Id);
            Mine(old, me);
            var latest = LatestGame(old, RoomStatus.Waiting);
            var mine = Participant(latest, isActive: true);
            mine.OriginRoomPlayerId = me.Id;
            _participationRepo.Setup(r => r.FindByOriginAsync(latest.Id, me.Id, It.IsAny<CancellationToken>())).ReturnsAsync(mine);

            var (result, refusal) = await _sut.FollowAsync(old.Id, CancellationToken.None);

            Assert.Equal(JoinRefusal.None, refusal);
            Assert.Equal(mine.Id, result!.RoomPlayerLinkId);
            _participationRepo.Verify(r => r.CreateRoomPlayer(It.IsAny<RoomEntity>(), It.IsAny<string>(), It.IsAny<UserEntity?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Follow_NicknameTaken_AddsNumber()
        {
            var old = Setup(RoomStatus.Finished);
            var me = Participant(old, isActive: true);
            AsGuest(me.Id);
            Mine(old, me);
            var latest = LatestGame(old, RoomStatus.Waiting);
            _participationRepo.Setup(r => r.IsNicknameTaken(latest.Id, "bob", It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var link = new RoomPlayerEntity { Id = Guid.NewGuid(), Room = latest, RoomId = latest.Id, Nickname = "bob 2" };
            _participationRepo.Setup(r => r.CreateRoomPlayer(latest, "bob 2", null, It.IsAny<CancellationToken>())).ReturnsAsync(link);

            var (result, refusal) = await _sut.FollowAsync(old.Id, CancellationToken.None);

            Assert.Equal(JoinRefusal.None, refusal);
            Assert.Equal("bob 2", result!.PlayerName);
        }

        [Theory]
        [InlineData(RoomStatus.InProgress, JoinRefusal.Started)]
        [InlineData(RoomStatus.Finished, JoinRefusal.NextFinished)]
        public async Task Follow_LatestStartedOrFinishedWithoutMe_Refused(RoomStatus status, JoinRefusal expected)
        {
            var old = Setup(RoomStatus.Finished);
            var me = Participant(old, isActive: true);
            AsGuest(me.Id);
            Mine(old, me);
            LatestGame(old, status);

            var (result, refusal) = await _sut.FollowAsync(old.Id, CancellationToken.None);

            Assert.Null(result);
            Assert.Equal(expected, refusal);
        }

        // уже сыгранная следующая игра со мной: открываем её итоги, состав не трогаем
        [Fact]
        public async Task Follow_LatestFinishedWithMe_ReturnsParticipationWithoutRejoin()
        {
            var old = Setup(RoomStatus.Finished);
            var me = MeAccount(old, [], 1);
            var latest = LatestGame(old, RoomStatus.Finished);
            var mine = Participant(latest, isActive: false);
            _participationRepo.Setup(r => r.FindParticipant(latest.Id, null, "alice-subject", It.IsAny<CancellationToken>())).ReturnsAsync(mine);

            var (result, refusal) = await _sut.FollowAsync(old.Id, CancellationToken.None);

            Assert.Equal(JoinRefusal.None, refusal);
            Assert.Equal(mine.Id, result!.RoomPlayerLinkId);
            Assert.False(mine.IsActive);
            _roomRepo.Verify(r => r.MarkChanged(It.IsAny<RoomEntity>()), Times.Never);
        }

        // пропустил игру 2: из итогов игры 1 сразу в игру 3
        [Fact]
        public async Task Follow_SkippedGame_EntersLatestWithOrigin()
        {
            var old = Setup(RoomStatus.Finished);
            var me = Participant(old, isActive: true);
            AsGuest(me.Id);
            Mine(old, me);
            var latest = LatestGame(old, RoomStatus.Waiting, gameNumber: 3);
            var link = new RoomPlayerEntity { Id = Guid.NewGuid(), Room = latest, RoomId = latest.Id, Nickname = "bob" };
            _participationRepo.Setup(r => r.CreateRoomPlayer(latest, "bob", null, It.IsAny<CancellationToken>())).ReturnsAsync(link);

            var (result, refusal) = await _sut.FollowAsync(old.Id, CancellationToken.None);

            Assert.Equal(JoinRefusal.None, refusal);
            Assert.Equal(latest.Id, result!.RoomId);
            Assert.Equal(me.Id, link.OriginRoomPlayerId);
        }

        // устаревшая вкладка игры 1: человек уже перешёл в 2 и 3, второго участия не появляется
        [Fact]
        public async Task Follow_StaleTab_ReturnsExistingParticipationInLatest()
        {
            var old = Setup(RoomStatus.Finished);
            var me = Participant(old, isActive: true);
            AsGuest(me.Id);
            Mine(old, me);
            var latest = LatestGame(old, RoomStatus.Waiting, gameNumber: 3);
            var mine = Participant(latest, isActive: true);
            mine.OriginRoomPlayerId = me.Id;
            _participationRepo.Setup(r => r.FindByOriginAsync(latest.Id, me.Id, It.IsAny<CancellationToken>())).ReturnsAsync(mine);

            var (result, refusal) = await _sut.FollowAsync(old.Id, CancellationToken.None);

            Assert.Equal(JoinRefusal.None, refusal);
            Assert.Equal((mine.Id, latest.Id), (result!.RoomPlayerLinkId, result.RoomId));
            _participationRepo.Verify(r => r.CreateRoomPlayer(It.IsAny<RoomEntity>(), It.IsAny<string>(), It.IsAny<UserEntity?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        // убран в лобби игры 2 (без команды): переходит в самую маленькую команду
        [Fact]
        public async Task Follow_RemovedInNextLobby_GetsSmallestTeam()
        {
            var (old, teams, players) = TeamRoom(RoomStatus.Finished);
            MeAccount(old, players, 1);
            var latest = LatestGame(old, RoomStatus.Waiting, isTeamGame: true);
            var latestPlayers = new List<RoomPlayerEntity>();
            _roomRepo.Setup(r => r.GetTeams(latest.Id, It.IsAny<CancellationToken>())).ReturnsAsync(teams);
            _participationRepo.Setup(r => r.GetParticipationsByRoomId(latest.Id, It.IsAny<CancellationToken>())).ReturnsAsync(latestPlayers);
            InTeam(latest, latestPlayers, 1);
            var mine = InTeam(latest, latestPlayers, null, isActive: false);
            mine.ExitReason = ExitReason.Kicked;
            _participationRepo.Setup(r => r.FindParticipant(latest.Id, null, "alice-subject", It.IsAny<CancellationToken>())).ReturnsAsync(mine);

            var (result, refusal) = await _sut.FollowAsync(old.Id, CancellationToken.None);

            Assert.Equal(JoinRefusal.None, refusal);
            Assert.Equal(2, result!.TeamNumber);
            Assert.True(mine.IsActive);
            LobbySaved(latest);
        }

        // отмена игры 2 возвращает ссылки игре 1, следующая переигровка снова игра 2
        [Fact]
        public async Task CancelNextGame_ReturnsCodes_AndRematchAgainIsSameNumber()
        {
            var (previous, previousTeams, players) = TeamRoom(RoomStatus.Finished);
            previous.RoomCode = "ARCH0";
            previous.SeriesId = previous.Id;
            previousTeams.ForEach(t => t.JoinCode = $"ARCHT{t.Number}");
            var (host, _) = RematchHost(previous, players, team: 1);
            var cancelled = new RoomEntity
            {
                Id = Guid.NewGuid(),
                RoomCode = "CODE",
                Status = RoomStatus.Waiting,
                IsTeamGame = true,
                HostUserId = host.Id,
                PreviousRoomId = previous.Id,
                SeriesId = previous.Id,
                GameNumber = 2,
            };
            _roomRepo.Setup(r => r.GetRoomByIdAsync(cancelled.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cancelled);
            _roomRepo.Setup(r => r.GetTeams(cancelled.Id, It.IsAny<CancellationToken>())).ReturnsAsync(
            [
                new RoomTeamEntity { RoomId = cancelled.Id, Number = 1, Name = "Команда 1", JoinCode = "TEAM1" },
                new RoomTeamEntity { RoomId = cancelled.Id, Number = 2, Name = "Команда 2", JoinCode = "TEAM2" },
            ]);

            var deleted = await _sut.DeleteExistingRoomByIdAsync(cancelled.Id, CancellationToken.None);

            Assert.Equal(DeleteRoomResult.Deleted, deleted);
            Assert.Equal("CODE", previous.RoomCode);
            Assert.Equal(new[] { "TEAM1", "TEAM2" }, previousTeams.Select(t => t.JoinCode));
            _roomRepo.Verify(r => r.RemoveRoom(cancelled), Times.Once);
            _notifier.Verify(n => n.RoomUpdated(cancelled.Id), Times.Once);
            _notifier.Verify(n => n.RoomUpdated(previous.Id), Times.Once);

            var (next, _, _) = NextCreated(host, "CODE");

            var (result, refusal) = await _sut.RematchAsync(previous.Id, CancellationToken.None);

            Assert.Equal(RematchRefusal.None, refusal);
            Assert.Equal(next.Id, result!.Id);
            Assert.Equal(2, next.GameNumber);
        }

        // лобби игры 2: ведущему настройки прошлой игры и сколько уже перешли, игроку ничего
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task RoomState_NextLobby_PreviousSettingsOnlyForHost(bool isHost)
        {
            var room = Setup(RoomStatus.Waiting);
            var previousRoomId = Guid.NewGuid();
            room.PreviousRoomId = previousRoomId;
            room.SeriesId = previousRoomId;
            room.GameNumber = 2;
            var players = new List<RoomPlayerEntity>();
            _participationRepo.Setup(r => r.GetParticipationsByRoomId(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(players);
            var hostId = Guid.NewGuid();
            room.HostUserId = hostId;
            var guest = InTeam(room, players, null);
            guest.OriginRoomPlayerId = Guid.NewGuid();
            InTeam(room, players, null);

            if (isHost)
            {
                MeAccount(room, players, 1, isHost: true).TeamNumber = null;
            }
            else
            {
                AsGuest(guest.Id);
                Mine(room, guest);
            }

            var categoryId = Guid.NewGuid();
            _matchRepo.Setup(r => r.GetMatchByRoomId(previousRoomId, It.IsAny<CancellationToken>())).ReturnsAsync(new MatchEntity
            {
                RoomId = previousRoomId,
                CategoryIds = [categoryId],
                RequestedQuestionCount = 3,
                RoomMode = RoomMode.Fastest,
            });
            _participationRepo.Setup(r => r.CountActiveNonHostAsync(previousRoomId, room.HostUserId, It.IsAny<CancellationToken>())).ReturnsAsync(2);

            var state = await _sut.GetRoomStateAsync(room.Id, CancellationToken.None);

            Assert.Equal((2, previousRoomId), (state!.GameNumber, state.PreviousRoomId!.Value));

            if (isHost)
            {
                Assert.Equal((2, 1), (state.PreviousPlayers, state.ReturnedPlayers));
                Assert.Equal([categoryId], state.PreviousSettings!.CategoryIds);
                Assert.Equal((3, RoomMode.Fastest), (state.PreviousSettings.QuestionCount, state.PreviousSettings.Mode));
            }
            else
            {
                Assert.Equal((0, 0), (state.PreviousPlayers, state.ReturnedPlayers));
                Assert.Null(state.PreviousSettings);
            }
        }
    }
}
