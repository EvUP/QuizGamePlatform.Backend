using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.DataAccess.Seed
{
    /// <summary>
    /// Досев банка вопросов из quiz_seed.json: добавляет недостающие темы и вопросы, существующие не трогает.
    /// Тему, которой больше нет в сиде, удаляет, а если по её вопросам уже играли — прячет.
    /// Возвращает темы, которые изменились, — их кэш надо сбросить
    /// </summary>
    public static class DbSeeder
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static async Task<IReadOnlyCollection<Guid>> SeedAsync(ApplicationDbContext db, string contentRootPath, CancellationToken ct = default)
        {
            var path = Path.Combine(contentRootPath, "Seed", "quiz_seed.json");

            if (!File.Exists(path))
                return [];

            await using var stream = File.OpenRead(path);
            var seed = await JsonSerializer.DeserializeAsync<SeedRoot>(stream, JsonOptions, ct);

            if (seed?.Categories is null || seed.Categories.Count == 0)
                return [];

            // тема узнаётся по названию, вопрос — по тексту внутри темы
            var categories = await db.Categories
                .Include(c => c.Questions)
                .ToDictionaryAsync(c => c.Name, ct);

            var changed = new HashSet<Guid>();

            foreach (var seedCategory in seed.Categories)
            {
                if (!categories.TryGetValue(seedCategory.Name, out var category))
                {
                    category = new CategoryEntity
                    {
                        Id = Guid.NewGuid(),
                        Name = seedCategory.Name
                    };
                    categories[category.Name] = category;
                    db.Categories.Add(category);
                }
                else if (category.IsHidden)
                {
                    category.IsHidden = false;
                    changed.Add(category.Id);
                }

                var existingTexts = category.Questions.Select(q => q.Text).ToHashSet();

                foreach (var seedQuestion in seedCategory.Questions)
                {
                    if (!existingTexts.Add(seedQuestion.Text))
                        continue;

                    var question = new QuestionEntity
                    {
                        Id = Guid.NewGuid(),
                        CategoryId = category.Id,
                        Text = seedQuestion.Text
                    };

                    var options = seedQuestion.Wrong
                        .Select(text => (Text: text, IsCorrect: false))
                        .Append((Text: seedQuestion.Answer, IsCorrect: true));

                    var position = 1;
                    foreach (var option in options.OrderBy(_ => Random.Shared.Next()))
                    {
                        question.AnswerOptions.Add(new AnswerOptionEntity
                        {
                            Id = Guid.NewGuid(),
                            QuestionId = question.Id,
                            Text = option.Text,
                            IsCorrect = option.IsCorrect,
                            Position = position++
                        });
                    }

                    // явно через Add: Guid задан заранее, и через навигацию EF принял бы вопрос за существующий
                    db.Questions.Add(question);
                    changed.Add(category.Id);
                }
            }

            var seedNames = seed.Categories.Select(c => c.Name).ToHashSet();

            foreach (var category in categories.Values.Where(c => !seedNames.Contains(c.Name) && !c.IsHidden))
            {
                var played = await db.MatchQuestions.AnyAsync(mq => mq.Question.CategoryId == category.Id, ct);

                if (played)
                    category.IsHidden = true;
                else
                    db.Categories.Remove(category);

                changed.Add(category.Id);
            }

            if (changed.Count > 0)
                await db.SaveChangesAsync(ct);

            return changed;
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
            public string Answer { get; set; } = string.Empty;
            public List<string> Wrong { get; set; } = new();
        }
    }
}
