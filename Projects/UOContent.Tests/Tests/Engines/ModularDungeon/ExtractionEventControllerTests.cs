using System;
using System.Collections.Generic;
using System.Reflection;
using Server;
using Server.Engines.ModularDungeon;
using Server.Items;
using Server.Mobiles;
using Server.Network;
using Server.Tests.Network;
using Xunit;

namespace UOContent.Tests.Engines.ModularDungeon;

[Collection("Sequential UOContent Tests")]
public class ExtractionEventControllerTests : IDisposable
{
    private readonly List<IEntity> _created = [];
    private readonly List<NetState> _states = [];
    private readonly DateTime _startTime = Core.Now;
    private readonly long _startTicks = Core.TickCount;

    public ExtractionEventControllerTests() => Timer.Init(Core.TickCount);

    public void Dispose()
    {
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

        Core._now = _startTime;
        Core._tickCount = _startTicks;
        Timer.Init(Core.TickCount);
    }

    [Fact]
    public void UnconfiguredPolicy_CannotStartOrReset()
    {
        var integration = new FakeIntegration { PolicyReady = false };
        var controller = Create(integration);
        integration.Lobby.Add(Player());
        controller.ConfigureWaiting(TimeSpan.FromMinutes(5), true);
        Core._now += TimeSpan.FromMinutes(5);

        Assert.False(controller.TryStart(out var error));
        controller.Advance();

        Assert.NotNull(error);
        Assert.Equal(ExtractionEventPhase.Waiting, controller.Phase);
        Assert.Equal(0, integration.ResetCount);
        Assert.Empty(controller.Participants);
    }

    [Fact]
    public void EmptyLobby_DoesNotResetOrConsumeARound()
    {
        var integration = new FakeIntegration();
        var controller = Create(integration);

        Assert.False(controller.TryStart(out _));
        Assert.Equal(0, integration.ResetCount);
        Assert.Equal(0, controller.RoundNumber);
        Assert.Equal(ExtractionEventPhase.Waiting, controller.Phase);
    }

    [Fact]
    public void First25Spawns_UseOuterModulesThenInnerModules_ThenLoop()
    {
        var integration = new FakeIntegration();
        var controller = Create(integration);

        for (var index = 0; index < 51; index++)
        {
            integration.Lobby.Insert(0, Player());
        }

        Assert.True(controller.TryStart(out _));
        RunUntilActive(controller);

        int[] expected =
        [
            0, 1, 2, 3, 4, 5, 9, 10, 14, 15, 19, 20, 21, 22, 23, 24,
            6, 7, 8, 11, 12, 13, 16, 17, 18
        ];
        var previousSerial = 0u;

        for (var ordinal = 0; ordinal < integration.Admissions.Count; ordinal++)
        {
            var admission = integration.Admissions[ordinal];
            Assert.Equal(expected[ordinal % 25], admission.Spawn.ModuleId);
            Assert.Equal(ordinal / 25, admission.Cycle);
            Assert.True(admission.Player.Serial.Value > previousSerial);
            previousSerial = admission.Player.Serial.Value;
        }

        Assert.Equal(51, controller.Participants.Count);
        Assert.Equal(1, integration.ResetCount);
    }

    [Theory]
    [InlineData("Dead")]
    [InlineData("Disconnected")]
    [InlineData("Outside")]
    [InlineData("NonPlayer")]
    [InlineData("Deleted")]
    public void LobbySnapshot_ExcludesIneligiblePlayers(string invalidation)
    {
        var integration = new FakeIntegration();
        var controller = Create(integration);
        var eligible = Player();
        var excluded = Player();
        integration.Lobby.Add(eligible);
        integration.Lobby.Add(excluded);
        integration.Lobby.Add(eligible);

        switch (invalidation)
        {
            case "Dead":
            {
                excluded.Body = 0x192;
                break;
            }
            case "Disconnected":
            {
                excluded.NetState = null;
                break;
            }
            case "Outside":
            {
                excluded.MoveToWorld(new Point3D(120, 120, 0), Map.Felucca);
                break;
            }
            case "NonPlayer":
            {
                excluded.Player = false;
                break;
            }
            case "Deleted":
            {
                excluded.Delete();
                break;
            }
        }

        Assert.True(controller.TryStart(out _));
        RunUntilActive(controller);

        Assert.Single(controller.Participants);
        Assert.Same(eligible, controller.Participants[0]);
        Assert.Single(integration.Admissions);
    }

