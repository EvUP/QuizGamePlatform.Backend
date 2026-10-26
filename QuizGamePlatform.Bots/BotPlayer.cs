using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using QuizGamePlatform.Bots.Api;

namespace QuizGamePlatform.Bots;

// что случится с игроком посреди игры
public enum Mishap { None, Reconnect, Leave }

// один игрок как человек во вкладке браузера: держит связь с хабом и отвечает, подумав
public sealed class BotPlayer(int index, string nickname, Persona persona, Mishap mishap, QuizApi api, AnswerKey answers, BotOptions options)
{
    // страховка, если событие хаба потерялось: раз в пару секунд сверяемся с сервером сами
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    // столько на итогах ждём, позовёт ли ведущий ещё
    private static readonly TimeSpan PlayAgainWait = TimeSpan.FromMinutes(10);

    private readonly Channel<bool> _pokes = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    private readonly string _nickname = nickname;
    private readonly HashSet<int> _seenQuestions = [];
    private ITokenSource _tokens = null!;
    private HubConnection? _hub;
    private Guid _roomId;
    private int? _mishapAt;
    private int _closedQuestion = -1;
    private int _answered;
    private int _correct;
    private volatile bool _offline;
    private volatile bool _done;
    private bool _waitLogged;
    private int _gamesLeft = options.Again;
    private DateTime _finishedAt;
    private bool _moved;

    // откуда перешли «Сыграть ещё»: ведущий может отменить новую игру, тогда ждём на итогах прошлой
    private (Guid RoomId, ITokenSource Tokens, string Name)? _previous;

    public string Name { get; private set; } = nickname;

    public bool IsHost { get; private set; }

    public RoomState? FinalRoom { get; private set; }

    // больше не играет: вышел, убрали, не позвали или сломался
    public bool Done => _done;

    // итоги игры, после которой будет следующая (последние печатает Program в конце)
    public Action<RoomState>? RoundFinished { get; set; }

    // только у бота-ведущего: «Сыграть ещё» и старт следующей игры; false, если играть больше не с кем
    public Func<CancellationToken, Task<bool>>? PlayAgain { get; set; }

    public string Stats => $"верно {_correct} из {_answered} отвеченных — {persona.Kind}";

    // ведущий уже в комнате: он её создал
    public void AttachAsHost(Guid roomId, AccountToken account)
    {
        IsHost = true;
        _roomId = roomId;
        _tokens = account;
        Say($"создал комнату и ведёт игру — {persona}");
    }

    public async Task StartMatchAsync(StartMatchRequest request, CancellationToken ct)
        => await api.StartMatchAsync(await _tokens.GetAsync(ct), request with { RoomId = _roomId }, ct);

    public async Task<RoomState> GetRoomAsync(CancellationToken ct)
        => await api.GetRoomAsync(await _tokens.GetAsync(ct), _roomId, ct);

    // ведущий жмёт «Сыграть ещё» и сам переходит в лобби следующей игры
    public async Task<CreateRoomResponse> RematchAsync(CancellationToken ct)
    {
        var next = await api.RematchAsync(await _tokens.GetAsync(ct), _roomId, ct);
        await MoveToAsync(next.Id, _tokens, ct);
        return next;
    }

