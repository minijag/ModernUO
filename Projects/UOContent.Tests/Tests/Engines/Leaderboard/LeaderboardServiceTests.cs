using System;
using Server;
using Server.Engines.Custom;
using Server.Guilds;
using Server.Mobiles;
using Xunit;

namespace UOContent.Tests;

[Collection("Sequential UOContent Tests")]
public class LeaderboardServiceTests
{
    private const LeaderboardStat TestStat = LeaderboardStat.BoardsUsed;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlayerConstructors_RegisterForLeaderboard(bool serialConstructor)
    {
        var player = CreatePlayer("Registered Leaderboard Player", serialConstructor);

        try
        {
            player.SetLeaderboardStat(TestStat, long.MaxValue);

            Assert.Contains(new LeaderboardEntry(player.Name, long.MaxValue), LeaderboardService.GetTop(TestStat, 100));
        }
        finally
        {
            player.Delete();
        }
    }

    [Fact]
    public void TopRows_SortByTotalThenNameAndOmitNonpositiveTotals()
    {
        var players = new[]
        {
            CreatePlayer("beta"), CreatePlayer("Alpha"), CreatePlayer("Omega"),
            CreatePlayer("Zero"), CreatePlayer("Negative")
        };

        try
        {
            players[0].SetLeaderboardStat(TestStat, long.MaxValue - 1);
            players[1].SetLeaderboardStat(TestStat, long.MaxValue - 1);
            players[2].SetLeaderboardStat(TestStat, long.MaxValue);
            players[4].SetLeaderboardStat(TestStat, -5);

            var top = LeaderboardService.GetTop(TestStat, 3);

            Assert.Equal(new[]
            {
                new LeaderboardEntry("Omega", long.MaxValue),
                new LeaderboardEntry("Alpha", long.MaxValue - 1),
                new LeaderboardEntry("beta", long.MaxValue - 1)
            }, top);
            Assert.Equal<LeaderboardEntry>(top, LeaderboardService.GetPage(TestStat, 0, 3).Entries);
            Assert.DoesNotContain(LeaderboardService.GetTop(TestStat, 100), row => row.PlayerName is "Zero" or "Negative");
        }
        finally
        {
            foreach (var player in players)
            {
                player.Delete();
            }
        }
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(200, 100)]
    public void RequestedCount_IsClampedToOneHundred(int requested, int expected)
    {
        var players = new PlayerMobile[105];

        try
        {
            for (var i = 0; i < players.Length; i++)
            {
                players[i] = CreatePlayer($"Leaderboard Cap {i}");
                players[i].SetLeaderboardStat(TestStat, long.MaxValue - i);
            }

            var top = LeaderboardService.GetTop(TestStat, requested);

            Assert.Equal(expected, top.Count);
            for (var i = 0; i < top.Count; i++)
            {
                Assert.Equal(new LeaderboardEntry(players[i].Name, long.MaxValue - i), top[i]);
            }
        }
        finally
        {
            foreach (var player in players)
            {
                player?.Delete();
            }
        }
    }

    [Fact]
    public void DeletedPlayers_AreRemovedAndCannotRecordMore()
    {
        var player = CreatePlayer("Deleted Leaderboard Player");
        player.SetLeaderboardStat(TestStat, long.MaxValue);
        Assert.Contains(new LeaderboardEntry(player.Name, long.MaxValue), LeaderboardService.GetTop(TestStat, 100));

        player.Delete();
        LeaderboardService.Record(player, TestStat, 10);

        Assert.DoesNotContain(LeaderboardService.GetTop(TestStat, 100), row => row.PlayerName == player.Name);
        Assert.Equal(long.MaxValue, player.GetLeaderboardStat(TestStat));
    }