    [Fact]
    public void CreatureActivation_IsChunkedAfterAdmission_AndDoesNotConsumeTheMatchDeadline()
    {
        var integration = new FakeIntegration { ActivationOperations = 17 };
        var controller = Create(integration);
        integration.Lobby.Add(Player());
        Assert.True(controller.TryStart(out _));
        controller.Advance();
        Assert.True(integration.Session.Complete);

        controller.Advance();
        Assert.Single(controller.Participants);
        Assert.Equal(ExtractionEventPhase.Starting, controller.Phase);
        Assert.Equal(DateTime.MinValue, controller.MatchDeadline);
        Assert.Equal(new[] { 7 }, integration.ActivationBudgets);
        Core._now += TimeSpan.FromSeconds(3);
        controller.Advance();
        Assert.Equal(ExtractionEventPhase.Starting, controller.Phase);
        Assert.Equal(DateTime.MinValue, controller.MatchDeadline);
        Core._now += TimeSpan.FromSeconds(3);
        controller.Advance();

        Assert.Equal(new[] { 7, 8, 8 }, integration.ActivationBudgets);
        Assert.Equal(ExtractionEventPhase.Active, controller.Phase);
        Assert.Equal(Core.Now + TimeSpan.FromMinutes(15), controller.MatchDeadline);
        Assert.Equal(1, integration.ResetCount);
        Assert.Single(integration.Admissions);
    }

    [Fact]
    public void ResetAndAdmission_AreChunked_AndDeadlineStartsOnlyAfterAdmissionCompletes()
    {
        var integration = new FakeIntegration { ResetOperations = 17 };
        var controller = Create(integration);

        for (var index = 0; index < 17; index++)
        {
            integration.Lobby.Add(Player());
        }

        Assert.True(controller.TryStart(out _));

        for (var index = 0; index < 3; index++)
        {
            Core._now += TimeSpan.FromSeconds(10);
            controller.Advance();
            Assert.Equal(ExtractionEventPhase.Starting, controller.Phase);
            Assert.Empty(integration.Admissions);
            Assert.Equal(DateTime.MinValue, controller.MatchDeadline);
        }

        Assert.Equal(new[] { 8, 8, 8 }, integration.Session.Budgets);
        controller.Advance();
        Assert.Equal(8, integration.Admissions.Count);
        Assert.Equal(ExtractionEventPhase.Starting, controller.Phase);
        controller.Advance();
        Assert.Equal(16, integration.Admissions.Count);
        Assert.Equal(DateTime.MinValue, controller.MatchDeadline);
        Core._now += TimeSpan.FromSeconds(10);
        controller.Advance();

        Assert.Equal(ExtractionEventPhase.Active, controller.Phase);
        Assert.Equal(17, integration.Admissions.Count);
        Assert.Equal(Core.Now + TimeSpan.FromMinutes(15), controller.MatchDeadline);
    }

    [Fact]
    public void LateLobbyArrivals_WaitForTheNextMatch()
    {
        var integration = new FakeIntegration();
        var controller = Create(integration);
        var first = Player();
        integration.Lobby.Add(first);
        Assert.True(controller.TryStart(out _));
        var second = Player();
        integration.Lobby.Add(second);
        RunUntilActive(controller);
        var third = Player();
        integration.Lobby.Add(third);
        Assert.True(controller.TryJoinLobby(third, out _));

        Assert.True(controller.IsParticipant(first));
        Assert.False(controller.IsParticipant(second));
        Assert.False(controller.IsParticipant(third));
        Assert.Single(integration.Admissions);
    }

    [Fact]
    public void LeavingLobbyDuringPreparation_PreventsAdmission()
    {
        var integration = new FakeIntegration();
        var controller = Create(integration);
        integration.Lobby.Add(Player());
        var leaver = Player();
        integration.Lobby.Add(leaver);
        Assert.True(controller.TryStart(out _));
        leaver.MoveToWorld(new Point3D(120, 120, 0), Map.Felucca);
        RunUntilActive(controller);

        Assert.Single(controller.Participants);
        Assert.False(controller.IsParticipant(leaver));
    }

    [Fact]
    public void ActiveTicks_NeverResetOrRespawnAgain()
    {
        var integration = new FakeIntegration();
        var controller = Create(integration);
        integration.Lobby.Add(Player());
        Assert.True(controller.TryStart(out _));
        RunUntilActive(controller);
        var resetCalls = integration.Session.Budgets.Count;

        for (var index = 0; index < 100; index++)
        {
            Core._now += TimeSpan.FromSeconds(1);
            controller.Advance();
        }

        Assert.Equal(1, integration.ResetCount);
        Assert.Equal(resetCalls, integration.Session.Budgets.Count);
        Assert.Equal(ExtractionEventPhase.Active, controller.Phase);
        Assert.False(controller.TryStart(out _));
    }

    [Fact]
    public void FifteenMinuteDeadline_EndsRoundWithoutASecondReset()
    {
        var integration = new FakeIntegration();
        var controller = Create(integration);
        integration.Lobby.Add(Player());
        Assert.True(controller.TryStart(out _));
        RunUntilActive(controller);
        Core._now = controller.MatchDeadline - TimeSpan.FromMilliseconds(1);
        controller.Advance();
        Assert.Equal(ExtractionEventPhase.Active, controller.Phase);
        Core._now = controller.MatchDeadline;
        controller.Advance();
        Assert.Equal(ExtractionEventPhase.Finishing, controller.Phase);
        controller.Advance();

        Assert.Equal(ExtractionEventPhase.Waiting, controller.Phase);
        Assert.Single(integration.Finishes);
        Assert.Equal(ExtractionFinishReason.TimeExpired, integration.Finishes[0].Reason);
        Assert.Empty(controller.Participants);
        Assert.Equal(1, integration.ResetCount);
    }

