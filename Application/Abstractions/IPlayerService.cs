
using QuizGamePlatform.Backend.Application.Contracts.Player;

namespace QuizGamePlatform.Backend.Application.Abstractions
{
    public interface IPlayerService
    {
        Task<CreatePlayerResponse?> CreatePlayer(CreatePlayerRequest playerRequest, CancellationToken ct);
    }
}