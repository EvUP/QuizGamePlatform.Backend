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
            // Сохранение без БД.
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

        [Fact]
        public async Task Join_RoomNotFound_ReturnsNull()
        {
            _roomRepo.Setup(r => r.GetRoomByRoomCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RoomEntity?)null);

            var (result, _) = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task Join_RoomFinished_ReturnsNull()
        {
            Setup(RoomStatus.Finished);

            var (result, _) = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task Join_NewGuest_NicknameAndCodeNormalized()
        {
            var room = Setup(RoomStatus.Waiting);
            var link = Participant(room, isActive: true);
            _participationRepo.Setup(r => r.CreateRoomPlayer(room, "bob", null, It.IsAny<CancellationToken>())).ReturnsAsync(link);

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
        public async Task Join_NewPlayer_MatchInProgress_NotFoundEvenIfNicknameTaken()
        {
            var room = Setup(RoomStatus.InProgress);
            _participationRepo.Setup(r => r.IsNicknameTaken(room.Id, "bob", It.IsAny<CancellationToken>())).ReturnsAsync(true);

            var (result, refusal) = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", CancellationToken.None);

            Assert.Null(result);
            Assert.NotEqual(JoinRefusal.NicknameTaken, refusal);
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

            var (result, refusal) = await _sut.JoinToRoomByRoomCodeAsync(null, "CODE", CancellationToken.None);

            Assert.NotEqual(JoinRefusal.NicknameTaken, refusal);
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

        [Theory]
        [InlineData(60, -20, true)]  // вышел в матче, ещё в окне
        [InlineData(60, -41, false)] // вышел в матче, окно истекло
        [InlineData(10, -20, false)] // вышел ещё до старта
        public async Task Rejoin_InProgress_OnlyLeftAfterStartWithinWindow(int startedAgo, int leftAt, bool returns)
        {
            var room = Setup(RoomStatus.InProgress);
            MatchStarted(room, startedAgo);
            var left = Participant(room, isActive: false, finishedAt: Now.AddSeconds(leftAt));
            AsGuest(left.Id);
            Mine(room, left);

            var (result, _) = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", CancellationToken.None);

            Assert.Equal(returns, result is not null);
            Assert.Equal(returns, left.IsActive);
            _notifier.Verify(n => n.RoomUpdated(room.Id), returns ? Times.Once() : Times.Never());
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

            var (result, refusal) = await _sut.JoinToRoomByRoomCodeAsync(null, "CODE", CancellationToken.None);

            Assert.NotNull(result);
            Assert.NotEqual(JoinRefusal.NicknameTaken, refusal);
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

            var (result, refusal) = await _sut.JoinToRoomByRoomCodeAsync("bob", " team2 ", CancellationToken.None);

            Assert.NotEqual(JoinRefusal.TeamLinkRequired, refusal);
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

        [Fact]
        public void TeamResults_SamePoints_SamePlace()
        {
            var room = new RoomEntity { Id = Guid.NewGuid() };
            var players = new List<RoomPlayerEntity>();
            InTeam(room, players, 1);
            InTeam(room, players, 2);
            InTeam(room, players, 3);
            var teams = Enumerable.Range(1, 3).Select(n => new RoomTeamEntity { Number = n, Name = $"T{n}" }).ToList();

            var result = teams.ToTeamResponses(RoomStatus.Finished, players, m => m[0].TeamNumber == 3 ? 100 : 200, _ => null);

            Assert.Equal(new[] { (1, 1), (2, 1), (3, 3) }, result.Select(t => (t.Number, t.Place!.Value)));
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
    }
}
