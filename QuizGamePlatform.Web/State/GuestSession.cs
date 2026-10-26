using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using QuizGamePlatform.Web.Api.Contracts;

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
    // запас прибавляем к «сейчас», а не вычитаем из срока: у забытого токена срок DateTime.MinValue, и вычитание падает
    public GuestToken? Current => token is not null && token.ExpiresAtUtc > DateTime.UtcNow + ExpirySkew ? token : null;

    /// <summary>Ник последнего гостевого входа, в том числе из истёкшего токена: форму входа заполняем им</summary>
    public string? LastNickname => token?.Nickname;

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
            // приватный режим или испорченная запись: просто без гостевой сессии
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
            // хранилище недоступно: токен останется в памяти до перезагрузки
        }
    }

    /// <summary>Запомнить гостевой токен из ответа входа или перехода в следующую игру (аккаунту токен не выдают)</summary>
    public async Task SaveFromJoinAsync(JoinToRoomResponse joined)
    {
        if (joined.GuestAccessToken is { } accessToken && joined.GuestTokenExpiresAtUtc is { } expiresAt)
        {
            await SaveAsync(new GuestToken(
                accessToken, expiresAt, joined.Room.RoomId, joined.Room.RoomPlayerLinkId, joined.Room.PlayerName));
        }
    }

    /// <summary>Комнату токена удалили (ведущий отменил игру): токен больше не действует, ник для формы входа оставляем</summary>
    public async Task ForgetRoomAsync(Guid roomId)
    {
        if (token is { } current && current.RoomId == roomId && Current is not null)
        {
            await SaveAsync(current with { ExpiresAtUtc = DateTime.MinValue });
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