    [Fact]
    public void MatchCompletion_RecoversAtMostEightParticipantsPerTick()
    {
        var integration = new FakeIntegration();
        var controller = Create(integration);

        for (var index = 0; index < 19; index++)
        {
            integration.Lobby.Add(Player());
        }

        Assert.True(controller.TryStart(out _));
        RunUntilActive(controller);
        Core._now = controller.MatchDeadline;
        controller.Advance();
        controller.Advance();
        Assert.Equal(8, integration.Finishes.Count);
        Assert.Equal(11, controller.Participants.Count);
        Assert.Equal(ExtractionEventPhase.Finishing, controller.Phase);
        controller.Advance();
        Assert.Equal(16, integration.Finishes.Count);
        Assert.Equal(3, controller.Participants.Count);
        controller.Advance();

        Assert.Equal(19, integration.Finishes.Count);
        Assert.Empty(controller.Participants);
        Assert.Equal(ExtractionEventPhase.Waiting, controller.Phase);
    }

    [Fact]
    public void Extraction_RequiresMembershipAndArenaPresence_AndPreservesPossessions()
    {
        var integration = new FakeIntegration();
        var controller = Create(integration);
        var player = Player();
        var backpack = Track(new Backpack());
        var gold = Track(new Gold(777));
        player.AddItem(backpack);
        backpack.DropItem(gold);
        integration.Lobby.Add(player);
        Assert.True(controller.TryStart(out _));
        RunUntilActive(controller);
        Assert.False(controller.TryExtract(Player(), out _));
        player.MoveToWorld(FakeIntegration.LobbyPoint, Map.Felucca);
        Assert.False(controller.TryExtract(player, out _));
        player.MoveToWorld(integration.Admissions[0].Spawn.Location, Map.Felucca);
        Core._now = controller.MatchDeadline - ExtractionEventController.MatchDuration + ExtractionEventController.ExtractionDelay;

        Assert.True(controller.TryExtract(player, out _));
        Assert.False(controller.TryExtract(player, out _));
        Assert.False(controller.IsParticipant(player));
        Assert.Same(backpack, gold.Parent);
        Assert.Equal(777, gold.Amount);
        Assert.False(gold.Deleted);
        Assert.Single(integration.Finishes);
        Assert.Equal(ExtractionFinishReason.Extracted, integration.Finishes[0].Reason);
    }

    [Theory]
    [InlineData(419999, false)]
    [InlineData(420000, true)]
    [InlineData(900000, false)]
    public void ExtractionDelay_HasExactSevenMinuteOpeningAndFifteenMinuteDeadline(int elapsedMilliseconds, bool allowed)
    {
        var integration = new FakeIntegration();
        var controller = Create(integration);
        var player = Player();
        integration.Lobby.Add(player);
        Assert.True(controller.TryStart(out _));
        RunUntilActive(controller);
        Core._now = controller.MatchDeadline - ExtractionEventController.MatchDuration + TimeSpan.FromMilliseconds(elapsedMilliseconds);

        Assert.Equal(allowed, controller.ExtractionUnlocked);
        Assert.Equal(allowed, controller.TryExtract(player, out var error));
        if (allowed)
        {
            Assert.False(controller.IsParticipant(player));
            Assert.Equal(ExtractionFinishReason.Extracted, Assert.Single(integration.Finishes).Reason);
        }
        else
        {
            Assert.True(controller.IsParticipant(player));
            Assert.Empty(integration.Finishes);
            Assert.Equal(elapsedMilliseconds == 900000 ? ExtractionEventPhase.Finishing : ExtractionEventPhase.Active,
                controller.Phase);
            Assert.Contains(elapsedMilliseconds == 900000 ? "ended" : "seven minutes", error);
        }
    }

    [Fact]
    public void SevenMinuteThreshold_PublishesExactlyOnceAndTheAvailabilityGetterIsPure()
    {
        var integration = new FakeIntegration();
        var controller = Create(integration);
        integration.Lobby.Add(Player());
        Assert.False(controller.ExtractionUnlocked);
        Assert.True(controller.TryStart(out _));
        RunUntilActive(controller);
        var count = integration.PhaseNotifications.Count;
        var unlockAt = controller.MatchDeadline - ExtractionEventController.MatchDuration + ExtractionEventController.ExtractionDelay;
        Core._now = unlockAt - TimeSpan.FromMilliseconds(1);
        controller.Advance();
        Assert.False(controller.ExtractionUnlocked);
        Assert.Equal(count, integration.PhaseNotifications.Count);
        Core._now = unlockAt;
        Assert.True(controller.ExtractionUnlocked);
        Assert.True(controller.ExtractionUnlocked);
        Assert.Equal(count, integration.PhaseNotifications.Count);
        controller.Advance();
        Assert.Equal(count + 1, integration.PhaseNotifications.Count);
        Assert.Equal((ExtractionEventPhase.Active, true), integration.PhaseNotifications[^1]);
        controller.Advance();
        Core._now += TimeSpan.FromMinutes(1);
        controller.Advance();
        Assert.Equal(count + 1, integration.PhaseNotifications.Count);
        integration.PolicyReady = false;
        Assert.False(controller.ExtractionUnlocked);
        controller.Advance();
        Assert.Equal(count + 1, integration.PhaseNotifications.Count);
        integration.PolicyReady = true;
        Core._now = controller.MatchDeadline;
        controller.Advance();
        Assert.False(controller.ExtractionUnlocked);
        Assert.Equal((ExtractionEventPhase.Finishing, false), integration.PhaseNotifications[^1]);
    }

