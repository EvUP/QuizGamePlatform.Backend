using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using QuizGamePlatform.Web.State;

namespace QuizGamePlatform.Web.Api;

/// <summary>
/// Один источник токена для HTTP и SignalR: сначала аккаунт Keycloak, иначе гостевой.
/// </summary>
public sealed class ApiAccessTokenSource(IAccessTokenProvider accountTokens, GuestSession guest)
{
    public async ValueTask<string?> GetAsync()
    {
        // без входа в аккаунт возвращает RequiresRedirect, исключения нет
        var result = await accountTokens.RequestAccessToken();

        if (result.TryGetToken(out var accessToken))
        {
            return accessToken.Value;
        }

        return guest.Current?.AccessToken;
    }
}

/// <summary>
/// Подставляет Bearer в запросы к API. Стандартный AuthorizationMessageHandler не подходит:
/// без входа в аккаунт он бросает AccessTokenNotAvailableException, а гостям API нужен.
/// </summary>
public sealed class ApiAuthorizationHandler(ApiAccessTokenSource tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is null && await tokens.GetAsync() is { } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
