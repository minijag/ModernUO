using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Server;
using Server.Engines.ModularDungeon;
using Server.Engines.PvpEvents;
using Server.Items;
using Server.Mobiles;
using Server.Network;
using Server.Tests;
using Server.Tests.Network;
using Xunit;

namespace UOContent.Tests.Engines.ModularDungeon;

[Collection("Sequential UOContent Tests")]
public sealed class ExtractionEventStatisticsTests : IDisposable
{
    private readonly List<IEntity> _created = [];
    private readonly List<NetState> _states = [];
    private readonly DateTime _previousNow = Core.Now;
    private readonly PvpEventLedger _previousLedger = PvpEventHistory.Ledger;
    private readonly byte[] _previousJournal = JournalBytes();

    public ExtractionEventStatisticsTests()
    {
        Core._now = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
        Timer.Init(Core.TickCount);
        PvpEventHistory.Ledger = new PvpEventLedger();
        ClearJournal();
    }

    public void Dispose()
    {
        ExtractionEventDeath.ControllerLookup = null;
        for (var i = _created.Count - 1; i >= 0; i--)
        {
            if (_created[i] is Mobile mobile)
            {
                mobile.NetState = null;
            }
            _created[i].Delete();
        }
        foreach (var state in _states)
        {
            state.Dispose();
        }
        ExtractionEventStatistics.Deserialize(new BufferReader(_previousJournal));
        PvpEventHistory.Ledger = _previousLedger;
        Core._now = _previousNow;
        Timer.Init(Core.TickCount);
    }

    [Fact]
    public void NaturalFinishRetainsEarlyExtractorsDeathAndTimeoutWithoutCreatingRewards()
    {
        var escaped = Player("Escaped");
        var dead = Player("Defeated");
        var expired = Player("Late");
        var (controller, _) = Start(escaped, dead, expired);
        dead.Body = 0x192;
        controller.NotifyDeath(dead);
        Core._now += ExtractionEventController.ExtractionDelay;
        Assert.True(controller.TryExtract(escaped, out _));
        escaped.Name = "Renamed after leaving";
        Assert.Null(PvpEventHistory.Last(EventKind.Extraction));
        Finish(controller);

        var result = PvpEventHistory.Last(EventKind.Extraction);
        Assert.NotNull(result);
        Assert.Equal(controller.RoundNumber, result.RoundNumber);
        Assert.Equal(3, result.Rows.Count);
        var winner = Row(escaped);
        Assert.Equal("Escaped", winner.Name);
        Assert.Equal(1, winner.Points);
        Assert.Equal(1, winner.Matches);
        Assert.Equal(1, winner.Wins);
        Assert.Equal("Extracted", winner.Outcome);
        Assert.Equal("Died", Row(dead).Outcome);
        Assert.Equal(1, Row(dead).Deaths);
        Assert.Equal("Time expired", Row(expired).Outcome);
        Assert.Equal(1, Row(expired).Deaths);
        Assert.All(result.Rows, row =>
        {
            Assert.Equal(0, row.Rewards);
            Assert.Equal(0, row.Penalties);
            Assert.Equal(0, row.Healing);
        });
        Assert.Equal(0, JournalCount());
    }

    [Fact]
    public void AllExtractedStillWaitsForNaturalDeadlineAndCommitsExactlyOnce()
    {
        var player = Player();
        var (controller, _) = Start(player);
        Core._now += ExtractionEventController.ExtractionDelay;
        Assert.True(controller.TryExtract(player, out _));
        Assert.False(controller.TryExtract(player, out _));
        Assert.Empty(controller.Participants);
        Assert.Null(PvpEventHistory.Last(EventKind.Extraction));
        Finish(controller);
        controller.Advance();
        Assert.False(ExtractionEventStatistics.Complete(controller, Core.Now));
        Assert.Equal(1, PvpEventHistory.Ledger.Count);
        Assert.Equal(1, Row(player).Matches);
    }