    [Theory]
    [InlineData(299999, false)]
    [InlineData(300000, true)]
    [InlineData(360000, true)]
    public void RestoredActiveRound_DerivesRemainingLockoutFromItsSavedDeadline(int downtimeMilliseconds, bool unlocked)
    {
        var integration = new FakeIntegration();
        var controller = Create(integration);
        var player = Player();
        integration.Lobby.Add(player);
        Assert.True(controller.TryStart(out _));
        RunUntilActive(controller);
        var deadline = controller.MatchDeadline;
        Core._now = deadline - ExtractionEventController.MatchDuration + TimeSpan.FromMinutes(2);
        var bytes = Snapshot(controller);
        controller.Delete();
        Core._now += TimeSpan.FromMilliseconds(downtimeMilliseconds);
        var restored = Track(new ExtractionEventController(World.NewItem));
        restored.Deserialize(new BufferReader(bytes));
        Assert.False(restored.ExtractionUnlocked);
        var freshIntegration = new FakeIntegration();
        restored.BindIntegration(freshIntegration);
        Assert.Equal(deadline, restored.MatchDeadline);
        Assert.Equal(unlocked, restored.ExtractionUnlocked);
        Assert.Equal((ExtractionEventPhase.Active, unlocked), Assert.Single(freshIntegration.PhaseNotifications));
        Assert.Equal(0, freshIntegration.ResetCount);
        restored.Advance();
        Assert.Single(freshIntegration.PhaseNotifications);
        if (!unlocked)
        {
            Assert.False(restored.TryExtract(player, out _));
            Core._now = deadline - ExtractionEventController.MatchDuration + ExtractionEventController.ExtractionDelay;
            restored.Advance();
            Assert.Equal(2, freshIntegration.PhaseNotifications.Count);
        }
        Assert.True(restored.ExtractionUnlocked);
        Assert.True(restored.TryExtract(player, out _));
    }

    [Fact]
    public void NextRound_GetsItsOwnSevenMinuteLockout()
    {
        var integration = new FakeIntegration();
        var controller = Create(integration);
        var player = Player();
        integration.Lobby.Add(player);
        Assert.True(controller.TryStart(out _));
        RunUntilActive(controller);
        var firstDeadline = controller.MatchDeadline;
        Core._now = firstDeadline - ExtractionEventController.MatchDuration + ExtractionEventController.ExtractionDelay;
        controller.Advance();
        Assert.True(controller.ExtractionUnlocked);
        Assert.True(controller.TryExtract(player, out _));
        Core._now = firstDeadline;
        controller.Advance();
        controller.Advance();
        Assert.Equal(ExtractionEventPhase.Waiting, controller.Phase);
        Assert.False(controller.ExtractionUnlocked);
        Assert.True(controller.TryStart(out _));
        RunUntilActive(controller);
        Assert.Equal(2, controller.RoundNumber);
        Assert.False(controller.ExtractionUnlocked);
        Assert.False(controller.TryExtract(player, out _));
        Core._now = controller.MatchDeadline - ExtractionEventController.MatchDuration + ExtractionEventController.ExtractionDelay;
        controller.Advance();
        Assert.True(controller.ExtractionUnlocked);
        Assert.True(controller.TryExtract(player, out _));
    }

    [Fact]
    public void ExactDeadline_CannotExtractAsSuccessfulEscape()
    {
        var integration = new FakeIntegration();
        var controller = Create(integration);
        var player = Player();
        integration.Lobby.Add(player);
        Assert.True(controller.TryStart(out _));
        RunUntilActive(controller);
        Core._now = controller.MatchDeadline;

        Assert.False(controller.TryExtract(player, out _));
        Assert.Empty(integration.Finishes);
        Assert.Equal(ExtractionEventPhase.Finishing, controller.Phase);
        controller.Advance();
        Assert.Equal(ExtractionFinishReason.TimeExpired, integration.Finishes[0].Reason);
    }

    [Fact]
    public void DeathNotification_ProcessesParticipantOnlyOnce()
    {
        var integration = new FakeIntegration();
        var controller = Create(integration);
        var player = Player();
        integration.Lobby.Add(player);
        Assert.True(controller.TryStart(out _));
        RunUntilActive(controller);
        player.Body = 0x192;
        controller.NotifyDeath(player);
        controller.NotifyDeath(player);
        controller.Advance();

        Assert.Single(integration.Finishes);
        Assert.Equal(ExtractionFinishReason.Death, integration.Finishes[0].Reason);
        Assert.False(controller.IsParticipant(player));
    }

