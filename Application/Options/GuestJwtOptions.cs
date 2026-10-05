using System.ComponentModel.DataAnnotations;

namespace QuizGamePlatform.Backend.Application.Options
{
    /// <summary>Настройки гостевых JWT</summary>
    public class GuestJwtOptions
    {
        public const string SectionName = "GuestJwt";

        /// <summary>Издатель токенов (iss)</summary>
        [Required]
        public string Issuer { get; set; } = string.Empty;

        [Required]
        public string Audience { get; set; } = string.Empty;

        /// <summary>Ключ подписи HS256</summary>
        [Required]
        [MinLength(32, ErrorMessage = "GuestJwt:SigningKey must be at least 32 characters for HS256.")]
        public string SigningKey { get; set; } = string.Empty;

        [Range(1, 24 * 60)]
        public int LifetimeMinutes { get; set; } = 240;

        /// <summary>Допустимое расхождение часов, в секундах</summary>
        [Range(0, int.MaxValue)]
        public int ClockSkewSeconds { get; set; } = 30;
    }
}