    [Fact]
    public void DelayedRecoveryAcrossQuarterBoundaryUsesTheScheduledMatchEnd()
    {
        Core._now = new DateTime(2026, 9, 30, 23, 40, 0, DateTimeKind.Utc);
        var player = Player();
        var (controller, _) = Start(player);
        var deadline = controller.MatchDeadline;
        Core._now += ExtractionEventController.ExtractionDelay;
        Assert.True(controller.TryExtract(player, out _));
        Core._now = new DateTime(2026, 10, 1, 0, 5, 0, DateTimeKind.Utc);
        controller.Advance();
        AdvanceToWaiting(controller);

        Assert.Equal(deadline, PvpEventHistory.Last(EventKind.Extraction).FinishedUtc);
        Assert.Equal(1, Assert.Single(PvpEventHistory.Rankings(EventKind.Extraction, StatsScope.Seasonal, deadline)).Wins);
        Assert.Empty(PvpEventHistory.Rankings(EventKind.Extraction, StatsScope.Seasonal, Core.Now));
        Assert.Equal(1, Assert.Single(PvpEventHistory.Rankings(EventKind.Extraction, StatsScope.Lifetime, Core.Now)).Wins);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdministrativeStopDiscardsWholeRoundIncludingEarlyExtractors(bool allExtracted)
    {
        var first = Player();
        var second = Player();
        var (controller, _) = Start(first, second);
        Core._now += ExtractionEventController.ExtractionDelay;
        Assert.True(controller.TryExtract(first, out _));
        if (allExtracted)
        {
            Assert.True(controller.TryExtract(second, out _));
        }
        Assert.True(controller.TryStop(out _));
        AdvanceToWaiting(controller);
        Assert.Null(PvpEventHistory.Last(EventKind.Extraction));
        Assert.Equal(0, JournalCount());
    }

    [Fact]
    public void FailedRecoveryDiscardsStatisticsWithoutClaimingACompleteMatch()
    {
        var player = Player();
        var (controller, integration) = Start(player);
        integration.FailFinish = true;
        Core._now += ExtractionEventController.ExtractionDelay;
        Assert.False(controller.TryExtract(player, out _));
        Assert.Equal(ExtractionEventPhase.Faulted, controller.Phase);
        Assert.Null(PvpEventHistory.Last(EventKind.Extraction));
        Assert.Equal(0, JournalCount());
        integration.FailFinish = false;
        Assert.True(controller.TryStop(out _));
        AdvanceToWaiting(controller);
        Assert.Null(PvpEventHistory.Last(EventKind.Extraction));
    }

    [Fact]
    public void RejectedStopDoesNotDiscardAResumableActiveJournal()
    {
        var (controller, integration) = Start(Player());
        integration.PolicyReady = false;
        Assert.False(controller.TryStop(out _));
        Assert.Equal(ExtractionEventPhase.Active, controller.Phase);
        Assert.Equal(1, JournalCount());
        integration.PolicyReady = true;
        Finish(controller);
        Assert.Equal(1, PvpEventHistory.Ledger.Count);
    }

    [Fact]
    public void EmptyLobbyAndFailedPreparationDoNotCreateAStatisticsRoster()
    {
        var integration = new Integration();
        var controller = Controller(integration);
        Assert.False(controller.TryStart(out _));
        Assert.Equal(0, JournalCount());
        integration.Lobby.Add(Player());
        integration.FailAdmission = true;
        Assert.True(controller.TryStart(out _));
        for (var i = 0; i < 10 && controller.Phase == ExtractionEventPhase.Starting; i++)
        {
            controller.Advance();
        }
        Assert.Equal(ExtractionEventPhase.Faulted, controller.Phase);
        Assert.Equal(0, JournalCount());
        Assert.Null(PvpEventHistory.Last(EventKind.Extraction));
    }

    [Fact]
    public void LegacyActiveRoundWithoutJournalIsNotFalselyBackfilled()
    {
        var (controller, _) = Start(Player());
        ClearJournal();
        Finish(controller);
        Assert.Null(PvpEventHistory.Last(EventKind.Extraction));
        Assert.Equal(ExtractionEventPhase.Waiting, controller.Phase);
    }

    [Fact]
    public void DeletedPlayerRemainsInTheCompletedRosterAsUnavailable()
    {
        var player = Player("Deleted character");
        var (controller, _) = Start(player);
        player.NetState = null;
        player.Delete();
        controller.Advance();
        Finish(controller);
        Assert.Equal("Deleted character", Row(player).Name);
        Assert.Equal("Unavailable", Row(player).Outcome);
        Assert.Equal(0, Row(player).Wins);
        Assert.Equal(1, Row(player).Matches);
    }

    [Fact]
    public void JournalAndControllerRoundTripPreserveRemovedPlayersAndFinishExactlyOnce()
    {
        var extracted = Player("Saved extractor");
        var remaining = Player("Remaining");
        var (controller, integration) = Start(extracted, remaining);
        Core._now += ExtractionEventController.ExtractionDelay;
        Assert.True(controller.TryExtract(extracted, out _));
        var journal = JournalBytes();
        var controllerBytes = Bytes(controller.Serialize);
        var serial = controller.Serial;
        controller.Delete();
        extracted.NetState = null;
        extracted.Delete();
        Assert.Equal(0, JournalCount());

        ExtractionEventStatistics.Deserialize(new BufferReader(journal));
        var restored = Track(new ExtractionEventController(serial));
        restored.Deserialize(new BufferReader(controllerBytes));
        restored.BindIntegration(integration);
        Assert.Equal(ExtractionEventPhase.Active, restored.Phase);
        Finish(restored);
        Assert.Equal(1, PvpEventHistory.Ledger.Count);
        Assert.Equal("Saved extractor", Row(extracted).Name);
        Assert.Equal(1, Row(extracted).Wins);
        Assert.Equal("Time expired", Row(remaining).Outcome);
    }

    [Fact]
    public void NativeDeathHookCreditsOnlyOneKillAndDoesNotRelocateVictim()
    {
        var killer = Player("Killer");
        var victim = Player("Victim");
        var (controller, _) = Start(killer, victim);
        ExtractionEventDeath.ControllerLookup = () => controller;
        victim.RegisterDamage(25, killer);
        victim.Body = 0x192;
        var location = victim.Location;
        ExtractionEventDeath.OnPlayerDeath(victim);
        ExtractionEventDeath.OnPlayerDeath(victim);
        Assert.Equal(location, victim.Location);
        Assert.True(controller.IsParticipant(victim));
        controller.NotifyDeath(victim);
        Finish(controller);
        Assert.Equal(1, Row(killer).Kills);
        Assert.Equal(1, Row(victim).Deaths);
    }

    [Fact]
    public void NativeDeathHookResolvesAControlledCreatureToItsEnrolledMaster()
    {
        var killer = Player();
        var victim = Player();
        var (controller, _) = Start(killer, victim);
        ExtractionEventDeath.ControllerLookup = () => controller;
        var pet = Track(new Dog { Controlled = true, ControlMaster = killer });
        victim.RegisterDamage(25, pet);
        victim.Body = 0x192;
        ExtractionEventDeath.OnPlayerDeath(victim);
        controller.NotifyDeath(victim);
        Finish(controller);
        Assert.Equal(1, Row(killer).Kills);
    }

    [Theory]
    [InlineData("Self")]
    [InlineData("Unenrolled")]
    [InlineData("Outside")]
    [InlineData("AlreadyExtracted")]
    [InlineData("AtDeadline")]
    public void NonCompetitiveOrTimeoutDeathsNeverCreditAPlayerKill(string invalid)
    {
        var killer = Player();
        var victim = Player();
        var (controller, _) = Start(killer, victim);
        Mobile attacker = killer;
        switch (invalid)
        {
            case "Self":
                attacker = victim;
                break;
            case "Unenrolled":
                attacker = Player();
                break;
            case "Outside":
                killer.MoveToWorld(ExtractionDungeonLayout.LobbyEntry, Map.Felucca);
                break;
            case "AlreadyExtracted":
                Core._now += ExtractionEventController.ExtractionDelay;
                Assert.True(controller.TryExtract(killer, out _));
                killer.MoveToWorld(Integration.ArenaPoint, Map.Felucca);
                break;
            case "AtDeadline":
                Core._now = controller.MatchDeadline;
                break;
        }
        victim.Body = 0x192;
        controller.RecordStatisticsDeath(victim, attacker);
        Finish(controller);
        Assert.All(PvpEventHistory.Last(EventKind.Extraction).Rows, row => Assert.Equal(0, row.Kills));
    }

    [Fact]
    public void ExistingStaffAdmissionAlsoReceivesAnOutcomeWithoutChangingEligibility()
    {
        var player = Player();
        player.AccessLevel = AccessLevel.GameMaster;
        var (controller, _) = Start(player);
        Finish(controller);
        Assert.Equal(1, Row(player).Matches);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1_000_001)]
    public void InvalidJournalCountsAreRejectedWithoutOverwritingValidPendingRound(int count)
    {
        var (controller, _) = Start(Player());
        var bad = Bytes(writer =>
        {
            writer.WriteEncodedInt(0);
            writer.WriteEncodedInt(count);
        });
        Assert.Throws<InvalidDataException>(() => ExtractionEventStatistics.Deserialize(new BufferReader(bad)));
        Assert.Equal(1, JournalCount());
        Finish(controller);
        Assert.Equal(1, PvpEventHistory.Ledger.Count);
    }

