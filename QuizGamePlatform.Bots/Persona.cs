namespace QuizGamePlatform.Bots;

// характер игрока: как часто прав, сколько думает и как часто зевает вопрос
public sealed record Persona(string Kind, double Accuracy, double MinThinkSeconds, double MaxThinkSeconds, double MissChance)
{
    // на чтение: длинный вопрос читают дольше
    private const double ReadSecondsPerChar = 0.03;

    public static readonly Persona[] All =
    [
        new("эрудит", 0.9, 3, 8, 0.02),
        new("середнячок", 0.65, 5, 12, 0.05),
        new("тугодум", 0.55, 9, 20, 0.08),
        new("торопыга", 0.35, 1, 3, 0),
        new("везунчик", 0.3, 2, 10, 0.04),
    ];

    public static readonly string[] Nicknames =
    [
        "Большой Шлёпа", "Крипер", "Плотва", "Котик Ctrl+Z", "Кекс из бухгалтерии",
        "Тот самый Петрович", "Сеньор Помидор", "Ждун", "Капитан Очевидность", "Дед Инсайд",
        "Кот Шрёдингера", "Пельмень", "Баг в проде", "Сова на дедлайне", "Чайный гриб",
        "Енот-полоскун", "Тапок", "Сырник", "Бублик", "Мистер Кофе",
        "Тихий ниндзя", "Гуру Excel", "Король созвонов", "Печенька", "Лось",
        "Сэр Шредер", "Принтер Олег", "Джун на испытательном", "Морж", "Квокка",
    ];

    public TimeSpan ThinkTime(string questionText, double speed)
    {
        var seconds = MinThinkSeconds + Random.Shared.NextDouble() * (MaxThinkSeconds - MinThinkSeconds);
        seconds += questionText.Length * ReadSecondsPerChar * (0.5 + Random.Shared.NextDouble() / 2);

        return TimeSpan.FromSeconds(seconds / speed);
    }

    public override string ToString() => $"{Kind}, верно ~{Accuracy:P0}, думает {MinThinkSeconds:0}–{MaxThinkSeconds:0} с";
}
