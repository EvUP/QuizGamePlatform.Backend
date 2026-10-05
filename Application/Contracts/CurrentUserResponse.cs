namespace QuizGamePlatform.Backend.Application.Contracts
{
    /// <summary>Данные аккаунта или гостевого участия</summary>
    public record CurrentUserResponse(
        string Kind,
        string? KeycloakSubject,
        string? DisplayName,
        string? Email,
        Guid? RoomPlayerLinkId,
        Guid? RoomId,
        string? Nickname)
    {
        public const string RegisteredKind = "registered";
        public const string GuestKind = "guest";

        public static CurrentUserResponse Registered(string subject, string? displayName, string? email) =>
            new(RegisteredKind, subject, displayName, email, null, null, null);

        public static CurrentUserResponse Guest(Guid roomPlayerLinkId, Guid? roomId, string? nickname) =>
            new(GuestKind, null, null, null, roomPlayerLinkId, roomId, nickname);
    }
}
