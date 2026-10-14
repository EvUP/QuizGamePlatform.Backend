using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;
using QuizGamePlatform.Backend.Api.Controllers;
using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.Auth;
using QuizGamePlatform.Backend.Application.Contracts.Room;
using QuizGamePlatform.Backend.Application.Enums;
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

            var result = await _sut.CreateRoomAsync(CancellationToken.None);

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

            var (result, nicknameTaken) = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", CancellationToken.None);

            Assert.Null(result);
            Assert.False(nicknameTaken);
        }

        [Theory]
        [InlineData(5, false)]
        [InlineData(4, true)]
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

            var (result, nicknameTaken) = await _sut.JoinToRoomByRoomCodeAsync(null, "CODE", CancellationToken.None);

            Assert.False(nicknameTaken);
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
        [InlineData(5, false)] // пока его не было комнату заполнили
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

            var (result, nicknameTaken) = await _sut.JoinToRoomByRoomCodeAsync(null, "CODE", CancellationToken.None);

            Assert.NotNull(result);
            Assert.False(nicknameTaken);
            _participationRepo.Verify(
                r => r.IsNicknameTaken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // параллельный вход занял последнее место или комнату удалили
        [Fact]
        public async Task Join_ConcurrentRoomChange_NotJoinedWithoutToken()
        {
            var room = Setup(RoomStatus.Waiting);
            _participationRepo.Setup(r => r.CreateRoomPlayer(room, "bob", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Participant(room, isActive: true));
            _context.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new DbUpdateConcurrencyException());
            var tokens = new Mock<IGuestTokenService>(MockBehavior.Strict);
            var controller = CreateController(tokens);

            var result = await controller.JoinToRoom(new("bob", "CODE"), CancellationToken.None);

            Assert.IsType<NotFoundObjectResult>(result);
            _roomRepo.Verify(r => r.MarkChanged(room), Times.Once);
            _notifier.Verify(n => n.RoomUpdated(It.IsAny<Guid>()), Times.Never);
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
    }
}
