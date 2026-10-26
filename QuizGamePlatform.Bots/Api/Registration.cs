using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace QuizGamePlatform.Bots.Api;

// регистрация как у человека: форма Keycloak, письмо «Verify email» в Mailpit, переход по ссылке из письма
public sealed partial class Registration(BotOptions options)
{
    // куда Keycloak вернул бы браузер после входа; сами туда не ходим, но адрес должен быть в списке клиента
    private const string RedirectUri = "http://localhost:5200/authentication/login-callback";

    private static readonly TimeSpan MailTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan MailPoll = TimeSpan.FromMilliseconds(500);

    // логин может оказаться занят, тогда пробуем другой; возвращает того, кто зарегистрировался
    public async Task<NewPerson> RegisterAsync(NewPerson person, CancellationToken ct)
    {
        // у каждого свой «браузер»: ссылка из письма подтверждает почту в той же сессии
        using var browser = new Browser();

        for (var attempt = 1; ; attempt++)
        {
            var error = await SubmitFormAsync(browser, person, ct);

            if (error is null)
            {
                break;
            }

            if (attempt == 3 || !error.Contains("exists", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Keycloak не зарегистрировал {person.Login}: {error}");
            }

            person = NewPerson.Random();
        }

        var link = await WaitForVerifyLinkAsync(browser, person.Email, ct);
        using var verified = await browser.SendAsync(HttpMethod.Get, link, null, ct);

        // подтверждённая почта ведёт дальше по входу редиректом, ошибка ведёт на страницу с текстом
        if (verified.StatusCode != HttpStatusCode.Found)
        {
            throw new InvalidOperationException($"почта {person.Email} не подтвердилась ({(int)verified.StatusCode})");
        }

        return person;
    }

    // null: форма принята и Keycloak ждёт подтверждения почты, иначе текст ошибки с формы
    private async Task<string?> SubmitFormAsync(Browser browser, NewPerson person, CancellationToken ct)
    {
        var realm = options.Keycloak.TrimEnd('/');
        using var opened = await browser.SendAsync(
            HttpMethod.Get,
            $"{realm}/protocol/openid-connect/registrations?client_id={options.ClientId}&response_type=code&scope=openid"
            + $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}&code_challenge={CodeChallenge()}&code_challenge_method=S256",
            null,
            ct);
        var page = await opened.Content.ReadAsStringAsync(ct);

        if (FormAction().Match(page) is not { Success: true } action)
        {
            return "нет формы регистрации — проверьте, что в realm включена регистрация";
        }

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["firstName"] = person.FirstName,
            ["lastName"] = person.LastName,
            ["email"] = person.Email,
            ["username"] = person.Login,
            ["password"] = options.Password,
            ["password-confirm"] = options.Password,
        });

        using var response = await browser.SendAsync(HttpMethod.Post, WebUtility.HtmlDecode(action.Groups[1].Value), form, ct);

        if (response.StatusCode == HttpStatusCode.Found && response.Headers.Location is { } verifyPage
            && verifyPage.ToString().Contains("VERIFY_EMAIL"))
        {
            // письмо Keycloak отправляет, когда браузер открывает страницу «Подтвердите почту»
            using var shown = await browser.SendAsync(HttpMethod.Get, verifyPage.ToString(), null, ct);
            return null;
        }

        // форма вернулась с подсказками под полями
        var errors = FieldError().Matches(await response.Content.ReadAsStringAsync(ct))
            .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value.Trim()))
            .Where(e => e.Length > 0)
            .Distinct()
            .ToList();

        return errors.Count > 0 ? string.Join("; ", errors) : $"{(int)response.StatusCode} {response.StatusCode}";
    }

    private async Task<string> WaitForVerifyLinkAsync(Browser browser, string email, CancellationToken ct)
    {
        var mailpit = options.Mailpit.TrimEnd('/');
        var query = Uri.EscapeDataString($"to:\"{email}\"");
        var deadline = DateTime.UtcNow + MailTimeout;

        while (DateTime.UtcNow < deadline)
        {
            var found = await browser.Http.GetFromJsonAsync<MailSearch>($"{mailpit}/api/v1/search?query={query}", ct);

            if (found?.Messages is [{ } mail, ..])
            {
                var message = await browser.Http.GetFromJsonAsync<MailMessage>($"{mailpit}/api/v1/message/{mail.ID}", ct);

                if (VerifyLink().Match(message?.Text ?? "") is { Success: true } link)
                {
                    return link.Value;
                }
            }

            await Task.Delay(MailPoll, ct);
        }

        throw new InvalidOperationException($"письмо для {email} не пришло в Mailpit ({mailpit}) за {MailTimeout.TotalSeconds:0} с");
    }

    // PKCE обязателен у клиента; код потом не обмениваем, но вызов должен быть настоящим
    private static string CodeChallenge()
        => Convert.ToBase64String(SHA256.HashData(RandomNumberGenerator.GetBytes(32)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [GeneratedRegex(@"<form[^>]*id=""kc-register-form""[^>]*action=""([^""]+)""")]
    private static partial Regex FormAction();

    // <span class="... kc-feedback-text">Username already exists.</span>: под полем или общим сообщением
    [GeneratedRegex("""kc-feedback-text">\s*([^<]+?)\s*<""")]
    private static partial Regex FieldError();

    [GeneratedRegex(@"https?://\S*/login-actions/action-token\S*")]
    private static partial Regex VerifyLink();

    // куки Keycloak помечены Secure: браузер шлёт их на http://localhost, а CookieContainer .NET нет, поэтому держим их сами
    private sealed class Browser : IDisposable
    {
        private readonly Dictionary<string, string> _cookies = [];

        public HttpClient Http { get; } = new(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, HttpContent? content, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(method, url) { Content = content };

            if (_cookies.Count > 0)
            {
                request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}")));
            }

            var response = await Http.SendAsync(request, ct);

            // AUTH_SESSION_ID=...;Version=1;Path=/realms/quizgame/;Secure: нужна только пара имя=значение
            foreach (var cookie in response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [])
            {
                var pair = cookie.Split(';', 2)[0].Split('=', 2);
                _cookies[pair[0].Trim()] = pair.Length > 1 ? pair[1] : "";
            }

            if (method == HttpMethod.Get && !response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Found)
            {
                throw new InvalidOperationException($"Keycloak ответил {(int)response.StatusCode} на {url.Split('?')[0]}");
            }

            return response;
        }

        public void Dispose() => Http.Dispose();
    }

    private sealed record MailSearch(List<MailSummary>? Messages);

    private sealed record MailSummary(string ID);

    private sealed record MailMessage(string? Text);
}
