namespace QuizGamePlatform.Backend.Application.Enums
{
    // почему не пустили в комнату, None если вошёл
    public enum JoinRefusal
    {
        None,
        NotFound,          // нет комнаты или команды по этой ссылке
        Finished,          // игра уже прошла
        Started,           // игра идёт, новые не входят
        ReturnExpired,     // игра идёт, окно возврата истекло
        Full,              // мест нет
        NicknameTaken,     // ник занят другим гостем
        TeamLinkRequired,  // в командную только по ссылке команды
        Busy               // комнату меняли одновременно, можно повторить
    }
}
