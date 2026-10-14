using System.ComponentModel.DataAnnotations;

namespace QuizGamePlatform.Backend.Application.Options
{
    /// <summary>Ссылки для входа и письма со ссылками</summary>
    public class InviteOptions
    {
        public const string SectionName = "Invites";

        /// <summary>Адрес фронта, ссылка входа {JoinBaseUrl}/join/{код}</summary>
        [Required]
        public string JoinBaseUrl { get; set; } = string.Empty;

        [Required]
        public string SmtpHost { get; set; } = string.Empty;

        [Range(1, 65535)]
        public int SmtpPort { get; set; } = 1025;

        [Required]
        public string From { get; set; } = string.Empty;

        public string JoinLink(string code) => $"{JoinBaseUrl.TrimEnd('/')}/join/{code}";
    }
}
