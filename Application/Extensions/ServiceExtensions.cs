using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using QuizGamePlatform.Backend.Api.Hubs;
using QuizGamePlatform.Backend.Api.Swagger;
using QuizGamePlatform.Backend.Application.Auth;
using QuizGamePlatform.Backend.Application.Options;
using QuizGamePlatform.Backend.Application.Services;
using QuizGamePlatform.Backend.DataAccess;
using StackExchange.Redis;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Text;
using System.Text.Json.Serialization;

namespace QuizGamePlatform.Backend.Application.Extensions
{
    public static class ServiceExtensions
    {
        private static string? GetConnection(string connectionName, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString(connectionName)
               ?? throw new InvalidOperationException($"Connection string {connectionName} not found.");

            return connectionString;
        }

        public static IServiceCollection AddApplicationDbContext(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var connectionString = GetConnection("DefaultConnection", configuration);

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseNpgsql(connectionString, npgsqlOptions =>
                {
                    npgsqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorCodesToAdd: null);
                });
            });

            return services;
        }

        public static IServiceCollection AddRedisCache(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var connectionString = GetConnection("Redis", configuration);

            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = connectionString;
                options.InstanceName = "quiz-platform-redis";
            });

            return services;
        }

        /// <summary>Проверка JWT Keycloak и гостей</summary>
        public static IServiceCollection AddApiAuthentication(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddOptions<KeycloakOptions>()
                .Bind(configuration.GetSection(KeycloakOptions.SectionName))
                .ValidateDataAnnotations()
                .Validate(options => Uri.TryCreate(options.Authority, UriKind.Absolute, out var uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps),
                    "Keycloak:Authority must be an absolute HTTP or HTTPS URI.")
                .ValidateOnStart();

            services.AddOptions<GuestJwtOptions>()
                .Bind(configuration.GetSection(GuestJwtOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services
                .AddAuthentication(AuthenticationSchemes.Selector)
                .AddPolicyScheme(AuthenticationSchemes.Selector, AuthenticationSchemes.Selector, _ => { })
                .AddJwtBearer()
                .AddJwtBearer(AuthenticationSchemes.Guest);

            services.AddOptions<PolicySchemeOptions>(AuthenticationSchemes.Selector)
                .Configure<IOptions<GuestJwtOptions>>((options, guestOptions) =>
                {
                    var guestIssuer = guestOptions.Value.Issuer;

                    options.ForwardDefaultSelector = context =>
                        GuestTokenReader.IsGuestToken(GetAuthorization(context.Request), guestIssuer)
                            ? AuthenticationSchemes.Guest
                            : JwtBearerDefaults.AuthenticationScheme;
                });

            services.AddOptions<JwtBearerOptions>(AuthenticationSchemes.Guest)
                .Configure<IOptions<GuestJwtOptions>>((options, guestOptions) =>
                {
                    var guest = guestOptions.Value;
                    options.MapInboundClaims = false;
                    options.Events = new JwtBearerEvents { OnMessageReceived = ReadHubToken };

                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = guest.Issuer,
                        ValidateAudience = true,
                        ValidAudience = guest.Audience,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(guest.SigningKey)),

                        // Только HS256.
                        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],

                        ClockSkew = TimeSpan.FromSeconds(guest.ClockSkewSeconds),
                        NameClaimType = GuestTokenService.NicknameClaim,

                        // Для различения типов пользователей.
                        AuthenticationType = AuthenticationSchemes.Guest,
                    };
                });

            services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
                .Configure<IOptions<KeycloakOptions>>((options, keycloakOptions) =>
                {
                    var keycloak = keycloakOptions.Value;
                    options.Authority = keycloak.Authority;
                    options.Audience = keycloak.Audience;
                    options.RequireHttpsMetadata = keycloak.RequireHttpsMetadata;

                    // Сохраняем имена полей токена, включая sub
                    options.MapInboundClaims = false;
                    options.Events = new JwtBearerEvents { OnMessageReceived = ReadHubToken };

                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = keycloak.Authority,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,

                        // Только RS256
                        ValidAlgorithms = [SecurityAlgorithms.RsaSha256],

                        ClockSkew = TimeSpan.FromSeconds(keycloak.ClockSkewSeconds),
                        NameClaimType = "preferred_username",
                        AuthenticationType = JwtBearerDefaults.AuthenticationScheme,
                    };
                });

            services.AddAuthorization(options =>
                options.AddPolicy(AuthorizationPolicies.Registered, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireAssertion(context =>
                        context.User.Identity?.AuthenticationType == JwtBearerDefaults.AuthenticationScheme)));

            return services;
        }

        // браузер не передаёт заголовки в WebSocket, поэтому токен хаба приходит в query
        private static string? GetHubToken(HttpRequest request)
            => request.Path.StartsWithSegments(GameHub.Path)
                ? request.Query["access_token"].ToString()
                : null;

        private static string GetAuthorization(HttpRequest request)
            => GetHubToken(request) is { Length: > 0 } token
                ? $"Bearer {token}"
                : request.Headers.Authorization.ToString();

        private static Task ReadHubToken(MessageReceivedContext context)
        {
            if (GetHubToken(context.Request) is { Length: > 0 } token)
            {
                context.Token = token;
            }

            return Task.CompletedTask;
        }

        // события игры, через Redis доходят до клиентов на любом инстансе
        public static IServiceCollection AddGameHub(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddSignalR()
                // статусы строками, как в REST
                .AddJsonProtocol(options =>
                    options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
                .AddStackExchangeRedis(GetConnection("Redis", configuration)!, options =>
                    options.Configuration.ChannelPrefix = RedisChannel.Literal("quiz-platform-signalr"));

            return services;
        }

        public static IServiceCollection AddApiSwagger(
            this IServiceCollection services)
        {
            services.AddSwaggerGen();
            services.AddOptions<SwaggerGenOptions>()
                .Configure<IOptions<KeycloakOptions>>((options, keycloakOptions) =>
                {
                    var keycloak = keycloakOptions.Value;
                    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Quiz Game API", Version = "v1" });

                    options.AddSecurityDefinition("Keycloak", new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.OAuth2,
                        Flows = new OpenApiOAuthFlows
                        {
                            AuthorizationCode = new OpenApiOAuthFlow
                            {
                                AuthorizationUrl = new Uri(keycloak.AuthorizationUrl),
                                TokenUrl = new Uri(keycloak.TokenUrl),
                                Scopes = new Dictionary<string, string>
                                {
                                    { "openid", "OpenID Connect Scope" },
                                    { "profile", "User Profile Scope" },
                                },
                            },
                        },
                    });

                    options.AddSecurityDefinition(AuthenticationSchemes.Guest, new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.Http,
                        Scheme = "bearer",
                        BearerFormat = "JWT",
                        Description = "Гостевой токен, выданный при входе в комнату.",
                    });

                    options.OperationFilter<AuthorizeOperationFilter>();
                });

            return services;
        }
    }
}
