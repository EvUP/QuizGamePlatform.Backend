namespace QuizGamePlatform.Web.Infrastructure;

/// <summary>Ограничения игры — копия Core/Helpers/MatchHelper.cs на бэке, сервер проверяет их сам</summary>
public static class GameRules
{
    public const int MaxSimplePlayers = 10;
    public const int MaxTeamGamePlayers = 30;
    public const int MinTeams = 2;
    public const int MaxTeams = 5;
    public const int TeamNameMaxLength = 30;
    public const int NicknameMaxLength = 25;
    public const int MaxQuestionCount = 30;

    // сервер считает игрока вышедшим через 5 с без связи и возвращает в игру ещё 40 с
    public const int DisconnectGraceSeconds = 5;
    public const int ReturnWindowSeconds = 40;

    public static string DefaultTeamName(int number) => $"Команда {number}";
}
