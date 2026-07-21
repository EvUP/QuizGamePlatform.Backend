using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.Contracts;
using QuizGamePlatform.Backend.Core.Abstractions;

namespace QuizGamePlatform.Backend.Application.Services
{
    public class QuizContentService(IQuizContentRepository repository) : IQuizContentService
    {
        public async Task<List<CategoryDto>> GetCategoriesAsync(CancellationToken ct)
        {
            var categories = await repository.GetCategoriesAsync(ct);

            return categories
                .Select(c => new CategoryDto { Id = c.Id, Name = c.Name })
                .ToList();
        }

        public async Task<List<QuestionAdminDto>?> GetQuestionsByCategoryAsync(Guid categoryId, CancellationToken ct)
        {
            var questions = await repository.GetQuestionsByCategoryAsync(categoryId, ct);

            if (questions is null)
                return null;

            return questions
                .Select(q => new QuestionAdminDto
                {
                    Id = q.Id,
                    Text = q.Text,
                    Source = q.Source,
                    Options = q.AnswerOptions
                        .OrderBy(o => o.Position)
                        .Select(o => new AnswerOptionAdminDto
                        {
                            Id = o.Id,
                            Text = o.Text,
                            Position = o.Position
                        })
                        .ToList()
                })
                .ToList();
        }
    }
}
