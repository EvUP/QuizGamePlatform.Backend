using QuizGamePlatform.Web.Api.Contracts;
using QuizGamePlatform.Web.Infrastructure;

namespace QuizGamePlatform.Web.Features.History;

/// <summary>Серия «Сыграть ещё»: игры по номеру, строка показывает последнюю</summary>
public sealed record RoomSeries(IReadOnlyList<MyRoomResponse> Games)
{
    public MyRoomResponse Last => Games[^1];

    // прошлые игры свежие сверху, как и весь список
    public IEnumerable<MyRoomResponse> Past => Games.Take(Games.Count - 1).Reverse();
}

/// <summary>Что показывает колонка результата</summary>
public enum RoomOutcome
{
    Win,
    Place,
    Left,
    Finished,
    Lobby,
    Stale,
    Live
}

/// <summary>Фильтр страницы «Вся история»</summary>
public enum HistoryKind
{
    All,
    Simple,
    Team
}

// Note: «вы вышли» третьей строкой, когда место и очки есть, а я уже не в игре
public sealed record RoomResultCell(RoomOutcome Outcome, string Main, string? Sub = null, string? Note = null);

/// <summary>Тексты и порядок строк истории: общие для «Моих комнат» на главной и страницы «Вся история»</summary>
public static class RoomHistory
{
    // лобби, которое так и не начали, через полсуток уже не «ждёт»
    private static readonly TimeSpan StaleLobby = TimeSpan.FromHours(12);

    /// <summary>Серии по SeriesId ?? Id: сначала идущие игры, затем лобби, остальное свежие сверху</summary>
    public static List<RoomSeries> Build(IEnumerable<MyRoomResponse> rooms)
        => rooms
            .Where(r => !IsHidden(r))
            .GroupBy(r => r.SeriesId ?? r.Id)
            .Select(g => new RoomSeries(g.OrderBy(r => r.GameNumber).ToList()))
            .OrderBy(s => SortOrder(s.Last))
            .ThenByDescending(s => s.Last.CreatedAt)
            .ToList();

    /// <summary>Идёт сейчас или ждёт в лобби: наверху списка</summary>
    public static bool IsCurrent(MyRoomResponse room)
        => room.Status == RoomStatus.InProgress || room.Status == RoomStatus.Waiting && !IsStale(room);

    // из лобби меня убрали или я сам вышел; лобби ведущего и обрыв связи не прячем, туда можно вернуться
    private static bool IsHidden(MyRoomResponse room)
        => room.Status == RoomStatus.Waiting && !room.IsHost && !room.IsActive
           && room.MyExitReason is ExitReason.Normal or ExitReason.Kicked;

    private static int SortOrder(MyRoomResponse room) => room.Status switch
    {
        RoomStatus.InProgress => 0,
        RoomStatus.Waiting when !IsStale(room) => 1,
        _ => 2
    };

    private static bool IsStale(MyRoomResponse room)
        => room.Status == RoomStatus.Waiting && DateTime.UtcNow - Utc(room.CreatedAt) > StaleLobby;

    /// <summary>Формат игры, не содержимое: «Обычная игра», «Командная игра · 3 команды»</summary>
    public static string Title(MyRoomResponse room)
        => room.IsTeamGame ? $"Командная игра{TeamsSuffix(room)}" : "Обычная игра";

    /// <summary>« · 3 команды»: сколько команд, если сервер их отдал</summary>
    public static string TeamsSuffix(MyRoomResponse room)
        => room.Teams is { Count: > 0 } teams ? $" · {RussianPlural.Format(teams.Count, "команда", "команды", "команд")}" : "";

    /// <summary>Вторая часть строки 2: за какую команду играл; у обычной игры ничего</summary>
    public static string? Role(MyRoomResponse room)
    {
        if (!room.IsTeamGame)
        {
            return null;
        }

        var team = room.Teams?.FirstOrDefault(t => t.Number == room.MyTeamNumber);
        return team is null ? null : $"за {Quoted(team.Name)}";
    }

    /// <summary>Метка в строке 1 «ведущий: Иван», только в чужой игре: свои игры без метки, иначе она в каждой строке</summary>
    public static string? HostTag(MyRoomResponse room)
        => room.IsHost || string.IsNullOrEmpty(room.HostName) ? null : $"ведущий: {room.HostName}";

