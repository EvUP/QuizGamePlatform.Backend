using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using QuizGamePlatform.Web.Api;
using QuizGamePlatform.Web.Api.Contracts;
using QuizGamePlatform.Web.Infrastructure;
using QuizGamePlatform.Web.Realtime;

namespace QuizGamePlatform.Web.State;

public enum GamePhase
{
    None,
    Lobby,      // комната Waiting
    Question,   // матч QuestionActive
    Reveal,     // матч QuestionClosed — показ правильного ответа
    Results     // матч или комната Finished
}

public enum SessionStatus
{
    Idle,
    Loading,
    Ready,
    NotParticipant,   // хаб не пустил: по токену вы не участник комнаты
    Removed,          // вы в комнате, но уже не играете: убрал ведущий или вышли сами (MyExitReason)
    Gone,             // комнату удалили
    Failed
}

/// <summary>
/// Состояние открытой комнаты (state container из документации Blazor).
/// Источник правды — сервер: события хаба только запускают перечитывание.
/// Компоненты подписываются на Changed и перерисовываются через InvokeAsync(StateHasChanged).
/// </summary>
public sealed class GameSession : IDisposable
{
    private readonly RoomApiClient roomApi;
    private readonly MatchApiClient matchApi;
    private readonly GameHubClient hub;
    private readonly ServerClock clock;
    private readonly LatestRequest roomRequest = new();
    private readonly LatestRequest questionRequest = new();
    private readonly HashSet<Guid> answered = [];

    // свой ответ: сразу после отправки, после перезагрузки страницы — от сервера
    private (int QuestionOrder, Guid OptionId)? myAnswer;

    public GameSession(RoomApiClient roomApi, MatchApiClient matchApi, GameHubClient hub, ServerClock clock)
    {
        this.roomApi = roomApi;
        this.matchApi = matchApi;
        this.hub = hub;
        this.clock = clock;

        hub.RoomUpdated += OnRoomUpdatedAsync;
        hub.MatchUpdated += OnMatchUpdatedAsync;
        hub.PlayerAnswered += OnPlayerAnsweredAsync;
        hub.Reconnected += OnReconnectedAsync;
        hub.StateChanged += OnHubStateChanged;
    }

    /// <summary>Когда пропала связь (для отсчёта «место держим ещё N с»); null — связь есть</summary>
    public DateTime? ReconnectingSinceUtc { get; private set; }

    public event Action? Changed;

    public Guid? RoomId { get; private set; }

    public SessionStatus Status { get; private set; }

    public ApiError? Error { get; private set; }

    public RoomStateResponse? Room { get; private set; }

    public CurrentQuestionResponse? Question { get; private set; }

    /// <summary>Кто ответил на текущий вопрос: снимок сервера плюс события хаба</summary>
    public IReadOnlySet<Guid> AnsweredLinkIds => answered;

    public HubConnectionState Connection => hub.State;

    /// <summary>Мой ответ на текущий вопрос (null — не отвечал)</summary>
    public Guid? MyAnswerOptionId
        => myAnswer is { } mine && mine.QuestionOrder == Question?.QuestionOrder ? mine.OptionId : null;

    public RoomParticipationResponse? Me
        => Room?.Players.Find(p => p.RoomPlayerLinkId == Room.MyRoomPlayerLinkId);

    public bool IsHost => Me?.IsHost == true;

    /// <summary>Почему вы больше не в игре (для статуса Removed)</summary>
    public ExitReason? MyExitReason => Me is { IsActive: false } me ? me.ExitReason : null;

    public GamePhase Phase => Room switch
    {
        null => GamePhase.None,
        { Status: RoomStatus.Waiting } => GamePhase.Lobby,
        { Status: RoomStatus.Finished } => GamePhase.Results,
        _ => Question?.Status switch
        {
            MatchStatus.QuestionActive => GamePhase.Question,
            MatchStatus.QuestionClosed => GamePhase.Reveal,
            MatchStatus.Finished => GamePhase.Results,
            _ => GamePhase.None
        }
    };

