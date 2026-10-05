using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;
using QuizGamePlatform.Backend.Application.Extensions;
using QuizGamePlatform.Backend.Application.Options;
using QuizGamePlatform.Backend.Application.Services;
using QuizGamePlatform.Backend.Application.Auth;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;

namespace QuizGamePlatform.Backend.Tests;

public class GuestAuthenticationTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _keycloakKey;
    private readonly GuestTokenService _sut;
    private readonly ServiceProvider _provider;

    public GuestAuthenticationTests()
    {
        _keycloakKey = new RsaSecurityKey(_rsa) { KeyId = "keycloak-test-key" };

        _sut = CreateTokenService(_time);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApiAuthentication(KeycloakTestConfiguration.Create());

        // Метаданные и ключи в памяти.
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Configuration = new OpenIdConnectConfiguration
            {
                Issuer = KeycloakTestConfiguration.Issuer,
            };
            options.Configuration.SigningKeys.Add(_keycloakKey);
        });

        _provider = services.BuildServiceProvider();
    }

    private string CreateKeycloakToken() =>
        new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(
            new SecurityTokenDescriptor
            {
                Issuer = KeycloakTestConfiguration.Issuer,
                Audience = KeycloakTestConfiguration.Audience,
                Claims = new Dictionary<string, object>
                {
                    ["sub"] = "registered-subject",
                    ["preferred_username"] = "registered_player",
                },
                IssuedAt = DateTime.UtcNow.AddMinutes(-1),
                NotBefore = DateTime.UtcNow.AddMinutes(-1),
                Expires = DateTime.UtcNow.AddMinutes(10),
                SigningCredentials = new SigningCredentials(_keycloakKey, SecurityAlgorithms.RsaSha256),
            });

    private static HttpContext ContextWithToken(string? token)
    {
        var context = new DefaultHttpContext();

        if (token is not null)
        {
            context.Request.Headers.Authorization = $"Bearer {token}";
        }

        return context;
    }

    private Task<AuthenticateResult> AuthenticateAsync(string token) =>
        AuthenticateWith(AuthenticationSchemes.Guest, token);

    [Fact]
    public void Issue_PutsRoomPlayerAndRoomIntoToken()
    {
        var roomPlayerLinkId = Guid.NewGuid();
        var roomId = Guid.NewGuid();

        var result = _sut.Issue(roomPlayerLinkId, roomId, "guest_one");

        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken);
        Assert.Equal(KeycloakTestConfiguration.GuestIssuer, token.Issuer);
        Assert.Equal(roomPlayerLinkId.ToString(), token.Claims.First(c => c.Type == "sub").Value);
        Assert.Equal(roomId.ToString(), token.Claims.First(c => c.Type == GuestTokenService.RoomIdClaim).Value);
        Assert.Equal("guest_one", token.Claims.First(c => c.Type == GuestTokenService.NicknameClaim).Value);
        Assert.Equal(_time.GetUtcNow().UtcDateTime.AddMinutes(240), result.ExpiresAtUtc);
    }

    private static GuestTokenService CreateTokenService(TimeProvider time, string? signingKey = null) =>
        new(
            Options.Create(new GuestJwtOptions
            {
                Issuer = KeycloakTestConfiguration.GuestIssuer,
                Audience = KeycloakTestConfiguration.GuestAudience,
                SigningKey = signingKey ?? KeycloakTestConfiguration.GuestSigningKey,
                LifetimeMinutes = 240,
                ClockSkewSeconds = 30,
            }),
            time);

    [Fact]
    public async Task ValidGuestToken_IsAccepted()
    {
        var roomPlayerLinkId = Guid.NewGuid();
        var token = CreateTokenService(TimeProvider.System).Issue(roomPlayerLinkId, Guid.NewGuid(), "guest_one").AccessToken;

        var result = await AuthenticateAsync(token);

        Assert.True(result.Succeeded);
        Assert.Equal(roomPlayerLinkId.ToString(), result.Principal!.FindFirstValue("sub"));
    }

    [Fact]
    public async Task TokenSignedWithOtherKey_IsRejected()
    {
        var foreign = CreateTokenService(
            TimeProvider.System, "completely-different-signing-key-32-chars");

        var result = await AuthenticateAsync(foreign.Issue(Guid.NewGuid(), Guid.NewGuid(), "spoofed").AccessToken);

        Assert.False(result.Succeeded);
        Assert.IsType<SecurityTokenSignatureKeyNotFoundException>(result.Failure);
    }

    [Fact]
    public async Task UnsignedToken_IsRejected()
    {
        var unsigned = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: KeycloakTestConfiguration.GuestIssuer,
            audience: KeycloakTestConfiguration.GuestAudience,
            claims: [new Claim("sub", Guid.NewGuid().ToString())],
            expires: DateTime.UtcNow.AddHours(1)));

        var result = await AuthenticateAsync(unsigned);

        Assert.False(result.Succeeded);
    }

    private static GuestTokenService ServiceIssuingExpiredBy(TimeSpan overdue) =>
        CreateTokenService(new FakeTimeProvider(DateTimeOffset.UtcNow - TimeSpan.FromMinutes(240) - overdue));

    [Fact]
    public async Task ExpiredGuestToken_IsRejectedBeyondSkew()
    {
        var token = ServiceIssuingExpiredBy(TimeSpan.FromMinutes(2))
            .Issue(Guid.NewGuid(), Guid.NewGuid(), "guest_one").AccessToken;

        var result = await AuthenticateAsync(token);

        Assert.False(result.Succeeded);
        Assert.IsType<SecurityTokenExpiredException>(result.Failure);
    }

    [Fact]
    public async Task ExpiredGuestToken_IsStillAcceptedWithinSkew()
    {
        // Токен истёк недавно и ещё принимается с учётом допуска.
        var token = ServiceIssuingExpiredBy(TimeSpan.FromSeconds(10))
            .Issue(Guid.NewGuid(), Guid.NewGuid(), "guest_one").AccessToken;

        var result = await AuthenticateAsync(token);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer not-a-jwt")]
    [InlineData("что-то совсем другое")]
    public void Selector_IgnoresBrokenHeaders(string? header) =>
        Assert.False(GuestTokenReader.IsGuestToken(header, KeycloakTestConfiguration.GuestIssuer));

    [Fact]
    public void Selector_RoutesByIssuer()
    {
        var guestToken = CreateTokenService(TimeProvider.System).Issue(Guid.NewGuid(), Guid.NewGuid(), "guest_one").AccessToken;

        Assert.True(GuestTokenReader.IsGuestToken(
            $"Bearer {guestToken}", KeycloakTestConfiguration.GuestIssuer));

        Assert.False(GuestTokenReader.IsGuestToken(
            $"Bearer {guestToken}", KeycloakTestConfiguration.Issuer));
    }

    [Fact]
    public async Task EachSchemeAcceptsItsOwnTokenAndRejectsTheOther()
    {
        var guestToken = CreateTokenService(TimeProvider.System).Issue(Guid.NewGuid(), Guid.NewGuid(), "guest_one").AccessToken;
        var keycloakToken = CreateKeycloakToken();

        Assert.True((await AuthenticateWith(AuthenticationSchemes.Guest, guestToken)).Succeeded);
        Assert.True((await AuthenticateWith(JwtBearerDefaults.AuthenticationScheme, keycloakToken)).Succeeded);

        var guestInKeycloak = await AuthenticateWith(JwtBearerDefaults.AuthenticationScheme, guestToken);
        var keycloakInGuest = await AuthenticateWith(AuthenticationSchemes.Guest, keycloakToken);
        Assert.False(guestInKeycloak.Succeeded, "гостевой токен принят схемой Keycloak");
        Assert.False(keycloakInGuest.Succeeded, "токен Keycloak принят гостевой схемой");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Selector_RoutesTokenToTheRightScheme(bool guest)
    {
        var token = guest
            ? CreateTokenService(TimeProvider.System).Issue(Guid.NewGuid(), Guid.NewGuid(), "guest_one").AccessToken
            : CreateKeycloakToken();

        using var scope = _provider.CreateScope();
        var context = ContextWithToken(token);
        context.RequestServices = scope.ServiceProvider;

        var result = await context.AuthenticateAsync();

        Assert.True(result.Succeeded, result.Failure?.ToString());

        var currentUser = new CurrentUserContext(new HttpContextAccessor { HttpContext = ContextWithUser(result) });
        Assert.Equal(guest, currentUser.IsGuest);
        Assert.Equal(!guest, currentUser.IsRegistered);
    }

    private async Task<AuthenticateResult> AuthenticateWith(string scheme, string token)
    {
        using var scope = _provider.CreateScope();
        var context = ContextWithToken(token);
        context.RequestServices = scope.ServiceProvider;

        return await context.AuthenticateAsync(scheme);
    }

    private static HttpContext ContextWithUser(AuthenticateResult result) =>
        new DefaultHttpContext { User = result.Principal! };

    [Fact]
    public void CurrentUserContext_ReadsGuestClaims()
    {
        var roomPlayerLinkId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        var identity = new ClaimsIdentity(
            [
                new Claim("sub", roomPlayerLinkId.ToString()),
                new Claim(GuestTokenService.RoomIdClaim, roomId.ToString()),
                new Claim(GuestTokenService.NicknameClaim, "guest_one"),
            ],
            authenticationType: AuthenticationSchemes.Guest);

        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
        var sut = new CurrentUserContext(accessor);

        Assert.True(sut.IsAuthenticated);
        Assert.True(sut.IsGuest);
        Assert.False(sut.IsRegistered);
        Assert.Equal(roomPlayerLinkId, sut.RoomPlayerLinkId);
        Assert.Equal(roomId, sut.RoomId);
        Assert.Equal("guest_one", sut.Nickname);
        Assert.Null(sut.KeycloakSubject);
    }

    [Fact]
    public void CurrentUserContext_DoesNotTreatRegisteredUserAsGuest()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim("sub", Guid.NewGuid().ToString()),
                new Claim("preferred_username", "player_one"),
            ],
            authenticationType: JwtBearerDefaults.AuthenticationScheme);

        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
        var sut = new CurrentUserContext(accessor);

        Assert.True(sut.IsRegistered);
        Assert.False(sut.IsGuest);
        Assert.Null(sut.RoomPlayerLinkId);
        Assert.Null(sut.RoomId);
        Assert.NotNull(sut.KeycloakSubject);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _rsa.Dispose();
    }
}