    public static RoomResultCell Result(MyRoomResponse room)
    {
        if (room.Status == RoomStatus.Waiting)
        {
            return IsStale(room) ? new(RoomOutcome.Stale, "Не начата") : new(RoomOutcome.Lobby, "Лобби");
        }

        if (room.Status == RoomStatus.InProgress)
        {
            return new(RoomOutcome.Live, "Идёт");
        }

        // итог сервер считает только для последних 20 завершённых
        if (room.Result is not { } result)
        {
            return new(RoomOutcome.Finished, "Завершена");
        }

        if (result.MyPlace is not { } place)
        {
            return new(RoomOutcome.Left, "—", "вы вышли");
        }

        var points = Points(room, result.MyPoints);
        var left = room.IsActive ? null : "вы вышли";

        // при ничьей сервер всё равно рассаживает двух первых по местам 1 и 2, но победы нет.
        // времени в истории нет, поэтому делящими первенство считаем оба первых места
        if (result.IsDraw && place <= 2)
        {
            return new(RoomOutcome.Place, "Ничья", points, left);
        }

        if (place == 1)
        {
            return new(RoomOutcome.Win, "Победа", points, left);
        }

        // после старта сервер отдаёт только сыгравшие команды, поэтому «из N» совпадает с итогами
        return room.IsTeamGame && room.Teams is { Count: > 0 } teams
            ? new(RoomOutcome.Place, $"{place}-е из {teams.Count}", points, left)
            : new(RoomOutcome.Place, $"{place}-е место", points, left);
    }

    // в обычной верные ответы, в командной очки команды
    private static string? Points(MyRoomResponse room, int? points) => points switch
    {
        null => null,
        { } p when room.IsTeamGame => RussianPlural.Format(p, "очко", "очка", "очков"),
        { } p => RussianPlural.Format(p, "верный", "верных", "верных")
    };

    /// <summary>Итог одной строчкой для прошлых игр серии</summary>
    public static string ShortResult(MyRoomResponse room)
    {
        var cell = Result(room);
        return cell.Outcome == RoomOutcome.Left ? "вы вышли" : cell.Note is null ? cell.Main : $"{cell.Main}, {cell.Note}";
    }

    /// <summary>Победитель только в подсказке и для скринридера, в строку не выводим</summary>
    public static string? Winner(MyRoomResponse room)
        => room.Result is { WinnerName: { } winner, MyPlace: not 1 } ? $"Победитель: {winner}" : null;

    public static string ActionText(MyRoomResponse room) => room.Status switch
    {
        RoomStatus.Waiting => "В лобби",
        RoomStatus.InProgress => CanRejoin(room) ? "Вернуться" : "Открыть",
        _ => "Итоги"
    };

    // оборвалась связь: сервер ждёт 40 с, страница комнаты сама вернёт в игру
    private static bool CanRejoin(MyRoomResponse room) => room.IsActive || room.MyExitReason == ExitReason.Disconnected;

    /// <summary>«Итоги игры 3 · Командная игра · 3 команды, сегодня, 21:34. 2-е из 3, 50 очков. Победитель: Бета»</summary>
    public static string AriaLabel(MyRoomResponse room)
    {
        var number = room.GameNumber > 1 ? $" {room.GameNumber}" : "";
        var action = room.Status switch
        {
            RoomStatus.Waiting => $"Лобби игры{number}",
            RoomStatus.InProgress => CanRejoin(room) ? $"Вернуться в игру{number}" : $"Открыть игру{number}",
            _ => $"Итоги игры{number}"
        };
        var role = (room.IsHost ? ", вы ведущий" : "") + (HostTag(room) is { } h ? $", {h}" : "") + (Role(room) is { } r ? $", {r}" : "");
        var cell = Result(room);
        var result = cell.Outcome == RoomOutcome.Left ? "Вы вышли" : string.Join(", ", new[] { cell.Main, cell.Sub, cell.Note }.Where(t => t is not null));
        var winner = Winner(room) is { } w ? $". {w}" : "";

        return $"{action} · {Title(room)}, {RussianDates.Short(room.CreatedAt)}{role}. {result}{winner}";
    }

    /// <summary>Только время «21:34», когда день уже понятен из заголовка группы</summary>
    public static string Time(DateTime utc) => $"{Local(utc):HH:mm}";

    /// <summary>Заголовок дня: «Сегодня», «Вчера», «14 октября»</summary>
    public static string DayTitle(DateTime localDate)
    {
        var today = DateTime.Now.Date;

        if (localDate == today)
        {
            return "Сегодня";
        }

        if (localDate == today.AddDays(-1))
        {
            return "Вчера";
        }

        // для прошлых дней RussianDates.Short отдаёт дату без времени
        return RussianDates.Short(DateTime.SpecifyKind(localDate.AddHours(12), DateTimeKind.Local).ToUniversalTime());
    }

    public static DateTime Local(DateTime utc) => Utc(utc).ToLocalTime();

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    // в названии могут быть свои «», тогда не обрамляем
    private static string Quoted(string name) => name.Contains('«') ? name : $"«{name}»";
}
