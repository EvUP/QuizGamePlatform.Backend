using QuizGamePlatform.Web.Api.Contracts;

namespace QuizGamePlatform.Web.Features.Leaderboard;

public sealed record LeaderboardRow(int Place, GlobalLeaderboardEntry Entry)
{
    /// <summary>
    /// Места по порядку с сервера, без общих: равные очки он уже развёл точностью и временем
    /// </summary>
    public static List<LeaderboardRow> Rank(IReadOnlyList<GlobalLeaderboardEntry> entries)
        => entries.Select((entry, i) => new LeaderboardRow(i + 1, entry)).ToList();
}
