using QuizGamePlatform.Web.Api.Contracts;

namespace QuizGamePlatform.Web.Api;

/// <summary>Комнаты и лобби: /api/Room</summary>
public sealed class RoomApiClient(HttpClient http) : ApiClientBase(http)
{
    private static ApiJsonContext Json => ApiJsonContext.Default;

    public Task<ApiResult<CreateRoomResponse>> CreateAsync(CreateRoomRequest request, CancellationToken ct = default)
        => PostAsync("api/Room", request, Json.CreateRoomRequest, Json.CreateRoomResponse, ct);

    public Task<ApiResult<RoomStateResponse>> GetStateAsync(Guid roomId, CancellationToken ct = default)
        => GetAsync($"api/Room/{roomId}", Json.RoomStateResponse, ct);

    public Task<ApiResult<List<MyRoomResponse>>> GetMyAsync(CancellationToken ct = default)
        => GetAsync("api/Room/my", Json.ListMyRoomResponse, ct);

    public Task<ApiResult> DeleteAsync(Guid roomId, CancellationToken ct = default)
        => DeleteAsync($"api/Room/{roomId}", ct);

    public Task<ApiResult<InviteResponse>> GetInviteAsync(string code, CancellationToken ct = default)
        => GetAsync($"api/Room/invite/{Uri.EscapeDataString(code)}", Json.InviteResponse, ct);

    public Task<ApiResult<JoinToRoomResponse>> JoinAsync(JoinToRoomRequest request, CancellationToken ct = default)
        => PostAsync("api/Room/join", request, Json.JoinToRoomRequest, Json.JoinToRoomResponse, ct);

    public Task<ApiResult<LeaveRoomResponse>> LeaveAsync(LeaveRoomRequest request, CancellationToken ct = default)
        => PostAsync("api/Room/leave", request, Json.LeaveRoomRequest, Json.LeaveRoomResponse, ct);

    // лобби командной игры, ответы 204

    public Task<ApiResult> ChangeTeamAsync(Guid roomId, Guid linkId, int teamNumber, CancellationToken ct = default)
        => PutAsync($"api/Room/{roomId}/players/{linkId}/team", new ChangeTeamRequest(teamNumber), Json.ChangeTeamRequest, ct);

    public Task<ApiResult> RenameTeamAsync(Guid roomId, int teamNumber, string name, CancellationToken ct = default)
        => PutAsync($"api/Room/{roomId}/teams/{teamNumber}", new TeamNameRequest(name), Json.TeamNameRequest, ct);

    public Task<ApiResult> AddTeamAsync(Guid roomId, string? name, CancellationToken ct = default)
        => PostAsync($"api/Room/{roomId}/teams", new TeamNameRequest(name), Json.TeamNameRequest, ct);

    public Task<ApiResult> DeleteTeamAsync(Guid roomId, int teamNumber, CancellationToken ct = default)
        => DeleteAsync($"api/Room/{roomId}/teams/{teamNumber}", ct);

    public Task<ApiResult> RemovePlayerAsync(Guid roomId, Guid linkId, CancellationToken ct = default)
        => DeleteAsync($"api/Room/{roomId}/players/{linkId}", ct);
}
