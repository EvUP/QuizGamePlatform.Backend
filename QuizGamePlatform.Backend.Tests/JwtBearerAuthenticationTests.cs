using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using QuizGamePlatform.Backend.Api.Controllers;
using QuizGamePlatform.Backend.Application.Contracts;
using QuizGamePlatform.Backend.Application.Extensions;
using QuizGamePlatform.Backend.Application.Services;

namespace QuizGamePlatform.Backend.Tests;

public class JwtBearerAuthenticationTests : IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RSA _otherRsa = RSA.Create(2048);
    private readonly RsaSecurityKey _key;
    private readonly ServiceProvider _provider;

    public JwtBearerAuthenticationTests()
    {
        _key = new RsaSecurityKey(_rsa) { KeyId = "test-key" };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApiAuthentication(KeycloakTestConfiguration.Create());
        // Настоящий JwtBearer, тестовые метаданные и ключи.
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Configuration = new OpenIdConnectConfiguration
            {
                Issuer = KeycloakTestConfiguration.Issuer,
            };
            options.Configuration.SigningKeys.Add(_key);
        });
        _provider = services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("valid", null)]
    [InlineData("expired-within-skew", null)]
    [InlineData("wrong-signature", typeof(SecurityTokenInvalidSignatureException))]
    [InlineData("wrong-issuer", typeof(SecurityTokenInvalidIssuerException))]
    [InlineData("wrong-audience", typeof(SecurityTokenInvalidAudienceException))]
    [InlineData("expired", typeof(SecurityTokenExpiredException))]
    [InlineData("future", typeof(SecurityTokenNotYetValidException))]
    [InlineData("missing-expiration", typeof(SecurityTokenNoExpirationException))]
    [InlineData("unsigned", typeof(SecurityTokenInvalidSignatureException))]
    public async Task Bearer_ValidatesRealTokens(string scenario, Type? failureType)
    {
        using var scope = _provider.CreateScope();
        var context = CreateContext(scope.ServiceProvider, CreateToken(scenario));

        var result = await context.AuthenticateAsync();

        if (failureType is not null)
        {
            Assert.False(result.Succeeded);
            Assert.IsType(failureType, result.Failure);
            await context.ChallengeAsync();
            Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
            return;
        }

        Assert.True(result.Succeeded, result.Failure?.ToString());
        context.User = result.Principal!;
        var currentUser = new CurrentUserContext(new HttpContextAccessor { HttpContext = context });
        Assert.Equal("test-subject", currentUser.KeycloakSubject);
        Assert.Equal("test-player", currentUser.DisplayName);
        Assert.Equal("test@example.test", currentUser.Email);
        Assert.Equal("test-player", context.User.Identity!.Name);
    }

    [Fact]
    public async Task MissingToken_ChallengesWith401()
    {
        using var scope = _provider.CreateScope();
        var context = CreateContext(scope.ServiceProvider);

        var result = await context.AuthenticateAsync();
        await context.ChallengeAsync();

        Assert.True(result.None);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task SignedTokenWithoutSub_PassesDefaultPolicy_ButMeRejectsIt()
    {
        using var scope = _provider.CreateScope();
        var context = CreateContext(scope.ServiceProvider, CreateToken("missing-sub"));
        var authentication = await context.AuthenticateAsync();
        Assert.True(authentication.Succeeded);
        context.User = authentication.Principal!;
        var policy = await scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>()
            .GetDefaultPolicyAsync();
        var authorization = await scope.ServiceProvider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(context.User, null, policy);
        Assert.True(authorization.Succeeded);
        var controller = new AuthController(new CurrentUserContext(new HttpContextAccessor { HttpContext = context }))
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };

        var response = controller.GetCurrentUser();

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(response.Result);
        Assert.IsType<CommonErrorResponse>(unauthorized.Value);
    }

    private static DefaultHttpContext CreateContext(IServiceProvider services, string? token = null)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = "GET";
        context.Request.Path = "/api/Auth/me";
        if (token is not null)
        {
            context.Request.Headers.Authorization = $"Bearer {token}";
        }
        return context;
    }

    private string CreateToken(string scenario)
    {
        var now = DateTime.UtcNow;
        var claims = new Dictionary<string, object>
        {
            ["preferred_username"] = "test-player",
            ["email"] = "test@example.test",
        };
        if (scenario != "missing-sub")
        {
            claims["sub"] = "test-subject";
        }
        var signingKey = scenario == "wrong-signature"
            ? new RsaSecurityKey(_otherRsa) { KeyId = _key.KeyId }
            : _key;
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = scenario == "wrong-issuer" ? "https://other.example.test/realms/other" : KeycloakTestConfiguration.Issuer,
            Audience = scenario == "wrong-audience" ? "another-api" : KeycloakTestConfiguration.Audience,
            Claims = claims,
            IssuedAt = now.AddMinutes(-5),
            NotBefore = scenario == "future" ? now.AddMinutes(5) : now.AddMinutes(-5),
            Expires = scenario switch
            {
                "expired" => now.AddMinutes(-2),
                "expired-within-skew" => now.AddSeconds(-5),
                "missing-expiration" => null,
                _ => now.AddMinutes(10),
            },
            SigningCredentials = scenario == "unsigned" ? null : new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256),
        });
    }

    public void Dispose()
    {
        _provider.Dispose();
        _rsa.Dispose();
        _otherRsa.Dispose();
    }
}
