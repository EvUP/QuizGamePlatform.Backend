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
        private readonly Mock<IPlayerRepository> _playerRepo = new();
        private readonly Mock<IRoomHelper> _roomHelper = new();
        private readonly FakeTimeProvider _time = new();
        private readonly RoomService _sut;

        public RoomServiceTests()
        {
            // Сохранение без БД.
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().Options;
            var context = new Mock<ApplicationDbContext>(options);
            context.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            _sut = new RoomService(
                _roomRepo.Object,
                _participationRepo.Object,
                _playerRepo.Object,
                _roomHelper.Object,
                context.Object,
                Mock.Of<ILogger<RoomService>>(),
                _time);
        }

        private DateTime Now => _time.GetUtcNow().UtcDateTime;

        private RoomController CreateController(
            DefaultHttpContext httpContext,
            Mock<IGuestTokenService> tokens) =>
            new(_sut, new CurrentUserContext(new HttpContextAccessor { HttpContext = httpContext }), tokens.Object)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext },
            };

        private static RoomPlayerEntity Participant(RoomEntity room, PlayerEntity player, bool isActive, DateTime? finishedAt = null)
            => new()
            {
                Id = Guid.NewGuid(),
                RoomId = room.Id,
                Room = room,
                PlayerId = player.Id,
                Player = player,
                IsActive = isActive,
                FinishedAt = finishedAt
            };

        private (RoomEntity room, PlayerEntity player) Setup(RoomStatus status)
        {
            var room = new RoomEntity { Id = Guid.NewGuid(), RoomCode = "CODE", Status = status };
            var player = new PlayerEntity { Id = Guid.NewGuid(), UserName = "bob" };

            _roomRepo.Setup(r => r.GetRoomByRoomCodeAsync("CODE", It.IsAny<CancellationToken>())).ReturnsAsync(room);
            _playerRepo.Setup(r => r.GetOrCreatePlayerAsync("bob", It.IsAny<CancellationToken>())).ReturnsAsync(player);

            return (room, player);
        }

        // вход нового игрока

        [Fact]
        public async Task Join_RoomNotFound_ReturnsNull()
        {
            _roomRepo.Setup(r => r.GetRoomByRoomCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RoomEntity?)null);

            var (result, _) = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", null, CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task Join_RoomFinished_ReturnsNull()
        {
            _roomRepo.Setup(r => r.GetRoomByRoomCodeAsync("CODE", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RoomEntity { Id = Guid.NewGuid(), Status = RoomStatus.Finished });

            var (result, _) = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", null, CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task Join_NewPlayer_WaitingRoom_CreatesParticipation()
        {
            var (room, player) = Setup(RoomStatus.Waiting);
            _participationRepo.Setup(r => r.GetRoomPlayerById(room.Id, player.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync((RoomPlayerEntity?)null);

            var link = Participant(room, player, isActive: true);
            _participationRepo.Setup(r => r.CreateRoomPlayer(player, room, It.IsAny<CancellationToken>())).ReturnsAsync(link);

            var (result, _) = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", null, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(player.Id, result!.PlayerId);
            Assert.True(result.IsActive);
        }

        [Fact]
        public async Task Join_NewPlayer_MatchInProgress_ReturnsNull()
        {
            var (room, player) = Setup(RoomStatus.InProgress);
            _participationRepo.Setup(r => r.GetRoomPlayerById(room.Id, player.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync((RoomPlayerEntity?)null);

            var (result, _) = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", null, CancellationToken.None);

            Assert.Null(result);
        }

        [Theory]
        [InlineData(5, false)]
        [InlineData(3, true)]
        public async Task Join_NewPlayer_RespectsActiveLimit(int activeCount, bool shouldJoin)
        {
            var (room, player) = Setup(RoomStatus.Waiting);
            _participationRepo.Setup(r => r.GetRoomPlayerById(room.Id, player.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync((RoomPlayerEntity?)null);
            _participationRepo.Setup(r => r.CountActivePlayers(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(activeCount);
            _participationRepo.Setup(r => r.CreateRoomPlayer(player, room, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Participant(room, player, isActive: true));

            var (result, _) = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", null, CancellationToken.None);

            Assert.Equal(shouldJoin, result is not null);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Join_NewParticipant_IssuesTokenOnlyToGuest(bool registered)
        {
            var (room, player) = Setup(RoomStatus.Waiting);
            var link = Participant(room, player, isActive: true);
            _participationRepo.Setup(r => r.CreateRoomPlayer(player, room, It.IsAny<CancellationToken>()))
                .ReturnsAsync(link);
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim("sub", "registered-subject") }, registered ? "Bearer" : null)),
            };
            var tokens = new Mock<IGuestTokenService>(MockBehavior.Strict);
            if (!registered)
            {
                tokens.Setup(t => t.Issue(link.Id, room.Id, "bob"))
                    .Returns(new GuestTokenResult("guest-token", Now.AddHours(4)));
            }
            var controller = CreateController(httpContext, tokens);

            var result = await controller.JoinToRoom(new("bob", "CODE"), CancellationToken.None);

            var response = Assert.IsType<JoinToRoomResponse>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal(link.Id, response.Room.RoomPlayerLinkId);
            Assert.Equal(registered ? null : "guest-token", response.GuestAccessToken);
            Assert.Equal(registered ? (DateTime?)null : Now.AddHours(4), response.GuestTokenExpiresAtUtc);
        }

        // реконнект

        [Fact]
        public async Task Join_InvalidProvidedToken_ReturnsUnauthorized()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers.Authorization = "Bearer expired-token";
            var tokens = new Mock<IGuestTokenService>(MockBehavior.Strict);
            var controller = CreateController(httpContext, tokens);

            var result = await controller.JoinToRoom(new("bob", "CODE"), CancellationToken.None);

            Assert.IsType<UnauthorizedObjectResult>(result);
            _roomRepo.Verify(
                r => r.GetRoomByRoomCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Theory]
        [InlineData(null)]
        [InlineData(AuthenticationSchemes.Guest)]
        [InlineData("Bearer")]
        public async Task Join_AnotherParticipantsNickname_ReturnsConflictWithoutIssuingToken(string? authenticationType)
        {
            var (room, player) = Setup(RoomStatus.Waiting);
            var existing = Participant(room, player, isActive: false, finishedAt: Now.AddSeconds(-20));
            _participationRepo.Setup(r => r.GetRoomPlayerById(room.Id, player.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existing);
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim("sub", Guid.NewGuid().ToString()) }, authenticationType)),
            };
            var tokens = new Mock<IGuestTokenService>(MockBehavior.Strict);
            var controller = CreateController(httpContext, tokens);

            var result = await controller.JoinToRoom(new("bob", "CODE"), CancellationToken.None);

            Assert.IsType<ConflictObjectResult>(result);
            Assert.False(existing.IsActive);
            Assert.Equal(Now.AddSeconds(-20), existing.FinishedAt);
            tokens.Verify(t => t.Issue(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Join_OwnParticipation_ReconnectsAndIssuesTokenForSameParticipation()
        {
            var (room, player) = Setup(RoomStatus.Waiting);
            var existing = Participant(room, player, isActive: false, finishedAt: Now.AddSeconds(-20));
            _participationRepo.Setup(r => r.GetRoomPlayerById(room.Id, player.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existing);
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim("sub", existing.Id.ToString()) }, AuthenticationSchemes.Guest)),
            };
            var tokens = new Mock<IGuestTokenService>(MockBehavior.Strict);
            tokens.Setup(t => t.Issue(existing.Id, room.Id, "bob"))
                .Returns(new GuestTokenResult("guest-token", Now.AddHours(4)));
            var controller = CreateController(httpContext, tokens);

            var result = await controller.JoinToRoom(new("bob", "CODE"), CancellationToken.None);

            var response = Assert.IsType<JoinToRoomResponse>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal(existing.Id, response.Room.RoomPlayerLinkId);
            Assert.Equal("guest-token", response.GuestAccessToken);
            Assert.True(existing.IsActive);
            Assert.Null(existing.FinishedAt);
        }

        [Fact]
        public async Task Rejoin_AlreadyActive_ReturnsResponse()
        {
            var (room, player) = Setup(RoomStatus.InProgress);
            var existing = Participant(room, player, isActive: true);
            _participationRepo.Setup(r => r.GetRoomPlayerById(room.Id, player.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existing);

            var (result, _) = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", existing.Id, CancellationToken.None);

            Assert.NotNull(result);
            Assert.True(result!.IsActive);
        }

        [Fact]
        public async Task Rejoin_InProgress_WithinWindow_Reconnects()
        {
            var (room, player) = Setup(RoomStatus.InProgress);
            var left = Participant(room, player, isActive: false, finishedAt: Now.AddSeconds(-20)); // ещё в окне
            _participationRepo.Setup(r => r.GetRoomPlayerById(room.Id, player.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(left);

            var (result, _) = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", left.Id, CancellationToken.None);

            Assert.NotNull(result);
            Assert.True(left.IsActive);
            Assert.Null(left.FinishedAt);
        }

        [Fact]
        public async Task Rejoin_InProgress_PastWindow_ReturnsNull()
        {
            var (room, player) = Setup(RoomStatus.InProgress);
            var left = Participant(room, player, isActive: false, finishedAt: Now.AddSeconds(-41)); // уже за окном
            _participationRepo.Setup(r => r.GetRoomPlayerById(room.Id, player.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(left);

            var (result, _) = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", left.Id, CancellationToken.None);

            Assert.Null(result);
            Assert.False(left.IsActive);
        }

        [Fact]
        public async Task Rejoin_WaitingRoom_IgnoresReconnectWindow()
        {
            var (room, player) = Setup(RoomStatus.Waiting);
            var left = Participant(room, player, isActive: false, finishedAt: Now.AddSeconds(-500)); // в waiting окно не важно
            _participationRepo.Setup(r => r.GetRoomPlayerById(room.Id, player.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(left);

            var (result, _) = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", left.Id, CancellationToken.None);

            Assert.NotNull(result);
            Assert.True(left.IsActive);
            Assert.Null(left.FinishedAt);
        }

        // выход из комнаты

        [Fact]
        public async Task Leave_PlayerNotInRoom_ReturnsNull()
        {
            _participationRepo.Setup(r => r.GetRoomPlayerById(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RoomPlayerEntity?)null);

            var result = await _sut.LeaveRoom(Guid.NewGuid(), Guid.NewGuid(), ExitReason.Normal, CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task Leave_AlreadyInactive_ReturnsNull()
        {
            var room = new RoomEntity { Id = Guid.NewGuid(), Status = RoomStatus.InProgress };
            var player = new PlayerEntity { Id = Guid.NewGuid(), UserName = "bob" };
            var left = Participant(room, player, isActive: false, finishedAt: Now);
            _participationRepo.Setup(r => r.GetRoomPlayerById(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(left);

            var result = await _sut.LeaveRoom(room.Id, player.Id, ExitReason.Normal, CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task Leave_ActivePlayer_SetsFinishedAtNow_AndInactive()
        {
            var room = new RoomEntity { Id = Guid.NewGuid(), Status = RoomStatus.InProgress };
            var player = new PlayerEntity { Id = Guid.NewGuid(), UserName = "bob" };
            var active = Participant(room, player, isActive: true);
            _participationRepo.Setup(r => r.GetRoomPlayerById(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(active);

            var result = await _sut.LeaveRoom(room.Id, player.Id, ExitReason.Disconnected, CancellationToken.None);

            Assert.NotNull(result);
            Assert.False(active.IsActive);
            Assert.Equal(Now, active.FinishedAt);
            Assert.Equal(ExitReason.Disconnected, active.ExitReason);
        }
    }
}
