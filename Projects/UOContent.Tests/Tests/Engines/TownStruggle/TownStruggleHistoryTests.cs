using System;
using System.Collections.Generic;
using System.Reflection;
using Server;
using Server.Engines.PvpEvents;
using Server.Engines.TownStruggle;
using Server.Guilds;
using Server.Items;
using Server.Mobiles;
using Server.Network;
using Server.Tests.Network;
using Xunit;

namespace UOContent.Tests.Engines.TownStruggle;

[Collection("Sequential UOContent Tests")]
public class TownStruggleHistoryTests : IDisposable
{
    private const string VenueJson = """
        {"name":"History Town","mapId":0,"x":100,"y":100,"width":200,"height":200,
         "neutralBase":{"x":90,"y":90,"z":0},"orderBase":{"x":110,"y":110,"z":0},
         "chaosBase":{"x":280,"y":280,"z":0},
         "capturePoints":[{"x":140,"y":140,"z":0},{"x":240,"y":140,"z":0},{"x":200,"y":240,"z":0}],
         "respawnPoints":[{"x":140,"y":220,"z":0},{"x":220,"y":180,"z":0},{"x":160,"y":180,"z":0}]}
        """;
    private readonly List<IEntity> _created = [];
    private readonly List<NetState> _states = [];
    private readonly List<Guild> _guilds = [];
    private readonly PvpEventLedger _previousLedger = PvpEventHistory.Ledger;
    private readonly DateTime _now = Core.Now;
    private readonly long _ticks = Core.TickCount;

    public TownStruggleHistoryTests()
    {
        Timer.Init(Core.TickCount);
        PvpEventHistory.Ledger = new PvpEventLedger();
    }

    public void Dispose()
    {
        foreach (var entity in _created)
        {
            if (entity is TownStruggleController controller)
            {
                foreach (var participant in new List<TownStruggleParticipant>(controller.Participants))
                {
                    participant.Delete();
                }
            }
        }
        foreach (var guild in _guilds)
        {
            guild.Disband();
        }
        for (var index = _created.Count - 1; index >= 0; index--)
        {
            if (_created[index] is Mobile mobile)
            {
                mobile.NetState = null;
            }
            _created[index].Delete();
        }
        foreach (var state in _states)
        {
            state.Dispose();
        }
        TownStruggleCombat.Clear(null);
        PvpEventHistory.Ledger = _previousLedger;
        Core._now = _now;
        Core._tickCount = _ticks;
        Timer.Init(Core.TickCount);
    }

    [Fact]
    public void CompletedResultSurvivesRosterResetAndUsesMatchFinishQuarter()
    {
        Core._now = new DateTime(2026, 6, 30, 23, 20, 0, DateTimeKind.Utc);
        var (controller, _) = Active();
        var entry = controller.Participants[0];
        var playerSerial = entry.PlayerSerial;
        entry.ControlPoints = 12.5;
        entry.KillPoints = 20;
        var matchEnd = controller.MatchDeadline;
        Core._now = matchEnd;
        controller.Advance();
        Assert.Null(PvpEventHistory.Last(EventKind.TownStruggle));
        Core._now = controller.ResultsDeadline;
        Assert.Equal(7, Core.Now.Month);
        controller.Advance();
        Assert.Equal(TownStrugglePhase.Dormant, controller.Phase);
        Assert.Empty(controller.Participants);
        var result = PvpEventHistory.Last(EventKind.TownStruggle);
        Assert.NotNull(result);
        Assert.Equal(matchEnd, result.FinishedUtc);
        Assert.Equal("2026 Q2", PvpEventHistory.CurrentQuarter(result.FinishedUtc));
        Assert.Equal(controller.Serial.Value, result.ControllerSerial);
        Assert.Equal(controller.RoundNumber, result.RoundNumber);
        Assert.Equal("History Town", result.Venue);
        Assert.Equal(32.5, Find(result, playerSerial).Points);
        Assert.Equal(1, PvpEventHistory.Ledger.Count);
    }