    /// <summary>
    /// Вход на страницу комнаты. Порядок важен: хаб → JoinRoom → GET,
    /// иначе событие между GET и подпиской потеряется.
    /// </summary>
    public async Task EnterAsync(Guid roomId)
    {
        if (RoomId == roomId && Status == SessionStatus.Ready)
        {
            await ReloadAllAsync();
            return;
        }

        RoomId = roomId;
        Room = null;
        Question = null;
        answered.Clear();
        myAnswer = null;
        Error = null;
        Status = SessionStatus.Loading;
        NotifyChanged();

        try
        {
            await hub.EnsureConnectedAsync();
            var joined = await hub.JoinRoomAsync(roomId);

            // соединение могло открыться ещё под прежней личностью (до входа в аккаунт или в другую комнату гостем)
            if (!joined)
            {
                await hub.ReconnectWithFreshTokenAsync();
                joined = await hub.JoinRoomAsync(roomId);
            }

            if (RoomId != roomId)
            {
                return;
            }

            if (!joined)
            {
                Status = SessionStatus.NotParticipant;
                NotifyChanged();
                return;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or HubException or TimeoutException)
        {
            Fail(new ApiError(0, "Не удалось подключиться к игре. Проверьте связь и попробуйте ещё раз"));
            return;
        }

        await ReloadAllAsync();
    }

    /// <summary>Уход со страницы комнаты: соединение остаётся, состояние сбрасываем</summary>
    public void Exit()
    {
        RoomId = null;
        Room = null;
        Question = null;
        answered.Clear();
        Status = SessionStatus.Idle;
        roomRequest.Dispose();
        questionRequest.Dispose();
    }

    public async Task ReloadAllAsync()
    {
        await ReloadRoomAsync();

        if (Room?.MatchId is not null)
        {
            await ReloadQuestionAsync();
        }
    }

    // ── действия в лобби: успех сервер разошлёт как RoomUpdated, перечитываем и сами — быстрее ──

    public Task<ApiResult> RenameTeamAsync(int teamNumber, string name)
        => RunLobbyActionAsync(id => roomApi.RenameTeamAsync(id, teamNumber, name));

    public Task<ApiResult> AddTeamAsync()
        => RunLobbyActionAsync(id => roomApi.AddTeamAsync(id, null));

    public Task<ApiResult> DeleteTeamAsync(int teamNumber)
        => RunLobbyActionAsync(id => roomApi.DeleteTeamAsync(id, teamNumber));

    public Task<ApiResult> MovePlayerAsync(Guid linkId, int teamNumber)
        => RunLobbyActionAsync(id => roomApi.ChangeTeamAsync(id, linkId, teamNumber));

    public Task<ApiResult> RemovePlayerAsync(Guid linkId)
        => RunLobbyActionAsync(id => roomApi.RemovePlayerAsync(id, linkId));

    public async Task<ApiResult> StartMatchAsync(IReadOnlyCollection<Guid> categoryIds, int questionCount, RoomMode mode)
    {
        if (RoomId is not { } roomId)
        {
            return ApiResult.Failure(new ApiError(0, "Комната ещё не загрузилась — попробуйте через секунду"));
        }

        var result = await matchApi.StartAsync(new StartMatchRequest(roomId, [.. categoryIds], questionCount, mode));

        if (!result.IsSuccess)
        {
            return ApiResult.Failure(result.Error);
        }

        await ReloadAllAsync();
        return ApiResult.Success();
    }

    // ── матч ──

    /// <summary>Ответ на текущий вопрос; 400 «Ответ не принят» — второй раз, после дедлайна или уже вышли</summary>
    public async Task<ApiResult> SubmitAnswerAsync(Guid optionId)
    {
        if (Question is not { Status: MatchStatus.QuestionActive } question)
        {
            return ApiResult.Failure(new ApiError(0, "Не успели — вопрос уже закрыт"));
        }

        var result = await matchApi.AnswerAsync(question.MatchId, optionId);

        if (result.IsSuccess)
        {
            myAnswer = (question.QuestionOrder, optionId);

            if (Me is { } me)
            {
                answered.Add(me.RoomPlayerLinkId);
            }

            NotifyChanged();
        }

        return result;
    }

    /// <summary>Ведущий: закрыть вопрос сейчас — все увидят правильный ответ, затем следующий вопрос</summary>
    public async Task<ApiResult> CloseQuestionAsync()
    {
        if (Room?.MatchId is not { } matchId)
        {
            return ApiResult.Failure(new ApiError(0, "Игра ещё не началась или уже закончилась"));
        }

        var result = await matchApi.CloseQuestionAsync(matchId);

        if (!result.IsSuccess)
        {
            return ApiResult.Failure(result.Error);
        }

        await ReloadQuestionAsync();
        return ApiResult.Success();
    }

    public async Task<ApiResult> DeleteRoomAsync()
    {
        if (RoomId is not { } roomId)
        {
            return ApiResult.Failure(new ApiError(0, "Комната ещё не загрузилась — попробуйте через секунду"));
        }

        var result = await roomApi.DeleteAsync(roomId);

        if (result.IsSuccess)
        {
            Exit();
        }

        return result;
    }

    /// <summary>Сам вышел — автоматически не вернётся, только по ссылке</summary>
    public async Task<ApiResult> LeaveAsync()
    {
        if (RoomId is not { } roomId)
        {
            return ApiResult.Failure(new ApiError(0, "Комната ещё не загрузилась — попробуйте через секунду"));
        }

        var result = await roomApi.LeaveAsync(new LeaveRoomRequest(roomId, ExitReason.Normal));

        if (!result.IsSuccess)
        {
            return ApiResult.Failure(result.Error);
        }

        Exit();
        return ApiResult.Success();
    }

    private async Task<ApiResult> RunLobbyActionAsync(Func<Guid, Task<ApiResult>> action)
    {
        if (RoomId is not { } roomId)
        {
            return ApiResult.Failure(new ApiError(0, "Комната ещё не загрузилась — попробуйте через секунду"));
        }

        var result = await action(roomId);

        if (result.IsSuccess)
        {
            await ReloadRoomAsync();
        }

        return result;
    }

    private async Task ReloadRoomAsync()
    {
        if (RoomId is not { } roomId)
        {
            return;
        }

        var ct = roomRequest.Next();

        try
        {
            var result = await roomApi.GetStateAsync(roomId, ct);

            if (ct.IsCancellationRequested || RoomId != roomId)
            {
                return;
            }

            if (result.IsSuccess)
            {
                Room = result.Value;
                Error = null;

                // сервер продолжает отдавать комнату ушедшему: убранного ведущим или вышедшего сам
                // не показываем в лобби/игре (оборвавший связь — другое дело, его вернёт JoinRoom);
                // итоги завершённой игры видны всем, кто в ней был
                Status = MyExitReason is ExitReason.Kicked or ExitReason.Normal && Room.Status != RoomStatus.Finished
                    ? SessionStatus.Removed
                    : SessionStatus.Ready;

                NotifyChanged();

                // матч есть, а вопроса к нему нет: его загрузку могло отменить более свежее событие
                if (Room.MatchId is { } matchId && Question?.MatchId != matchId)
                {
                    await ReloadQuestionAsync(matchId);
                }
            }
            else if (result.Error.StatusCode == 404)
            {
                // комнату удалили или нас из неё убрали
                Room = null;
                Status = SessionStatus.Gone;
                NotifyChanged();
            }
            else
            {
                Fail(result.Error);
            }
        }
        catch (OperationCanceledException)
        {
            // пришло более свежее событие
        }
    }

    private async Task ReloadQuestionAsync(Guid? knownMatchId = null)
    {
        if ((knownMatchId ?? Room?.MatchId) is not { } matchId)
        {
            return;
        }

        var ct = questionRequest.Next();

        try
        {
            var result = await matchApi.GetCurrentQuestionAsync(matchId, ct);

            if (ct.IsCancellationRequested)
            {
                return;
            }

            if (result.IsSuccess)
            {
                if (Question?.QuestionOrder != result.Value.QuestionOrder)
                {
                    answered.Clear();
                }

                Question = result.Value;
                clock.Sync(Question.ServerTimeUtc);

                // события хаба, пришедшие пока шёл запрос, не теряем
                answered.UnionWith(Question.AnsweredLinkIds);

                if (Question.MyAnswerOptionId is { } mine)
                {
                    myAnswer = (Question.QuestionOrder, mine);
                }

                NotifyChanged();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private Task OnRoomUpdatedAsync(Guid roomId)
        => roomId == RoomId ? ReloadRoomAsync() : Task.CompletedTask;

    private async Task OnMatchUpdatedAsync(MatchStateResponse match)
    {
        if (match.RoomId != RoomId)
        {
            return;
        }

        // старт, смена вопроса, закрытие или финиш — меняется и комната (статус, очки команд).
        // matchId берём из события: перечитывание комнаты могут отменить параллельные RoomUpdated
        await ReloadQuestionAsync(match.MatchId);
        await ReloadRoomAsync();
    }

    private Task OnPlayerAnsweredAsync(PlayerAnsweredEvent answer)
    {
        if (answer.RoomId == RoomId && answer.QuestionIndex == Question?.QuestionOrder && answered.Add(answer.RoomPlayerLinkId))
        {
            NotifyChanged();
        }

        return Task.CompletedTask;
    }

    private async Task OnReconnectedAsync()
    {
        if (RoomId is not { } roomId)
        {
            return;
        }

        // сервер вернёт в игру, если окно 40 с ещё не прошло
        await hub.JoinRoomAsync(roomId);
        await ReloadAllAsync();
    }

    private void OnHubStateChanged()
    {
        ReconnectingSinceUtc = hub.State switch
        {
            HubConnectionState.Connected => null,
            _ when ReconnectingSinceUtc is null && hub.State == HubConnectionState.Reconnecting => clock.UtcNow,
            _ => ReconnectingSinceUtc
        };

        NotifyChanged();
    }

    private void Fail(ApiError error)
    {
        Error = error;
        Status = SessionStatus.Failed;
        NotifyChanged();
    }

    private void NotifyChanged() => Changed?.Invoke();

    public void Dispose()
    {
        hub.RoomUpdated -= OnRoomUpdatedAsync;
        hub.MatchUpdated -= OnMatchUpdatedAsync;
        hub.PlayerAnswered -= OnPlayerAnsweredAsync;
        hub.Reconnected -= OnReconnectedAsync;
        hub.StateChanged -= OnHubStateChanged;
        roomRequest.Dispose();
        questionRequest.Dispose();
    }
}
