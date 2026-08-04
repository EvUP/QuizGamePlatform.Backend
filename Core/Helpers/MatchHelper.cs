using QuizGamePlatform.Backend.Application.Contracts.Enums;

namespace QuizGamePlatform.Backend.Core.Helpers;

public static class MatchHelper
{
    static public readonly int MaxMatchPlayer = 5;
    
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