    [Fact]
    public void ResetFailure_FaultsClosedWithoutAdmittingPlayers()
    {
        var integration = new FakeIntegration { ThrowReset = true };
        var controller = Create(integration);
        var player = Player();
        integration.Lobby.Add(player);
        Assert.True(controller.TryStart(out _));
        controller.Advance();
        controller.Advance();

        Assert.Equal(ExtractionEventPhase.Faulted, controller.Phase);
        Assert.Empty(integration.Admissions);
        Assert.Equal(FakeIntegration.LobbyPoint, player.Location);
        Assert.False(controller.TryStart(out _));
        Assert.False(controller.TryJoinLobby(player, out _));
        Assert.True(controller.TryStop(out _));
        Assert.Equal(ExtractionEventPhase.Waiting, controller.Phase);
    }

    [Fact]
    public void PartialTransportFailure_KeepsARecoveryReference()
    {
        var integration = new FakeIntegration { FailAdmission = true };
        var controller = Create(integration);
        var player = Player();
        integration.Lobby.Add(player);
        Assert.True(controller.TryStart(out _));
        controller.Advance();
        controller.Advance();

        Assert.Equal(ExtractionEventPhase.Faulted, controller.Phase);
        Assert.True(controller.IsParticipant(player));
        Assert.Single(controller.Participants);
        Assert.True(controller.TryStop(out _));
        controller.Advance();
        Assert.Equal(FakeIntegration.LobbyPoint, player.Location);
        Assert.Empty(controller.Participants);
    }

    [Fact]
    public void FailedFinish_PreservesReferencesAndPossessions_UntilStaffRecovery()
    {
        var integration = new FakeIntegration { FailFinish = true };
        var controller = Create(integration);
        var player = Player();
        var backpack = Track(new Backpack());
        var gold = Track(new Gold(99));
        player.AddItem(backpack);
        backpack.DropItem(gold);
        integration.Lobby.Add(player);
        Assert.True(controller.TryStart(out _));
        RunUntilActive(controller);

        Core._now = controller.MatchDeadline - ExtractionEventController.MatchDuration + ExtractionEventController.ExtractionDelay;
        Assert.False(controller.TryExtract(player, out _));
        Assert.Equal(ExtractionEventPhase.Faulted, controller.Phase);
        Assert.True(controller.IsParticipant(player));
        Assert.Same(backpack, gold.Parent);
        Assert.False(gold.Deleted);
        integration.FailFinish = false;
        Assert.True(controller.TryStop(out _));
        controller.Advance();
        Assert.Empty(controller.Participants);
        Assert.Equal(ExtractionEventPhase.Waiting, controller.Phase);
    }

    [Fact]
    public void PauseForRecovery_PersistsFaultAndRetainsParticipantsDeadlineAndOwnedReferences()
    {
        var integration = new FakeIntegration();
        var controller = Create(integration);
        var player = Player();
        var creature = Track(new Skeleton());
        var gold = Track(new Gold(42));
        controller.AddOwnedCreature(creature);
        controller.AddOwnedLoot(gold);
        integration.Lobby.Add(player);
        Assert.True(controller.TryStart(out _));
        RunUntilActive(controller);
        var deadline = controller.MatchDeadline;
        controller.PauseForRecovery("The recorded installation could not be verified.");
        controller.Advance();

        Assert.Equal(ExtractionEventPhase.Faulted, controller.Phase);
        Assert.Equal(deadline, controller.MatchDeadline);
        Assert.True(controller.IsParticipant(player));
        Assert.Same(creature, Assert.Single(controller.OwnedCreatures));
        Assert.Same(gold, Assert.Single(controller.OwnedLoot));
        Assert.Equal(42, gold.Amount);
        Assert.False(creature.Deleted);
        Assert.Empty(integration.Finishes);
        var bytes = Snapshot(controller);
        controller.Delete();
        var restored = Track(new ExtractionEventController(World.NewItem));
        restored.Deserialize(new BufferReader(bytes));
        restored.BindIntegration(new FakeIntegration());

        Assert.Equal(ExtractionEventPhase.Faulted, restored.Phase);
        Assert.Equal("The recorded installation could not be verified.", restored.FaultMessage);
        Assert.Equal(deadline, restored.MatchDeadline);
        Assert.True(restored.IsParticipant(player));
    }

