using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
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
            // мокаем контекст, нужен только SaveChanges
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

            var result = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task Join_RoomFinished_ReturnsNull()
        {
            _roomRepo.Setup(r => r.GetRoomByRoomCodeAsync("CODE", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RoomEntity { Id = Guid.NewGuid(), Status = RoomStatus.Finished });

            var result = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", CancellationToken.None);

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

            var result = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", CancellationToken.None);

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

            var result = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", CancellationToken.None);

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

            var result = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", CancellationToken.None);

            Assert.Equal(shouldJoin, result is not null);
        }

        // реконнект

        [Fact]
        public async Task Rejoin_AlreadyActive_ReturnsResponse()
        {
            var (room, player) = Setup(RoomStatus.InProgress);
            var existing = Participant(room, player, isActive: true);
            _participationRepo.Setup(r => r.GetRoomPlayerById(room.Id, player.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(existing);

            var result = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", CancellationToken.None);

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

            var result = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", CancellationToken.None);

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

            var result = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", CancellationToken.None);

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

            var result = await _sut.JoinToRoomByRoomCodeAsync("bob", "CODE", CancellationToken.None);

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