    [Fact]
    public void RowsExposeCreditedKillAssistsDeathsHealingAndWinningTeam()
    {
        var (controller, _) = Active();
        var order = Find(controller, TownStruggleTeam.Order);
        var chaos = Find(controller, TownStruggleTeam.Chaos);
        order.ControlPoints = 2.5;
        order.KillPoints = 30;
        order.Deaths = 2;
        order.Healing = 107;
        SetOrderScore(controller, 60);
        var orderSerial = order.PlayerSerial;
        var chaosSerial = chaos.PlayerSerial;
        Complete(controller);
        var result = PvpEventHistory.Last(EventKind.TownStruggle);
        var row = Find(result, orderSerial);
        Assert.Equal(32.5, row.Points);
        Assert.Equal(3, row.Kills);
        Assert.Equal(2, row.Deaths);
        Assert.Equal(107, row.Healing);
        Assert.Equal(1, row.Matches);
        Assert.Equal(1, row.Wins);
        Assert.Equal("Win", row.Outcome);
        Assert.Equal("Order", row.Team);
        Assert.Equal("Loss", Find(result, chaosSerial).Outcome);
        Assert.Equal(60, result.OrderScore);
    }

    [Fact]
    public void DisqualifiedRowRetainsParticipationButForfeitsScoreRewardsAndWin()
    {
        var (controller, _) = Active();
        var entry = Find(controller, TownStruggleTeam.Order);
        var serial = entry.PlayerSerial;
        entry.ControlPoints = 50;
        entry.KillPoints = 20;
        entry.Healing = 80;
        entry.Disqualified = true;
        entry.Returned = true;
        SetOrderScore(controller, 100);
        Complete(controller);
        var row = Find(PvpEventHistory.Last(EventKind.TownStruggle), serial);
        Assert.Equal("Abandoned", row.Outcome);
        Assert.Equal(0, row.Points);
        Assert.Equal(0, row.Rewards);
        Assert.Equal(0, row.Wins);
        Assert.Equal(1, row.Penalties);
        Assert.Equal(1, row.Matches);
        Assert.Equal(2, row.Kills);
        Assert.Equal(80, row.Healing);
    }

    [Fact]
    public void LeavingDuringResultsRemainsInCompletedHistory()
    {
        var (controller, _) = Active();
        var entry = controller.Participants[0];
        var serial = entry.PlayerSerial;
        Core._now = controller.MatchDeadline;
        controller.Advance();
        Assert.True(controller.TryLeave(entry.Player, out var error), error);
        Assert.True(entry.Returned);
        Core._now = controller.ResultsDeadline;
        controller.Advance();
        var result = PvpEventHistory.Last(EventKind.TownStruggle);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("Draw", Find(result, serial).Outcome);
    }

