using Microsoft.AspNetCore.Components.Authorization;
using QuizGamePlatform.Web.Api.Contracts;

namespace QuizGamePlatform.Web.Features.Leaderboard;

public sealed record LeaderboardRow(int Place, GlobalLeaderboardEntry Entry)
{
    /// <summary>
    /// Места по порядку с сервера, без общих: равные очки он уже развёл точностью и временем
    /// </summary>
    public static List<LeaderboardRow> Rank(IReadOnlyList<GlobalLeaderboardEntry> entries)
        => entries.Select((entry, i) => new LeaderboardRow(i + 1, entry)).ToList();

    /// <summary>
    /// Своя строка. Id в ответе рейтинга нет, поэтому сравниваем логин: сервер берёт его
    /// из того же preferred_username, что фронт держит в Identity.Name
    /// </summary>
    public bool IsMine(string? myName)
        => !string.IsNullOrEmpty(myName) && string.Equals(Entry.UserName, myName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Логин аккаунта или null у гостя</summary>
    public static async Task<string?> MyNameAsync(Task<AuthenticationState>? authState)
    {
        if (authState is null)
        {
            return null;
        }

        var user = (await authState).User;
        return user.Identity?.IsAuthenticated == true ? user.Identity.Name : null;
    }
}