    [Fact]
    public void UnknownJournalVersionDoesNotClearExistingPendingRound()
    {
        var (controller, _) = Start(Player());
        var bad = Bytes(writer => writer.WriteEncodedInt(1));
        Assert.Throws<InvalidDataException>(() => ExtractionEventStatistics.Deserialize(new BufferReader(bad)));
        Finish(controller);
        Assert.Equal(1, PvpEventHistory.Ledger.Count);
    }

    private static PvpEventScore Row(Mobile player) =>
        Assert.Single(PvpEventHistory.Last(EventKind.Extraction).Rows, row => row.PlayerSerial == player.Serial.Value);

    private (ExtractionEventController Controller, Integration Integration) Start(params Mobile[] players)
    {
        var integration = new Integration();
        integration.Lobby.AddRange(players);
        var controller = Controller(integration);
        Assert.True(controller.TryStart(out var error), error);
        for (var i = 0; i < 1000 && controller.Phase == ExtractionEventPhase.Starting; i++)
        {
            controller.Advance();
        }
        Assert.Equal(ExtractionEventPhase.Active, controller.Phase);
        return (controller, integration);
    }

    private ExtractionEventController Controller(Integration integration)
    {
        var controller = Track(new ExtractionEventController());
        controller.BindIntegration(integration);
        return controller;
    }

