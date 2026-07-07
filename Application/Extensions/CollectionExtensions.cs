
using testBdControllers.Application.Abstractions;
using testBdControllers.Application.Services;
using testBdControllers.Core.Abstractions;
using testBdControllers.DataAccess.Repositories;

namespace testBdControllers.Application.Extensions
{
    public static class CollectionExtensions
    {
        public static WebApplicationBuilder AddAppServices(this WebApplicationBuilder builder)
        {
            builder.Services.AddScoped<IUserService, UserService>();
            builder.Services.AddScoped<IUserRepository, UserRepository>();

            builder.Services.AddScoped<IRoomService, RoomService>();
            builder.Services.AddScoped<IRoomRepository, RoomRepository>();
            builder.Services.AddScoped<IQuizContentService, QuizContentService>();
            builder.Services.AddScoped<IQuizContentRepository, QuizContentRepository>();

            return builder;
        }
    }
}

