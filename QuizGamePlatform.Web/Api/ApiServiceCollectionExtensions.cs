namespace QuizGamePlatform.Web.Api;

public sealed class ApiOptions
{
    public const string SectionName = "Api";

    /// <summary>Адрес бэка, например http://localhost:53507/</summary>
    public string BaseUrl { get; set; } = "";
}

/// <summary>Адрес бэка (с завершающим «/») для HTTP-клиентов и хаба</summary>
public sealed record ApiEndpoint(Uri BaseAddress);

public static class ApiServiceCollectionExtensions
{
    /// <summary>Типизированные HTTP-клиенты API (IHttpClientFactory)</summary>
    public static IServiceCollection AddApiClients(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(ApiOptions.SectionName).Get<ApiOptions>();

        if (!Uri.TryCreate(options?.BaseUrl, UriKind.Absolute, out var baseAddress))
        {
            throw new InvalidOperationException("В wwwroot/appsettings.json не задан абсолютный адрес Api:BaseUrl");
        }

        // без завершающего «/» относительные пути отрезают последний сегмент адреса
        if (!baseAddress.AbsoluteUri.EndsWith('/'))
        {
            baseAddress = new Uri(baseAddress.AbsoluteUri + "/");
        }

        void Configure(HttpClient client) => client.BaseAddress = baseAddress;

        services.AddSingleton(new ApiEndpoint(baseAddress));
        services.AddScoped<ApiAccessTokenSource>();
        services.AddTransient<ApiAuthorizationHandler>();

        // с токеном аккаунта или гостя
        services.AddHttpClient<RoomApiClient>(Configure).AddHttpMessageHandler<ApiAuthorizationHandler>();
        services.AddHttpClient<MatchApiClient>(Configure).AddHttpMessageHandler<ApiAuthorizationHandler>();
        services.AddHttpClient<AuthApiClient>(Configure).AddHttpMessageHandler<ApiAuthorizationHandler>();

        // публичные
        services.AddHttpClient<LeaderboardApiClient>(Configure);
        services.AddHttpClient<CategoriesApiClient>(Configure);

        return services;
    }
}
