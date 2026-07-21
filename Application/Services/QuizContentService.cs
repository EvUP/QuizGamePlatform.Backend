using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.Contracts;
using QuizGamePlatform.Backend.Core.Abstractions;
using QuizGamePlatform.Backend.DataAccess.Caching;

namespace QuizGamePlatform.Backend.Application.Services
{
    public class QuizContentService(IQuizContentRepository repository, IDistributedCache cache) : IQuizContentService
    {
        public async Task<List<CategoryDto>> GetCategoriesAsync(CancellationToken ct)
        {
            var json = await cache.GetStringAsync(RedisKeysFactory.CategoriesListKey, ct);

            if (!string.IsNullOrEmpty(json))
            {
                return JsonSerializer.Deserialize<List<CategoryDto>>(json)
                ?? Enumerable.Empty<CategoryDto>().ToList();
            }

            var categories = await repository.GetCategoriesAsync(ct);

            var dtos = categories
            .Select(c => new CategoryDto { Id = c.Id, Name = c.Name })
            .ToList();

            await cache.SetStringAsync(
            RedisKeysFactory.CategoriesListKey,
            JsonSerializer.Serialize(dtos),
            CachePolicy.Lists,
            ct
            );

            return dtos;
        }

        public async Task<List<QuestionAdminDto>?> GetQuestionsByCategoryAsync(Guid categoryId, CancellationToken ct)
        {
            var cacheKey = RedisKeysFactory.QuestionsByCategory(categoryId);
            var json = await cache.GetStringAsync(cacheKey, ct);

            if (!string.IsNullOrEmpty(json))
            {
                var cached = JsonSerializer.Deserialize<List<QuestionAdminDto>>(json);

                return cached;
            }

            var questions = await repository.GetQuestionsByCategoryAsync(categoryId, ct);

            if (questions is null)
            {
                return null;
            }

            var dtos = questions
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

            await cache.SetStringAsync(
                cacheKey,
                JsonSerializer.Serialize(dtos),
                CachePolicy.Questions,
                ct
            );

            return dtos;
        }
    }
}