    [Fact]
    public void Pages_IncludeEveryRankBeyondTheTopOneHundred()
    {
        var players = new PlayerMobile[137];

        try
        {
            for (var i = 0; i < players.Length; i++)
            {
                players[i] = CreatePlayer($"Paged Leaderboard Player {i}");
                players[i].SetLeaderboardStat(TestStat, 1000 - i);
            }

            var first = LeaderboardService.GetPage(TestStat, 0);
            Assert.Equal(0, first.PageIndex);
            Assert.Equal(14, first.PageCount);
            Assert.Equal(137, first.TotalEntries);
            Assert.Equal<LeaderboardEntry>(LeaderboardService.GetTop(TestStat), first.Entries);

            for (var pageIndex = 0; pageIndex < first.PageCount; pageIndex++)
            {
                var page = LeaderboardService.GetPage(TestStat, pageIndex);
                Assert.Equal(Math.Min(10, players.Length - pageIndex * 10), page.Entries.Count);
                for (var i = 0; i < page.Entries.Count; i++)
                {
                    var playerIndex = pageIndex * 10 + i;
                    Assert.Equal(new LeaderboardEntry(players[playerIndex].Name, 1000 - playerIndex), page.Entries[i]);
                }
            }

            var last = LeaderboardService.GetPage(TestStat, 13, viewer: players[^1]);
            Assert.Equal(137, last.OwnRank);
            Assert.Equal(864L, last.OwnTotal);
            Assert.Equal(7, last.Entries.Count);
            Assert.Equal(100, LeaderboardService.GetTop(TestStat, 1000).Count);
        }
        finally
        {
            foreach (var player in players)
            {
                player?.Delete();
            }
        }
    }

    [Theory]
    [InlineData(int.MinValue, int.MinValue, 0, 25, 1)]
    [InlineData(int.MaxValue, 10, 2, 3, 5)]
    [InlineData(int.MaxValue, int.MaxValue, 0, 1, 25)]
    [InlineData(1, 0, 1, 25, 1)]
    public void Pages_ClampRequestedPageAndPageSize(int requestedPage, int requestedSize, int expectedPage, int expectedPages, int expectedRows)
    {
        var players = new PlayerMobile[25];

        try
        {
            for (var i = 0; i < players.Length; i++)
            {
                players[i] = CreatePlayer($"Page Clamp Player {i}");
                players[i].SetLeaderboardStat(TestStat, 1000 - i);
            }

            var page = LeaderboardService.GetPage(TestStat, requestedPage, requestedSize);

            Assert.Equal(expectedPage, page.PageIndex);
            Assert.Equal(expectedPages, page.PageCount);
            Assert.Equal(25, page.TotalEntries);
            Assert.Equal(expectedRows, page.Entries.Count);
            Assert.Null(page.OwnRank);
            Assert.Equal(0L, page.OwnTotal);
        }
        finally
        {
            foreach (var player in players)
            {
                player?.Delete();
            }
        }
    }

    [Fact]
    public void EmptyLeaderboard_AlwaysHasOneEmptyPage()
    {
        var viewer = CreatePlayer("Unranked Empty Leaderboard Player");

        try
        {
            var page = LeaderboardService.GetPage(TestStat, int.MaxValue, 0, viewer, true);

            Assert.Empty(page.Entries);
            Assert.Equal(0, page.PageIndex);
            Assert.Equal(1, page.PageCount);
            Assert.Equal(0, page.TotalEntries);
            Assert.Null(page.OwnRank);
            Assert.Equal(0L, page.OwnTotal);
        }
        finally
        {
            viewer.Delete();
        }
    }

    [Fact]
    public void ShowMyRanking_JumpsToTheViewersGlobalRank()
    {
        var players = new PlayerMobile[25];
        var unranked = CreatePlayer("Unranked Show My Ranking Player");

        try
        {
            for (var i = 0; i < players.Length; i++)
            {
                players[i] = CreatePlayer($"Own Ranking Player {i}");
                players[i].SetLeaderboardStat(TestStat, 1000 - i);
            }

            var first = LeaderboardService.GetPage(TestStat, 0, viewer: players[^1]);
            Assert.Equal(0, first.PageIndex);
            Assert.Equal(25, first.OwnRank);
            Assert.Equal(976L, first.OwnTotal);

            var own = LeaderboardService.GetPage(TestStat, 0, viewer: players[^1], showMyRanking: true);
            Assert.Equal(2, own.PageIndex);
            Assert.Equal(25, own.OwnRank);
            Assert.Equal(new LeaderboardEntry(players[^1].Name, 976), own.Entries[^1]);

            var unchanged = LeaderboardService.GetPage(TestStat, 1, viewer: unranked, showMyRanking: true);
            Assert.Equal(1, unchanged.PageIndex);
            Assert.Null(unchanged.OwnRank);
        }
        finally
        {
            unranked.Delete();
            foreach (var player in players)
            {
                player?.Delete();
            }
        }
    }

