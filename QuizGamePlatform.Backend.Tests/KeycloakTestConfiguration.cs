using Microsoft.Extensions.Configuration;

namespace QuizGamePlatform.Backend.Tests;

internal static class KeycloakTestConfiguration
{
    // Тестовый издатель, без сетевых обращений
    public const string Issuer = "https://auth.example.test/realms/quizgame";
    public const string Audience = "quiz-backend";
    public const string GuestIssuer = "quiz-api";
    public const string GuestAudience = "quiz-guest";
    public const string GuestSigningKey = "test-guest-signing-key-at-least-32-characters";

    public static IConfiguration Create(string? key = null, string? value = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Keycloak:Authority"] = Issuer,
            ["Keycloak:Audience"] = Audience,
            ["Keycloak:ClientId"] = "swagger-client",
            ["Keycloak:RequireHttpsMetadata"] = "false",
            ["Keycloak:ClockSkewSeconds"] = "30",
            ["GuestJwt:Issuer"] = GuestIssuer,
            ["GuestJwt:Audience"] = GuestAudience,
            ["GuestJwt:SigningKey"] = GuestSigningKey,
            ["GuestJwt:LifetimeMinutes"] = "240",
            ["GuestJwt:ClockSkewSeconds"] = "30",
        };
        if (key is not null)
        {
            values[$"Keycloak:{key}"] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
