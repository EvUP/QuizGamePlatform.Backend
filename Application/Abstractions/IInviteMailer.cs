using QuizGamePlatform.Backend.Application.Contracts.Room;

namespace QuizGamePlatform.Backend.Application.Abstractions
{
    // письмо ведущему со ссылками команд, сбой почты игру не ломает
    public interface IInviteMailer
    {
        Task SendTeamLinksAsync(string to, string roomCode, List<TeamLinkResponse> teams, CancellationToken ct);
    }
}
