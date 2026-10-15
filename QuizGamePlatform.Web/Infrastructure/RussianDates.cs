namespace QuizGamePlatform.Web.Infrastructure;

/// <summary>
/// Короткие даты по-русски в часовом поясе браузера: «сегодня, 14:20», «вчера, 09:05», «13 октября».
/// Месяцы вручную — не зависим от того, какие данные культуры загрузил WebAssembly.
/// </summary>
public static class RussianDates
{
    private static readonly string[] MonthsGenitive =
    [
        "января", "февраля", "марта", "апреля", "мая", "июня",
        "июля", "августа", "сентября", "октября", "ноября", "декабря"
    ];

    public static string Short(DateTime utc)
    {
        var local = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
        var today = DateTime.Now.Date;

        if (local.Date == today)
        {
            return $"сегодня, {local:HH:mm}";
        }

        if (local.Date == today.AddDays(-1))
        {
            return $"вчера, {local:HH:mm}";
        }

        var date = $"{local.Day} {MonthsGenitive[local.Month - 1]}";
        return local.Year == today.Year ? date : $"{date} {local.Year}";
    }
}
