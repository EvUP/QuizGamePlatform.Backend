namespace QuizGamePlatform.Backend.Core.Abstractions;

public interface IRoomHelper
{
    string GenerateRoomCode(int length = 8);
}