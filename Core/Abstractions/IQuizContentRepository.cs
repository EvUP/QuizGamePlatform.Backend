using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.Core.Abstractions
{
    public interface IQuizContentRepository
    {
        Task<List<CategoryEntity>> GetCategoriesAsync(CancellationToken ct);

        Task<List<QuestionEntity>?> GetQuestionsByCategoryAsync(Guid categoryId, CancellationToken ct);

        Task<List<QuestionEntity>> GetQuestionsByCategoriesAsync(IReadOnlyCollection<Guid> categoryIds, CancellationToken ct);
    }
}
