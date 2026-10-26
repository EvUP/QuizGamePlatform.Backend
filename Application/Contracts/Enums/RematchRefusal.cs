namespace QuizGamePlatform.Backend.Application.Enums
{
    // почему «Сыграть ещё» не собрало игру, None если собрало или она уже есть
    public enum RematchRefusal
    {
        None,
        NotFound,      // нет комнаты
        Forbidden,     // не ведущий этой комнаты
        NotFinished,   // итогов ещё нет
        Busy           // комнату меняли одновременно, можно повторить
    }
}
