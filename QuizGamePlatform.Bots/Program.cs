using QuizGamePlatform.Bots;
using QuizGamePlatform.Bots.Api;

var (options, error) = BotOptions.Parse(args);

if (options is null)
{
    if (error is not null)
    {
        Log.Error("боты", error);
    }

    Console.WriteLine(BotOptions.Help);
    return error is null ? 0 : 1;
}

using var cts = new CancellationTokenSource();
var ct = cts.Token;

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Log.System("Ctrl+C — боты закрывают вкладки");
    cts.Cancel();
};

using var http = new HttpClient { BaseAddress = options.Api, Timeout = TimeSpan.FromSeconds(30) };
var api = new QuizApi(http);
var answers = AnswerKey.Load(options.Seed);
Log.System($"API {options.Api}, верных ответов в сиде: {answers.Count}");

var registration = new Registration(options);

AccountToken Account(string login, string? password = null)
    => new(http, options.Keycloak, options.ClientId, login, password ?? options.Password);

try
{
    // ведущий-бот создаёт комнату; в join комнату создали вы
    BotPlayer? host = null;
    List<string> codes;
    bool isTeamGame;

    if (options.Mode == BotMode.Host)
    {
        var hostAccount = Account(options.HostAccount!, options.HostPassword);
        string[] teamNames = ["Бухгалтерия", "IT", "Продажи", "Маркетинг", "HR"];
        var teams = options.Teams > 0
            ? teamNames.Take(options.Teams).Select(name => new NewTeamRequest(name)).ToList()
            : null;

        var room = await api.CreateRoomAsync(await hostAccount.GetAsync(ct), new CreateRoomRequest(teams is not null, teams), ct);

        // в командную игру входят по ссылке своей команды
        isTeamGame = room.IsTeamGame;
        codes = room.IsTeamGame ? room.Teams!.Select(t => CodeOf(t.JoinLink!)).ToList() : [room.RoomCode];

        Log.System(room.IsTeamGame
            ? $"комната {room.RoomCode}, команды: {string.Join(", ", room.Teams!.Select(t => $"{t.Name} — {t.JoinLink}"))}"
            : $"комната {room.RoomCode}, ссылка: {room.JoinLink}");

        host = new BotPlayer(0, options.HostAccount!, RandomPersona(), Mishap.None, api, answers, options);
        host.AttachAsHost(room.Id, hostAccount);
    }
    else
    {
        (codes, isTeamGame) = options.HostAccount is not null
            ? await LobbyOfHostAsync(options, api, Account(options.HostAccount, options.HostPassword ?? ReadSecret($"пароль {options.HostAccount}: ")), ct)
            : await CheckLinksAsync(options, api, ct);
    }

    // мест: в обычной 10, в командной 30, одно у ведущего
    var seats = (isTeamGame ? 30 : 10) - 1;
    var bots = CreateBots(options, api, answers, firstIndex: host is null ? 0 : 1, seats);

    // заходят не разом, а как люди по ссылке из чата: вразнобой за несколько секунд
    var joined = await Task.WhenAll(bots.Select(async (bot, i) =>
    {
        await Task.Delay(Random.Shared.Next(300, 4000), ct);
        AccountToken? account = null;

        if (bot.Newcomer is { } person)
        {
            bot.Player.Say($"регистрируется: {person}, {person.Email}");

            try
            {
                person = await registration.RegisterAsync(person, ct);
            }
            catch (Exception e) when (e is InvalidOperationException or HttpRequestException)
            {
                Log.Error(bot.Player.Name, e.Message);
                return null;
            }

            bot.Player.Say($"почта подтверждена по ссылке из письма — аккаунт {person.Login} готов");
            account = Account(person.Login);
        }
        else if (bot.Login is { } login)
        {
            account = Account(login);
        }

        return await bot.Player.JoinAsync(codes[i % codes.Count], account, ct) ? bot.Player : null;
    }));

    var players = joined.OfType<BotPlayer>().ToList();

    if (players.Count == 0)
    {
        Log.Error("боты", "никто не вошёл — причина в строках выше");
        return 1;
    }

    List<BotPlayer> everyone = host is null ? players : [host, .. players];

    // с --again итоги печатаем после каждой игры, последней в конце, как без него
    var printed = new HashSet<Guid>();

    void PrintOnce(RoomState room)
    {
        lock (printed)
        {
            if (printed.Add(room.Id))
            {
                PrintResults(room, everyone);
            }
        }
    }

    foreach (var player in everyone)
    {
        player.RoundFinished = PrintOnce;
    }

    // темы выбираем один раз, следующие игры с теми же
    var picked = host is null ? [] : await PickCategoriesAsync(options, api, ct);

    if (host is not null)
    {
        host.PlayAgain = token => PlayAgainAsync(host, players, options, picked, token);
    }

    var runs = everyone.Select(p => p.RunAsync(ct)).ToList();

    if (host is not null)
    {
        // пусть все подключатся к хабу, потом старт
        await Task.Delay(TimeSpan.FromSeconds(3), ct);
        await StartMatchAsync(host, options, picked, ct);
    }
    else
    {
        Log.System($"ботов в комнате: {players.Count} — жмите «Начать игру». Остановить — Ctrl+C");
    }

    await Task.WhenAll(runs);

    if (everyone.Select(p => p.FinalRoom).OfType<RoomState>().MaxBy(r => r.GameNumber) is { } final)
    {
        PrintOnce(final);
    }

    return 0;
}
catch (OperationCanceledException) when (ct.IsCancellationRequested)
{
    return 0;
}
catch (Exception e) when (e is ApiException or InvalidOperationException or HttpRequestException)
{
    Log.Error("боты", e.Message);
    return 1;
}

