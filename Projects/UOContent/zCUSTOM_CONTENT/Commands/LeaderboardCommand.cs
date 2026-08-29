using Server.Engines.Custom;
using Server.Gumps;
using Server.Mobiles;
using Server.Network;

namespace Server.Commands;

public static class LeaderboardCommand
{
    public static void Configure() => CommandSystem.Register("Leaderboard", AccessLevel.Player, OnLeaderboard);

    [Usage("Leaderboard")]
    [Description("Shows the shard leaderboards.")]
    private static void OnLeaderboard(CommandEventArgs e)
    {
        if (e.Mobile is PlayerMobile player)
        {
            e.Mobile.SendGump(new LeaderboardGump(player));
        }
    }
}
