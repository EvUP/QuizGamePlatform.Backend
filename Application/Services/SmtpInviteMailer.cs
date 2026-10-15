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
            // одна команда — её добавили в лобби, несколько — комнату только что создали.
            // Код комнаты в письмо не пишем: на экране его нет, вход только по ссылке.
            var single = teams.Count == 1;

            var body = new StringBuilder()
                .AppendLine("Здравствуйте!")
                .AppendLine()
                .AppendLine(single
                    ? "Вы добавили в комнату новую команду. Отправьте ей эту ссылку — игроки попадут сразу в команду."
                    : "Комната для квиза готова. Отправьте каждой команде её ссылку — игроки попадут сразу к своим.")
                .AppendLine();

            foreach (var team in teams)
            {
                var expected = team.ExpectedPlayers is { } count ? $" — ждём игроков: {count}" : "";
                body.AppendLine($"{team.Name}{expected}")
                    .AppendLine(team.JoinLink)
                    .AppendLine();
            }

            body.AppendLine(single
                    ? "Эта же ссылка есть в лобби — её можно скопировать оттуда."
                    : "Эти же ссылки есть в лобби — если письмо затеряется, их можно скопировать оттуда.")
                .AppendLine()
                .AppendLine("Удачной игры!")
                .AppendLine("QuizGame");

            var subject = single ? "QuizGame: ссылка для новой команды" : "QuizGame: ссылки для команд";

            // зависший SMTP не держит создание комнаты
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            try
            {
                using var message = new MailMessage(options.Value.From, to, subject, body.ToString());
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
