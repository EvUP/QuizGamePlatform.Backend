using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using testBdControllers.DataAccess.Entities;

namespace testBdControllers.DataAccess.Seed
{
    /// <summary>
    /// Разовое наполнение банка вопросов из quiz_seed.json
    /// </summary>
    public static class DbSeeder
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static async Task SeedAsync(ApplicationDbContext db, string contentRootPath, CancellationToken ct = default)
        {
            if (await db.Categories.AnyAsync(ct))
                return;

            var path = Path.Combine(contentRootPath, "Seed", "quiz_seed.json");
            if (!File.Exists(path))
                return;

            await using var stream = File.OpenRead(path);
            var seed = await JsonSerializer.DeserializeAsync<SeedRoot>(stream, JsonOptions, ct);

            if (seed?.Categories is null || seed.Categories.Count == 0)
                return;

            foreach (var seedCategory in seed.Categories)
            {
                var category = new CategoryEntity
                {
                    Id = Guid.NewGuid(),
                    Name = seedCategory.Name
                };

                foreach (var seedQuestion in seedCategory.Questions)
                {
                    var question = new QuestionEntity
                    {
                        Id = Guid.NewGuid(),
                        CategoryId = category.Id,
                        Text = seedQuestion.Text,
                        Source = seedQuestion.Source
                    };

                    var position = 1;
                    foreach (var seedOption in seedQuestion.Options)
                    {
                        question.AnswerOptions.Add(new AnswerOptionEntity
                        {
                            Id = Guid.NewGuid(),
                            QuestionId = question.Id,
                            Text = seedOption.Text,
                            IsCorrect = seedOption.IsCorrect,
                            Position = position++
                        });
                    }

                    category.Questions.Add(question);
                }

                db.Categories.Add(category);
            }

            await db.SaveChangesAsync(ct);
        }

        private sealed class SeedRoot
        {
            public List<SeedCategory> Categories { get; set; } = new();
        }

        private sealed class SeedCategory
        {
            public string Name { get; set; } = string.Empty;
            public List<SeedQuestion> Questions { get; set; } = new();
        }

        private sealed class SeedQuestion
        {
            public string Text { get; set; } = string.Empty;
            public string? Source { get; set; }
            public List<SeedOption> Options { get; set; } = new();
        }

        private sealed class SeedOption
        {
            public string Text { get; set; } = string.Empty;
            public bool IsCorrect { get; set; }
        }
    }
}