    // вход по коду комнаты или команды; гость с ником, аккаунт под своим логином
    public async Task<bool> JoinAsync(string code, AccountToken? account, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                var token = account is null ? null : await account.GetAsync(ct);
                var joined = await api.JoinAsync(token, new JoinRequest(account is null ? Name : null, code), ct);

                _tokens = account ?? (ITokenSource)new GuestToken(joined.GuestAccessToken!);
                _roomId = joined.Room.RoomId;
                Name = joined.Room.PlayerName;

                var team = joined.Room.TeamNumber is { } number ? $", команда {number}" : "";
                Say($"вошёл{team} — {persona}");

                return true;
            }
            catch (ApiException e) when (e.Status == HttpStatusCode.Conflict && account is null && e.Message.Contains("занят"))
            {
                Name = $"{_nickname} {attempt + 1}";
            }
            catch (ApiException e) when (e.Status == HttpStatusCode.Conflict && e.Message.Contains("повторите"))
            {
                await Task.Delay(Random.Shared.Next(100, 500), ct);
            }
            catch (Exception e) when (e is ApiException or InvalidOperationException or HttpRequestException)
            {
                Log.Error(Name, $"не вошёл: {e.Message}");
                return false;
            }
        }

        Log.Error(Name, "не вошёл: сервер всё время занят");
        return false;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await ConnectAsync(ct);

            while (!_done && !ct.IsCancellationRequested)
            {
                await WaitForNewsAsync(ct);

                if (!_offline)
                {
                    await SyncAsync(ct);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            Log.Error(Name, $"сломался: {e.Message}");
        }
        finally
        {
            _done = true;

            if (_hub is not null)
            {
                await _hub.DisposeAsync();
            }
        }
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        _hub = new HubConnectionBuilder()
            .WithUrl(new Uri(options.Api, "hubs/game"), o => o.AccessTokenProvider = async () => await _tokens.GetAsync(CancellationToken.None))
            .WithAutomaticReconnect()
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();

        _hub.On<Guid>("RoomUpdated", _ => Poke());
        _hub.On<JsonElement>("MatchUpdated", _ => Poke());
        // ведущему важно, кто ответил: когда ответили все, он закрывает вопрос
        _hub.On<JsonElement>("PlayerAnswered", _ =>
        {
            if (IsHost)
            {
                Poke();
            }
        });

        // после переподключения группы на сервере потеряны, подписываемся снова
        _hub.Reconnected += async _ =>
        {
            await JoinRoomGroupAsync(CancellationToken.None);
            Poke();
        };

        await _hub.StartAsync(ct);
        await JoinRoomGroupAsync(ct);
    }

    private async Task<bool> JoinRoomGroupAsync(CancellationToken ct)
    {
        var joined = await _hub!.InvokeAsync<bool>("JoinRoom", _roomId, ct);

        if (!joined)
        {
            Log.Error(Name, "хаб не пустил в комнату");
        }

        return joined;
    }

    private void Poke() => _pokes.Writer.TryWrite(true);

    private async Task WaitForNewsAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(PollInterval);

        try
        {
            await _pokes.Reader.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
        }
    }

    // что сейчас в комнате и что от меня нужно
    private async Task SyncAsync(CancellationToken ct)
    {
        var token = await _tokens.GetAsync(ct);
        RoomState room;

        try
        {
            room = await api.GetRoomAsync(token, _roomId, ct);
        }
        catch (ApiException e) when (e.Status is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            // удалить можно только лобби: перешли в него с итогов, значит, ведущий отменил игру, раунд ещё не сыгран
            if (_previous is { } previous)
            {
                _previous = null;
                Say("ведущий отменил игру — жду на итогах прошлой, позовёт ли ещё");
                Name = previous.Name;
                await MoveToAsync(previous.RoomId, previous.Tokens, ct);
                _finishedAt = DateTime.UtcNow;
                Poke();
                return;
            }

            Say("комнаты больше нет");
            _done = true;
            return;
        }

        var me = room.Players.FirstOrDefault(p => p.RoomPlayerLinkId == room.MyRoomPlayerLinkId);

        if (_moved)
        {
            _moved = false;
            var team = me?.TeamNumber is { } number ? $", команда {number}" : "";
            Say($"перешёл в игру {room.GameNumber}{team}");
        }

        if (room.Status == RoomStatus.Finished)
        {
            await OnFinishedAsync(room, me, ct);
            return;
        }

        if (me is { IsActive: false, ExitReason: ExitReason.Kicked })
        {
            Say("ведущий убрал меня из комнаты");
            _done = true;
            return;
        }

        if (room.MatchId is not { } matchId)
        {
            if (!_waitLogged)
            {
                _waitLogged = true;

                if (!IsHost)
                {
                    Say("жду, когда ведущий начнёт игру");
                }
            }

            return;
        }

        var question = await api.GetCurrentQuestionAsync(token, matchId, ct);

        if (question.Status != MatchStatus.QuestionActive)
        {
            return;
        }

        if (IsHost)
        {
            await CloseWhenEveryoneAnsweredAsync(token, room, question, ct);
        }

        if (!_seenQuestions.Add(question.QuestionOrder))
        {
            return;
        }

        _mishapAt ??= PickMishapQuestion(question.TotalQuestions);

        if (mishap != Mishap.None && question.QuestionOrder == _mishapAt)
        {
            await MishapAsync(token, ct);
            return;
        }

        // после переподключения свой ответ уже может быть на сервере
        if (question.MyAnswerOptionId is null)
        {
            _ = AnswerLaterAsync(matchId, question, ct);
        }
    }

    // итоги: конец, «Сыграть ещё» у ведущего или ждём, позовёт ли он ещё
    private async Task OnFinishedAsync(RoomState room, Participant? me, CancellationToken ct)
    {
        if (FinalRoom?.Id != room.Id)
        {
            FinalRoom = room;
            Say($"игра окончена: {Stats}{(me?.Score is { } score ? $", очков {score}" : "")}");

            if (_gamesLeft == 0)
            {
                _done = true;
                return;
            }

            _gamesLeft--;
            _finishedAt = DateTime.UtcNow;
            RoundFinished?.Invoke(room);

            if (IsHost)
            {
                await PlayAgainAsHostAsync(ct);
                return;
            }

            Say("жду, позовёт ли ведущий ещё");
        }

        switch (room.NextRoomStatus)
        {
            case RoomStatus.Waiting:
                await FollowAsync(ct);
                break;
            case RoomStatus.InProgress:
                Say("следующая игра уже идёт без меня — ухожу");
                _done = true;
                break;
            case RoomStatus.Finished:
                Say("следующая игра уже сыграна — ухожу");
                _done = true;
                break;
            default:
                if (DateTime.UtcNow - _finishedAt > PlayAgainWait)
                {
                    Say("ведущий не позвал ещё раз — ухожу");
                    _done = true;
                }

                break;
        }
    }

    private async Task PlayAgainAsHostAsync(CancellationToken ct)
    {
        try
        {
            if (PlayAgain is null || !await PlayAgain(ct))
            {
                _done = true;
            }
        }
        catch (ApiException e)
        {
            Log.Error(Name, $"«Сыграть ещё» не вышло: {e.Message}");
            _done = true;
        }
    }

    // как вкладка на итогах: ведущий собрал игру, переходим сами, гостю сервер выдаёт новый токен
    private async Task FollowAsync(CancellationToken ct)
    {
        // не все разом, как люди
        await Task.Delay(Random.Shared.Next(0, 1000), ct);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var joined = await api.FollowAsync(await _tokens.GetAsync(ct), _roomId, ct);

                _previous = (_roomId, _tokens, Name);
                Name = joined.Room.PlayerName;
                await MoveToAsync(joined.Room.RoomId, joined.GuestAccessToken is { } guest ? new GuestToken(guest) : _tokens, ct);
                _moved = true;
                Poke();
                return;
            }
            catch (ApiException e) when (e.Status == HttpStatusCode.Conflict && e.Message.Contains("ещё раз"))
            {
                await Task.Delay(Random.Shared.Next(100, 500), ct);
            }
            catch (ApiException e)
            {
                Log.Error(Name, $"не перешёл: {e.Message}");
                _done = true;
                return;
            }
        }

        Log.Error(Name, "не перешёл: сервер всё время занят");
        _done = true;
    }

    // следующая игра: та же вкладка, но другая комната; со сменой токена гостю нужно новое соединение
    private async Task MoveToAsync(Guid roomId, ITokenSource tokens, CancellationToken ct)
    {
        var reconnect = !ReferenceEquals(tokens, _tokens);

        _tokens = tokens;
        _roomId = roomId;
        _seenQuestions.Clear();
        _mishapAt = null;
        _closedQuestion = -1;
        _waitLogged = false;
        _answered = 0;
        _correct = 0;

        if (reconnect)
        {
            await _hub!.DisposeAsync();
            await ConnectAsync(ct);
        }
        else
        {
            await JoinRoomGroupAsync(ct);
        }
    }

    // ведущий не ждёт таймер, когда ответили все, кто в игре
    private async Task CloseWhenEveryoneAnsweredAsync(string token, RoomState room, CurrentQuestion question, CancellationToken ct)
    {
        var inGame = room.Players.Where(p => p.IsActive).Select(p => p.RoomPlayerLinkId).ToHashSet();

        if (_closedQuestion == question.QuestionOrder || !inGame.IsSubsetOf(question.AnsweredLinkIds))
        {
            return;
        }

        _closedQuestion = question.QuestionOrder;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            await api.CloseQuestionAsync(token, question.MatchId, ct);
            Say($"все ответили — показываю ответ на вопрос {question.QuestionOrder + 1}");
        }
        catch (ApiException)
        {
            // таймер успел закрыть сам
        }
    }

    // не первый и не последний вопрос, чтобы было видно и до, и после
    private static int PickMishapQuestion(int total) => total >= 3 ? Random.Shared.Next(1, total - 1) : 0;

    private async Task MishapAsync(string token, CancellationToken ct)
    {
        if (mishap == Mishap.Leave)
        {
            Say("надоело — выхожу из игры");
            await api.LeaveAsync(token, _roomId, ct);
            _done = true;
            return;
        }

        // как закрытая крышка ноутбука: сервер через 5 с считает игрока вышедшим, вернуться можно в течение 40 с
        var seconds = Random.Shared.Next(8, 26);
        Say($"пропала связь на {seconds} с");

        _offline = true;
        await _hub!.StopAsync(ct);
        await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
        await _hub.StartAsync(ct);
        var back = await JoinRoomGroupAsync(ct);
        _offline = false;

        Say(back ? "связь вернулась — снова в игре" : "обратно не пустили");
        Poke();
    }

    private async Task AnswerLaterAsync(Guid matchId, CurrentQuestion question, CancellationToken ct)
    {
        var number = $"{question.QuestionOrder + 1}/{question.TotalQuestions}";

        try
        {
            if (Random.Shared.NextDouble() < persona.MissChance)
            {
                Say($"вопрос {number}: засмотрелся в телефон и не ответил");
                return;
            }

            // успеть до конца таймера по часам сервера
            var left = question.QuestionEndsAt - question.ServerTimeUtc - TimeSpan.FromSeconds(1);
            var think = persona.ThinkTime(question.Text, options.Speed);

            if (think > left)
            {
                think = left > TimeSpan.Zero ? left : TimeSpan.Zero;
            }

            await Task.Delay(think, ct);

            if (_offline || _done)
            {
                return;
            }

            var (option, right) = Choose(question);
            await api.AnswerAsync(await _tokens.GetAsync(ct), matchId, option.Id, ct);

            Interlocked.Increment(ref _answered);

            if (right == true)
            {
                Interlocked.Increment(ref _correct);
            }

            var mark = right switch { true => "✓", false => "✗", null => "?" };
            Say($"вопрос {number}: «{option.Text}» {mark} за {think.TotalSeconds:0.0} с");
        }
        catch (ApiException e)
        {
            Say($"вопрос {number}: ответ не принят — {e.Message}");
        }
        catch (OperationCanceledException)
        {
        }
    }

    // верный с вероятностью точности характера; вопроса нет в сиде, тогда наугад
    private (AnswerOption Option, bool? Right) Choose(CurrentQuestion question)
    {
        var rightText = answers.Find(question.Text);
        var right = rightText is null ? null : question.Options.FirstOrDefault(o => o.Text == rightText);

        if (right is null)
        {
            return (question.Options[Random.Shared.Next(question.Options.Count)], null);
        }

        if (Random.Shared.NextDouble() < persona.Accuracy)
        {
            return (right, true);
        }

        var wrong = question.Options.Where(o => o.Id != right.Id).ToList();
        return (wrong[Random.Shared.Next(wrong.Count)], false);
    }

    public void Say(string message) => Log.Bot(index, Name, message);
}
