
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

            return builder;
        }
    }
}

