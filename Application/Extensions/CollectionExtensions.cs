
using QuizGamePlatform.Backend.Api.Hubs;
using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.BackgroundServices;
using QuizGamePlatform.Backend.Application.Options;
using QuizGamePlatform.Backend.Application.Services;
using QuizGamePlatform.Backend.Core.Abstractions;
using QuizGamePlatform.Backend.Core.Helpers;
using QuizGamePlatform.Backend.DataAccess.Repositories;

namespace QuizGamePlatform.Backend.Application.Extensions
{
    public static class CollectionExtensions
    {
        public static WebApplicationBuilder AddAppServices(this WebApplicationBuilder builder)
        {
            builder.Services.AddSingleton(TimeProvider.System);

            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
            builder.Services.AddSingleton<IGuestTokenService, GuestTokenService>();

            builder.Services.AddOptions<InviteOptions>()
                .Bind(builder.Configuration.GetSection(InviteOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            builder.Services.AddSingleton<IInviteMailer, SmtpInviteMailer>();

            builder.Services.AddScoped<IRoomService, RoomService>();
            builder.Services.AddScoped<IRoomRepository, RoomRepository>();
            builder.Services.AddSingleton<IRoomHelper, RoomHelper>();

            builder.Services.AddScoped<IQuizContentService, QuizContentService>();
            builder.Services.AddScoped<IQuizContentRepository, QuizContentRepository>();

            builder.Services.AddScoped<IPlayerService, PlayerService>();
            builder.Services.AddScoped<IUserRepository, UserRepository>();

            builder.Services.AddScoped<IRoomParticipationRepository, RoomPatricipationRepository>();

            builder.Services.AddScoped<IMatchService, MatchService>();
            builder.Services.AddScoped<IMatchRepository, MatchRepository>();
            builder.Services.AddSingleton<IMatchLockProvider, MatchLockProvider>();

            builder.Services.AddHostedService<MatchTimerService>();

            builder.Services.AddSingleton<SignalRGameNotifier>();
            builder.Services.AddSingleton<IGameNotifier>(sp => sp.GetRequiredService<SignalRGameNotifier>());
            builder.Services.AddHostedService(sp => sp.GetRequiredService<SignalRGameNotifier>());

            return builder;
        }
    }
}

