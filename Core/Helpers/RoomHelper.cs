using System.Security.Cryptography;
using QuizGamePlatform.Backend.Core.Abstractions;
namespace QuizGamePlatform.Backend.Core.Helpers;

public class RoomHelper : IRoomHelper
{
    private const string AllowedChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public string GenerateRoomCode(int length = 8)
    {
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[length];
        rng.GetBytes(bytes);

        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = AllowedChars[bytes[i] % AllowedChars.Length];
        }

        return new string(chars);
    }
}
