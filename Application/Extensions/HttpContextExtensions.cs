namespace QuizGamePlatform.Backend.Core.Extensions;

public static class HttpContextExtensions
{
    public static string GetMethodWithPath(this HttpContext context)
    {
        if (context == null) return "Unknown";
        return $"{context.Request.Method} {context.Request.Path}";
    }
}