static Persona RandomPersona() => Persona.All[Random.Shared.Next(Persona.All.Length)];

// http://localhost:5200/join/5X6N4XAE → 5X6N4XAE
static string CodeOf(string link) => link.TrimEnd('/').Split('/')[^1];

// аккаунты, новые сотрудники и гости с забавными никами; у кого-то посреди игры пропадёт связь, кто-то уйдёт
static List<Recruit> CreateBots(BotOptions options, QuizApi api, AnswerKey answers, int firstIndex, int seats)
{
    var nicknames = Persona.Nicknames.OrderBy(_ => Random.Shared.Next()).ToList();
    var people = options.Accounts.Select(login => (Name: login, Login: (string?)login, Newcomer: (NewPerson?)null))
        .Concat(Enumerable.Range(0, options.Newcomers).Select(_ => NewPerson.Random()).Select(p => (Name: p.Login, Login: (string?)null, Newcomer: (NewPerson?)p)))
        .Concat(Enumerable.Range(0, options.GuestCount).Select(i => (Name: nicknames[i % nicknames.Count], Login: (string?)null, Newcomer: (NewPerson?)null)))
        .ToList();

    // лишних не пускает сервер: не мучаем их зря, первыми остаются аккаунты
    if (people.Count > seats)
    {
        Log.System($"мест для ботов {seats}, просили {people.Count} — зайдут первые {seats}");
        people = people.Take(seats).ToList();
    }

    var reconnects = Math.Min(options.Reconnects, people.Count);
    var leavers = Math.Min(options.Leavers, people.Count - reconnects);
    var mishaps = Enumerable.Repeat(Mishap.Reconnect, reconnects)
        .Concat(Enumerable.Repeat(Mishap.Leave, leavers))
        .Concat(Enumerable.Repeat(Mishap.None, people.Count - reconnects - leavers))
        .OrderBy(_ => Random.Shared.Next())
        .ToList();

    return people
        .Select((p, i) => new Recruit(new BotPlayer(firstIndex + i, p.Name, RandomPersona(), mishaps[i], api, answers, options), p.Login, p.Newcomer))
        .ToList();
}

