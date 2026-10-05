namespace QuizGamePlatform.Backend.Application.Abstractions
{
    /// <summary>Выпуск гостевых JWT</summary>
    public interface IGuestTokenService
    {
        GuestTokenResult Issue(Guid roomPlayerLinkId, Guid roomId, string nickname);
    }

    public record GuestTokenResult(string AccessToken, DateTime ExpiresAtUtc);
}
