namespace QuizGamePlatform.Bots;

public enum BotMode { Join, Host }

public sealed class BotOptions
{
    public const string Help = """
        Боты-игроки для QuizGamePlatform: входят по ссылке, держат связь через SignalR и отвечают как люди.

        Войти в вашу комнату (вы ведущий и жмёте «Начать игру»):
          dotnet run --project QuizGamePlatform.Bots -- join <ссылка или код> [ещё ссылки команд...] [параметры]
          dotnet run --project QuizGamePlatform.Bots -- join --host-account <ваш логин> [параметры]
        Обычная или командная комната — боты поймут сами. В командную входят по ссылкам команд:
        передайте ссылки всех команд или --host-account — тогда боты возьмут ваше открытое лобби и ссылки команд сами
        (пароль спросят, если не задан --host-password).

        Провести игру целиком (комнату создаёт и игру начинает бот-аккаунт):
          dotnet run --project QuizGamePlatform.Bots -- host --host-account <логин> [параметры]

        Игроки (в обычной комнате до 10 вместе с ведущим, в командной до 30):
          --bots N              сколько гостей с забавными никами (join — 4, host — 5)
          --register N          сколько новых сотрудников зарегистрируются сами: форма Keycloak,
                                письмо в Mailpit, ссылка из письма — и входят под новым логином
          --accounts a,b        уже заведённые аккаунты Keycloak (входят под логином, очки идут в рейтинг)
          --password P          пароль аккаунтов и новых регистраций (по умолчанию test)
          --reconnect N         у N игроков посреди игры пропадёт связь на 8–25 с, потом вернутся
          --leave N             N игроков выйдут посреди игры сами
          --speed X             во сколько раз быстрее думают (по умолчанию 1)
          --again N             после итогов сыграть ещё N раз тем же составом (host: бот-ведущий жмёт «Сыграть ещё»
                                и ждёт ботов до 60 с; join: боты ждут, когда позовёте, до 10 мин)

        Игра (только host):
          --host-account L      аккаунт ведущего (пароль — --password)
          --team N              командная игра на N команд (2–5), игроки делятся по командам поровну
          --questions N         вопросов (по умолчанию 10)
          --fast                режим «на скорость»: 15 с на вопрос вместо 30
          --categories N        N случайных тем (по умолчанию все)

        Подключение:
          --api URL             адрес API (по умолчанию http://localhost:53507)
          --keycloak URL        realm Keycloak (по умолчанию http://localhost:8080/realms/quizgame)
          --client ID           клиент Keycloak (по умолчанию quiz-backend)
          --mailpit URL         Mailpit для писем подтверждения (по умолчанию http://localhost:8025)
          --host-password P     пароль --host-account в join (иначе спросят)
          --seed PATH           Seed/quiz_seed.json — откуда боты знают верные ответы (ищется сам)
        """;

    public BotMode Mode { get; private set; }
    public List<string> Codes { get; } = [];
    public int? Guests { get; private set; }
    public int Newcomers { get; private set; }
    public List<string> Accounts { get; } = [];
    public string Password { get; private set; } = "test";
    public int Reconnects { get; private set; }
    public int Leavers { get; private set; }
    public double Speed { get; private set; } = 1;
    public int Again { get; private set; }
    public string? HostAccount { get; private set; }
    public string? HostPassword { get; private set; }
    public int Teams { get; private set; }
    public int Questions { get; private set; } = 10;
    public bool Fast { get; private set; }
    public int? Categories { get; private set; }
    public Uri Api { get; private set; } = new("http://localhost:53507/");
    public string Keycloak { get; private set; } = "http://localhost:8080/realms/quizgame";
    public string ClientId { get; private set; } = "quiz-backend";
    public string Mailpit { get; private set; } = "http://localhost:8025";
    public string? Seed { get; private set; }

    public int GuestCount => Guests ?? (Mode == BotMode.Join ? 4 : 5);

    public int PlayerCount => GuestCount + Newcomers + Accounts.Count;

    // null и текст ошибки, если аргументы не подходят
    public static (BotOptions? Options, string? Error) Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            return (null, null);
        }

        var options = new BotOptions();

        switch (args[0])
        {
            case "join":
                options.Mode = BotMode.Join;
                break;
            case "host":
                options.Mode = BotMode.Host;
                break;
            default:
                return (null, $"неизвестная команда «{args[0]}»: нужна join или host");
        }

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];

            if (!arg.StartsWith("--"))
            {
                options.Codes.Add(CodeFromLink(arg));
                continue;
            }

            if (arg == "--fast")
            {
                options.Fast = true;
                continue;
            }

            if (i + 1 >= args.Length)
            {
                return (null, $"после {arg} нужно значение");
            }

            var value = args[++i];

            try
            {
                switch (arg)
                {
                    case "--bots": options.Guests = int.Parse(value); break;
                    case "--register": options.Newcomers = int.Parse(value); break;
                    case "--accounts": options.Accounts.AddRange(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)); break;
                    case "--password": options.Password = value; break;
                    case "--reconnect": options.Reconnects = int.Parse(value); break;
                    case "--leave": options.Leavers = int.Parse(value); break;
                    case "--speed": options.Speed = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--again": options.Again = int.Parse(value); break;
                    case "--host-account": options.HostAccount = value; break;
                    case "--host-password": options.HostPassword = value; break;
                    case "--team": options.Teams = int.Parse(value); break;
                    case "--questions": options.Questions = int.Parse(value); break;
                    case "--categories": options.Categories = int.Parse(value); break;
                    case "--api": options.Api = new Uri(value.EndsWith('/') ? value : value + "/"); break;
                    case "--keycloak": options.Keycloak = value; break;
                    case "--client": options.ClientId = value; break;
                    case "--mailpit": options.Mailpit = value; break;
                    case "--seed": options.Seed = value; break;
                    default: return (null, $"неизвестный параметр {arg}");
                }
            }
            catch (FormatException)
            {
                return (null, $"{arg}: не число «{value}»");
            }
        }

        return options.Validate() is { } error ? (null, error) : (options, null);
    }

    private string? Validate()
    {
        if (Mode == BotMode.Join && Codes.Count == 0 && HostAccount is null)
        {
            return "join: укажите ссылку на комнату или --host-account";
        }

        if (Mode == BotMode.Host && string.IsNullOrWhiteSpace(HostAccount))
        {
            return "host: укажите --host-account — комнату создаёт только аккаунт";
        }

        if (Mode == BotMode.Host && Teams is not 0 and (< 2 or > 5))
        {
            return "--team: от 2 до 5 команд";
        }

        if (HostAccount is not null && Accounts.Contains(HostAccount, StringComparer.OrdinalIgnoreCase))
        {
            return $"{HostAccount} — ведущий, уберите его из --accounts";
        }

        if (PlayerCount < 1)
        {
            return "нужен хотя бы один бот";
        }

        // больше не пустит и командная комната: 30 мест вместе с ведущим
        if (PlayerCount > 29)
        {
            return "ботов больше 29 — в комнате не хватит мест";
        }

        if (Reconnects + Leavers > PlayerCount)
        {
            return "--reconnect и --leave вместе больше, чем игроков";
        }

        if (Again < 0)
        {
            return "--again: число игр не меньше нуля";
        }

        return Speed <= 0 ? "--speed должен быть больше нуля" : null;
    }

    // http://localhost:5200/join/5X6N4XAE → 5X6N4XAE
    private static string CodeFromLink(string value)
        => value.TrimEnd('/').Split('/')[^1].Trim();
}
