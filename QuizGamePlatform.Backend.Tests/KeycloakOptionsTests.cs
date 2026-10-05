using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuizGamePlatform.Backend.Application.Extensions;

namespace QuizGamePlatform.Backend.Tests;

public class KeycloakOptionsTests
{
    [Theory]
    [InlineData("Authority", "")]
    [InlineData("Authority", "not-a-uri")]
    [InlineData("Authority", "/realms/quizgame")]
    [InlineData("Authority", "ftp://auth.example.test/realms/quizgame")]
    [InlineData("Audience", "   ")]
    [InlineData("ClientId", "")]
    [InlineData("ClockSkewSeconds", "-1")]
    public async Task InvalidConfiguration_FailsOnHostStart(string key, string value)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddApiAuthentication(KeycloakTestConfiguration.Create(key, value));
        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(key, exception.Message);
    }

    [Theory]
    [InlineData("http://auth.example.test/realms/quizgame", "0")]
    [InlineData("https://auth.example.test/realms/quizgame", "30")]
    public async Task ValidConfiguration_Starts(string authority, string skew)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        var configuration = KeycloakTestConfiguration.Create("Authority", authority);
        configuration["Keycloak:ClockSkewSeconds"] = skew;
        builder.Services.AddApiAuthentication(configuration);
        using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();
    }

    [Theory]
    [InlineData("Issuer", "")]
    [InlineData("Audience", " ")]
    [InlineData("SigningKey", "too-short")]
    [InlineData("LifetimeMinutes", "0")]
    [InlineData("ClockSkewSeconds", "-1")]
    public async Task InvalidGuestConfiguration_FailsOnHostStart(string key, string value)
    {
        var configuration = KeycloakTestConfiguration.Create();
        configuration[$"GuestJwt:{key}"] = value;
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddApiAuthentication(configuration);
        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(key, exception.Message);
    }
}
