using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using QuizGamePlatform.Web;
using QuizGamePlatform.Web.Api;
using QuizGamePlatform.Web.Infrastructure;
using QuizGamePlatform.Web.Realtime;
using QuizGamePlatform.Web.State;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Аккаунт: Keycloak, authorization code + PKCE (PKCE библиотека включает сама)
builder.Services.AddOidcAuthentication(options =>
{
    builder.Configuration.Bind("Oidc", options.ProviderOptions);
    options.ProviderOptions.ResponseType = "code";
    options.ProviderOptions.DefaultScopes.Add("email");

    // в игре аккаунт виден под логином — так же показываем его в шапке
    options.UserOptions.NameClaim = "preferred_username";
});
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddSingleton<GuestSession>();
builder.Services.AddSingleton<ServerClock>();
builder.Services.AddApiClients(builder.Configuration);

// в WebAssembly scoped живёт всё время работы приложения: одно соединение с хабом и одна открытая комната
builder.Services.AddScoped<GameHubClient>();
builder.Services.AddScoped<GameSession>();

var host = builder.Build();

// гостевой токен из localStorage нужен до первого запроса
await host.Services.GetRequiredService<GuestSession>().LoadAsync();

await host.RunAsync();