    [Fact]
    public void ActiveRound_SaveLoadRetainsDeadlineReferencesAndLoot_WithoutReset()
    {
        var integration = new FakeIntegration();
        var controller = Create(integration);
        var player = Player();
        var creature = Track(new Skeleton());
        var loot = Track(new Gold(123));
        var managed = Track(new Item(0x1F14));
        var recovery = Track(new Backpack());
        controller.AddOwnedCreature(creature);
        controller.AddOwnedLoot(loot);
        controller.AddManagedItem(managed);
        controller.SetRecoveryStorage(recovery);
        integration.Lobby.Add(player);
        Assert.True(controller.TryStart(out _));
        RunUntilActive(controller);
        var deadline = controller.MatchDeadline;
        var bytes = Snapshot(controller);
        controller.Delete();
        Core._now += TimeSpan.FromMinutes(2);
        var restored = Track(new ExtractionEventController(World.NewItem));
        restored.Deserialize(new BufferReader(bytes));
        var freshIntegration = new FakeIntegration();
        restored.BindIntegration(freshIntegration);
        restored.Advance();

        Assert.Equal(deadline, restored.MatchDeadline);
        Assert.Equal(ExtractionEventPhase.Active, restored.Phase);
        Assert.Equal(1, restored.RoundNumber);
        Assert.True(restored.IsParticipant(player));
        Assert.Same(creature, Assert.Single(restored.OwnedCreatures));
        Assert.Same(loot, Assert.Single(restored.OwnedLoot));
        Assert.Same(managed, Assert.Single(restored.ManagedItems));
        Assert.Same(recovery, restored.RecoveryStorage);
        Assert.Equal(0, freshIntegration.ResetCount);
        Assert.Equal(123, loot.Amount);
    }

    [Fact]
    public void OfflineDeadlineExpiry_FinishesSavedRoundInsteadOfResettingIt()
    {
        var integration = new FakeIntegration();
        var controller = Create(integration);
        integration.Lobby.Add(Player());
        Assert.True(controller.TryStart(out _));
        RunUntilActive(controller);
        var bytes = Snapshot(controller);
        var deadline = controller.MatchDeadline;
        controller.Delete();
        Core._now = deadline + TimeSpan.FromHours(1);
        var restored = Track(new ExtractionEventController(World.NewItem));
        restored.Deserialize(new BufferReader(bytes));
        var freshIntegration = new FakeIntegration();
        restored.BindIntegration(freshIntegration);
        restored.Advance();
        restored.Advance();

        Assert.Equal(ExtractionEventPhase.Waiting, restored.Phase);
        Assert.Equal(0, freshIntegration.ResetCount);
        Assert.Equal(ExtractionFinishReason.TimeExpired, Assert.Single(freshIntegration.Finishes).Reason);
    }

    [Fact]
    public void PreparationInterruptedByRestart_FaultsWithoutReplayingLootOrSpawn()
    {
        var integration = new FakeIntegration { ResetOperations = 100 };
        var controller = Create(integration);
        integration.Lobby.Add(Player());
        Assert.True(controller.TryStart(out _));
        controller.Advance();
        var bytes = Snapshot(controller);
        controller.Delete();
        var restored = Track(new ExtractionEventController(World.NewItem));
        restored.Deserialize(new BufferReader(bytes));
        var freshIntegration = new FakeIntegration();
        restored.BindIntegration(freshIntegration);
        restored.Advance();

        Assert.Equal(ExtractionEventPhase.Faulted, restored.Phase);
        Assert.NotNull(restored.FaultMessage);
        Assert.Equal(0, freshIntegration.ResetCount);
        Assert.Empty(freshIntegration.Admissions);
    }

    [Theory]
    [InlineData("Count")]
    [InlineData("Duplicate")]
    [InlineData("Invalid")]
    public void InvalidModuleLayout_FaultsBeforeSchedulingGameplay(string invalidation)
    {
        var integration = new FakeIntegration();

        if (invalidation == "Count")
        {
            integration.Spawns.RemoveAt(24);
        }
        else
        {
            integration.Spawns[24] = integration.Spawns[24] with
            {
                ModuleId = invalidation == "Duplicate" ? 1 : 25
            };
        }

        var controller = Create(integration);
        Assert.Equal(ExtractionEventPhase.Faulted, controller.Phase);
        Assert.False(controller.TryStart(out _));
        Assert.Equal(0, integration.ResetCount);
    }

    [Fact]
    public void CountdownStartsOneRound_AndStaffStopDisablesAutomaticRounds()
    {
        var integration = new FakeIntegration();
        var controller = Create(integration);
        integration.Lobby.Add(Player());
        controller.ConfigureWaiting(TimeSpan.FromMinutes(15), true);
        Core._now += TimeSpan.FromMinutes(15);
        controller.Advance();
        Assert.Equal(ExtractionEventPhase.Starting, controller.Phase);
        Assert.Equal(1, integration.ResetCount);
        RunUntilActive(controller);
        Assert.True(controller.TryStop(out _));
        controller.Advance();
        Core._now += TimeSpan.FromHours(1);
        controller.Advance();

        Assert.Equal(ExtractionEventPhase.Waiting, controller.Phase);
        Assert.False(controller.AutomaticRounds);
        Assert.Equal(DateTime.MinValue, controller.WaitingDeadline);
        Assert.Equal(1, integration.ResetCount);
    }

