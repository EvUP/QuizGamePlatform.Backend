namespace QuizGamePlatform.Backend.Application.Abstractions
{
    /// <summary>Данные пользователя текущего запроса.</summary>
    public interface ICurrentUserContext
    {
        bool IsAuthenticated { get; }

        /// <summary>Пользователь вошёл с гостевым токеном</summary>
        bool IsGuest { get; }

        /// <summary>Пользователь вошёл через Keycloak.</summary>
        bool IsRegistered { get; }

        /// <summary>Идентификатор пользователя в Keycloak (sub)</summary>
        string? KeycloakSubject { get; }

        /// <summary>Имя учётной записи (preferred_username).</summary>
        string? DisplayName { get; }

        string? Email { get; }

        /// <summary>ID участия гостя.</summary>
        Guid? RoomPlayerLinkId { get; }

        /// <summary>ID комнаты из гостевого токена.</summary>
        Guid? RoomId { get; }

        /// <summary>Игровой ник гостя.</summary>
        string? Nickname { get; }
    }
}
