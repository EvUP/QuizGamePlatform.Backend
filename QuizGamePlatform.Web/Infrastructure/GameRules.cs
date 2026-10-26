namespace QuizGamePlatform.Web.Infrastructure;

/// <summary>Ограничения игры: копия Core/Helpers/MatchHelper.cs на бэке, сервер проверяет их сам</summary>
public static class GameRules
{
    public const int MaxSimplePlayers = 10;
    public const int MaxTeamGamePlayers = 30;
    public const int MinTeams = 2;
    public const int MaxTeams = 5;
    public const int TeamNameMaxLength = 30;
    public const int NicknameMaxLength = 25;
    public const int MinQuestionCount = 1;
    public const int MaxQuestionCount = 30;

    // время на вопрос по темпу (MatchHelper.GetQuestionDurationByRoomMode) и показ верного ответа (MatchHelper.RevealSeconds)
    public const int CalmQuestionSeconds = 30;
    public const int BlitzQuestionSeconds = 15;
    public const int RevealSeconds = 3;

    // сервер считает игрока вышедшим через 5 с без связи и возвращает в игру ещё 40 с
    public const int DisconnectGraceSeconds = 5;
    public const int ReturnWindowSeconds = 40;

    // правила счёта одной формулировкой: лобби и итоги
    public const string SimpleScoringText = "Очко за каждый верный ответ. Очков поровну — выше тот, кто в среднем отвечал верно быстрее.";
    public static readonly string SimpleExitText = $"Вышли и не вернулись за {ReturnWindowSeconds} с — 0 очков.";
    public const string TeamScoringText = "За вопрос команда получает до 100 очков — долю верных ответов среди своих игроков в игре. Очков поровну — выше команда, которая в среднем отвечала верно быстрее.";
    public const string TeamExitText = "Вышли во время вопроса и не ответили — ответ неверный. Очки, которые игрок уже принёс команде, остаются за ней.";

    public static string DefaultTeamName(int number) => $"Команда {number}";
}
