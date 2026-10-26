using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR.Client;
using QuizGamePlatform.Web.Api;
using QuizGamePlatform.Web.Api.Contracts;

namespace QuizGamePlatform.Web.Realtime;

/// <summary>
/// Одно соединение с /hubs/game на всё приложение. События только сигнал «перечитай»,
/// данные берутся из REST. После переподключения группы на сервере теряются, подписчик
/// снова вызывает JoinRoom (событие Reconnected).
/// </summary>
public sealed class GameHubClient : IAsyncDisposable
{
    private readonly HubConnection connection;
    private readonly SemaphoreSlim startLock = new(1, 1);

    public GameHubClient(ApiEndpoint api, ApiAccessTokenSource tokens)
    {
        connection = new HubConnectionBuilder()
            .WithUrl(new Uri(api.BaseAddress, "hubs/game"), options =>
            {
                // токен аккаунта или гостя; спрашивается при каждом (пере)подключении
                options.AccessTokenProvider = async () => await tokens.GetAsync();
            })
            .WithAutomaticReconnect(new GameRetryPolicy())
            // тихий обрыв сети замечаем за 10 с, а не за 30 (сервер пингует раз в 5 с)
            .WithServerTimeout(TimeSpan.FromSeconds(10))
            .AddJsonProtocol(options =>
            {
                // статусы строками, как на сервере
                options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default);
            })
            .Build();

        connection.On<Guid>("RoomUpdated", roomId => RoomUpdated?.Invoke(roomId) ?? Task.CompletedTask);
        connection.On<MatchStateResponse>("MatchUpdated", match => MatchUpdated?.Invoke(match) ?? Task.CompletedTask);
        connection.On<PlayerAnsweredEvent>("PlayerAnswered", answer => PlayerAnswered?.Invoke(answer) ?? Task.CompletedTask);

        connection.Reconnecting += _ =>
        {
            StateChanged?.Invoke();
            return Task.CompletedTask;
        };
        connection.Reconnected += async _ =>
        {
            StateChanged?.Invoke();

            if (Reconnected is not null)
            {
                await Reconnected();
            }
        };
        connection.Closed += _ =>
        {
            StateChanged?.Invoke();
            return Task.CompletedTask;
        };
    }

    public HubConnectionState State => connection.State;

    public event Func<Guid, Task>? RoomUpdated;

    public event Func<MatchStateResponse, Task>? MatchUpdated;

    public event Func<PlayerAnsweredEvent, Task>? PlayerAnswered;

    /// <summary>Связь вернулась сама: нужно снова войти в группу комнаты и перечитать состояние</summary>
    public event Func<Task>? Reconnected;

    public event Action? StateChanged;

    public async Task EnsureConnectedAsync(CancellationToken ct = default)
    {
        await startLock.WaitAsync(ct);

        try
        {
            if (connection.State == HubConnectionState.Disconnected)
            {
                await connection.StartAsync(ct);
                StateChanged?.Invoke();
            }
        }
        finally
        {
            startLock.Release();
        }
    }

    /// <summary>
    /// Переподключение с новым токеном: личность закрепляется при подключении,
    /// а гость мог войти в аккаунт (или получить новый гостевой токен) уже после старта.
    /// </summary>
    public async Task ReconnectWithFreshTokenAsync(CancellationToken ct = default)
    {
        await startLock.WaitAsync(ct);

        try
        {
            await connection.StopAsync(ct);
            await connection.StartAsync(ct);
            StateChanged?.Invoke();
        }
        finally
        {
            startLock.Release();
        }
    }

    /// <summary>Подписка на события комнаты; false, если вы не участник</summary>
    public Task<bool> JoinRoomAsync(Guid roomId, CancellationToken ct = default)
        => connection.InvokeAsync<bool>("JoinRoom", roomId, ct);

    public async ValueTask DisposeAsync()
    {
        await connection.DisposeAsync();
        startLock.Dispose();
    }
}
