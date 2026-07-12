namespace QuizGamePlatform.Backend.Application.Contracts.Player
{
    public record CreatePlayerResponse(Guid? Id, string Username);
    public record CreatePlayerRequest(string Username);
}