    [Fact]
    public void RewardRetryAndRecoveryDoNotDuplicateCompletedResultOrTotals()
    {
        var (controller, host) = Active();
        var entry = controller.Participants[0];
        entry.KillPoints = 30;
        var serial = entry.PlayerSerial;
        Core._now = controller.MatchDeadline;
        controller.Advance();
        host.FailMoves = true;
        Core._now = controller.ResultsDeadline;
        controller.Advance();
        Assert.Equal(TownStrugglePhase.Faulted, controller.Phase);
        Assert.True(controller.RewardsCalculated);
        Assert.All(controller.Participants, participant => Assert.True(participant.RewardsDelivered));
        var original = PvpEventHistory.Last(EventKind.TownStruggle);
        Assert.Equal(30, Find(original, serial).Points);
        entry.KillPoints = 100;
        typeof(TownStruggleController).GetMethod("PayRewards", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(controller, null);
        Assert.Equal(1, PvpEventHistory.Ledger.Count);
        Assert.Same(original, PvpEventHistory.Last(EventKind.TownStruggle));
        host.FailMoves = false;
        Assert.True(controller.TryAbort(out var error), error);
        Assert.Equal(1, PvpEventHistory.Ledger.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AbortedSignupOrMatchDoesNotProduceCompletedHistory(bool active)
    {
        var (controller, _) = Create();
        Assert.True(controller.TryOpenSignup(out _));
        Assert.True(controller.TryJoin(Player(), out _));
        Assert.True(controller.TryJoin(Player(), out _));
        if (active)
        {
            Core._now = controller.SignupDeadline;
            controller.Advance();
        }
        Assert.True(controller.TryAbort(out var error), error);
        Assert.Null(PvpEventHistory.Last(EventKind.TownStruggle));
        Assert.Equal(0, PvpEventHistory.Ledger.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void CancelledSignupDoesNotProduceCompletedHistory(int count)
    {
        var (controller, _) = Create();
        Assert.True(controller.TryOpenSignup(out _));
        for (var i = 0; i < count; i++)
        {
            Assert.True(controller.TryJoin(Player(), out _));
        }
        Core._now = controller.SignupDeadline;
        controller.Advance();
        Assert.Equal(TownStrugglePhase.Dormant, controller.Phase);
        Assert.Equal(0, PvpEventHistory.Ledger.Count);
    }

    [Fact]
    public void SignupSnapshotsUnmodifiedNameAndOriginalGuildAbbreviation()
    {
        var (controller, _) = Create();
        var player = Player();
        player.Name = "Original fighter";
        player.NameMod = "Disguise";
        var guild = new Guild(player, "Original guild", "FIRST");
        _guilds.Add(guild);
        Assert.True(controller.TryOpenSignup(out _));
        Assert.True(controller.TryJoin(player, out _));
        var participant = controller.GetParticipant(player);
        Assert.Equal(player.Serial.Value, participant.PlayerSerial);
        Assert.Equal("Original fighter", participant.PlayerName);
        Assert.Equal("FIRST", participant.GuildAbbreviation);
        player.Name = "Later fighter";
        guild.Abbreviation = "LATER";
        participant.CaptureIdentity();
        Assert.Equal("Original fighter", participant.PlayerName);
        Assert.Equal("FIRST", participant.GuildAbbreviation);
    }

    [Fact]
    public void VersionTwoIdentitySurvivesMissingPlayerReferenceAndIsRecorded()
    {
        var (controller, _) = Active();
        var entry = controller.Participants[0];
        var serial = entry.PlayerSerial;
        var name = entry.PlayerName;
        entry.GuildAbbreviation = "OLD";
        entry.Player = null;
        var writer = new BufferWriter(true);
        entry.Serialize(writer);
        var restored = Track(new TownStruggleParticipant(World.NewItem));
        restored.Deserialize(new BufferReader(writer.Buffer.AsSpan(0, (int)writer.Position).ToArray()));
        Assert.Null(restored.Player);
        Assert.Equal(serial, restored.PlayerSerial);
        Assert.Equal(name, restored.PlayerName);
        Assert.Equal("OLD", restored.GuildAbbreviation);
        Complete(controller);
        var row = Find(PvpEventHistory.Last(EventKind.TownStruggle), serial);
        Assert.Equal(name, row.Name);
        Assert.Equal("OLD", row.Guild);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void LegacyParticipantMigrationPreservesFieldsAndCapturesAvailableIdentity(int version)
    {
        var player = Player();
        player.Name = "Legacy fighter";
        var guild = new Guild(player, "Legacy guild", null);
        _guilds.Add(guild);
        var vote = Player();
        var item = Track(new Item(1));
        var outside = Core.Now - TimeSpan.FromSeconds(7);
        var writer = new BufferWriter(true);
        item.Serialize(writer);
        writer.WriteEncodedInt(version);
        writer.Write(player);
        writer.WriteEnum(TownStruggleTeam.Chaos);
        writer.Write(true);
        writer.Write(Core.Now + TimeSpan.FromSeconds(12));
        writer.Write(Core.Now - TimeSpan.FromMinutes(3));
        writer.Write(12.5d);
        writer.Write(30);
        writer.Write(81);
        writer.Write(2);
        writer.Write(4);
        writer.Write(vote);
        writer.Write(new Point3D(51, 52, 3));
        writer.Write(Map.Felucca);
        writer.Write(true);
        writer.Write((int)guild.Serial.Value);
        writer.Write(true);
        if (version == 1)
        {
            writer.Write(outside);
        }
        var restored = Track(new TownStruggleParticipant(World.NewItem));
        restored.Deserialize(new BufferReader(writer.Buffer.AsSpan(0, (int)writer.Position).ToArray()));
        Assert.Same(player, restored.Player);
        Assert.Equal(TownStruggleTeam.Chaos, restored.Team);
        Assert.True(restored.Disqualified);
        Assert.Equal(Core.Now + TimeSpan.FromSeconds(12), restored.RespawnAt);
        Assert.Equal(Core.Now - TimeSpan.FromMinutes(3), restored.IdleSince);
        Assert.Equal(12.5d, restored.ControlPoints);
        Assert.Equal(30, restored.KillPoints);
        Assert.Equal(81, restored.Healing);
        Assert.Equal(2, restored.Deaths);
        Assert.Equal(4, restored.Rewards);
        Assert.Same(vote, restored.VoteFor);
        Assert.Equal(new Point3D(51, 52, 3), restored.ReturnLocation);
        Assert.Same(Map.Felucca, restored.ReturnMap);
        Assert.True(restored.RewardsDelivered);
        Assert.Equal((int)guild.Serial.Value, restored.GuildId);
        Assert.True(restored.Returned);
        Assert.Equal(version == 1 ? outside : DateTime.MinValue, restored.OutsideSince);
        Assert.Equal(player.Serial.Value, restored.PlayerSerial);
        Assert.Equal("Legacy fighter", restored.PlayerName);
        Assert.Null(restored.GuildAbbreviation);
        guild.Abbreviation = "LOADED";
        typeof(TownStruggleParticipant).GetMethod("AfterDeserialization", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(restored, null);
        Assert.Equal("LOADED", restored.GuildAbbreviation);
    }

    private (TownStruggleController Controller, Host Host) Create()
    {
        var controller = Track(new TownStruggleController());
        var host = new Host();
        controller.ConfigureVenue(VenueJson, host, false);
        return (controller, host);
    }

    private (TownStruggleController Controller, Host Host) Active()
    {
        var (controller, host) = Create();
        Assert.True(controller.TryOpenSignup(out var error), error);
        Assert.True(controller.TryJoin(Player(), out error), error);
        Assert.True(controller.TryJoin(Player(), out error), error);
        Core._now = controller.SignupDeadline;
        controller.Advance();
        Assert.Equal(TownStrugglePhase.Active, controller.Phase);
        return (controller, host);
    }

    private static void Complete(TownStruggleController controller)
    {
        Core._now = controller.MatchDeadline;
        controller.Advance();
        Assert.Equal(TownStrugglePhase.Results, controller.Phase);
        Core._now = controller.ResultsDeadline;
        controller.Advance();
    }

    private Mobile Player()
    {
        var eventTime = Core.Now;
        var state = PacketTestUtilities.CreateTestNetState();
        Core._now = eventTime;
        _states.Add(state);
        var account = new MockAccount();
        state.Account = account;
        var player = Track(new PlayerMobile
        {
            Player = true, Body = 0x190, RawStr = 100, RawDex = 100, RawInt = 100,
            Account = account, Name = "History fighter"
        });
        player.AddItem(Track(new Backpack()));
        player.NetState = state;
        state.Mobile = player;
        player.MoveToWorld(new Point3D(50, 50, 0), Map.Felucca);
        return player;
    }

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }

    private static TownStruggleParticipant Find(TownStruggleController controller, TownStruggleTeam team)
    {
        foreach (var entry in controller.Participants)
        {
            if (entry.Team == team)
            {
                return entry;
            }
        }
        throw new InvalidOperationException("Test team missing.");
    }

    private static PvpEventScore Find(PvpEventResult result, uint serial)
    {
        foreach (var row in result.Rows)
        {
            if (row.PlayerSerial == serial)
            {
                return row;
            }
        }
        throw new InvalidOperationException("Test result row missing.");
    }

    private static void SetOrderScore(TownStruggleController controller, int value) =>
        typeof(TownStruggleController).GetProperty(nameof(TownStruggleController.OrderScore))!.SetValue(controller, value);

    private sealed class Host : ITownStruggleHost
    {
        public DateTime Now => Core.Now;
        public bool FailMoves { get; set; }
        public bool Eligible(Mobile player) => player is { Deleted: false, Player: true, Alive: true, NetState: not null };
        public bool AtNeutralBase(Mobile player, TownStruggleVenue venue) => player.Map == venue.Map && player.Location == venue.NeutralBase;
        public bool Move(Mobile player, Point3D point, Map map)
        {
            if (FailMoves)
            {
                return false;
            }
            player.MoveToWorld(point, map);
            return true;
        }
        public bool MoveOutside(Mobile player, TownStruggleVenue venue, Point3D point, Map map) =>
            Move(player, new Point3D(venue.Area.Start.X - 2, venue.Area.Start.Y, 0), venue.Map);
        public void Notify(TownStruggleController controller) { }
    }
}
