using QuizGamePlatform.Backend.Application.Contracts.Enums;

namespace QuizGamePlatform.Backend.Core.Helpers;

public static class MatchHelper
{
    static public readonly int MaxMatchPlayer = 10;
    static public readonly int MaxTeamGamePlayer = 30;
    static public readonly int MinTeams = 2;
    static public readonly int MaxTeams = 5;
    public const int TeamNameMaxLength = 30;
    static public readonly int MaxQuestionCount = 30;

    // показ правильного ответа перед следующим вопросом
    public const int RevealSeconds = 3;

    static public string DefaultTeamName(int number) => $"Команда {number}";

    // \0 роняет запрос в базу, перевод строки ломает письмо со ссылками
    static public bool HasControlChars(string? text) => text?.Any(char.IsControl) == true;

    static public int GetQuestionDurationByRoomMode(RoomMode roomMode)
    {
        return roomMode switch
        {
            RoomMode.Simple => 30,
            RoomMode.Fastest => 15,
            _ => 30
        };
    }
}