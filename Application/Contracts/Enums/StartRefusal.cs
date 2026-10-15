namespace QuizGamePlatform.Backend.Application.Enums
{
    // почему матч не стартовал, None если стартовал
    public enum StartRefusal
    {
        None,
        NotFound,
        Started,        // игра уже идёт
        Finished,       // игра уже прошла, нужна новая комната
        TooFewPlayers,
        TooFewTeams,
        NoQuestions,
        Concurrent      // комнату меняли одновременно, можно повторить
    }
}