// по ссылкам видно, обычная комната или командная; в командную гостю нужна ссылка команды
static async Task<(List<string> Codes, bool IsTeamGame)> CheckLinksAsync(BotOptions options, QuizApi api, CancellationToken ct)
{
    var invites = new List<Invite>();

    foreach (var code in options.Codes)
    {
        var invite = await api.GetInviteAsync(code, ct);

        if (invite.Status != RoomStatus.Waiting)
        {
            throw new InvalidOperationException($"{code}: игра уже {(invite.Status == RoomStatus.Finished ? "закончилась" : "идёт")} — новые игроки не войдут");
        }

        if (invite.IsTeamGame && invite.TeamNumber is null)
        {
            throw new InvalidOperationException(
                $"{code} — ссылка командной комнаты, а входят по ссылкам команд: передайте ссылки всех команд (кнопки у команд в лобби) или --host-account <ваш логин>");
        }

        invites.Add(invite);
    }

    var isTeamGame = invites.Any(i => i.IsTeamGame);
    var host = invites[0].HostName is { } name ? $", ведущий {name}" : "";

    Log.System(isTeamGame
        ? $"командная комната{host}, команды: {string.Join(", ", invites.Select(i => i.TeamName).Distinct())}"
        : $"обычная комната{host}");

    return (options.Codes, isTeamGame);
}

// лобби ведущего: по ссылке, если дали, иначе последнее открытое; ссылки команд видит только он
static async Task<(List<string> Codes, bool IsTeamGame)> LobbyOfHostAsync(BotOptions options, QuizApi api, AccountToken host, CancellationToken ct)
{
    var rooms = await api.GetMyRoomsAsync(await host.GetAsync(ct), ct);
    var code = options.Codes.FirstOrDefault()?.ToUpperInvariant();

    var room = code is null
        ? rooms.Where(r => r.Status == RoomStatus.Waiting).MaxBy(r => r.CreatedAt)
        : rooms.FirstOrDefault(r => r.RoomCode == code || r.Teams?.Any(t => t.JoinLink is { } link && CodeOf(link) == code) == true);

    if (room is null)
    {
        throw new InvalidOperationException(code is null
            ? $"у {host.Login} нет открытого лобби — создайте комнату на фронте"
            : $"у {host.Login} нет комнаты {code}");
    }

    if (room.Status != RoomStatus.Waiting)
    {
        throw new InvalidOperationException($"в комнате {room.RoomCode} игра уже началась — новые игроки не войдут");
    }

    if (room.IsTeamGame)
    {
        Log.System($"командная комната {room.RoomCode}, команды: {string.Join(", ", room.Teams!.Select(t => t.Name))}");
        return (room.Teams!.Select(t => CodeOf(t.JoinLink!)).ToList(), true);
    }

    Log.System($"обычная комната {room.RoomCode}");
    return ([room.RoomCode], false);
}

// пароль не показываем на экране: на показе его видят все
static string ReadSecret(string prompt)
{
    Console.Write(prompt);

    if (Console.IsInputRedirected)
    {
        return Console.ReadLine() ?? "";
    }

    var secret = new System.Text.StringBuilder();

    for (var key = Console.ReadKey(intercept: true); key.Key != ConsoleKey.Enter; key = Console.ReadKey(intercept: true))
    {
        if (key.Key == ConsoleKey.Backspace)
        {
            secret.Length = Math.Max(0, secret.Length - 1);
        }
        else if (!char.IsControl(key.KeyChar))
        {
            secret.Append(key.KeyChar);
        }
    }

    Console.WriteLine();
    return secret.ToString();
}

static async Task<List<Category>> PickCategoriesAsync(BotOptions options, QuizApi api, CancellationToken ct)
{
    var categories = await api.GetCategoriesAsync(ct);

    return options.Categories is { } count
        ? categories.OrderBy(_ => Random.Shared.Next()).Take(count).ToList()
        : categories;
}

