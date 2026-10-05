using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.Options;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace QuizGamePlatform.Backend.Application.Services
{
    public class GuestTokenService(
        IOptions<GuestJwtOptions> options,
        TimeProvider timeProvider) : IGuestTokenService
    {
        public const string RoomIdClaim = "room_id";
        public const string NicknameClaim = "nickname";

        public GuestTokenResult Issue(Guid roomPlayerLinkId, Guid roomId, string nickname)
        {
            var settings = options.Value;

            var issuedAt = timeProvider.GetUtcNow().UtcDateTime;
            var expiresAt = issuedAt.AddMinutes(settings.LifetimeMinutes);

            var credentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
                SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                // sub = ID участия в комнате
                new(JwtRegisteredClaimNames.Sub, roomPlayerLinkId.ToString()),
                new(RoomIdClaim, roomId.ToString()),
                new(NicknameClaim, nickname),
            };

            var token = new JwtSecurityToken(
                issuer: settings.Issuer,
                audience: settings.Audience,
                claims: claims,
                notBefore: issuedAt,
                expires: expiresAt,
                signingCredentials: credentials);

            return new GuestTokenResult(
                new JwtSecurityTokenHandler().WriteToken(token),
                expiresAt);
        }
    }
}
