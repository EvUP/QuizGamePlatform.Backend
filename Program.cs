using Microsoft.Extensions.Options;
using QuizGamePlatform.Backend.Application.Options;
using QuizGamePlatform.Backend.Api.Handlers;
using QuizGamePlatform.Backend.Application.Extensions;
using QuizGamePlatform.Backend.DataAccess.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationDbContext(builder.Configuration);
builder.Services.AddRedisCache(builder.Configuration);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddApiAuthentication(builder.Configuration);
builder.Services.AddApiSwagger();

builder.AddAppServices();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();
var keycloak = app.Services.GetRequiredService<IOptions<KeycloakOptions>>().Value;

await app.MigrateAndSeedIfNeededAsync();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Quiz API v1");
        options.OAuthClientId(keycloak.ClientId);
        options.OAuthAppName("Quest Game - Swagger");
        options.OAuthUsePkce();
    });
    app.UseCors();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
