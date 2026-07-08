using Microsoft.EntityFrameworkCore;
using QuizGamePlatform.Backend.Core.Abstractions;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.DataAccess.Repositories
{
    public class QuizContentRepository(ApplicationDbContext context) : IQuizContentRepository
    {
        public async Task<List<CategoryEntity>> GetCategoriesAsync(CancellationToken ct)
        {
            return await context.Categories
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .ToListAsync(ct);
        }

        public async Task<List<QuestionEntity>?> GetQuestionsByCategoryAsync(Guid categoryId, CancellationToken ct)
        {
            var categoryExists = await context.Categories
                .AnyAsync(c => c.Id == categoryId, ct);

            if (!categoryExists)
                return null;

            return await context.Questions
                .AsNoTracking()
                .Where(q => q.CategoryId == categoryId)
                .Include(q => q.AnswerOptions.OrderBy(o => o.Position))
                .OrderBy(q => q.Text)
                .ToListAsync(ct);
        }
    }
}
