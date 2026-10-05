namespace QuizGamePlatform.Backend.Api.Swagger
{
    /// <summary>Swagger отправляет токен, если он указан</summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public sealed class OptionalAuthorizationAttribute : Attribute;
}
