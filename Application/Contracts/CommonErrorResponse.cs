
namespace QuizGamePlatform.Backend.Application.Contracts;

//Обобщенный ответ ошибок, желательно для всех контроллеров
public record CommonErrorResponse(
    string Message,
    string Method,
    DateTime Timestamp = default
)
{
    public CommonErrorResponse(string message, string method)
        : this(message, method, DateTime.UtcNow)
    {}
}
