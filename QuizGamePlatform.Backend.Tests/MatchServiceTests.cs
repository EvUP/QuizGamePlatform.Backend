using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using QuizGamePlatform.Backend.Api.Controllers;
using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.Contracts.Enums;
using QuizGamePlatform.Backend.Application.Contracts.Match;
using QuizGamePlatform.Backend.Application.Enums;
using QuizGamePlatform.Backend.Application.Services;
using QuizGamePlatform.Backend.Core.Abstractions;
using QuizGamePlatform.Backend.Core.Helpers;
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
        private readonly Mock<ICurrentUserContext> _currentUser = new();
        private readonly Mock<IGameNotifier> _notifier = new();
        private readonly FakeTimeProvider _time = new();
        private readonly MatchService _sut;

        public MatchServiceTests()
        {
            _locks.Setup(l => l.GetLock(It.IsAny<Guid>())).Returns(new SemaphoreSlim(1, 1));
            _participationRepo.Setup(r => r.GetParticipationsByRoomId(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

            _sut = new MatchService(
                _matchRepo.Object,
                _roomRepo.Object,
                _participationRepo.Object,
                _contentRepo.Object,
                _locks.Object,
                _currentUser.Object,
                _notifier.Object,
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

        // гость с токеном на участие в комнате матча
        private RoomPlayerEntity ParticipantOf(MatchEntity match, bool isActive = true)
        {
            var participant = new RoomPlayerEntity { Id = Guid.NewGuid(), RoomId = match.RoomId, IsActive = isActive };
            _currentUser.Setup(u => u.RoomPlayerLinkId).Returns(participant.Id);
            _participationRepo.Setup(r => r.FindParticipant(match.RoomId, participant.Id, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(participant);

            return participant;
        }

        // владелец комнаты

        [Fact]
        public async Task IsRoomHost_ChecksOwnerByTokenSubject()
        {
            var roomId = Guid.NewGuid();
            _currentUser.Setup(u => u.KeycloakSubject).Returns("host-subject");
            _roomRepo.Setup(r => r.IsHost(roomId, "host-subject", It.IsAny<CancellationToken>())).ReturnsAsync(true);

            Assert.True(await _sut.IsRoomHostAsync(roomId, CancellationToken.None));
        }

        [Fact]
        public async Task IsMatchHost_WithoutAccount_ReturnsFalse()
        {
            var ok = await _sut.IsMatchHostAsync(Guid.NewGuid(), CancellationToken.None);

            Assert.False(ok);
            _matchRepo.Verify(r => r.IsHost(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Controller_NotHost_ForbidsMatchControl()
        {
            var service = new Mock<IMatchService>(); // не владелец
            var controller = new MatchController(service.Object)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };
            var matchId = Guid.NewGuid();

            var results = new[]
            {
                await controller.StartMatch(new StartMatchRequest(Guid.NewGuid(), [Guid.NewGuid()], 1, RoomMode.Simple), CancellationToken.None),
                await controller.CloseQuestion(matchId, CancellationToken.None),
                await controller.NextQuestion(matchId, CancellationToken.None),
            };

            Assert.All(results, r => Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(r).StatusCode));
            service.Verify(s => s.StartMatchAsync(It.IsAny<StartMatchRequest>(), It.IsAny<CancellationToken>()), Times.Never);
            service.Verify(s => s.CloseQuestionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
            service.Verify(s => s.NextQuestionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData(0, 1)]  // вопросов меньше одного
        [InlineData(31, 1)] // вопросов больше максимума
        [InlineData(5, 0)]  // нет тем
        public async Task Controller_InvalidStartRequest_BadRequest(int questionCount, int themeCount)
        {
            var service = new Mock<IMatchService>(MockBehavior.Strict);
            var controller = new MatchController(service.Object)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };
            var themes = Enumerable.Range(0, themeCount).Select(_ => Guid.NewGuid()).ToList();

            var result = await controller.StartMatch(
                new StartMatchRequest(Guid.NewGuid(), themes, questionCount, RoomMode.Simple), CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        // старт матча

        [Fact]
        public async Task StartMatch_RoomNotWaiting_ReturnsNull()
        {
            var roomId = Guid.NewGuid();
            _roomRepo.Setup(r => r.GetRoomByIdAsync(roomId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RoomEntity { Id = roomId, Status = RoomStatus.InProgress });

            var (result, _) = await _sut.StartMatchAsync(new StartMatchRequest(roomId, [Guid.NewGuid()], 5, RoomMode.Simple), CancellationToken.None);

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

            var (result, _) = await _sut.StartMatchAsync(new StartMatchRequest(roomId, [Guid.NewGuid()], 5, RoomMode.Simple), CancellationToken.None);

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
            _contentRepo.Setup(r => r.GetQuestionsByCategoriesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<QuestionEntity>());

            var (result, _) = await _sut.StartMatchAsync(new StartMatchRequest(roomId, [Guid.NewGuid()], 5, RoomMode.Simple), CancellationToken.None);

            Assert.Null(result);
        }

        [Theory]
        [InlineData(RoomMode.Simple, 30)]
        [InlineData(RoomMode.Fastest, 15)]
        public async Task StartMatch_Valid_CreatesActiveMatch_SetsRoomInProgress_DeadlineByMode(RoomMode mode, int seconds)
        {
            var room = new RoomEntity { Id = Guid.NewGuid(), Status = RoomStatus.Waiting };
            _roomRepo.Setup(r => r.GetRoomByIdAsync(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(room);
            _participationRepo.Setup(r => r.CountActivePlayers(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(2);
            _contentRepo.Setup(r => r.GetQuestionsByCategoriesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<QuestionEntity> { new() { Id = Guid.NewGuid() }, new() { Id = Guid.NewGuid() } });

            MatchEntity? created = null;
            _matchRepo.Setup(r => r.AddMatchAsync(It.IsAny<MatchEntity>(), It.IsAny<CancellationToken>()))
                .Callback<MatchEntity, CancellationToken>((m, _) => created = m);

            var (result, _) = await _sut.StartMatchAsync(new StartMatchRequest(room.Id, [Guid.NewGuid()], 2, mode), CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(MatchStatus.QuestionActive, result!.Status);
            Assert.Equal(RoomStatus.InProgress, room.Status);
            Assert.NotNull(created);
            Assert.Equal(mode, created!.RoomMode);
            Assert.Equal(Now.AddSeconds(seconds), created.QuestionEndsAt);
            _matchRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
            _notifier.Verify(n => n.MatchUpdated(It.Is<MatchStateResponse>(s =>
                s.RoomId == room.Id && s.CurrentQuestionIndex == 0 && s.QuestionEndsAt == Now.AddSeconds(seconds))), Times.Once);
        }

        // от открытия вопроса считается время ответа: первый открыт со стартом, остальные ещё нет
        [Fact]
        public async Task StartMatch_FirstQuestionOpenedAtStart()
        {
            var (match, _) = await StartWithThemes(3, 3);

            Assert.Equal(Now, match.StartedAt);
            Assert.Equal(new DateTime?[] { Now, null, null }, match.Questions.OrderBy(q => q.Order).Select(q => q.OpenedAt));
        }

        // темы с заданным числом вопросов, возвращает созданный матч
        private async Task<(MatchEntity Match, List<QuestionEntity> Questions)> StartWithThemes(int count, params int[] perTheme)
        {
            var room = new RoomEntity { Id = Guid.NewGuid(), Status = RoomStatus.Waiting };
            _roomRepo.Setup(r => r.GetRoomByIdAsync(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(room);
            _participationRepo.Setup(r => r.CountActivePlayers(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(2);
            var themes = perTheme.Select(_ => Guid.NewGuid()).ToList();
            var questions = themes
                .SelectMany((theme, i) => Enumerable.Range(0, perTheme[i]).Select(_ => new QuestionEntity { Id = Guid.NewGuid(), CategoryId = theme }))
                .ToList();
            _contentRepo.Setup(r => r.GetQuestionsByCategoriesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(questions);
            MatchEntity? created = null;
            _matchRepo.Setup(r => r.AddMatchAsync(It.IsAny<MatchEntity>(), It.IsAny<CancellationToken>()))
                .Callback<MatchEntity, CancellationToken>((m, _) => created = m);

            await _sut.StartMatchAsync(new StartMatchRequest(room.Id, themes, count, RoomMode.Simple), CancellationToken.None);

            return (created!, questions);
        }

        [Fact]
        public async Task StartMatch_FiveThemes_FifteenRandomQuestionsWithoutRepeats()
        {
            var (match, questions) = await StartWithThemes(15, 12, 12, 12, 12, 12);

            Assert.Equal(15, match.Questions.Select(q => q.QuestionId).Distinct().Count());
            Assert.All(match.Questions, mq => Assert.Contains(questions, q => q.Id == mq.QuestionId));
            Assert.Equal(Enumerable.Range(0, 15), match.Questions.Select(q => q.Order));
        }

        [Fact]
        public async Task StartMatch_MoreRequestedThanAvailable_TakesAll()
        {
            var (match, _) = await StartWithThemes(30, 2, 12);

            Assert.Equal(14, match.Questions.Select(q => q.QuestionId).Distinct().Count());
        }

        [Fact]
        public async Task StartMatch_SecondActiveMatch_ReturnsNull()
        {
            var room = new RoomEntity { Id = Guid.NewGuid(), Status = RoomStatus.Waiting };
            _roomRepo.Setup(r => r.GetRoomByIdAsync(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(room);
            _participationRepo.Setup(r => r.CountActivePlayers(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(2);
            _contentRepo.Setup(r => r.GetQuestionsByCategoriesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<QuestionEntity> { new() { Id = Guid.NewGuid() } });
            _matchRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DbUpdateException("one active match per room"));

            var (result, refusal) = await _sut.StartMatchAsync(new StartMatchRequest(room.Id, [Guid.NewGuid()], 1, RoomMode.Simple), CancellationToken.None);

            Assert.Null(result);
            Assert.Equal(StartRefusal.Concurrent, refusal); // контроллер ответит 409 «повторите»
            _notifier.Verify(n => n.MatchUpdated(It.IsAny<MatchStateResponse>()), Times.Never);
        }

        // командная комната с игроками по командам, null у вышедших до старта
        private (RoomEntity Room, List<RoomPlayerEntity> Players) TeamRoomReady(params (int? Team, bool IsActive)[] players)
        {
            var room = new RoomEntity { Id = Guid.NewGuid(), Status = RoomStatus.Waiting, IsTeamGame = true };
            var participants = players
                .Select(p => new RoomPlayerEntity { Id = Guid.NewGuid(), RoomId = room.Id, TeamNumber = p.Team, IsActive = p.IsActive })
                .ToList();
            _roomRepo.Setup(r => r.GetRoomByIdAsync(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(room);
            _participationRepo.Setup(r => r.CountActivePlayers(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(participants.Count(p => p.IsActive));
            _participationRepo.Setup(r => r.GetParticipationsByRoomId(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(participants);
            _contentRepo.Setup(r => r.GetQuestionsByCategoriesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<QuestionEntity> { new() { Id = Guid.NewGuid() } });

            return (room, participants);
        }

        [Fact]
        public async Task StartMatch_TeamRoom_OneNonEmptyTeam_ReturnsNull()
        {
            var (room, _) = TeamRoomReady((1, true), (1, true), (2, false));

            var (result, _) = await _sut.StartMatchAsync(new StartMatchRequest(room.Id, [Guid.NewGuid()], 1, RoomMode.Simple), CancellationToken.None);

            Assert.Null(result);
            Assert.Equal(RoomStatus.Waiting, room.Status);
            _matchRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task StartMatch_TeamRoom_TwoTeams_StartsAndLocksRoster()
        {
            var (room, players) = TeamRoomReady((1, true), (2, true), (2, false));

            var (result, _) = await _sut.StartMatchAsync(new StartMatchRequest(room.Id, [Guid.NewGuid()], 1, RoomMode.Simple), CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(RoomStatus.InProgress, room.Status);
            Assert.Equal(new int?[] { 1, 2, null }, players.Select(p => p.TeamNumber)); // ушедший из лобби не в составе
        }

        // обрыв перед «Старт» не исключает: команда остаётся, вышедший сам и убранный её теряют
        [Fact]
        public async Task StartMatch_TeamRoom_DisconnectedKeepsTeam_LeftAndKickedLose()
        {
            var (room, players) = TeamRoomReady((1, true), (2, true), (1, false), (2, false), (2, false));
            players[2].ExitReason = ExitReason.Disconnected;
            players[3].ExitReason = ExitReason.Normal;
            players[4].ExitReason = ExitReason.Kicked;

            var (result, _) = await _sut.StartMatchAsync(new StartMatchRequest(room.Id, [Guid.NewGuid()], 1, RoomMode.Simple), CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(new int?[] { 1, 2, 1, null, null }, players.Select(p => p.TeamNumber));
        }

        // непустые команды для старта считаются по активным, оборвавший связь команду не делает непустой
        [Fact]
        public async Task StartMatch_TeamRoom_OnlyDisconnectedInSecondTeam_ReturnsNull()
        {
            var (room, players) = TeamRoomReady((1, true), (1, true), (2, false));
            players[2].ExitReason = ExitReason.Disconnected;

            var (result, refusal) = await _sut.StartMatchAsync(new StartMatchRequest(room.Id, [Guid.NewGuid()], 1, RoomMode.Simple), CancellationToken.None);

            Assert.Null(result);
            Assert.Equal(StartRefusal.TooFewTeams, refusal);
        }

        // текущий вопрос

        [Fact]
        public async Task CurrentQuestion_NotRoomParticipant_ReturnsNull()
        {
            var matchId = Guid.NewGuid();
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(RunningMatch(matchId));

            Assert.Null(await _sut.GetCurrentQuestionAsync(matchId, CancellationToken.None));
            _matchRepo.Verify(r => r.GetMatchQuestionAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData(MatchStatus.QuestionActive, false)]
        [InlineData(MatchStatus.QuestionClosed, true)]
        public async Task CurrentQuestion_CorrectOptionOnlyAfterClose(MatchStatus status, bool shown)
        {
            var matchId = Guid.NewGuid();
            var match = RunningMatch(matchId, status: status);
            ParticipantOf(match);
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(match);
            var correct = new AnswerOptionEntity { Id = Guid.NewGuid(), Text = "Волга", Position = 1, IsCorrect = true };
            var wrong = new AnswerOptionEntity { Id = Guid.NewGuid(), Text = "Дунай", Position = 2 };
            var question = match.Questions.First();
            question.Question = new QuestionEntity { Id = question.QuestionId, Text = "Самая длинная река Европы?", AnswerOptions = [wrong, correct] };
            _matchRepo.Setup(r => r.GetMatchQuestionAsync(matchId, 0, It.IsAny<CancellationToken>())).ReturnsAsync(question);

            var result = await _sut.GetCurrentQuestionAsync(matchId, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(shown ? correct.Id : null, result.CorrectOptionId);
        }

        // после перезагрузки страницы игрок видит свой ответ и кто уже ответил
        [Fact]
        public async Task CurrentQuestion_RestoresMyAnswerAndWhoAnswered()
        {
            var matchId = Guid.NewGuid();
            var match = RunningMatch(matchId);
            match.RoomMode = RoomMode.Fastest;
            var me = ParticipantOf(match);
            var other = Guid.NewGuid();
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(match);
            var volga = new AnswerOptionEntity { Id = Guid.NewGuid(), Text = "Волга", Position = 1, IsCorrect = true };
            var danube = new AnswerOptionEntity { Id = Guid.NewGuid(), Text = "Дунай", Position = 2 };
            var question = match.Questions.First();
            question.Question = new QuestionEntity { Id = question.QuestionId, Text = "Самая длинная река Европы?", AnswerOptions = [volga, danube] };
            question.Answers =
            [
                new PlayerAnswerEntity { Id = Guid.NewGuid(), RoomPlayerId = other, SelectedOptionId = volga.Id },
                new PlayerAnswerEntity { Id = Guid.NewGuid(), RoomPlayerId = me.Id, SelectedOptionId = danube.Id },
            ];
            _matchRepo.Setup(r => r.GetMatchQuestionAsync(matchId, 0, It.IsAny<CancellationToken>())).ReturnsAsync(question);

            var result = await _sut.GetCurrentQuestionAsync(matchId, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(danube.Id, result.MyAnswerOptionId);
            Assert.Equal(new[] { other, me.Id }, result.AnsweredLinkIds);
            Assert.Equal(15, result.StageSeconds); // блиц
            Assert.Equal(Now, result.ServerTimeUtc);
        }

        // разбор после финала

        private MatchController ReviewController() => new(_sut)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        [Fact]
        public async Task Review_NotRoomParticipant_NotFound()
        {
            var matchId = Guid.NewGuid();
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(RunningMatch(matchId, status: MatchStatus.Finished));

            var (review, notFinished) = await _sut.GetReviewAsync(matchId, CancellationToken.None);

            Assert.Null(review);
            Assert.False(notFinished);
            Assert.IsType<NotFoundObjectResult>(await ReviewController().GetReview(matchId, CancellationToken.None));
            _matchRepo.Verify(r => r.GetQuestionsForReviewAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Review_MatchNotFound_NotFound()
        {
            Assert.IsType<NotFoundObjectResult>(await ReviewController().GetReview(Guid.NewGuid(), CancellationToken.None));
        }

        // до финала разбор подсказал бы верные ответы
        [Theory]
        [InlineData(MatchStatus.QuestionActive)]
        [InlineData(MatchStatus.QuestionClosed)]
        public async Task Review_BeforeFinish_Conflict(MatchStatus status)
        {
            var matchId = Guid.NewGuid();
            var match = RunningMatch(matchId, status: status);
            ParticipantOf(match);
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(match);

            var (review, notFinished) = await _sut.GetReviewAsync(matchId, CancellationToken.None);

            Assert.Null(review);
            Assert.True(notFinished);
            Assert.IsType<ConflictObjectResult>(await ReviewController().GetReview(matchId, CancellationToken.None));
            _matchRepo.Verify(r => r.GetQuestionsForReviewAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        // вышедший до финала тоже видит разбор: свой ответ, верный вариант и сколько ответили верно
        [Fact]
        public async Task Review_Finished_MyAnswerCorrectOptionAndCounts()
        {
            var matchId = Guid.NewGuid();
            var match = RunningMatch(matchId, questionCount: 2, status: MatchStatus.Finished);
            var me = ParticipantOf(match, isActive: false);
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(match);

            var volga = new AnswerOptionEntity { Id = Guid.NewGuid(), Text = "Волга", Position = 1, IsCorrect = true };
            var danube = new AnswerOptionEntity { Id = Guid.NewGuid(), Text = "Дунай", Position = 2 };
            var first = match.Questions.First(q => q.Order == 0);
            first.Question = new QuestionEntity { Id = first.QuestionId, Text = "Самая длинная река Европы?", AnswerOptions = [danube, volga] };
            first.Answers =
            [
                new PlayerAnswerEntity { RoomPlayerId = me.Id, SelectedOptionId = danube.Id },
                new PlayerAnswerEntity { RoomPlayerId = Guid.NewGuid(), SelectedOptionId = volga.Id },
                new PlayerAnswerEntity { RoomPlayerId = Guid.NewGuid(), SelectedOptionId = volga.Id },
            ];

            var two = new AnswerOptionEntity { Id = Guid.NewGuid(), Text = "2", Position = 1 };
            var four = new AnswerOptionEntity { Id = Guid.NewGuid(), Text = "4", Position = 2, IsCorrect = true };
            var second = match.Questions.First(q => q.Order == 1);
            second.Question = new QuestionEntity { Id = second.QuestionId, Text = "2 + 2?", AnswerOptions = [two, four] };
            second.Answers = [new PlayerAnswerEntity { RoomPlayerId = Guid.NewGuid(), SelectedOptionId = two.Id }];

            _matchRepo.Setup(r => r.GetQuestionsForReviewAsync(matchId, It.IsAny<CancellationToken>()))
                .ReturnsAsync([first, second]);

            var (review, notFinished) = await _sut.GetReviewAsync(matchId, CancellationToken.None);

            Assert.False(notFinished);
            Assert.NotNull(review);
            Assert.Equal(matchId, review.MatchId);
            Assert.Equal(
                new[] { (0, volga.Id, (Guid?)danube.Id, 3, 2), (1, four.Id, (Guid?)null, 1, 0) },
                review.Questions.Select(q => (q.Order, q.CorrectOptionId!.Value, q.MyAnswerOptionId, q.AnsweredCount, q.CorrectCount)));
            // варианты по позиции, как в игре
            Assert.Equal(new[] { "Волга", "Дунай" }, review.Questions[0].Options.Select(o => o.Text));
        }

        // приём ответа

        [Fact]
        public async Task SubmitAnswer_MatchNotFound_ReturnsFalse()
        {
            _matchRepo.Setup(r => r.GetMatchAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((MatchEntity?)null);

            var ok = await _sut.SubmitAnswerAsync(Guid.NewGuid(), new SubmitAnswerRequest(Guid.NewGuid()), CancellationToken.None);

            Assert.False(ok);
        }

        [Fact]
        public async Task SubmitAnswer_QuestionClosed_ReturnsFalse()
        {
            var matchId = Guid.NewGuid();
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(RunningMatch(matchId, status: MatchStatus.QuestionClosed));

            var ok = await _sut.SubmitAnswerAsync(matchId, new SubmitAnswerRequest(Guid.NewGuid()), CancellationToken.None);

            Assert.False(ok);
        }

        [Fact]
        public async Task SubmitAnswer_AfterDeadline_ReturnsFalse()
        {
            var matchId = Guid.NewGuid();
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(RunningMatch(matchId));

            _time.Advance(TimeSpan.FromSeconds(31)); // окно ответа истекло

            var ok = await _sut.SubmitAnswerAsync(matchId, new SubmitAnswerRequest(Guid.NewGuid()), CancellationToken.None);

            Assert.False(ok);
            _matchRepo.Verify(r => r.AddAnswerAsync(It.IsAny<PlayerAnswerEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task SubmitAnswer_OptionNotBelongToCurrentQuestion_ReturnsFalse()
        {
            var matchId = Guid.NewGuid();
            var match = RunningMatch(matchId);
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(match);
            ParticipantOf(match);
            _matchRepo.Setup(r => r.OptionBelongsToQuestionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var ok = await _sut.SubmitAnswerAsync(matchId, new SubmitAnswerRequest(Guid.NewGuid()), CancellationToken.None);

            Assert.False(ok);
            _matchRepo.Verify(r => r.AddAnswerAsync(It.IsAny<PlayerAnswerEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task SubmitAnswer_Valid_PersistsAnswerForTokenParticipant_ReturnsTrue()
        {
            var matchId = Guid.NewGuid();
            var match = RunningMatch(matchId);
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(match);
            var participant = ParticipantOf(match);
            _matchRepo.Setup(r => r.OptionBelongsToQuestionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var ok = await _sut.SubmitAnswerAsync(matchId, new SubmitAnswerRequest(Guid.NewGuid()), CancellationToken.None);

            Assert.True(ok);
            _matchRepo.Verify(r => r.AddAnswerAsync(
                It.Is<PlayerAnswerEntity>(a => a.RoomPlayerId == participant.Id), It.IsAny<CancellationToken>()), Times.Once);
            _matchRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
            _notifier.Verify(n => n.PlayerAnswered(It.Is<PlayerAnsweredEvent>(e =>
                e.MatchId == matchId && e.QuestionIndex == 0 && e.RoomPlayerLinkId == participant.Id)), Times.Once);
        }

        [Fact]
        public async Task SubmitAnswer_NotRoomParticipant_ReturnsFalse()
        {
            var matchId = Guid.NewGuid();
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(RunningMatch(matchId));
            _matchRepo.Setup(r => r.OptionBelongsToQuestionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var ok = await _sut.SubmitAnswerAsync(matchId, new SubmitAnswerRequest(Guid.NewGuid()), CancellationToken.None);

            Assert.False(ok);
            _matchRepo.Verify(r => r.AddAnswerAsync(It.IsAny<PlayerAnswerEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task SubmitAnswer_LeftParticipant_ReturnsFalse()
        {
            var matchId = Guid.NewGuid();
            var match = RunningMatch(matchId);
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(match);
            ParticipantOf(match, isActive: false);
            _matchRepo.Setup(r => r.OptionBelongsToQuestionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var ok = await _sut.SubmitAnswerAsync(matchId, new SubmitAnswerRequest(Guid.NewGuid()), CancellationToken.None);

            Assert.False(ok);
            _matchRepo.Verify(r => r.AddAnswerAsync(It.IsAny<PlayerAnswerEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task SubmitAnswer_DuplicateAnswer_ReturnsFalse()
        {
            var matchId = Guid.NewGuid();
            var match = RunningMatch(matchId);
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(match);
            ParticipantOf(match);
            _matchRepo.Setup(r => r.OptionBelongsToQuestionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            _matchRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DbUpdateException("one answer per question"));

            var ok = await _sut.SubmitAnswerAsync(matchId, new SubmitAnswerRequest(Guid.NewGuid()), CancellationToken.None);

            Assert.False(ok);
            _notifier.Verify(n => n.PlayerAnswered(It.IsAny<PlayerAnsweredEvent>()), Times.Never);
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
            _notifier.Verify(n => n.MatchUpdated(It.IsAny<MatchStateResponse>()), Times.Never);
        }

        [Fact]
        public async Task CloseQuestion_MarksClosed_RevealPause()
        {
            var matchId = Guid.NewGuid();
            var match = RunningMatch(matchId);
            match.Questions.First().OpenedAt = Now.AddSeconds(-10);
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(match);
            var active = new RoomPlayerEntity { Id = Guid.NewGuid(), IsActive = true };
            var leftBeforeOpen = new RoomPlayerEntity { Id = Guid.NewGuid(), IsActive = false, FinishedAt = Now.AddSeconds(-11) };
            var leftAfterOpen = new RoomPlayerEntity { Id = Guid.NewGuid(), IsActive = false, FinishedAt = Now.AddSeconds(-1) };
            _participationRepo.Setup(r => r.GetParticipationsByRoomId(match.RoomId, It.IsAny<CancellationToken>()))
                .ReturnsAsync([active, leftBeforeOpen, leftAfterOpen]);

            var result = await _sut.CloseQuestionAsync(matchId, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(MatchStatus.QuestionClosed, match.Status);
            Assert.Equal(Now, match.Questions.First().ClosedAt);
            // вышедший на вопросе в игре: выйти и вернуться не выгоднее, чем ошибиться; вышедший до открытия нет
            Assert.Equal([active.Id, leftAfterOpen.Id], match.Questions.First().InGamePlayerIds);
            Assert.Equal(Now.AddSeconds(MatchHelper.RevealSeconds), match.QuestionEndsAt); // пауза на показ ответа
            _matchRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
            _notifier.Verify(n => n.MatchUpdated(It.Is<MatchStateResponse>(m => m.Status == MatchStatus.QuestionClosed)), Times.Once);
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
            _notifier.Verify(n => n.MatchUpdated(It.IsAny<MatchStateResponse>()), Times.Never);
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
            _notifier.Verify(n => n.MatchUpdated(It.Is<MatchStateResponse>(m =>
                m.CurrentQuestionIndex == 1 && m.Status == MatchStatus.QuestionActive)), Times.Once);
        }

        [Fact]
        public async Task NextQuestion_OpensNextQuestionAtTransition()
        {
            var matchId = Guid.NewGuid();
            var match = RunningMatch(matchId, index: 0, questionCount: 2, status: MatchStatus.QuestionClosed);
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(match);
            _time.Advance(TimeSpan.FromSeconds(7));

            await _sut.NextQuestionAsync(matchId, CancellationToken.None);

            Assert.Equal(Now, match.Questions.Single(q => q.Order == 1).OpenedAt);
            Assert.Null(match.Questions.Single(q => q.Order == 0).OpenedAt); // прошлый не трогаем
        }

        [Fact]
        public async Task NextQuestion_ClosedOnLastQuestion_FinishesMatch_ScoresActive_ZeroesExpiredLeavers_ClosesRoom()
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
                new() { Id = activeId, IsActive = true },
                new() { Id = leftId, IsActive = false, FinishedAt = Now.AddSeconds(-RoomHelper.ReconnectWindowSeconds - 1) }
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
            Assert.Equal(0, participants[1].Score); // окно возврата истекло ещё до финиша
            _notifier.Verify(n => n.MatchUpdated(It.Is<MatchStateResponse>(m => m.Status == MatchStatus.Finished)), Times.Once);
        }

        // обрыв на последнем вопросе не обнуляет игру: очки остаются, пока к финишу не истекло окно возврата
        [Theory]
        [InlineData(5, 3)]
        [InlineData(40, 3)]
        [InlineData(41, 0)]
        public async Task NextQuestion_Finish_LeaverWithinReturnWindowKeepsScore(int leftAgo, int expected)
        {
            var matchId = Guid.NewGuid();
            var match = RunningMatch(matchId, index: 0, questionCount: 1, status: MatchStatus.QuestionClosed);
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(match);
            _roomRepo.Setup(r => r.GetRoomByIdAsync(match.RoomId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RoomEntity { Id = match.RoomId, Status = RoomStatus.InProgress });
            var left = new RoomPlayerEntity { Id = Guid.NewGuid(), IsActive = false, ExitReason = ExitReason.Disconnected, FinishedAt = Now.AddSeconds(-leftAgo) };
            _participationRepo.Setup(r => r.GetParticipationsByRoomId(match.RoomId, It.IsAny<CancellationToken>())).ReturnsAsync([left]);
            _matchRepo.Setup(r => r.GetCorrectAnswerCountsAsync(matchId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, int> { [left.Id] = 3 });

            await _sut.NextQuestionAsync(matchId, CancellationToken.None);

            Assert.Equal(MatchStatus.Finished, match.Status);
            Assert.Equal(expected, left.Score);
        }

        // автопилот по таймеру

        [Fact]
        public async Task AdvanceExpiredMatch_StillWithinDeadline_DoesNotAdvance()
        {
            var matchId = Guid.NewGuid();
            var match = RunningMatch(matchId); // дедлайн не истёк
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(match);

            await _sut.AdvanceExpiredMatchAsync(matchId, CancellationToken.None);

            Assert.Equal(MatchStatus.QuestionActive, match.Status);
            Assert.Equal(0, match.CurrentQuestionIndex);
            _matchRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
            _notifier.Verify(n => n.MatchUpdated(It.IsAny<MatchStateResponse>()), Times.Never);
        }

        [Fact]
        public async Task AdvanceExpiredMatch_PastDeadline_ShowsAnswer()
        {
            var matchId = Guid.NewGuid();
            var match = RunningMatch(matchId, index: 0, questionCount: 2);
            var deadline = match.QuestionEndsAt;
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(match);
            var leftAfterDeadline = new RoomPlayerEntity { Id = Guid.NewGuid(), IsActive = false, FinishedAt = deadline.AddMilliseconds(500) };
            _participationRepo.Setup(r => r.GetParticipationsByRoomId(match.RoomId, It.IsAny<CancellationToken>()))
                .ReturnsAsync([leftAfterDeadline]);

            _time.Advance(TimeSpan.FromSeconds(31)); // дедлайн вопроса истёк

            await _sut.AdvanceExpiredMatchAsync(matchId, CancellationToken.None);

            Assert.Equal(0, match.CurrentQuestionIndex); // сначала показ ответа
            Assert.Equal(MatchStatus.QuestionClosed, match.Status);
            Assert.Equal(deadline, match.Questions.First().ClosedAt); // закрыт по таймеру в дедлайн
            Assert.Equal([leftAfterDeadline.Id], match.Questions.First().InGamePlayerIds); // ушёл уже после дедлайна
            Assert.Equal(Now.AddSeconds(MatchHelper.RevealSeconds), match.QuestionEndsAt);
            _matchRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
            // игроки узнают о закрытии от сервера, без опроса
            _notifier.Verify(n => n.MatchUpdated(It.Is<MatchStateResponse>(s =>
                s.CurrentQuestionIndex == 0 && s.Status == MatchStatus.QuestionClosed)), Times.Once);
        }

        [Fact]
        public async Task AdvanceExpiredMatch_RevealOver_ActivatesNextQuestion()
        {
            var matchId = Guid.NewGuid();
            var match = RunningMatch(matchId, index: 0, questionCount: 2, status: MatchStatus.QuestionClosed);
            match.Questions.First().ClosedAt = Now;
            match.QuestionEndsAt = Now.AddSeconds(MatchHelper.RevealSeconds);
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(match);

            _time.Advance(TimeSpan.FromSeconds(MatchHelper.RevealSeconds + 1)); // показ ответа закончился

            await _sut.AdvanceExpiredMatchAsync(matchId, CancellationToken.None);

            Assert.Equal(1, match.CurrentQuestionIndex);
            Assert.Equal(MatchStatus.QuestionActive, match.Status);
            Assert.Null(match.Questions.Last().ClosedAt);
            _notifier.Verify(n => n.MatchUpdated(It.Is<MatchStateResponse>(s =>
                s.CurrentQuestionIndex == 1 && s.Status == MatchStatus.QuestionActive)), Times.Once);
        }

        [Fact]
        public async Task AdvanceExpiredMatch_ChangedByAnotherInstance_Skipped()
        {
            var matchId = Guid.NewGuid();
            var match = RunningMatch(matchId, index: 0, questionCount: 2);
            _matchRepo.Setup(r => r.GetMatchAsync(matchId, It.IsAny<CancellationToken>())).ReturnsAsync(match);
            _matchRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DbUpdateConcurrencyException());

            _time.Advance(TimeSpan.FromSeconds(31));

            Assert.False(await _sut.AdvanceExpiredMatchAsync(matchId, CancellationToken.None));
            // событие разошлёт инстанс, который сдвинул матч
            _notifier.Verify(n => n.MatchUpdated(It.IsAny<MatchStateResponse>()), Times.Never);
        }

        // «Сыграть ещё»: настройки старта подставим в следующую игру серии
        [Fact]
        public async Task StartMatch_SavesCategoriesAndRequestedCount()
        {
            var (match, _) = await StartWithThemes(5, 2, 2);

            Assert.Equal(2, match.CategoryIds.Count);
            Assert.Equal(5, match.RequestedQuestionCount);
            Assert.Equal(4, match.Questions.Count);
        }

        // в игре N>1 вопросы прошлых игр серии идут последними
        [Fact]
        public async Task StartMatch_NextGameInSeries_SkipsAskedQuestions()
        {
            var seriesId = Guid.NewGuid();
            var room = new RoomEntity { Id = Guid.NewGuid(), Status = RoomStatus.Waiting, GameNumber = 2, SeriesId = seriesId };
            _roomRepo.Setup(r => r.GetRoomByIdAsync(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(room);
            _participationRepo.Setup(r => r.CountActivePlayers(room.Id, It.IsAny<CancellationToken>())).ReturnsAsync(2);
            var questions = Enumerable.Range(0, 5).Select(_ => new QuestionEntity { Id = Guid.NewGuid() }).ToList();
            _contentRepo.Setup(r => r.GetQuestionsByCategoriesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(questions);
            _matchRepo.Setup(r => r.GetQuestionIdsOfSeriesAsync(seriesId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(questions.Take(3).Select(q => q.Id).ToHashSet());
            MatchEntity? created = null;
            _matchRepo.Setup(r => r.AddMatchAsync(It.IsAny<MatchEntity>(), It.IsAny<CancellationToken>()))
                .Callback<MatchEntity, CancellationToken>((m, _) => created = m);

            await _sut.StartMatchAsync(new StartMatchRequest(room.Id, [Guid.NewGuid()], 2, RoomMode.Simple), CancellationToken.None);

            Assert.Equal(
                questions.Skip(3).Select(q => q.Id).OrderBy(id => id),
                created!.Questions.Select(q => q.QuestionId).OrderBy(id => id));
        }
    }
}
