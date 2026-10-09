using Server.Engines.Custom;

namespace Server.Commands;

public static class LeaderboardCommand
{
    public static void Configure() => CommandSystem.Register("Leaderboard", AccessLevel.Player, OnLeaderboard);

    [Usage("Leaderboard")]
    [Description("Shows the shard leaderboards.")]
    private static void OnLeaderboard(CommandEventArgs e)
    {
        LeaderboardGump.DisplayTo(e.Mobile);
    }
}
