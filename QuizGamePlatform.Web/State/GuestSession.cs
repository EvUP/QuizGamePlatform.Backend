using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace QuizGamePlatform.Web.State;

/// <summary>Гостевой токен из POST /api/Room/join (живёт 240 минут)</summary>
public sealed record GuestToken(
    string AccessToken,
    DateTime ExpiresAtUtc,
    Guid RoomId,
    Guid RoomPlayerLinkId,
    string Nickname);

/// <summary>
/// Последнее гостевое участие. Singleton: HTTP-обработчики создаются в своём DI-scope,
/// а токен должен быть один на всё приложение. Хранится в localStorage, чтобы пережить перезагрузку.
/// </summary>
public sealed class GuestSession(IJSRuntime js)
{
    private const string StorageKey = "qg.guest";

    // запас, чтобы не отправить токен, который истечёт в пути
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromSeconds(30);

    private GuestToken? token;

    /// <summary>Действующий токен или null, если его нет или он истёк</summary>
    public GuestToken? Current => token is not null && token.ExpiresAtUtc - ExpirySkew > DateTime.UtcNow ? token : null;

    /// <summary>Вызывается один раз при старте приложения</summary>
    public async Task LoadAsync()
    {
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            token = json is null ? null : JsonSerializer.Deserialize(json, GuestJsonContext.Default.GuestToken);
        }
        catch (Exception ex) when (ex is JSException or JsonException)
        {
            // приватный режим или испорченная запись — просто без гостевой сессии
            token = null;
        }
    }

    public async Task SaveAsync(GuestToken value)
    {
        token = value;

        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", StorageKey, JsonSerializer.Serialize(value, GuestJsonContext.Default.GuestToken));
        }
        catch (JSException)
        {
            // хранилище недоступно — токен останется в памяти до перезагрузки
        }
    }

    public async Task ClearAsync()
    {
        token = null;

        try
        {
            await js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
        }
        catch (JSException)
        {
        }
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(GuestToken))]
internal partial class GuestJsonContext : JsonSerializerContext
{
}
