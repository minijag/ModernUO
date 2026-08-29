using System;
using System.Collections.Generic;
using System.Linq;
using Server.Mobiles;

namespace Server.Engines.Custom;

public enum LeaderboardStat
{
    OreMined,
    LumberHarvested,
    MonsterGoldGenerated,
    MonstersKilled,
    TreasureMapsCompleted,
    LeatherGathered,
    BoardsUsed,
    LeatherUsed,
    IngotsUsed
}

public readonly record struct LeaderboardEntry(string PlayerName, long Total);

public static class LeaderboardService
{
    public static void Configure() => EventSink.WorldLoad += ReconcileLegacyTotals;

    public static void Record(Mobile mobile, LeaderboardStat stat, long amount)
    {
        if (amount > 0 && mobile is PlayerMobile player)
        {
            player.AddLeaderboardStat(stat, amount);
        }
    }

    public static List<LeaderboardEntry> GetTop(LeaderboardStat stat, int count = 10) =>
        World.Mobiles.Values.OfType<PlayerMobile>()
             .Select(p => new LeaderboardEntry(p.Name ?? "Unknown", p.GetLeaderboardStat(stat)))
             .Where(e => e.Total > 0)
             .OrderByDescending(e => e.Total).ThenBy(e => e.PlayerName)
             .Take(count).ToList();

    private static void ReconcileLegacyTotals()
    {
        foreach (var player in World.Mobiles.Values.OfType<PlayerMobile>())
        {
            if (player.TotalOreMined > player.GetLeaderboardStat(LeaderboardStat.OreMined))
            {
                player.SetLeaderboardStat(LeaderboardStat.OreMined, player.TotalOreMined);
            }
        }
    }
}