    [Fact]
    public void DuplicateNames_AreRankedByIdentityWithStableSerialTies()
    {
        var first = CreatePlayer("Duplicate Leaderboard Name");
        var second = CreatePlayer("Duplicate Leaderboard Name");

        try
        {
            first.SetLeaderboardStat(TestStat, 500);
            second.SetLeaderboardStat(TestStat, 500);
            var lowerSerial = first.Serial.Value < second.Serial.Value ? first : second;
            var higherSerial = lowerSerial == first ? second : first;

            var firstPage = LeaderboardService.GetPage(TestStat, 0, 1, lowerSerial, true);
            var secondPage = LeaderboardService.GetPage(TestStat, 0, 1, higherSerial, true);

            Assert.Equal(1, firstPage.OwnRank);
            Assert.Equal(0, firstPage.PageIndex);
            Assert.Equal(2, secondPage.OwnRank);
            Assert.Equal(1, secondPage.PageIndex);
            Assert.Equal(2, secondPage.TotalEntries);
            Assert.Equal<LeaderboardEntry>(LeaderboardService.GetTop(TestStat), LeaderboardService.GetPage(TestStat, 0, 100).Entries);
        }
        finally
        {
            first.Delete();
            second.Delete();
        }
    }

    [Fact]
    public void UnrankedAndDeletedPlayers_DoNotJumpOrAppearInPages()
    {
        var players = new[]
        {
            CreatePlayer("Ranked Pagination Player"), CreatePlayer("Zero Pagination Player"),
            CreatePlayer("Deleted Pagination Player"), CreatePlayer("Negative Pagination Player")
        };

        try
        {
            players[0].SetLeaderboardStat(TestStat, 100);
            players[2].SetLeaderboardStat(TestStat, 1000);
            players[2].Delete();
            players[3].SetLeaderboardStat(TestStat, -100);

            foreach (var viewer in players[1..])
            {
                var page = LeaderboardService.GetPage(TestStat, int.MaxValue, 1, viewer, true);

                Assert.Equal(1, page.TotalEntries);
                Assert.Equal(0, page.PageIndex);
                Assert.Null(page.OwnRank);
                Assert.Equal(0L, page.OwnTotal);
                Assert.Equal(new LeaderboardEntry(players[0].Name, 100), Assert.Single(page.Entries));
            }
        }
        finally
        {
            foreach (var player in players)
            {
                player.Delete();
            }
        }
    }

    [Fact]
    public void GoldPagination_OnlyReadsHistoryWithoutReplayingGuildCredit()
    {
        var player = CreatePlayer("Read Only Pagination Gold Player");
        player.SetLeaderboardStat(LeaderboardStat.MonsterGoldGenerated, 500);
        var guild = new Guild(player, "Pagination Read Guild", "PRG");

        try
        {
            var historical = LeaderboardService.GetPage(LeaderboardStat.GoldFarmed, 0, viewer: player, showMyRanking: true);
            Assert.Equal(500L, historical.OwnTotal);
            Assert.Equal(default, GuildProgressService.GetProgress(guild));

            LeaderboardService.Record(player, LeaderboardStat.GoldFarmed, 125);
            for (var i = 0; i < 3; i++)
            {
                var page = LeaderboardService.GetPage(LeaderboardStat.GoldFarmed, i, viewer: player, showMyRanking: true);
                Assert.Equal(625L, page.OwnTotal);
                Assert.NotNull(page.OwnRank);
                Assert.Equal(new GuildProgress(125, 0, 0), GuildProgressService.GetProgress(guild));
                Assert.Equal(625L, player.GetLeaderboardStat(LeaderboardStat.MonsterGoldGenerated));
            }
        }
        finally
        {
            guild.Disband();
            player.Delete();
        }
    }

    [Fact]
    public void Recording_IgnoresNonpositiveAmountsAndNonPlayerMobiles()
    {
        var player = CreatePlayer("Leaderboard Recording Player");
        var npc = new Mobile();

        try
        {
            LeaderboardService.Record(player, TestStat, 10);
            LeaderboardService.Record(player, TestStat, 0);
            LeaderboardService.Record(player, TestStat, -5);
            LeaderboardService.Record(npc, TestStat, 100);

            Assert.Equal(10L, player.GetLeaderboardStat(TestStat));
        }
        finally
        {
            player.Delete();
            npc.Delete();
        }
    }

