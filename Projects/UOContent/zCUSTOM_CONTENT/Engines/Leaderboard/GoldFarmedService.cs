using Server.Mobiles;

namespace Server.Engines.Custom;

public static class GoldFarmedService
{
    // The legacy counter stores configured monster-gold averages earned through kill credit.
    public static long GetTotal(PlayerMobile player) =>
        player?.GetLeaderboardStat(LeaderboardStat.MonsterGoldGenerated) ?? 0;

    internal static void Record(PlayerMobile player, long averageGold)
    {
        if (player?.Deleted != false || averageGold <= 0)
        {
            return;
        }

        player.AddLeaderboardStat(LeaderboardStat.MonsterGoldGenerated, averageGold);
        GuildProgressService.RecordGold(player, averageGold);
    }
}
