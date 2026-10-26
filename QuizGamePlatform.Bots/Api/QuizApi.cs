using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuizGamePlatform.Bots.Api;

public sealed class ApiException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

// REST API игры: те же запросы, что шлёт фронт
public sealed class QuizApi(HttpClient http)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public Task<CreateRoomResponse> CreateRoomAsync(string token, CreateRoomRequest request, CancellationToken ct)
        => SendAsync<CreateRoomResponse>(HttpMethod.Post, "api/Room", token, request, ct);

    public Task<JoinResponse> JoinAsync(string? token, JoinRequest request, CancellationToken ct)
        => SendAsync<JoinResponse>(HttpMethod.Post, "api/Room/join", token, request, ct);

    public Task<Invite> GetInviteAsync(string code, CancellationToken ct)
        => SendAsync<Invite>(HttpMethod.Get, $"api/Room/invite/{Uri.EscapeDataString(code)}", null, null, ct);

    public Task<List<MyRoom>> GetMyRoomsAsync(string token, CancellationToken ct)
        => SendAsync<List<MyRoom>>(HttpMethod.Get, "api/Room/my", token, null, ct);

    public Task<RoomState> GetRoomAsync(string token, Guid roomId, CancellationToken ct)
        => SendAsync<RoomState>(HttpMethod.Get, $"api/Room/{roomId}", token, null, ct);

    // «Сыграть ещё»: следующая игра той же комнаты, или уже собранная, если она есть
    public Task<CreateRoomResponse> RematchAsync(string token, Guid roomId, CancellationToken ct)
        => SendAsync<CreateRoomResponse>(HttpMethod.Post, $"api/Room/{roomId}/rematch", token, null, ct);

    // перейти из итогов в игру, которую собрал ведущий; гостю новый токен
    public Task<JoinResponse> FollowAsync(string token, Guid roomId, CancellationToken ct)
        => SendAsync<JoinResponse>(HttpMethod.Post, $"api/Room/{roomId}/follow", token, null, ct);

    public Task LeaveAsync(string token, Guid roomId, CancellationToken ct)
        => SendAsync(HttpMethod.Post, "api/Room/leave", token, new LeaveRequest(roomId, ExitReason.Normal), ct);

    public Task<List<Category>> GetCategoriesAsync(CancellationToken ct)
        => SendAsync<List<Category>>(HttpMethod.Get, "api/Categories", null, null, ct);

    public Task StartMatchAsync(string token, StartMatchRequest request, CancellationToken ct)
        => SendAsync(HttpMethod.Post, "api/Match/start", token, request, ct);

    public Task<CurrentQuestion> GetCurrentQuestionAsync(string token, Guid matchId, CancellationToken ct)
        => SendAsync<CurrentQuestion>(HttpMethod.Get, $"api/Match/{matchId}/current-question", token, null, ct);

    public Task AnswerAsync(string token, Guid matchId, Guid optionId, CancellationToken ct)
        => SendAsync(HttpMethod.Post, $"api/Match/{matchId}/answer", token, new SubmitAnswerRequest(optionId), ct);

    public Task CloseQuestionAsync(string token, Guid matchId, CancellationToken ct)
        => SendAsync(HttpMethod.Post, $"api/Match/{matchId}/close-question", token, null, ct);

    private async Task<T> SendAsync<T>(HttpMethod method, string uri, string? token, object? body, CancellationToken ct)
    {
        using var response = await SendRawAsync(method, uri, token, body, ct);
        return (await response.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    private async Task SendAsync(HttpMethod method, string uri, string? token, object? body, CancellationToken ct)
    {
        using var response = await SendRawAsync(method, uri, token, body, ct);
    }

    private async Task<HttpResponseMessage> SendRawAsync(
        HttpMethod method, string uri, string? token, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, uri);

        if (token is not null)
        {
            request.Headers.Authorization = new("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        }

        var response = await http.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        // сервер отвечает понятным текстом, его и показываем
        string? message = null;

        try
        {
            message = (await response.Content.ReadFromJsonAsync<ErrorResponse>(Json, ct))?.Message;
        }
        catch (JsonException)
        {
        }

        var status = response.StatusCode;
        response.Dispose();

        throw new ApiException(status, message ?? $"{(int)status} {status}");
    }
}