    private PlayerMobile Player(string name = "Participant")
    {
        var player = Track(new PlayerMobile { Player = true, Body = 0x190, Name = name, Young = false });
        var now = Core.Now;
        var state = PacketTestUtilities.CreateTestNetState();
        Core._now = now;
        _states.Add(state);
        state.Account = new MockAccount();
        player.NetState = state;
        state.Mobile = player;
        player.MoveToWorld(ExtractionDungeonLayout.LobbyEntry, Map.Felucca);
        return player;
    }

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }

    private static void Finish(ExtractionEventController controller)
    {
        Core._now = controller.MatchDeadline;
        controller.Advance();
        AdvanceToWaiting(controller);
    }

    private static void AdvanceToWaiting(ExtractionEventController controller)
    {
        for (var i = 0; i < 1000 && controller.Phase == ExtractionEventPhase.Finishing; i++)
        {
            controller.Advance();
        }
        Assert.Equal(ExtractionEventPhase.Waiting, controller.Phase);
    }

    private static byte[] Bytes(Action<IGenericWriter> serialize)
    {
        var writer = new BufferWriter(true);
        serialize(writer);
        return writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();
    }

    private static byte[] JournalBytes() => Bytes(ExtractionEventStatistics.Serialize);

    private static int JournalCount()
    {
        IGenericReader reader = new BufferReader(JournalBytes());
        Assert.Equal(0, reader.ReadEncodedInt());
        return reader.ReadEncodedInt();
    }

    private static void ClearJournal() => ExtractionEventStatistics.Deserialize(new BufferReader(Bytes(writer =>
    {
        writer.WriteEncodedInt(0);
        writer.WriteEncodedInt(0);
    })));

    private sealed class Integration : IExtractionEventIntegration
    {
        internal static Point3D ArenaPoint => new(5260, 810, 0);
        public DateTime UtcNow => Core.Now;
        public bool PolicyReady { get; set; } = true;
        public List<Mobile> Lobby { get; } = [];
        public bool FailAdmission { get; set; }
        public bool FailFinish { get; set; }
        public IReadOnlyList<ExtractionModuleSpawn> ModuleSpawns { get; } = Enumerable.Range(0, 25)
            .Select(index => new ExtractionModuleSpawn(index, ArenaPoint)).ToArray();

        public bool IsInLobby(Mobile player) => player.Map == Map.Felucca && player.Location == ExtractionDungeonLayout.LobbyEntry;
        public bool IsInArena(Mobile player) => player.Map == Map.Felucca && player.Location == ArenaPoint;
        public Mobile[] SnapshotLobbyPlayers() => Lobby.ToArray();
        public bool TryMoveToLobby(Mobile player, out string error)
        {
            player.MoveToWorld(ExtractionDungeonLayout.LobbyEntry, Map.Felucca);
            error = null;
            return true;
        }
        public bool TryAdmit(ExtractionEventController controller, Mobile player, ExtractionModuleSpawn spawn, int moduleCycle,
            out string error)
        {
            player.MoveToWorld(spawn.Location, Map.Felucca);
            error = FailAdmission ? "Test admission failure." : null;
            return !FailAdmission;
        }
        public bool TryFinish(ExtractionEventController controller, Mobile player, ExtractionFinishReason reason, out string error)
        {
            if (FailFinish)
            {
                error = "Test recovery failure.";
                return false;
            }
            return TryMoveToLobby(player, out error);
        }
        public IExtractionEventResetSession CreateResetSession(ExtractionEventController controller) => new Reset();
        public void NotifyPhase(ExtractionEventController controller)
        {
        }
        private sealed class Reset : IExtractionEventResetSession
        {
            public bool Complete => true;
            public void Process(int operationBudget)
            {
            }
        }
    }
}
