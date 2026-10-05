using System.IdentityModel.Tokens.Jwt;

namespace QuizGamePlatform.Backend.Application.Auth
{
    /// <summary>Выбор обработчика по издателю токена, без проверки подписи</summary>
    public static class GuestTokenReader
    {
        private const string BearerPrefix = "Bearer ";

        public static bool IsGuestToken(string? authorizationHeader, string guestIssuer)
        {
            var issuer = TryReadIssuer(authorizationHeader);

            return issuer is not null
                && string.Equals(issuer, guestIssuer, StringComparison.Ordinal);
        }

        private static string? TryReadIssuer(string? authorizationHeader)
        {
            if (string.IsNullOrWhiteSpace(authorizationHeader)
                || !authorizationHeader.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var token = authorizationHeader[BearerPrefix.Length..].Trim();

            if (token.Length == 0)
            {
                return null;
            }

            var handler = new JwtSecurityTokenHandler();

            if (!handler.CanReadToken(token))
            {
                return null;
            }

            try
            {
                return handler.ReadJwtToken(token).Issuer;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