// «Сыграть ещё»: бот-ведущий собирает следующую игру, ждёт, пока боты перейдут, и начинает с теми же настройками
static async Task<bool> PlayAgainAsync(BotPlayer host, List<BotPlayer> players, BotOptions options, List<Category> picked, CancellationToken ct)
{
    if (players.All(p => p.Done))
    {
        Log.System("играть больше не с кем — ведущий заканчивает");
        return false;
    }

    var next = await host.RematchAsync(ct);
    var deadline = DateTime.UtcNow.AddSeconds(60);

    while (true)
    {
        var room = await host.GetRoomAsync(ct);
        var arrived = room.Players.Count(p => p.IsActive && !p.IsHost);
        var expected = players.Count(p => !p.Done);

        if (arrived >= expected)
        {
            Log.System($"игра {room.GameNumber} (комната {next.RoomCode}): перешли все — {arrived}");
            break;
        }

        if (DateTime.UtcNow >= deadline)
        {
            Log.System($"игра {room.GameNumber}: пришли {arrived} из {expected} — начинаю");
            break;
        }

        await Task.Delay(TimeSpan.FromSeconds(1), ct);
    }

    // пусть перешедшие подпишутся на хаб, потом старт
    await Task.Delay(TimeSpan.FromSeconds(1), ct);
    await StartMatchAsync(host, options, picked, ct);
    return true;
}

static async Task StartMatchAsync(BotPlayer host, BotOptions options, List<Category> picked, CancellationToken ct)
{
    var mode = options.Fast ? RoomMode.Fastest : RoomMode.Simple;
    await host.StartMatchAsync(new StartMatchRequest(Guid.Empty, picked.Select(c => c.Id).ToList(), options.Questions, mode), ct);

    Log.System($"игра началась: вопросов {options.Questions}, {(options.Fast ? "на скорость" : "обычный темп")}, темы: {string.Join(", ", picked.Select(c => c.Name))}");
}

static void PrintResults(RoomState room, List<BotPlayer> bots)
{
    Log.System(room.GameNumber > 1 ? $"—— итоги игры {room.GameNumber} ——" : "—— итоги ——");

    if (room.IsTeamGame && room.Teams is { } teams)
    {
        foreach (var team in teams.OrderBy(t => t.Place ?? int.MaxValue))
        {
            Log.System($"{team.Place}. команда «{team.Name}» — {Points(team.Points ?? 0)}, игроков {team.Members}");
        }
    }

    foreach (var player in room.Players.OrderBy(p => p.Place ?? int.MaxValue).ThenByDescending(p => p.Score))
    {
        var place = player.Place is { } p ? $"{p}. " : "";
        // в командной игре личных очков нет, показываем команду
        var team = room.Teams?.FirstOrDefault(t => t.Number == player.TeamNumber) is { } t ? $" — «{t.Name}»" : "";
        var score = player.Score is { } s ? $" — {Points(s)}" : team;
        var exit = player.IsActive ? "" : player.ExitReason switch
        {
            ExitReason.Normal => " (вышел сам)",
            ExitReason.Disconnected => " (пропал со связи)",
            ExitReason.Kicked => " (убран ведущим)",
            _ => "",
        };

        Log.System($"{place}{player.PlayerName}{score}{exit}");
    }

    foreach (var bot in bots)
    {
        Log.System($"{bot.Name}: {bot.Stats}");
    }
}

// 1 очко, 2 очка, 5 очков, 21 очко
static string Points(int n) => (n % 100, n % 10) switch
{
    (>= 11 and <= 14, _) => $"{n} очков",
    (_, 1) => $"{n} очко",
    (_, >= 2 and <= 4) => $"{n} очка",
    _ => $"{n} очков",
};

// бот и как он входит: гостем, под своим логином или сначала регистрируется
internal sealed record Recruit(BotPlayer Player, string? Login, NewPerson? Newcomer);
