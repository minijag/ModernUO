using System;
using Server;
using Server.Engines.Custom;
using Server.Guilds;
using Server.Mobiles;
using Xunit;

namespace UOContent.Tests;

[Collection("Sequential UOContent Tests")]
public class GuildProgressServiceTests : IDisposable
{
    public GuildProgressServiceTests() => GuildProgressService.SetBossTypes([]);

    public void Dispose() => GuildProgressService.SetBossTypes([]);

    [Theory]
    [InlineData(-1L, 1, 10)]
    [InlineData(0L, 1, 10)]
    [InlineData(1L, 1, 9)]
    [InlineData(9L, 1, 1)]
    [InlineData(10L, 2, 10)]
    [InlineData(19L, 2, 1)]
    [InlineData(20L, 3, 10)]
    public void StandingLevels_StartAtOneAndAdvanceEveryTenPoints(long points, int level, int remaining)
    {
        var progress = new GuildProgress(0, 0, points);

        Assert.Equal(level, progress.Level);
        Assert.Equal(remaining, progress.PointsToNextLevel);
    }

    [Fact]
    public void EmptyBossWhitelist_DoesNotAwardStandingForDifficulty()
    {
        using var fixture = new TestGuild();
        var boss = new TestBoss { Fame = 100000 };

        try
        {
            Assert.Equal(1, GuildProgressService.GetProgress(fixture.Guild).Level);
            Assert.False(GuildProgressService.IsStandingBoss(boss));
            Assert.False(GuildProgressService.RecordBossKill(fixture.Leader, boss));
            Assert.Equal(default, GuildProgressService.GetProgress(fixture.Guild));
        }
        finally
        {
            boss.Delete();
        }
    }

    [Fact]
    public void BossWhitelist_RequiresExactConcreteFullTypeName()
    {
        using var fixture = new TestGuild();
        var boss = new TestBoss();
        var otherBoss = new DerivedTestBoss();

        try
        {
            foreach (var name in new[]
                     {
                         typeof(TestBoss).Name, typeof(TestBoss).FullName.ToUpperInvariant(), typeof(BaseCreature).FullName
                     })
            {
                GuildProgressService.SetBossTypes([name]);
                Assert.False(GuildProgressService.IsStandingBoss(boss));
                Assert.False(GuildProgressService.RecordBossKill(fixture.Leader, boss));
            }

            GuildProgressService.SetBossTypes([typeof(TestBoss).FullName]);
            Assert.True(GuildProgressService.IsStandingBoss(boss));
            Assert.False(GuildProgressService.IsStandingBoss(otherBoss));
            Assert.False(GuildProgressService.RecordBossKill(fixture.Leader, otherBoss));
            Assert.True(GuildProgressService.RecordBossKill(fixture.Leader, boss));
            Assert.Equal(new GuildProgress(0, 1, 1), GuildProgressService.GetProgress(fixture.Guild));
        }
        finally
        {
            boss.Delete();
            otherBoss.Delete();
        }
    }

