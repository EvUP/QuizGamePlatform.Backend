using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using QuizGamePlatform.Web.Api.Contracts;

namespace QuizGamePlatform.Web.Api;

/// <summary>
/// Общая отправка запросов: успех → тело ответа, ошибка → ApiError с текстом для пользователя.
/// Исключения наружу не летят, кроме отмены (OperationCanceledException).
/// </summary>
public abstract class ApiClientBase(HttpClient http)
{
    protected Task<ApiResult<TResponse>> GetAsync<TResponse>(
        string uri, JsonTypeInfo<TResponse> responseType, CancellationToken ct)
        => SendAsync(new HttpRequestMessage(HttpMethod.Get, uri), responseType, ct);

    protected Task<ApiResult<TResponse>> PostAsync<TResponse>(
        string uri, JsonTypeInfo<TResponse> responseType, CancellationToken ct)
        => SendAsync(new HttpRequestMessage(HttpMethod.Post, uri), responseType, ct);

    protected Task<ApiResult<TResponse>> PostAsync<TBody, TResponse>(
        string uri, TBody body, JsonTypeInfo<TBody> bodyType, JsonTypeInfo<TResponse> responseType, CancellationToken ct)
        => SendAsync(WithBody(HttpMethod.Post, uri, body, bodyType), responseType, ct);

    protected Task<ApiResult> PostAsync<TBody>(
        string uri, TBody body, JsonTypeInfo<TBody> bodyType, CancellationToken ct)
        => SendAsync(WithBody(HttpMethod.Post, uri, body, bodyType), ct);

    protected Task<ApiResult> PutAsync<TBody>(
        string uri, TBody body, JsonTypeInfo<TBody> bodyType, CancellationToken ct)
        => SendAsync(WithBody(HttpMethod.Put, uri, body, bodyType), ct);

    protected Task<ApiResult> DeleteAsync(string uri, CancellationToken ct)
        => SendAsync(new HttpRequestMessage(HttpMethod.Delete, uri), ct);

    private static HttpRequestMessage WithBody<TBody>(
        HttpMethod method, string uri, TBody body, JsonTypeInfo<TBody> bodyType)
        => new(method, uri) { Content = JsonContent.Create(body, bodyType) };

    private async Task<ApiResult<TResponse>> SendAsync<TResponse>(
        HttpRequestMessage request, JsonTypeInfo<TResponse> responseType, CancellationToken ct)
    {
        using (request)
        {
            try
            {
                using var response = await http.SendAsync(request, ct);

                if (!response.IsSuccessStatusCode)
                {
                    return ApiResult<TResponse>.Failure(await ReadErrorAsync(response, ct));
                }

                var value = await response.Content.ReadFromJsonAsync(responseType, ct);

                return value is null
                    ? ApiResult<TResponse>.Failure(new ApiError((int)response.StatusCode, "Сервер промолчал. Попробуйте ещё раз"))
                    : ApiResult<TResponse>.Success(value);
            }
            catch (HttpRequestException)
            {
                return ApiResult<TResponse>.Failure(NetworkError);
            }
            catch (JsonException)
            {
                return ApiResult<TResponse>.Failure(new ApiError(0, "Сервер ответил что-то непонятное. Обновите страницу"));
            }
        }
    }

    private async Task<ApiResult> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using (request)
        {
            try
            {
                using var response = await http.SendAsync(request, ct);

                return response.IsSuccessStatusCode
                    ? ApiResult.Success()
                    : ApiResult.Failure(await ReadErrorAsync(response, ct));
            }
            catch (HttpRequestException)
            {
                return ApiResult.Failure(NetworkError);
            }
        }
    }

    // В браузере сюда же попадают отказы CORS и недоступный сервер
    private static readonly ApiError NetworkError = new(0, "Нет связи с сервером. Проверьте интернет и попробуйте ещё раз");

    private static async Task<ApiError> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var status = (int)response.StatusCode;

        // 5xx отдают технический текст исключения — пользователю его не показываем
        if (status < 500 && response.Content.Headers.ContentType?.MediaType == "application/json")
        {
            try
            {
                var body = await response.Content.ReadFromJsonAsync(ApiJsonContext.Default.CommonErrorResponse, ct);

                if (!string.IsNullOrWhiteSpace(body?.Message))
                {
                    return new ApiError(status, body.Message);
                }
            }
            catch (JsonException)
            {
                // тело не в нашем формате — ниже общий текст по коду
            }
        }

        return new ApiError(status, DefaultMessage(response.StatusCode));
    }

    private static string DefaultMessage(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Unauthorized => "Вход устарел — войдите ещё раз",
        HttpStatusCode.Forbidden => "Это действие вам недоступно",
        HttpStatusCode.NotFound => "Не нашли — возможно, это уже удалили. Обновите страницу",
        HttpStatusCode.Conflict => "Кто-то успел всё поменять раньше вас. Попробуйте ещё раз",
        >= HttpStatusCode.InternalServerError => "У нас что-то сломалось. Попробуйте через минуту",
        _ => "Не получилось. Попробуйте ещё раз"
    };
}
