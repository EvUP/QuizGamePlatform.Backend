using testBdControllers.DataAccess.Entities;

namespace testBdControllers.Core.Abstractions
{
    public interface IQuizContentRepository
    {
        Task<List<CategoryEntity>> GetCategoriesAsync(CancellationToken ct);

        Task<List<QuestionEntity>?> GetQuestionsByCategoryAsync(Guid categoryId, CancellationToken ct);
    }
}
