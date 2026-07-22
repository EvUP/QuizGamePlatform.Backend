using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using QuizGamePlatform.Backend.Application.Contracts.Match;
using QuizGamePlatform.Backend.Application.Enums;
using QuizGamePlatform.Backend.Application.Services;
using QuizGamePlatform.Backend.Core.Abstractions;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.Tests
{
    public class MatchServiceTests
    {
        private readonly Mock<IMatchRepository> _matchRepo = new();
        private readonly Mock<IRoomRepository> _roomRepo = new();
        private readonly Mock<IRoomParticipationRepository> _participationRepo = new();
        private readonly Mock<IQuizContentRepository> _contentRepo = new();
        private readonly Mock<IMatchLockProvider> _locks = new();
        private readonly FakeTimeProvider _time = new();
        private readonly MatchService _sut;

        public MatchServiceTests()
        {
            _locks.Setup(l => l.GetLock(It.IsAny<Guid>())).Returns(new SemaphoreSlim(1, 1));

            _sut = new MatchService(
                _matchRepo.Object,
                _roomRepo.Object,
                _participationRepo.Object,
                _contentRepo.Object,
                _locks.Object,
                Mock.Of<ILogger<MatchService>>(),
                _time);
        }

        private DateTime Now => _time.GetUtcNow().UtcDateTime;

        private MatchEntity RunningMatch(
            Guid matchId,
            int index = 0,
            int questionCount = 1,
            MatchStatus status = MatchStatus.QuestionActive)
        {
            var questions = Enumerable.Range(0, questionCount)
                .Select(i => new MatchQuestionEntity { Id = Guid.NewGuid(), QuestionId = Guid.NewGuid(), Order = i })
                .ToList();

            return new MatchEntity
            {
                Id = matchId,
                RoomId = Guid.NewGuid(),
                Status = status,
                CurrentQuestionIndex = index,
                QuestionEndsAt = Now.AddSeconds(30),
                Questions = questions
            };
        }

        // старт матча

        [Fact]
        public async Task StartMatch_RoomNotWaiting_ReturnsNull()
        {
            var roomId = Guid.NewGuid();
            _roomRepo.Setup(r => r.GetRoomByIdAsync(roomId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RoomEntity { Id = roomId, Status = RoomStatus.InProgress });

            var result = await _sut.StartMatchAsync(new StartMatchRequest(roomId, Guid.NewGuid(), 5), CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task StartMatch_FewerThanTwoActivePlayers_ReturnsNull()
        {
            var roomId = Guid.NewGuid();
            _roomRepo.Setup(r => r.GetRoomByIdAsync(roomId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RoomEntity { Id = roomId, Status = RoomStatus.Waiting });
            _participationRepo.Setup(r => r.CountActivePlayers(roomId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);

            var result = await _sut.StartMatchAsync(new StartMatchRequest(roomId, Guid.NewGuid(), 5), CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task StartMatch_NoQuestions_ReturnsNull()
        {
            var roomId = Guid.NewGuid();
            _roomRepo.Setup(r => r.GetRoomByIdAsync(roomId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RoomEntity { Id = roomId, Status = RoomStatus.Waiting });
            _participationRepo.Setup(r => r.CountActivePlayers(roomId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(2);
            _contentRepo.Setup(r => r.GetQuestionsByCategoryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<QuestionEntity>());

            var result = await _sut.StartMatchAsync(new StartMatchRequest(roomId, Guid.NewGuid(), 5), CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task StartMatch_Valid_CreatesActiveMatch_SetsRoomInProgress_DeadlineIn30s()
        {
            var room = new RoomEntity { Id = Guid.NewGuid(), Status = RoomStatus.Waiting };
            _roomRepo.Setup(r => r.GetRoomByIdAsync(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(room);
            _participationRepo.Setup(r => r.CountActivePlayers(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(2);
            _contentRepo.Setup(r => r.GetQuestionsByCategoryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<QuestionEntity> { new() { Id = Guid.NewGuid() }, new() { Id = Guid.NewGuid() } });

            MatchEntity? created = null;
            _matchRepo.Setup(r => r.AddMatchAsync(It.IsAny<MatchEntity>(), It.IsAny<CancellationToken>()))
                .Callback<MatchEntity, CancellationToken>((m, _) => created = m);

            var result = await _sut.StartMatchAsync(new StartMatchRequest(room.Id, Guid.NewGuid(), 2), CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(MatchStatus.QuestionActive, result!.Status);
            Assert.Equal(RoomStatus.InProgress, room.Status);
            Assert.NotNull(created);
            Assert.Equal(Now.AddSeconds(30), created!.QuestionEndsAt);
            _matchRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task StartMatch_SecondActiveMatch_ReturnsNull()
        {
            var room = new RoomEntity { Id = Guid.NewGuid(), Status = RoomStatus.Waiting };
            _roomRepo.Setup(r => r.GetRoomByIdAsync(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(room);
            _participationRepo.Setup(r => r.CountActivePlayers(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(2);
            _contentRepo.Setup(r => r.GetQuestionsByCategoryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<QuestionEntity> { new() { Id = Guid.NewGuid() } });
            _matchRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DbUpdateException("one active match per room"));

            var result = await _sut.StartMatchAsync(new StartMatchRequest(room.Id, Guid.NewGuid(), 1), CancellationToken.None);

            Assert.Null(result);
        }

        // приём ответа

        [Fact]
        public async Task SubmitAnswer_MatchNotFound_ReturnsFalse()
        {
            _matchRepo.Setup(r => r.GetMatchAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((MatchEntity?)null);

            var ok = await _sut.SubmitAnswerAsync(Guid.NewGuid(), new SubmitAnswerRequest(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

            Assert.False(ok);
        }

        [Fact]
        public async Task SubmitAnswer_QuestionClosed_ReturnsFalse()
        {
            var matchId = Guid.NewGuid();
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(RunningMatch(matchId, status: MatchStatus.QuestionClosed));

            var ok = await _sut.SubmitAnswerAsync(matchId, new SubmitAnswerRequest(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

            Assert.False(ok);
        }

        [Fact]
        public async Task SubmitAnswer_AfterDeadline_ReturnsFalse()
        {
            var matchId = Guid.NewGuid();
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(RunningMatch(matchId));

            _time.Advance(TimeSpan.FromSeconds(31)); // окно ответа истекло

            var ok = await _sut.SubmitAnswerAsync(matchId, new SubmitAnswerRequest(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

            Assert.False(ok);
            _matchRepo.Verify(r => r.AddAnswerAsync(It.IsAny<PlayerAnswerEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task SubmitAnswer_OptionNotBelongToCurrentQuestion_ReturnsFalse()
        {
            var matchId = Guid.NewGuid();
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(RunningMatch(matchId));
            _matchRepo.Setup(r => r.OptionBelongsToQuestionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var ok = await _sut.SubmitAnswerAsync(matchId, new SubmitAnswerRequest(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

            Assert.False(ok);
            _matchRepo.Verify(r => r.AddAnswerAsync(It.IsAny<PlayerAnswerEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task SubmitAnswer_Valid_PersistsAnswer_ReturnsTrue()
        {
            var matchId = Guid.NewGuid();
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(RunningMatch(matchId));
            _matchRepo.Setup(r => r.OptionBelongsToQuestionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var ok = await _sut.SubmitAnswerAsync(matchId, new SubmitAnswerRequest(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

            Assert.True(ok);
            _matchRepo.Verify(r => r.AddAnswerAsync(It.IsAny<PlayerAnswerEntity>(), It.IsAny<CancellationToken>()), Times.Once);
            _matchRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SubmitAnswer_DuplicateAnswer_ReturnsFalse()
        {
            var matchId = Guid.NewGuid();
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(RunningMatch(matchId));
            _matchRepo.Setup(r => r.OptionBelongsToQuestionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            _matchRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DbUpdateException("one answer per question"));

            var ok = await _sut.SubmitAnswerAsync(matchId, new SubmitAnswerRequest(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

            Assert.False(ok);
        }

        // закрытие вопроса

        [Fact]
        public async Task CloseQuestion_NotActive_ReturnsNull()
        {
            var matchId = Guid.NewGuid();
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(RunningMatch(matchId, status: MatchStatus.QuestionClosed));

            var result = await _sut.CloseQuestionAsync(matchId, CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task CloseQuestion_Active_MarksClosed_ExtendsDeadline()
        {
            var matchId = Guid.NewGuid();
            var match = RunningMatch(matchId);
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(match);

            var result = await _sut.CloseQuestionAsync(matchId, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(MatchStatus.QuestionClosed, match.Status);
            Assert.Equal(Now.AddSeconds(30), match.QuestionEndsAt);
            _matchRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        // следующий вопрос и подсчёт очков

        [Fact]
        public async Task NextQuestion_NotClosed_ReturnsNull()
        {
            var matchId = Guid.NewGuid();
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(RunningMatch(matchId)); // не закрыт

            var result = await _sut.NextQuestionAsync(matchId, CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task NextQuestion_ClosedMidMatch_ActivatesNextQuestion()
        {
            var matchId = Guid.NewGuid();
            var match = RunningMatch(matchId, index: 0, questionCount: 2, status: MatchStatus.QuestionClosed);
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(match);

            var result = await _sut.NextQuestionAsync(matchId, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(1, match.CurrentQuestionIndex);
            Assert.Equal(MatchStatus.QuestionActive, match.Status);
            Assert.Equal(Now.AddSeconds(30), match.QuestionEndsAt);
        }

        [Fact]
        public async Task NextQuestion_ClosedOnLastQuestion_FinishesMatch_ScoresActive_ZeroesLeavers_ClosesRoom()
        {
            var matchId = Guid.NewGuid();
            var match = RunningMatch(matchId, index: 0, questionCount: 1, status: MatchStatus.QuestionClosed);
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(match);

            var room = new RoomEntity { Id = match.RoomId, Status = RoomStatus.InProgress };
            _roomRepo.Setup(r => r.GetRoomByIdAsync(match.RoomId, It.IsAny<CancellationToken>())).ReturnsAsync(room);

            var activeId = Guid.NewGuid();
            var leftId = Guid.NewGuid();
            var participants = new List<RoomPlayerEntity>
            {
                new() { PlayerId = activeId, IsActive = true },
                new() { PlayerId = leftId, IsActive = false }
            };
            _participationRepo.Setup(r => r.GetParticipationsByRoomId(match.RoomId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(participants);
            _matchRepo.Setup(r => r.GetCorrectAnswerCountsAsync(matchId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, int> { [activeId] = 2, [leftId] = 3 });

            var result = await _sut.NextQuestionAsync(matchId, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(MatchStatus.Finished, match.Status);
            Assert.Equal(Now, match.FinishedAt);
            Assert.Equal(RoomStatus.Finished, room.Status);
            Assert.Equal(2, participants[0].Score); // активному его очки
            Assert.Equal(0, participants[1].Score); // вышедшему ноль
        }

        // автопилот по таймеру

        [Fact]
        public async Task AdvanceExpiredMatches_StillWithinDeadline_DoesNotAdvance()
        {
            var matchId = Guid.NewGuid();
            var match = RunningMatch(matchId); // дедлайн не истёк
            _matchRepo.Setup(r => r.GetExpiredMatchIdsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Guid> { matchId });
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(match);

            await _sut.AdvanceExpiredMatchesAsync(CancellationToken.None);

            Assert.Equal(MatchStatus.QuestionActive, match.Status);
            Assert.Equal(0, match.CurrentQuestionIndex);
            _matchRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task AdvanceExpiredMatches_PastDeadline_Advances()
        {
            var matchId = Guid.NewGuid();
            var match = RunningMatch(matchId, index: 0, questionCount: 2);
            _matchRepo.Setup(r => r.GetExpiredMatchIdsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Guid> { matchId });
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(match);

            _time.Advance(TimeSpan.FromSeconds(31)); // дедлайн вопроса истёк

            await _sut.AdvanceExpiredMatchesAsync(CancellationToken.None);

            Assert.Equal(1, match.CurrentQuestionIndex);
            Assert.Equal(MatchStatus.QuestionActive, match.Status);
            _matchRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
