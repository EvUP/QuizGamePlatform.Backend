namespace QuizGamePlatform.Web.Infrastructure;

/// <summary>Буквы для аватара: по первой из двух слов имени, иначе первые две</summary>
public static class PlayerInitials
{
    public static string Of(string name)
    {
        var words = name.Split([' ', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
        var letters = words.Length >= 2 ? $"{words[0][0]}{words[1][0]}" : name[..Math.Min(2, name.Length)];
        return letters.ToUpperInvariant();
    }
}
