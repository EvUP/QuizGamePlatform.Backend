using System.Text.Json;

namespace QuizGamePlatform.Bots;

// верные ответы из сида: API их до закрытия вопроса не отдаёт, а бот должен ошибаться не наугад, а с заданной точностью
public sealed class AnswerKey
{
    private readonly Dictionary<string, string> _answers;

    private AnswerKey(Dictionary<string, string> answers) => _answers = answers;

    public int Count => _answers.Count;

    public string? Find(string questionText) => _answers.GetValueOrDefault(questionText);

    public static AnswerKey Load(string? path)
    {
        path ??= FindSeed();

        if (path is null || !File.Exists(path))
        {
            Log.System("сид с ответами не найден — боты отвечают наугад (укажите --seed путь/к/quiz_seed.json)");
            return new AnswerKey([]);
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var answers = new Dictionary<string, string>();

        foreach (var category in doc.RootElement.GetProperty("categories").EnumerateArray())
        {
            foreach (var question in category.GetProperty("questions").EnumerateArray())
            {
                answers[question.GetProperty("text").GetString()!] = question.GetProperty("answer").GetString()!;
            }
        }

        return new AnswerKey(answers);
    }

    // Seed/quiz_seed.json в корне репозитория: ищем вверх от текущей папки и от папки сборки
    private static string? FindSeed()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "Seed", "quiz_seed.json");

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
