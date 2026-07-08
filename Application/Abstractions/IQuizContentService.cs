using QuizGamePlatform.Backend.Application.Contracts;

namespace QuizGamePlatform.Backend.Application.Abstractions
{
    public interface IQuizContentService
    {
        Task<List<CategoryDto>> GetCategoriesAsync(CancellationToken ct);

        Task<List<QuestionAdminDto>?> GetQuestionsByCategoryAsync(Guid categoryId, CancellationToken ct);
    }
}
