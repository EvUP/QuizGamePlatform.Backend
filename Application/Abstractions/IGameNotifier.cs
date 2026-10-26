using QuizGamePlatform.Backend.Application.Contracts.Match;

namespace QuizGamePlatform.Backend.Application.Abstractions
{
    // рассылает участникам комнаты события игры, не задерживая саму игру
    public interface IGameNotifier
    {
        // состав комнаты изменился, клиент перечитывает её состояние
        void RoomUpdated(Guid roomId);

        // матч стартовал, сменил вопрос или завершился
        void MatchUpdated(MatchStateResponse match);

        // игрок ответил, без самого ответа
        void PlayerAnswered(PlayerAnsweredEvent answer);
    }
}
