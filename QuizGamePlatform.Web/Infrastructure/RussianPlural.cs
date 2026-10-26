namespace QuizGamePlatform.Web.Infrastructure;

/// <summary>Склонение по числу: 1 игра, 2 игры, 5 игр, 11 игр, 21 игра</summary>
public static class RussianPlural
{
    public static string Format(int count, string one, string few, string many)
        => $"{count} {Choose(count, one, few, many)}";

    public static string Choose(int count, string one, string few, string many)
    {
        var lastTwo = Math.Abs(count) % 100;
        var last = lastTwo % 10;

        if (lastTwo is >= 11 and <= 14)
        {
            return many;
        }

        return last switch
        {
            1 => one,
            >= 2 and <= 4 => few,
            _ => many
        };
    }
}
