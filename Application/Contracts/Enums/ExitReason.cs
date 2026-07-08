namespace QuizGamePlatform.Backend.Application.Enums
{
    public enum ExitReason
    {
        Normal,          // Игрок сам вышел
        RoomFinished,    // Комната завершилась (все игроки вышли)
        Disconnected,    // Потеря соединения
        Cheating,        // Бан за читерство
        Other            // Что-то непредвиденное
    }
}