    [Fact]
    public void BossDeath_AwardsOnePointOnceAcrossContributorsAndGuilds()
    {
        using var first = new TestGuild();
        using var second = new TestGuild();
        var boss = new TestBoss();
        GuildProgressService.SetBossTypes([typeof(TestBoss).FullName]);

        try
        {
            Assert.True(GuildProgressService.RecordBossKill(first.Leader, boss));
            Assert.False(GuildProgressService.RecordBossKill(first.Leader, boss));
            Assert.False(GuildProgressService.RecordBossKill(second.Leader, boss));
            Assert.Equal(new GuildProgress(0, 1, 1), GuildProgressService.GetProgress(first.Guild));
            Assert.Equal(default, GuildProgressService.GetProgress(second.Guild));
        }
        finally
        {
            boss.Delete();
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ControlledAndSummonedBosses_DoNotAwardStanding(bool controlled, bool summoned)
    {
        using var fixture = new TestGuild();
        var boss = new TestBoss { Controlled = controlled, Summoned = summoned };
        GuildProgressService.SetBossTypes([typeof(TestBoss).FullName]);

        try
        {
            Assert.False(GuildProgressService.IsStandingBoss(boss));
            Assert.False(GuildProgressService.RecordBossKill(fixture.Leader, boss));
            Assert.Equal(default, GuildProgressService.GetProgress(fixture.Guild));
        }
        finally
        {
            boss.Delete();
        }
    }

    [Fact]
    public void TenDistinctBossDeaths_AdvanceGuildToLevelTwo()
    {
        using var fixture = new TestGuild();
        GuildProgressService.SetBossTypes([typeof(TestBoss).FullName]);

        for (var i = 0; i < 10; i++)
        {
            var boss = new TestBoss();
            try
            {
                Assert.True(GuildProgressService.RecordBossKill(fixture.Leader, boss));
            }
            finally
            {
                boss.Delete();
            }
        }

        var progress = GuildProgressService.GetProgress(fixture.Guild);
        Assert.Equal(new GuildProgress(0, 10, 10), progress);
        Assert.Equal(2, progress.Level);
        Assert.Equal(10, progress.PointsToNextLevel);
    }

    [Theory]
    [InlineData(LeaderboardStat.GoldFarmed)]
    [InlineData(LeaderboardStat.MonsterGoldGenerated)]
    public void GoldFarmed_CreditsCurrentGuildOnceWithoutAwardingStanding(LeaderboardStat stat)
    {
        using var fixture = new TestGuild();

        LeaderboardService.Record(fixture.Leader, stat, 125);

        Assert.Equal(125L, fixture.Leader.GetLeaderboardStat(LeaderboardStat.GoldFarmed));
        Assert.Equal(new GuildProgress(125, 0, 0), GuildProgressService.GetProgress(fixture.Guild));
        Assert.Equal(1, GuildProgressService.GetProgress(fixture.Guild).Level);
    }

    [Fact]
    public void HistoricalGold_DoesNotReplayIntoCurrentGuild()
    {
        using var fixture = new TestGuild();
        var donor = CreatePlayer();
        var writer = new BufferWriter(true);

        try
        {
            fixture.Leader.Name = "Historical Guild Gold Player";
            donor.SetLeaderboardStat(LeaderboardStat.MonsterGoldGenerated, 500);
            donor.SerializeLeaderboardStats(writer);
            var reader = new BufferReader(writer.Buffer.AsSpan(0, (int)writer.Position).ToArray());
            fixture.Leader.DeserializeLeaderboardStats(reader);

            Assert.Equal(500L, fixture.Leader.GetLeaderboardStat(LeaderboardStat.GoldFarmed));
            Assert.Equal(500L, GoldFarmedService.GetTotal(fixture.Leader));
            Assert.Contains(new LeaderboardEntry(fixture.Leader.Name, 500), LeaderboardService.GetTop(LeaderboardStat.GoldFarmed, 100));
            Assert.Equal(default, GuildProgressService.GetProgress(fixture.Guild));

            LeaderboardService.Record(fixture.Leader, LeaderboardStat.GoldFarmed, 150);

            Assert.Equal(650L, fixture.Leader.GetLeaderboardStat(LeaderboardStat.GoldFarmed));
            Assert.Equal(new GuildProgress(150, 0, 0), GuildProgressService.GetProgress(fixture.Guild));
        }
        finally
        {
            writer.Close();
            donor.Delete();
        }
    }

    [Fact]
    public void LeavingAndJoiningGuilds_PreservesEachGuildsEarnedHistory()
    {
        using var first = new TestGuild();
        using var second = new TestGuild();
        var member = CreatePlayer();
        var firstBoss = new TestBoss();
        var secondBoss = new TestBoss();
        GuildProgressService.SetBossTypes([typeof(TestBoss).FullName]);

        try
        {
            first.Guild.AddMember(member);
            LeaderboardService.Record(member, LeaderboardStat.GoldFarmed, 100);
            Assert.True(GuildProgressService.RecordBossKill(member, firstBoss));

            first.Guild.RemoveMember(member);
            LeaderboardService.Record(member, LeaderboardStat.MonsterGoldGenerated, 25);
            second.Guild.AddMember(member);

            Assert.Equal(new GuildProgress(100, 1, 1), GuildProgressService.GetProgress(first.Guild));
            Assert.Equal(default, GuildProgressService.GetProgress(second.Guild));

            LeaderboardService.Record(member, LeaderboardStat.MonsterGoldGenerated, 200);
            Assert.True(GuildProgressService.RecordBossKill(member, secondBoss));

            Assert.Equal(new GuildProgress(100, 1, 1), GuildProgressService.GetProgress(first.Guild));
            Assert.Equal(new GuildProgress(200, 1, 1), GuildProgressService.GetProgress(second.Guild));
            Assert.Equal(325L, member.GetLeaderboardStat(LeaderboardStat.GoldFarmed));
        }
        finally
        {
            firstBoss.Delete();
            secondBoss.Delete();
            member.Delete();
        }
    }

    [Fact]
    public void GuildReferenceWithoutMembership_DoesNotReceiveProgress()
    {
        using var fixture = new TestGuild();
        var nonMember = CreatePlayer();
        var boss = new TestBoss();
        GuildProgressService.SetBossTypes([typeof(TestBoss).FullName]);

        try
        {
            nonMember.Guild = fixture.Guild;
            GuildProgressService.RecordGold(nonMember, 100);

            Assert.False(GuildProgressService.RecordBossKill(nonMember, boss));
            Assert.Equal(default, GuildProgressService.GetProgress(fixture.Guild));
            Assert.True(GuildProgressService.RecordBossKill(fixture.Leader, boss));
        }
        finally
        {
            nonMember.Guild = null;
            nonMember.Delete();
            boss.Delete();
        }
    }

    [Fact]
    public void Disband_RemovesProgressFromPersistence()
    {
        using var fixture = new TestGuild();
        GuildProgressService.RecordGold(fixture.Leader, 100);

        fixture.Guild.Disband();

        Assert.Equal(default, GuildProgressService.GetProgress(fixture.Guild));
        IGenericReader reader = new BufferReader(SaveProgress());
        Assert.Equal(0, reader.ReadEncodedInt());
        var count = reader.ReadEncodedInt();
        for (var i = 0; i < count; i++)
        {
            Assert.NotEqual(fixture.Guild.Serial.Value, reader.ReadUInt());
            reader.ReadLong();
            reader.ReadLong();
            reader.ReadLong();
        }
    }

    [Fact]
    public void PersistenceRoundTrip_RestoresIndependentGuildTotals()
    {
        using var first = new TestGuild();
        using var second = new TestGuild();
        var boss = new TestBoss();
        GuildProgressService.SetBossTypes([typeof(TestBoss).FullName]);

        try
        {
            GuildProgressService.RecordGold(first.Leader, 100);
            GuildProgressService.RecordGold(second.Leader, 250);
            Assert.True(GuildProgressService.RecordBossKill(first.Leader, boss));
            var saved = SaveProgress();
            GuildProgressService.RemoveGuild(first.Guild);
            GuildProgressService.RemoveGuild(second.Guild);
            Assert.Equal(default, GuildProgressService.GetProgress(first.Guild));
            Assert.Equal(default, GuildProgressService.GetProgress(second.Guild));

            var reader = new BufferReader(saved);
            GuildProgressService.Deserialize(reader);

            Assert.Equal((long)saved.Length, reader.Position);
            Assert.Equal(new GuildProgress(100, 1, 1), GuildProgressService.GetProgress(first.Guild));
            Assert.Equal(new GuildProgress(250, 0, 0), GuildProgressService.GetProgress(second.Guild));
        }
        finally
        {
            boss.Delete();
        }
    }

    private static byte[] SaveProgress()
    {
        var writer = new BufferWriter(true);
        try
        {
            GuildProgressService.Serialize(writer);
            return writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();
        }
        finally
        {
            writer.Close();
        }
    }

    private static PlayerMobile CreatePlayer()
    {
        var player = new PlayerMobile(World.NewMobile);
        player.DefaultMobileInit();
        player.Player = true;
        return player;
    }

    private sealed class TestGuild : IDisposable
    {
        public PlayerMobile Leader { get; } = CreatePlayer();
        public Guild Guild { get; }

        public TestGuild() => Guild = new Guild(Leader, "Leaderboard Test Guild", "LTG");

        public void Dispose()
        {
            Guild.Disband();
            Leader.Delete();
        }
    }

    private class TestBoss : BaseCreature
    {
        public TestBoss() : base(World.NewMobile)
        {
            DefaultMobileInit();
            Body = 0xC8;
        }
    }

    private class DerivedTestBoss : TestBoss
    {
    }
}