    [Fact]
    public void AutomaticRounds_StartAtPreviousMatchEnd_WithoutAnExtraWaitingPeriod()
    {
        var integration = new FakeIntegration();
        var controller = Create(integration);
        integration.Lobby.Add(Player());
        controller.ConfigureWaiting(TimeSpan.FromMinutes(15), true);
        var firstStart = Core.Now + TimeSpan.FromMinutes(15);
        Assert.Equal(firstStart, controller.WaitingDeadline);
        Core._now = firstStart;
        controller.Advance();
        RunUntilActive(controller);
        var nextBoundary = controller.MatchDeadline;
        Core._now = nextBoundary;
        controller.Advance();
        controller.Advance();

        Assert.Equal(ExtractionEventPhase.Waiting, controller.Phase);
        Assert.Equal(nextBoundary, controller.WaitingDeadline);
        controller.Advance();
        Assert.Equal(ExtractionEventPhase.Starting, controller.Phase);
        Assert.Equal(2, integration.ResetCount);
        RunUntilActive(controller);
        Assert.Equal(2, controller.RoundNumber);
        Assert.Equal(Core.Now + TimeSpan.FromMinutes(15), controller.MatchDeadline);
    }

    [Fact]
    public void AllPlayersExtractingEarly_DoesNotEndOrShortenTheCurrentMatch()
    {
        var integration = new FakeIntegration();
        var controller = Create(integration);
        var player = Player();
        integration.Lobby.Add(player);
        controller.ConfigureWaiting(TimeSpan.FromMinutes(15), true);
        Assert.True(controller.TryStart(out _));
        RunUntilActive(controller);
        var deadline = controller.MatchDeadline;
        Core._now += ExtractionEventController.ExtractionDelay;
        Assert.True(controller.TryExtract(player, out _));
        controller.Advance();

        Assert.Empty(controller.Participants);
        Assert.Equal(ExtractionEventPhase.Active, controller.Phase);
        Assert.Equal(deadline, controller.MatchDeadline);
        Assert.Equal(1, integration.ResetCount);
        Assert.False(controller.TryStart(out _));
    }

    [Fact]
    public void EmptyLobby_SkipsMissedStartsWithoutDriftingTheFifteenMinuteCadence()
    {
        var integration = new FakeIntegration();
        var controller = Create(integration);
        controller.ConfigureWaiting(TimeSpan.FromMinutes(15), true);
        var firstBoundary = controller.WaitingDeadline;
        Core._now = firstBoundary + TimeSpan.FromMinutes(32);
        controller.Advance();

        Assert.Equal(firstBoundary + TimeSpan.FromMinutes(45), controller.WaitingDeadline);
        Assert.Equal(0, integration.ResetCount);
        integration.Lobby.Add(Player());
        Core._now = controller.WaitingDeadline;
        controller.Advance();
        Assert.Equal(ExtractionEventPhase.Starting, controller.Phase);
        Assert.Equal(1, integration.ResetCount);
    }

