using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Options;
using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.Contracts.Room;
using QuizGamePlatform.Backend.Application.Options;

namespace QuizGamePlatform.Backend.Application.Services
{
    public class SmtpInviteMailer(IOptions<InviteOptions> options, ILogger<SmtpInviteMailer> logger) : IInviteMailer
    {
        public async Task SendTeamLinksAsync(string to, string roomCode, List<TeamLinkResponse> teams, CancellationToken ct)
        {
            var body = new StringBuilder()
                .AppendLine($"Ссылки команд квиза {roomCode}.")
                .AppendLine("Отправьте каждой команде её ссылку, по ней участники войдут сразу в свою команду.")
                .AppendLine();

            foreach (var team in teams)
            {
                var expected = team.ExpectedPlayers is { } count ? $" (~{count} чел.)" : "";
                body.AppendLine($"{team.Name}{expected}: {team.JoinLink}");
            }

            // зависший SMTP не держит создание комнаты
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            try
            {
                using var message = new MailMessage(options.Value.From, to, $"Ссылки команд для квиза {roomCode}", body.ToString());
                using var client = new SmtpClient(options.Value.SmtpHost, options.Value.SmtpPort);

                await client.SendMailAsync(message, timeout.Token);

                logger.LogInformation("Team links of room {RoomCode} sent to {Email}", roomCode, to);
            }
            catch (Exception ex)
            {
                // ссылки всё равно есть в ответе и в состоянии комнаты
                logger.LogWarning(ex, "Team links of room {RoomCode} were not sent", roomCode);
            }
        }
    }
}
