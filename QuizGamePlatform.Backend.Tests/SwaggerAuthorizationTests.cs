using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using QuizGamePlatform.Backend.Api.Controllers;
using QuizGamePlatform.Backend.Application.Extensions;
using Swashbuckle.AspNetCore.Swagger;

namespace QuizGamePlatform.Backend.Tests;

public class SwaggerAuthorizationTests
{
    [Fact]
    public async Task ApiDocument_RequiresAuthenticationOnlyForMe()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
        builder.Services.AddApiAuthentication(KeycloakTestConfiguration.Create());
        builder.Services.AddApiSwagger();
        await using var app = builder.Build();
        app.MapControllers();

        var document = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        Assert.Empty(document.SecurityRequirements);
        var protectedOperations = document.Paths
            .SelectMany(path => path.Value.Operations.Values.Select(operation => (path.Key, operation)))
            .Where(item => item.operation.Security.Count > 0)
            .ToList();
        // /join работает с токеном или без него.
        Assert.Equal(
            new[] { "/api/Auth/me", "/api/Room/join" },
            protectedOperations.Select(item => item.Key).Order().ToArray());
        var protectedOperation = protectedOperations.Single(item => item.Key == "/api/Auth/me");
        // Вход через Keycloak или с гостевым токеном.
        Assert.Equal(
            new[] { "Keycloak", "GuestBearer" },
            protectedOperation.operation.Security
                .Select(requirement => Assert.Single(requirement.Keys).Reference.Id)
                .ToArray());
        Assert.Equal(
            $"{KeycloakTestConfiguration.Issuer}/protocol/openid-connect/auth",
            document.Components.SecuritySchemes["Keycloak"].Flows.AuthorizationCode.AuthorizationUrl.AbsoluteUri);
        Assert.Equal("bearer", document.Components.SecuritySchemes["GuestBearer"].Scheme);
        var joinSecurity = document.Paths["/api/Room/join"].Operations.Values.Single().Security;
        Assert.Single(joinSecurity, requirement => requirement.Count == 0);
        Assert.Equal(
            new[] { "Keycloak", "GuestBearer" },
            joinSecurity.SelectMany(requirement => requirement.Keys).Select(scheme => scheme.Reference.Id));
    }

    [Fact]
    public async Task ControllerAuthorization_IsInherited_AndAllowAnonymousOverridesIt()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllers().AddApplicationPart(typeof(SwaggerProtectedController).Assembly);
        builder.Services.AddApiAuthentication(KeycloakTestConfiguration.Create());
        builder.Services.AddApiSwagger();
        await using var app = builder.Build();
        app.MapControllers();

        var document = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        Assert.Equal(2, document.Paths["/swagger-test/protected"].Operations.Values.Single().Security.Count);
        Assert.Empty(document.Paths["/swagger-test/public"].Operations.Values.Single().Security);
    }
}

[ApiController]
[Route("swagger-test")]
[Authorize]
public class SwaggerProtectedController : ControllerBase
{
    [HttpGet("protected")]
    public IActionResult Protected() => Ok();

    [HttpGet("public")]
    [AllowAnonymous]
    public IActionResult Public() => Ok();
}