    [Fact]
    public void TickTimer_UsesFastBoundedPreparation_AndSlowerActivePolling()
    {
        var integration = new FakeIntegration { ResetOperations = 17 };
        var controller = Create(integration);
        integration.Lobby.Add(Player());
        var timerField = typeof(ExtractionEventController).GetField("_tickTimer", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(controller.TryStart(out _));
        AdvanceMilliseconds(256);

        Assert.Equal(ExtractionEventPhase.Starting, controller.Phase);
        Assert.Single(integration.Session.Budgets);
        var preparationTimer = (TimerExecutionToken)timerField.GetValue(controller);
        Assert.InRange((preparationTimer.Next - Core.Now).TotalMilliseconds, 1, 64);
        AdvanceMilliseconds(512);

        Assert.Equal(ExtractionEventPhase.Active, controller.Phase);
        Assert.Equal(new[] { 8, 8, 8 }, integration.Session.Budgets);
        var activeTimer = (TimerExecutionToken)timerField.GetValue(controller);
        Assert.True(activeTimer.Running);
        Assert.InRange((activeTimer.Next - Core.Now).TotalMilliseconds, 1, 256);
        Assert.Equal(1, integration.ResetCount);
    }

    [Fact]
    public void Delete_CancelsTimerAndClearsReferences_WithoutDeletingOwnedEntities()
    {
        var integration = new FakeIntegration();
        var controller = Create(integration);
        var creature = Track(new Skeleton());
        var loot = Track(new Gold(10));
        var managed = Track(new Item());
        controller.AddOwnedCreature(creature);
        controller.AddOwnedLoot(loot);
        controller.AddManagedItem(managed);
        var timerField = typeof(ExtractionEventController).GetField("_tickTimer", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(((TimerExecutionToken)timerField.GetValue(controller)).Running);
        controller.Delete();

        Assert.False(((TimerExecutionToken)timerField.GetValue(controller)).Running);
        Assert.Empty(controller.OwnedCreatures);
        Assert.Empty(controller.OwnedLoot);
        Assert.Empty(controller.ManagedItems);
        Assert.False(creature.Deleted);
        Assert.False(loot.Deleted);
        Assert.False(managed.Deleted);
        Assert.False(controller.IsBound);
    }

    private ExtractionEventController Create(FakeIntegration integration)
    {
        var controller = Track(new ExtractionEventController());
        controller.BindIntegration(integration);
        return controller;
    }

    private Mobile Player()
    {
        var state = PacketTestUtilities.CreateTestNetState();
        _states.Add(state);
        state.Account = new MockAccount();
        var player = Track(new Mobile { Player = true, Body = 0x190 });
        player.NetState = state;
        state.Mobile = player;
        player.MoveToWorld(FakeIntegration.LobbyPoint, Map.Felucca);
        return player;
    }

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }

    private static void RunUntilActive(ExtractionEventController controller)
    {
        for (var tick = 0; tick < 1000 && controller.Phase == ExtractionEventPhase.Starting; tick++)
        {
            controller.Advance();
        }

        Assert.Equal(ExtractionEventPhase.Active, controller.Phase);
    }

    private static byte[] Snapshot(Item item)
    {
        var writer = new BufferWriter(true);
        item.Serialize(writer);
        return writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();
    }

    private static void AdvanceMilliseconds(long milliseconds)
    {
        var deadline = Core.TickCount + milliseconds;

        while (Core.TickCount - deadline < 0)
        {
            Core._tickCount += Timer.TickRate;
            Core._now += TimeSpan.FromMilliseconds(Timer.TickRate);
            Timer.Slice(Core.TickCount);
        }
    }

    private sealed class FakeIntegration : IExtractionEventIntegration
    {
        public static readonly Point3D LobbyPoint = new(100, 100, 0);

        public FakeIntegration()
        {
            for (var moduleId = 24; moduleId >= 0; moduleId--)
            {
                Spawns.Add(new ExtractionModuleSpawn(moduleId, new Point3D(200 + moduleId, 200, 0)));
            }
        }

        public DateTime UtcNow => Core.Now;
        public bool PolicyReady { get; set; } = true;
        public int ResetOperations { get; set; } = 1;
        public int ActivationOperations { get; set; }
        public List<int> ActivationBudgets { get; } = [];
        public bool ThrowReset { get; set; }
        public bool FailAdmission { get; set; }
        public bool FailFinish { get; set; }
        public int ResetCount { get; private set; }
        public List<Mobile> Lobby { get; } = [];
        public List<ExtractionModuleSpawn> Spawns { get; } = [];
        public IReadOnlyList<ExtractionModuleSpawn> ModuleSpawns => Spawns;
        public List<(Mobile Player, ExtractionModuleSpawn Spawn, int Cycle)> Admissions { get; } = [];
        public List<(Mobile Player, ExtractionFinishReason Reason)> Finishes { get; } = [];
        public List<(ExtractionEventPhase Phase, bool Unlocked)> PhaseNotifications { get; } = [];
        public FakeResetSession Session { get; private set; }

        public bool IsInLobby(Mobile player) => player.Map == Map.Felucca && player.Location == LobbyPoint;
        public bool IsInArena(Mobile player) => player.Map == Map.Felucca && player.X >= 200 && player.Y == 200;
        public Mobile[] SnapshotLobbyPlayers() => Lobby.ToArray();

        public bool TryMoveToLobby(Mobile player, out string error)
        {
            player.MoveToWorld(LobbyPoint, Map.Felucca);
            error = null;
            return true;
        }

        public bool TryAdmit(
            ExtractionEventController controller,
            Mobile player,
            ExtractionModuleSpawn spawn,
            int moduleCycle,
            out string error
        )
        {
            player.MoveToWorld(spawn.Location, Map.Felucca);
            Admissions.Add((player, spawn, moduleCycle));
            error = FailAdmission ? "Test partial admission failure." : null;
            return !FailAdmission;
        }

        public bool TryFinish(
            ExtractionEventController controller,
            Mobile player,
            ExtractionFinishReason reason,
            out string error
        )
        {
            if (FailFinish)
            {
                error = "Test finish failure.";
                return false;
            }

            player.MoveToWorld(LobbyPoint, Map.Felucca);
            Finishes.Add((player, reason));
            error = null;
            return true;
        }

        public IExtractionEventResetSession CreateResetSession(ExtractionEventController controller)
        {
            ResetCount++;
            Session = new FakeResetSession(ResetOperations, ThrowReset);
            return Session;
        }

        public bool ActivatePreparedCreatures(ExtractionEventController controller, int operationBudget)
        {
            if (ActivationOperations <= 0)
            {
                return true;
            }

            ActivationBudgets.Add(operationBudget);
            ActivationOperations -= operationBudget;
            return ActivationOperations <= 0;
        }

        public void NotifyPhase(ExtractionEventController controller)
        {
            PhaseNotifications.Add((controller.Phase, controller.ExtractionUnlocked));
        }
    }

    private sealed class FakeResetSession(int operations, bool throwOnProcess) : IExtractionEventResetSession
    {
        private int _remaining = operations;
        public bool Complete => _remaining <= 0;
        public List<int> Budgets { get; } = [];

        public void Process(int operationBudget)
        {
            Budgets.Add(operationBudget);

            if (throwOnProcess)
            {
                throw new InvalidOperationException("Test reset failure.");
            }

            _remaining -= operationBudget;
        }
    }
}
