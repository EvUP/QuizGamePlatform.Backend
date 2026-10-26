using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace QuizGamePlatform.Bots.Api;

public interface ITokenSource
{
    Task<string> GetAsync(CancellationToken ct);
}

// гость получает токен при входе в комнату, живёт он часами
public sealed class GuestToken(string token) : ITokenSource
{
    public Task<string> GetAsync(CancellationToken ct) => Task.FromResult(token);
}

// аккаунт Keycloak: вход по логину и паролю (direct grant), токен живёт минуты, берём новый заранее
public sealed class AccountToken(HttpClient http, string realmUrl, string clientId, string login, string password)
    : ITokenSource
{
    private static readonly TimeSpan RenewBefore = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTime _renewAt;

    public string Login => login;

    public async Task<string> GetAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);

        try
        {
            if (_token is null || DateTime.UtcNow >= _renewAt)
            {
                var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["grant_type"] = "password",
                    ["username"] = login,
                    ["password"] = password,
                });

                using var response = await http.PostAsync(
                    $"{realmUrl.TrimEnd('/')}/protocol/openid-connect/token", form, ct);

                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        $"Keycloak не пустил аккаунт {login} ({(int)response.StatusCode}): проверьте логин и пароль");
                }

                var issued = (await response.Content.ReadFromJsonAsync<TokenResponse>(ct))!;
                _token = issued.AccessToken;
                _renewAt = DateTime.UtcNow.AddSeconds(issued.ExpiresIn) - RenewBefore;
            }

            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
