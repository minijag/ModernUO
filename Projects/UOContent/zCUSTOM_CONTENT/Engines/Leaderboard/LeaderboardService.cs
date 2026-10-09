using System;
using System.Collections.Generic;
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
    IngotsUsed,
    GoldFarmed
}

public readonly record struct LeaderboardEntry(string PlayerName, long Total);

public readonly record struct LeaderboardPage(
    IReadOnlyList<LeaderboardEntry> Entries,
    int PageIndex,
    int PageCount,
    int TotalEntries,
    int? OwnRank,
    long OwnTotal
);

public static class LeaderboardService
{
    private readonly record struct RankedPlayer(PlayerMobile Player, LeaderboardEntry Entry);

    private static readonly HashSet<PlayerMobile> _players = new();

    public static void Configure() => EventSink.WorldLoad += ReconcileLegacyTotals;

    public static void RegisterPlayer(PlayerMobile player) => _players.Add(player);

    public static void RemovePlayer(PlayerMobile player) => _players.Remove(player);

    public static void Record(Mobile mobile, LeaderboardStat stat, long amount)
    {
        if (amount > 0 && mobile is PlayerMobile { Deleted: false } player)
        {
            if (stat is LeaderboardStat.GoldFarmed or LeaderboardStat.MonsterGoldGenerated)
            {
                GoldFarmedService.Record(player, amount);
            }
            else
            {
                player.AddLeaderboardStat(stat, amount);
            }
        }
    }

    public static List<LeaderboardEntry> GetTop(LeaderboardStat stat, int count = 10)
    {
        count = Math.Clamp(count, 0, 100);
        var rows = new List<LeaderboardEntry>(count);
        if (count == 0)
        {
            return rows;
        }

        var rankedPlayers = GetRankedPlayers(stat);
        for (var i = 0; i < count && i < rankedPlayers.Count; i++)
        {
            rows.Add(rankedPlayers[i].Entry);
        }

        return rows;
    }

    public static LeaderboardPage GetPage(
        LeaderboardStat stat, int pageIndex, int pageSize = 10, PlayerMobile viewer = null, bool showMyRanking = false
    )
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var rankedPlayers = GetRankedPlayers(stat);
        var totalEntries = rankedPlayers.Count;
        var pageCount = totalEntries == 0 ? 1 : (totalEntries - 1) / pageSize + 1;
        pageIndex = Math.Clamp(pageIndex, 0, pageCount - 1);
        var ownTotal = viewer?.Deleted == false ? viewer.GetLeaderboardStat(stat) : 0;
        int? ownRank = null;

        if (viewer?.Deleted == false)
        {
            for (var i = 0; i < totalEntries; i++)
            {
                if (ReferenceEquals(rankedPlayers[i].Player, viewer))
                {
                    ownRank = i + 1;
                    break;
                }
            }
        }

        if (showMyRanking && ownRank is { } rank)
        {
            pageIndex = (rank - 1) / pageSize;
        }

        var start = pageIndex * pageSize;
        var entries = new LeaderboardEntry[Math.Min(pageSize, totalEntries - start)];
        for (var i = 0; i < entries.Length; i++)
        {
            entries[i] = rankedPlayers[start + i].Entry;
        }

        return new LeaderboardPage(entries, pageIndex, pageCount, totalEntries, ownRank, ownTotal);
    }

    private static List<RankedPlayer> GetRankedPlayers(LeaderboardStat stat)
    {
        var rankedPlayers = new List<RankedPlayer>(_players.Count);
        foreach (var player in _players)
        {
            if (player.Deleted)
            {
                continue;
            }

            var total = player.GetLeaderboardStat(stat);
            if (total <= 0)
            {
                continue;
            }

            rankedPlayers.Add(new RankedPlayer(player, new LeaderboardEntry(player.Name ?? "Unknown", total)));
        }

        rankedPlayers.Sort(ComparePlayers);
        return rankedPlayers;
    }

    private static int ComparePlayers(RankedPlayer left, RankedPlayer right)
    {
        var comparison = right.Entry.Total.CompareTo(left.Entry.Total);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = StringComparer.OrdinalIgnoreCase.Compare(left.Entry.PlayerName, right.Entry.PlayerName);
        return comparison != 0 ? comparison : left.Player.Serial.Value.CompareTo(right.Player.Serial.Value);
    }

    private static void ReconcileLegacyTotals()
    {
        foreach (var player in _players)
        {
            if (player.TotalOreMined > player.GetLeaderboardStat(LeaderboardStat.OreMined))
            {
                player.SetLeaderboardStat(LeaderboardStat.OreMined, player.TotalOreMined);
            }
        }
    }
}
