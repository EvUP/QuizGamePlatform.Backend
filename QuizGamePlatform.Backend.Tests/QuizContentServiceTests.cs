using Moq;
using QuizGamePlatform.Backend.Application.Services;
using QuizGamePlatform.Backend.Core.Abstractions;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.Tests
{
    public class QuizContentServiceTests
    {
        private readonly Mock<IQuizContentRepository> _repository = new();
        private readonly QuizContentService _sut;

        public QuizContentServiceTests()
        {
            _sut = new QuizContentService(_repository.Object);
        }

        [Fact]
        public async Task GetCategoriesAsync_MapsEntitiesToDtos()
        {
            var id = Guid.NewGuid();
            _repository
                .Setup(r => r.GetCategoriesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<CategoryEntity>
                {
                    new() { Id = id, Name = "История" }
                });

            var result = await _sut.GetCategoriesAsync(CancellationToken.None);

            var dto = Assert.Single(result);
            Assert.Equal(id, dto.Id);
            Assert.Equal("История", dto.Name);
        }

        [Fact]
        public async Task GetQuestionsByCategoryAsync_WhenCategoryMissing_ReturnsNull()
        {
            _repository
                .Setup(r => r.GetQuestionsByCategoryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((List<QuestionEntity>?)null);

            var result = await _sut.GetQuestionsByCategoryAsync(Guid.NewGuid(), CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task GetQuestionsByCategoryAsync_MapsQuestion_AndOrdersOptionsByPosition()
        {
            var questionId = Guid.NewGuid();
            var question = new QuestionEntity
            {
                Id = questionId,
                Text = "Столица Франции?",
                Source = "CURATED_2026",
                AnswerOptions = new List<AnswerOptionEntity>
                {
                    new() { Id = Guid.NewGuid(), Text = "Париж",  Position = 2, IsCorrect = true },
                    new() { Id = Guid.NewGuid(), Text = "Берлин", Position = 1, IsCorrect = false }
                }
            };

            _repository
                .Setup(r => r.GetQuestionsByCategoryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<QuestionEntity> { question });

            var result = await _sut.GetQuestionsByCategoryAsync(Guid.NewGuid(), CancellationToken.None);

            Assert.NotNull(result);
            var dto = Assert.Single(result!);
            Assert.Equal(questionId, dto.Id);
            Assert.Equal("Столица Франции?", dto.Text);
            Assert.Equal("CURATED_2026", dto.Source);

            // Варианты должны идти по Position (1, 2), а не в порядке из репозитория.
            Assert.Collection(dto.Options,
                o => Assert.Equal("Берлин", o.Text),
                o => Assert.Equal("Париж", o.Text));
        }
    }
}
