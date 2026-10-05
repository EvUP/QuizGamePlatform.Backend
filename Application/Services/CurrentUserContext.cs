using Microsoft.AspNetCore.Authentication.JwtBearer;
using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.Auth;
using System.Security.Claims;

namespace QuizGamePlatform.Backend.Application.Services
{
    public class CurrentUserContext(IHttpContextAccessor httpContextAccessor) : ICurrentUserContext
    {
        private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

        public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

        private string? Scheme => User?.Identity?.AuthenticationType;

        public bool IsGuest => IsAuthenticated && Scheme == AuthenticationSchemes.Guest;

        public bool IsRegistered =>
            IsAuthenticated && Scheme == JwtBearerDefaults.AuthenticationScheme;

        public string? KeycloakSubject => IsRegistered ? GetClaim("sub") : null;

        public string? DisplayName => IsRegistered ? GetClaim("preferred_username") : null;

        public string? Email => IsRegistered ? GetClaim("email") : null;

        public Guid? RoomPlayerLinkId => IsGuest ? GetGuidClaim("sub") : null;

        public Guid? RoomId => IsGuest ? GetGuidClaim(GuestTokenService.RoomIdClaim) : null;

        public string? Nickname => IsGuest ? GetClaim(GuestTokenService.NicknameClaim) : null;

        private string? GetClaim(string claimType)
        {
            var value = User?.FindFirstValue(claimType);

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private Guid? GetGuidClaim(string claimType) =>
            Guid.TryParse(GetClaim(claimType), out var value) ? value : null;
    }
}
