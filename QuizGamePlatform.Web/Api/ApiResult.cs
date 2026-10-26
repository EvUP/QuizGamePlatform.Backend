using System.Diagnostics.CodeAnalysis;

namespace QuizGamePlatform.Web.Api;

/// <summary>Ошибка запроса с текстом для пользователя. StatusCode 0: до сервера не достучались.</summary>
public sealed record ApiError(int StatusCode, string Message)
{
    public bool IsNetworkError => StatusCode == 0;
}

/// <summary>Результат запроса без тела ответа (204, пустой 200)</summary>
public class ApiResult
{
    protected ApiResult(ApiError? error) => Error = error;

    public ApiError? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    public static ApiResult Success() => new(null);

    public static ApiResult Failure(ApiError error) => new(error);
}

/// <summary>Результат запроса с телом ответа</summary>
public sealed class ApiResult<T> : ApiResult
{
    private ApiResult(T? value, ApiError? error) : base(error) => Value = value;

    public T? Value { get; }

    // переобъявлено, чтобы анализатор null знал связь IsSuccess и Error в наследнике
    public new ApiError? Error => base.Error;

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public new bool IsSuccess => Error is null;

    public static ApiResult<T> Success(T value) => new(value, null);

    public static new ApiResult<T> Failure(ApiError error) => new(default, error);
}
