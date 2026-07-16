
using QuizGamePlatform.Backend.Application.Abstractions;
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
            builder.Services.AddScoped<IRoomService, RoomService>();
            builder.Services.AddScoped<IRoomRepository, RoomRepository>();
            builder.Services.AddSingleton<IRoomHelper, RoomHelper>();

            builder.Services.AddScoped<IQuizContentService, QuizContentService>();
            builder.Services.AddScoped<IQuizContentRepository, QuizContentRepository>();

            builder.Services.AddScoped<IPlayerService, PlayerService>();
            builder.Services.AddScoped<IPlayerRepository, PlayerRepository>();

            builder.Services.AddScoped<IRoomParticipationRepository, RoomPatricipationRepository>();

            builder.Services.AddScoped<IMatchService, MatchService>();
            builder.Services.AddScoped<IMatchRepository, MatchRepository>();

            return builder;
        }
    }
}

