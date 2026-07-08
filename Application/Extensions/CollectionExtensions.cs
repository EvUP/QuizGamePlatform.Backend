
using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.Services;
using QuizGamePlatform.Backend.Core.Abstractions;
using QuizGamePlatform.Backend.DataAccess.Repositories;

namespace QuizGamePlatform.Backend.Application.Extensions
{
    public static class CollectionExtensions
    {
        public static WebApplicationBuilder AddAppServices(this WebApplicationBuilder builder)
        {
            builder.Services.AddScoped<IRoomService, RoomService>();
            builder.Services.AddScoped<IRoomRepository, RoomRepository>();
            builder.Services.AddScoped<IQuizContentService, QuizContentService>();
            builder.Services.AddScoped<IQuizContentRepository, QuizContentRepository>();

            return builder;
        }
    }
}