    [Fact]
    public void GoldFarmed_ShowsLegacyMonsterGoldHistoryImmediately()
    {
        var player = CreatePlayer("Legacy Gold Leaderboard Player");

        try
        {
            player.SetLeaderboardStat(LeaderboardStat.MonsterGoldGenerated, long.MaxValue);

            Assert.Equal(long.MaxValue, player.GetLeaderboardStat(LeaderboardStat.GoldFarmed));
            Assert.Equal(long.MaxValue, GoldFarmedService.GetTotal(player));
            Assert.Contains(new LeaderboardEntry(player.Name, long.MaxValue), LeaderboardService.GetTop(LeaderboardStat.GoldFarmed, 100));

            player.SetLeaderboardStat(LeaderboardStat.GoldFarmed, 75);
            Assert.Equal(75L, player.GetLeaderboardStat(LeaderboardStat.MonsterGoldGenerated));
        }
        finally
        {
            player.Delete();
        }
    }

    [Theory]
    [InlineData(LeaderboardStat.GoldFarmed)]
    [InlineData(LeaderboardStat.MonsterGoldGenerated)]
    public void GoldRecording_UpdatesOneSharedCounter(LeaderboardStat stat)
    {
        var player = CreatePlayer("Gold Kill Credit Player");

        try
        {
            player.SetLeaderboardStat(LeaderboardStat.MonsterGoldGenerated, 75);
            LeaderboardService.Record(player, stat, 25);
            LeaderboardService.Record(player, stat, 0);
            LeaderboardService.Record(player, stat, -25);

            Assert.Equal(100L, player.GetLeaderboardStat(LeaderboardStat.GoldFarmed));
            Assert.Equal(100L, player.GetLeaderboardStat(LeaderboardStat.MonsterGoldGenerated));
            Assert.Equal(100L, GoldFarmedService.GetTotal(player));
        }
        finally
        {
            player.Delete();
        }
    }

    [Fact]
    public void Counters_SaturateAtLimitsAndRejectNegativeUpdates()
    {
        var player = CreatePlayer("Leaderboard Saturation Player");

        try
        {
            for (var stat = LeaderboardStat.OreMined; stat <= LeaderboardStat.GoldFarmed; stat++)
            {
                player.SetLeaderboardStat(stat, -1);
                Assert.Equal(0L, player.GetLeaderboardStat(stat));
                player.SetLeaderboardStat(stat, long.MaxValue - 1);
                LeaderboardService.Record(player, stat, 10);
                player.AddLeaderboardStat(stat, -10);
                Assert.Equal(long.MaxValue, player.GetLeaderboardStat(stat));
            }

            player.TotalOreMined = int.MaxValue - 1;
            player.IncrementOreMined(5);
            Assert.Equal(int.MaxValue, player.TotalOreMined);
            Assert.Equal(long.MaxValue, player.GetLeaderboardStat(LeaderboardStat.OreMined));
        }
        finally
        {
            player.Delete();
        }
    }

    [Fact]
    public void LegacyPlayerCounters_RoundTripTheSameNineFields()
    {
        var donor = CreatePlayer("Leaderboard Save Donor");
        var recipient = CreatePlayer("Leaderboard Save Recipient");
        var writer = new BufferWriter(true);

        try
        {
            for (var stat = LeaderboardStat.OreMined; stat <= LeaderboardStat.IngotsUsed; stat++)
            {
                donor.SetLeaderboardStat(stat, 100 + (int)stat);
            }

            GoldFarmedService.Record(donor, 25);

            donor.SerializeLeaderboardStats(writer);
            Assert.Equal(9L * sizeof(long), writer.Position);
            var reader = new BufferReader(writer.Buffer.AsSpan(0, (int)writer.Position).ToArray());
            recipient.DeserializeLeaderboardStats(reader);

            Assert.Equal(writer.Position, reader.Position);
            for (var stat = LeaderboardStat.OreMined; stat <= LeaderboardStat.IngotsUsed; stat++)
            {
                Assert.Equal(donor.GetLeaderboardStat(stat), recipient.GetLeaderboardStat(stat));
            }

            Assert.Equal(127L, donor.GetLeaderboardStat(LeaderboardStat.GoldFarmed));
            Assert.Equal(127L, recipient.GetLeaderboardStat(LeaderboardStat.GoldFarmed));
        }
        finally
        {
            writer.Close();
            donor.Delete();
            recipient.Delete();
        }
    }

    private static PlayerMobile CreatePlayer(string name, bool serialConstructor = true)
    {
        var player = serialConstructor ? new PlayerMobile(World.NewMobile) : new PlayerMobile();
        if (serialConstructor)
        {
            player.DefaultMobileInit();
        }

        player.Player = true;
        player.Name = name;
        return player;
    }
}
