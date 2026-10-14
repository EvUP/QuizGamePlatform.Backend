namespace QuizGamePlatform.Backend.Application.Enums
{
    public enum LobbyChangeResult
    {
        Done,
        NotFound,   // нет комнаты, команды или игрока, либо вы не в комнате
        Forbidden,  // не хватает прав
        Conflict,   // не лобби или ограничение команд
        Concurrent  // комнату изменили одновременно, можно повторить
    }
}
