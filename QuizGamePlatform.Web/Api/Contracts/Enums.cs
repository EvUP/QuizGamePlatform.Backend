namespace QuizGamePlatform.Web.Api.Contracts;

// Значения совпадают с enum'ами бэка, по сети передаются строками

public enum RoomStatus
{
    Waiting,
    InProgress,
    Finished
}

public enum MatchStatus
{
    QuestionActive,
    QuestionClosed,
    Finished
}

public enum RoomMode
{
    Simple,   // 30 секунд на вопрос
    Fastest   // 15 секунд на вопрос
}

public enum ExitReason
{
    Normal,
    RoomFinished,
    Disconnected,
    Cheating,
    Other,
    Kicked
}
