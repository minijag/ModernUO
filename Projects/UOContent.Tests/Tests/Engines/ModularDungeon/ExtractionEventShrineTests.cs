using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Server;
using Server.Engines.ModularDungeon;
using Server.Gumps;
using Server.Items;
using Server.Mobiles;
using Server.Network;
using Server.Tests.Network;
using Xunit;

namespace UOContent.Tests.Engines.ModularDungeon;

[Collection("Sequential UOContent Tests")]
public class ExtractionEventShrineTests : IDisposable
{
    private readonly ExtractionEventRuntimeTests _runtimeFixture = new();
    private readonly List<IEntity> _created = [];
    private readonly List<NetState> _states = [];

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
        _runtimeFixture.Dispose();
    }

    [Fact]
    public void NativeAppearance_IsOneCompleteAltarWithRedFlameCandlesAndBones_AndDefaultIsInert()
    {
        var shrine = Track(new ExtractionEventShrine());
        shrine.MoveToWorld(ExtractionShrinePrototype.Point, Map.Felucca);
        Assert.True(shrine.ExactAppearance);
        Assert.False(shrine.ShareHue);
        Assert.True(shrine.HandlesOnMovement);
        Assert.Equal(18, shrine.Components.Count);
        Assert.Equal(9, CountComponents(shrine, item => item.ItemID is >= 0x120E and <= 0x1216));
        Assert.Equal(4, CountComponents(shrine, item => item.ItemID is 0x1854 or 0x1858));
        Assert.Equal(4, CountComponents(shrine, item => item.ItemID is 0xECC or 0xECE));
        var flame = shrine.Components[9];
        Assert.Equal(0x19AB, flame.ItemID);
        Assert.Equal(0x28, flame.Hue);
        Assert.Equal(new Point3D(0, 0, 8), flame.Offset);
        Assert.Equal(shrine.Z + 8, flame.Z);
        Assert.Equal(LightType.Circle300, flame.Light);
        Assert.Equal(1, CountComponents(shrine, item => item.ItemID == 0x19AB));
        Assert.False(shrine.TryBegin(Player(), out _));
        Assert.Equal(0, shrine.ActiveChannels);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtractionFlame_IsHiddenAndRejectsClicksUntilExactlyMinuteSeven(bool cairn)
    {
        var (controller, shrine) = Create(cairn);
        var player = Participant(controller, shrine.Location);
        var matchStart = Core.Now;
        SetField(controller, "_matchDeadline", matchStart + ExtractionEventController.MatchDuration);
        shrine.OnEventPhaseChanged(controller);
        var unlockAt = matchStart + ExtractionEventController.ExtractionDelay;
        foreach (var now in new[] { matchStart, unlockAt - TimeSpan.FromMilliseconds(1) })
        {
            Core._now = now;
            controller.Advance();
            Assert.False(controller.ExtractionUnlocked);
            Assert.False(shrine.Components[9].Visible);
            Assert.False(shrine.TryBegin(player, out var error));
            Assert.Contains("7 minutes", error);
            foreach (var component in shrine.Components)
            {
                component.OnDoubleClick(player);
                Assert.Equal(0, shrine.ActiveChannels);
            }
            shrine.OnDoubleClick(player);
            Assert.Equal(0, shrine.ActiveChannels);
            Assert.False(((TimerExecutionToken)GetField(shrine, "_validationTimer")).Running);
            Assert.Null(shrine.Portal);
            Assert.True(controller.IsParticipant(player));
            Assert.Equal(shrine.Location, player.Location);
        }

        Core._now = unlockAt;
        controller.Advance();
        Assert.True(controller.ExtractionUnlocked);
        Assert.True(shrine.Components[9].Visible);
        shrine.Components[9].OnDoubleClick(player);
        Assert.Equal(1, shrine.ActiveChannels);
        Assert.True(((TimerExecutionToken)GetField(shrine, "_validationTimer")).Running);
        Assert.Null(shrine.Portal);
        shrine.CancelAll();

        Core._now = controller.MatchDeadline;
        Assert.False(shrine.TryBegin(player, out _));
        Assert.Equal(ExtractionEventPhase.Finishing, controller.Phase);
        Assert.False(shrine.Components[9].Visible);
        Assert.Equal(0, shrine.ActiveChannels);
        Assert.Null(shrine.Portal);
    }

    [Fact]
    public void TwelveSeconds_SummonsOneSharedMoongateWithoutTeleporting_RepeatClickDoesNotRestart()
    {
        var (controller, shrine) = Create();
        var player = Participant(controller);
        var loot = Track(new Gold(987));
        player.Backpack.DropItem(loot);
        Assert.True(shrine.TryBegin(player, out var error), error);
        Advance(shrine, 6000);
        Assert.False(shrine.TryBegin(player, out _));
        Assert.True(controller.IsParticipant(player));
        Assert.Equal(1, shrine.ActiveChannels);
        Advance(shrine, 5999);
        Assert.Null(shrine.Portal);
        Assert.True(controller.IsParticipant(player));
        Advance(shrine, 1);
        Assert.True(controller.IsParticipant(player));
        Assert.Equal(shrine.Location, player.Location);
        var portal = Assert.IsType<ExtractionShrinePortal>(shrine.Portal);
        Assert.Equal(0x21, portal.Hue);
        Assert.Equal(MoongateAppearance.Blue, portal.ItemID);
        Assert.Equal(shrine.Location, portal.Location);
        Assert.Equal(0, shrine.ActiveChannels);
        EnterPortal(portal, player);
        Assert.False(controller.IsParticipant(player));
        Assert.True(ExtractionDungeonLayout.LobbyBounds.Contains(player.Location));
        Assert.True(player.Alive);
        Assert.Same(player.Backpack, loot.Parent);
        Assert.Equal(987, loot.Amount);
        Assert.Equal(0, shrine.ActiveChannels);
        var point = player.Location;
        Advance(shrine, 30000);
        Assert.Equal(point, player.Location);
    }

    [Fact]
    public void TakingDamage_DoesNotInterruptOrRestartTheRitual()
    {
        var (controller, shrine) = Create();
        var player = Participant(controller);
        player.Str = 100;
        player.Hits = 100;
        Assert.True(shrine.TryBegin(player, out _));
        Advance(shrine, 6000);
        player.Damage(5);
        Assert.Equal(95, player.Hits);
        Assert.Equal(1, shrine.ActiveChannels);
        Advance(shrine, 6000);
        Assert.True(player.Alive);
        Assert.Equal(95, player.Hits);
        Assert.True(controller.IsParticipant(player));
        Assert.NotNull(shrine.Portal);
        EnterPortal(shrine.Portal, player);
        Assert.False(controller.IsParticipant(player));
        Assert.True(ExtractionDungeonLayout.LobbyBounds.Contains(player.Location));
    }

    [Fact]
    public void MovementAwayAndBack_BetweenValidationTicks_CancelsImmediately()
    {
        var (controller, shrine) = Create();
        var player = Participant(controller);
        Assert.True(shrine.TryBegin(player, out _));
        var original = player.Location;
        player.MoveToWorld(new Point3D(original.X + 1, original.Y, original.Z), Map.Felucca);
        Assert.Equal(0, shrine.ActiveChannels);
        player.MoveToWorld(original, Map.Felucca);
        Advance(shrine, 12000);
        Assert.True(controller.IsParticipant(player));
        Assert.Equal(original, player.Location);
    }

    [Fact]
    public void WalkingNotification_CancelsEvenWhenPlayerHasAlreadyReturnedToTheStartTile()
    {
        var (controller, shrine) = Create();
        var player = Participant(controller);
        Assert.True(shrine.TryBegin(player, out _));
        shrine.OnMovement(player, new Point3D(player.X + 1, player.Y, player.Z));
        Assert.Equal(0, shrine.ActiveChannels);
        Advance(shrine, 12000);
        Assert.True(controller.IsParticipant(player));
    }

    [Fact]
    public void TwoPlayers_CanChannelIndependently_AndFirstCompletionCreatesOneSharedGate()
    {
        var (controller, shrine) = Create();
        var first = Participant(controller);
        var second = Participant(controller, new Point3D(shrine.X, shrine.Y + 1, shrine.Z));
        Assert.True(shrine.TryBegin(first, out _));
        Advance(shrine, 3000);
        Assert.True(shrine.TryBegin(second, out _));
        Assert.Equal(2, shrine.ActiveChannels);
        Advance(shrine, 9000);
        Assert.True(controller.IsParticipant(first));
        Assert.True(controller.IsParticipant(second));
        Assert.Equal(0, shrine.ActiveChannels);
        var portal = Assert.IsType<ExtractionShrinePortal>(shrine.Portal);
        Advance(shrine, 3000);
        Assert.Same(portal, shrine.Portal);
        EnterPortal(portal, second);
        Assert.False(controller.IsParticipant(second));
        Assert.True(controller.IsParticipant(first));
        EnterPortal(portal, first);
        Assert.False(controller.IsParticipant(first));
        Assert.False(portal.Deleted);
    }

    [Theory]
    [InlineData("Disconnected")]
    [InlineData("Dead")]
    [InlineData("WrongMap")]
    [InlineData("Phase")]
    [InlineData("NotParticipant")]
    [InlineData("TamperedAppearance")]
    public void InvalidatedChannel_CannotExtract(string reason)
    {
        var (controller, shrine) = Create();
        var player = Participant(controller);
        Assert.True(shrine.TryBegin(player, out _));
        switch (reason)
        {
            case "Disconnected":
            {
                player.NetState = null;
                break;
            }
            case "Dead":
            {
                player.Body = 0x192;
                break;
            }
            case "WrongMap":
            {
                player.MoveToWorld(player.Location, Map.Trammel);
                break;
            }
            case "Phase":
            {
                SetField(controller, "_phase", ExtractionEventPhase.Waiting);
                break;
            }
            case "NotParticipant":
            {
                ((HashSet<Mobile>)GetField(controller, "_participantSet")).Remove(player);
                break;
            }
            case "TamperedAppearance":
            {
                shrine.Components[9].Hue++;
                break;
            }
        }
        Advance(shrine, 12000);
        Assert.Equal(0, shrine.ActiveChannels);
        Assert.False(ExtractionDungeonLayout.LobbyBounds.Contains(player.Location));
    }

    [Fact]
    public void ExactMatchTimeoutWinsOverRitualCompletion()
    {
        var (controller, shrine) = Create();
        var player = Participant(controller);
        SetField(controller, "_matchDeadline", Core.Now + TimeSpan.FromSeconds(12));
        Assert.True(shrine.TryBegin(player, out _));
        Advance(shrine, 12000);
        Assert.Null(shrine.Portal);
        Assert.True(controller.IsParticipant(player));
        Assert.Equal(ExtractionEventPhase.Finishing, controller.Phase);
        Assert.False(ExtractionDungeonLayout.LobbyBounds.Contains(player.Location));
        Assert.Equal(0, shrine.ActiveChannels);
    }

    [Fact]
    public void ANewRoundBetweenValidationTicks_CannotInheritAPreviousRoundsChannel()
    {
        var (controller, shrine) = Create();
        var player = Participant(controller);
        Assert.True(shrine.TryBegin(player, out _));
        SetField(controller, "_roundNumber", controller.RoundNumber + 1);
        Advance(shrine, 12000);
        Assert.Equal(0, shrine.ActiveChannels);
        Assert.True(controller.IsParticipant(player));
        Assert.False(ExtractionDungeonLayout.LobbyBounds.Contains(player.Location));
    }

    [Fact]
    public void TransientChannelsAndTimerAreNotSerialized_AndNativeLightRemainsValidAfterLoad()
    {
        var (controller, shrine) = Create();
        var player = Participant(controller);
        var before = Snapshot(shrine);
        Assert.True(shrine.TryBegin(player, out _));
        var during = Snapshot(shrine);
        Assert.Equal(before, during);
        var restored = Track(new ExtractionEventShrine((Serial)0x40004567));
        restored.Deserialize(new BufferReader(during));
        Assert.Same(controller, restored.Controller);
        Assert.Equal(0, restored.ActiveChannels);
        Assert.False(((TimerExecutionToken)GetField(restored, "_validationTimer")).Running);
        foreach (var component in shrine.Components)
        {
            AddonComponent.ApplyLightTo(component);
        }
        Assert.True(shrine.ExactAppearance);
        restored.Components.Clear();
    }

    [Fact]
    public void UnregisteredCloneAndStaffOutsideMatch_CannotBypassEventEligibility()
    {
        var (controller, shrine) = Create();
        var player = Participant(controller);
        var clone = Track(new ExtractionEventShrine(controller));
        clone.MoveToWorld(shrine.Location, Map.Felucca);
        Assert.False(clone.TryBegin(player, out _));
        player.AccessLevel = AccessLevel.Administrator;
        SetField(controller, "_phase", ExtractionEventPhase.Waiting);
        Assert.False(shrine.TryBegin(player, out _));
        Assert.Equal(0, shrine.ActiveChannels);
    }

    [Fact]
    public void EveryNativeAddonComponent_AndRoot_BeginTheSameFlameRitual()
    {
        var (controller, shrine) = Create();
        var player = Participant(controller);
        for (var index = 0; index < shrine.Components.Count; index++)
        {
            var component = shrine.Components[index];
            component.OnDoubleClick(player);
            Assert.Equal(1, shrine.ActiveChannels);
            shrine.CancelAll();
        }
        shrine.OnDoubleClick(player);
        Assert.Equal(1, shrine.ActiveChannels);
    }

    [Theory]
    [InlineData("Move")]
    [InlineData("Map")]
    [InlineData("Delete")]
    [InlineData("Deserialize")]
    public void ShrineLifecycle_CancelsTransientTimerAndPlayerReferences(string operation)
    {
        var (controller, shrine) = Create();
        var player = Participant(controller);
        Assert.True(shrine.TryBegin(player, out _));
        Assert.True(((TimerExecutionToken)GetField(shrine, "_validationTimer")).Running);
        switch (operation)
        {
            case "Move":
            {
                shrine.MoveToWorld(new Point3D(shrine.X + 1, shrine.Y, shrine.Z), Map.Felucca);
                break;
            }
            case "Map":
            {
                shrine.Map = Map.Trammel;
                break;
            }
            case "Delete":
            {
                shrine.Delete();
                Assert.Null(shrine.Controller);
                Assert.All(shrine.Components, component => Assert.True(component.Deleted));
                break;
            }
            case "Deserialize":
            {
                typeof(ExtractionEventShrine).GetMethod("AfterDeserialization", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(shrine, null);
                break;
            }
        }
        Assert.Equal(0, shrine.ActiveChannels);
        Assert.False(((TimerExecutionToken)GetField(shrine, "_validationTimer")).Running);
        Advance(shrine, 12000);
        Assert.True(controller.IsParticipant(player));
    }

    [Fact]
    public void HugeWrappedTicks_DoNotCompleteEarly()
    {
        var (controller, shrine) = Create();
        var player = Participant(controller);
        Core._tickCount = long.MaxValue - 6000;
        Assert.True(shrine.TryBegin(player, out _));
        Advance(shrine, 11999);
        Assert.Null(shrine.Portal);
        Assert.True(controller.IsParticipant(player));
        Advance(shrine, 1);
        Assert.True(controller.IsParticipant(player));
        Assert.NotNull(shrine.Portal);
    }

    [Fact]
    public void RitualHasPublicCueSoundAndRedFlamePulses_AndCountdownIsPrivate()
    {
        var (controller, shrine) = Create();
        var player = Participant(controller);
        var observer = Player(new Point3D(shrine.X + 3, shrine.Y + 2, shrine.Z));
        Assert.True(shrine.TryBegin(player, out _));
        Assert.True(HasText(observer, $"{player.Name} begins an extraction ritual."));
        Assert.True(HasText(player, "Summoning: 12 seconds"));
        Assert.False(HasText(observer, "Summoning: 12 seconds"));
        Assert.True(HasSound(observer, ExtractionEventShrine.RitualSound, shrine.Location));
        Advance(shrine, 1000);
        Assert.True(HasText(player, "Summoning: 11 seconds"));
        Assert.False(HasText(observer, "Summoning: 11 seconds"));
        Assert.True(HasEffect(observer, 0x19AB, new Point3D(shrine.X, shrine.Y, shrine.Z + 8)));
    }

    [Fact]
    public void SharedGate_AdmitsAnotherParticipantWithoutTheirOwnRitual_AndRetainsLoot()
    {
        var (controller, shrine) = Create();
        var performer = Participant(controller);
        var another = Participant(controller, new Point3D(shrine.X + 1, shrine.Y, shrine.Z));
        var loot = Track(new Gold(432));
        another.Backpack.DropItem(loot);
        SummonPortal(shrine, performer);
        var portal = Assert.IsType<ExtractionShrinePortal>(shrine.Portal);
        Assert.True(controller.IsParticipant(performer));
        EnterPortal(portal, another);
        Assert.False(controller.IsParticipant(another));
        Assert.True(ExtractionDungeonLayout.LobbyBounds.Contains(another.Location));
        Assert.Same(another.Backpack, loot.Parent);
        Assert.True(controller.IsParticipant(performer));
        Assert.Same(portal, shrine.Portal);
        Assert.False(portal.Deleted);
    }

    [Theory]
    [InlineData("Ghost")]
    [InlineData("Outsider")]
    [InlineData("Disconnected")]
    [InlineData("Distant")]
    [InlineData("WrongMap")]
    [InlineData("WrongZ")]
    public void SharedGate_DoesNotBypassEventEligibility(string reason)
    {
        var (controller, shrine) = Create();
        var performer = Participant(controller);
        SummonPortal(shrine, performer);
        var player = reason == "Outsider" ? Player() : Participant(controller);
        switch (reason)
        {
            case "Ghost":
            {
                player.Body = 0x192;
                break;
            }
            case "Disconnected":
            {
                player.NetState = null;
                break;
            }
            case "Distant":
            {
                player.MoveToWorld(new Point3D(shrine.X + 3, shrine.Y, shrine.Z), Map.Felucca);
                break;
            }
            case "WrongMap":
            {
                player.MoveToWorld(player.Location, Map.Trammel);
                break;
            }
            case "WrongZ":
            {
                player.Z += 25;
                break;
            }
        }
        var original = player.Location;
        Assert.True(shrine.Portal.OnMoveOver(player));
        Assert.Equal(original, player.Location);
        Assert.False(ExtractionDungeonLayout.LobbyBounds.Contains(player.Location));
    }

    [Fact]
    public void SharedGate_DeadlineWinsEvenBeforeItsCleanupTimerTicks()
    {
        var (controller, shrine) = Create();
        var player = Participant(controller);
        SummonPortal(shrine, player);
        var portal = shrine.Portal;
        Core._now = controller.MatchDeadline;
        Assert.True(portal.OnMoveOver(player));
        Assert.True(controller.IsParticipant(player));
        Assert.False(ExtractionDungeonLayout.LobbyBounds.Contains(player.Location));
        Assert.True(portal.Deleted);
        Assert.Null(shrine.Portal);
        Assert.Equal(ExtractionEventPhase.Finishing, controller.Phase);
    }

    [Theory]
    [InlineData("Phase")]
    [InlineData("Round")]
    [InlineData("ShrineMove")]
    [InlineData("ShrineMap")]
    [InlineData("ShrineDelete")]
    [InlineData("PortalMove")]
    [InlineData("PortalMap")]
    public void SharedGate_LifecycleAndRoundChangesRemoveOnlyTheTemporaryGate(string operation)
    {
        var (controller, shrine) = Create();
        var player = Participant(controller);
        SummonPortal(shrine, player);
        var portal = shrine.Portal;
        var managedCount = controller.ManagedItems.Count;
        switch (operation)
        {
            case "Phase":
            {
                SetField(controller, "_phase", ExtractionEventPhase.Waiting);
                shrine.OnEventPhaseChanged(controller);
                break;
            }
            case "Round":
            {
                SetField(controller, "_roundNumber", controller.RoundNumber + 1);
                shrine.OnEventPhaseChanged(controller);
                break;
            }
            case "ShrineMove":
            {
                shrine.X++;
                break;
            }
            case "ShrineMap":
            {
                shrine.Map = Map.Trammel;
                break;
            }
            case "ShrineDelete":
            {
                shrine.Delete();
                break;
            }
            case "PortalMove":
            {
                portal.X++;
                break;
            }
            case "PortalMap":
            {
                portal.Map = Map.Trammel;
                break;
            }
        }
        Assert.True(portal.Deleted);
        Assert.Null(portal.Shrine);
        Assert.Null(shrine.Portal);
        Assert.False(((TimerExecutionToken)GetField(portal, "_validationTimer")).Running);
        Assert.Equal(managedCount, controller.ManagedItems.Count);
        Assert.True(controller.IsParticipant(player));
    }

    [Fact]
    public void ADeletedGateCannotBeResummonedInTheSameRound_ButNextRoundAllowsANewGate()
    {
        var (controller, shrine) = Create();
        var player = Participant(controller);
        SummonPortal(shrine, player);
        var original = shrine.Portal;
        original.Delete();
        Assert.True(shrine.HasSummonedForRound(controller.RoundNumber));
        Assert.False(shrine.TryBegin(player, out _));
        SetField(controller, "_roundNumber", controller.RoundNumber + 1);
        SummonPortal(shrine, player);
        Assert.NotNull(shrine.Portal);
        Assert.NotSame(original, shrine.Portal);
        Assert.True(controller.IsParticipant(player));
    }

    [Fact]
    public void UnregisteredOrTamperedGateCannotExtract_AndOriginalThreeExitsAreUnchanged()
    {
        var (controller, shrine) = Create();
        var player = Participant(controller);
        SummonPortal(shrine, player);
        var clone = Track(new ExtractionShrinePortal(shrine, controller.RoundNumber));
        clone.MoveToWorld(shrine.Location, Map.Felucca);
        Assert.True(clone.OnMoveOver(player));
        Assert.True(controller.IsParticipant(player));
        var portal = shrine.Portal;
        portal.Hue++;
        Assert.True(portal.OnMoveOver(player));
        portal.Advance();
        Assert.True(portal.Deleted);
        Assert.True(controller.IsParticipant(player));
        var exitCount = 0;
        foreach (var item in controller.ManagedItems)
        {
            if (item is ExtractionEventGate { Kind: ExtractionEventGateKind.Extraction } gate)
            {
                exitCount++;
                Assert.Contains(gate.Location, ExtractionEventRuntime.ExitPoints);
            }
        }
        Assert.Equal(3, exitCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SavedActiveGate_RestoresWithSameSerialsAndLatch_UnderEitherDeferredCallbackOrder(bool callbackBeforeBinding, bool beforeUnlock)
    {
        var (controller, shrine) = Create();
        var performer = Participant(controller);
        SummonPortal(shrine, performer);
        var portal = shrine.Portal;
        if (beforeUnlock)
        {
            SetField(controller, "_matchDeadline", Core.Now + ExtractionEventController.MatchDuration);
            shrine.OnEventPhaseChanged(controller);
            Assert.False(shrine.Components[9].Visible);
            Assert.False(portal.Visible);
            Assert.False(ExtractionEventRuntime.IsValidShrinePortal(portal));
            Assert.False(portal.Deleted);
        }
        var rootSerial = shrine.Serial;
        var gateSerial = portal.Serial;
        var rootBytes = Snapshot(shrine);
        var portalBytes = Snapshot(portal);
        var componentSerials = new Serial[shrine.Components.Count];
        var componentBytes = new byte[shrine.Components.Count][];
        var components = shrine.Components.ToArray();
        for (var index = 0; index < components.Length; index++)
        {
            componentSerials[index] = components[index].Serial;
            componentBytes[index] = Snapshot(components[index]);
        }
        var adapter = typeof(ExtractionEventRuntime).GetField("_adapter", BindingFlags.Static | BindingFlags.NonPublic);
        var boundAdapter = adapter.GetValue(null);
        adapter.SetValue(null, null);
        shrine.Suspend();
        SetField(shrine, "_portal", null);
        SetField(portal, "_shrine", null);
        portal.Delete();
        shrine.Components.Clear();
        shrine.Delete();
        foreach (var component in components)
        {
            component.Addon = null;
            component.Delete();
        }

        var restoredShrine = Track(new ExtractionEventShrine(rootSerial));
        var restoredPortal = Track(new ExtractionShrinePortal(gateSerial));
        World.AddEntity(restoredShrine);
        World.AddEntity(restoredPortal);
        var restoredComponents = new AddonComponent[components.Length];
        for (var index = 0; index < restoredComponents.Length; index++)
        {
            restoredComponents[index] = Track(new AddonComponent(componentSerials[index]));
            World.AddEntity(restoredComponents[index]);
        }
        restoredPortal.Deserialize(new BufferReader(portalBytes));
        for (var index = 0; index < restoredComponents.Length; index++)
        {
            restoredComponents[index].Deserialize(new BufferReader(componentBytes[index]));
        }
        restoredShrine.Deserialize(new BufferReader(rootBytes));
        var managed = (List<Item>)GetField(controller, "_managedReferences");
        managed[managed.IndexOf(shrine)] = restoredShrine;
        var callback = typeof(ExtractionShrinePortal).GetMethod("AfterDeserialization", BindingFlags.Instance | BindingFlags.NonPublic);
        if (callbackBeforeBinding)
        {
            callback.Invoke(restoredPortal, null);
            Assert.False(restoredPortal.Deleted);
            Assert.False(((TimerExecutionToken)GetField(restoredPortal, "_validationTimer")).Running);
        }
        adapter.SetValue(null, boundAdapter);
        typeof(ExtractionEventRuntime).GetField("_prototypeShrine", BindingFlags.Static | BindingFlags.NonPublic)
            .SetValue(null, restoredShrine);
        restoredShrine.OnEventPhaseChanged(controller);
        if (!callbackBeforeBinding)
        {
            callback.Invoke(restoredPortal, null);
        }
        Assert.Same(controller, restoredShrine.Controller);
        Assert.Same(restoredPortal, restoredShrine.Portal);
        Assert.Same(restoredShrine, restoredPortal.Shrine);
        Assert.Equal(rootSerial, restoredShrine.Serial);
        Assert.Equal(gateSerial, restoredPortal.Serial);
        Assert.True(restoredShrine.ExactAppearance);
        Assert.True(restoredShrine.HasSummonedForRound(controller.RoundNumber));
        if (beforeUnlock)
        {
            Assert.False(restoredShrine.Components[9].Visible);
            Assert.False(restoredPortal.Visible);
            Assert.False(restoredPortal.Deleted);
            Assert.False(((TimerExecutionToken)GetField(restoredPortal, "_validationTimer")).Running);
            Assert.True(restoredPortal.OnMoveOver(performer));
            Assert.True(controller.IsParticipant(performer));
            Core._now = controller.MatchDeadline - ExtractionEventController.MatchDuration + ExtractionEventController.ExtractionDelay;
            restoredShrine.OnEventPhaseChanged(controller);
            Assert.True(restoredShrine.Components[9].Visible);
            Assert.True(restoredPortal.Visible);
        }
        Assert.True(ExtractionEventRuntime.IsValidShrinePortal(restoredPortal));
        Assert.True(((TimerExecutionToken)GetField(restoredPortal, "_validationTimer")).Running);
        Assert.False(restoredShrine.TryBegin(performer, out _));
        EnterPortal(restoredPortal, performer);
        Assert.False(controller.IsParticipant(performer));
    }

    [Fact]
    public void ReservedRuneFieldsRemainSaveCompatible_AndDoNotRestrictFlameSummoning()
    {
        var (controller, shrine) = Create();
        var player = Participant(controller);
        SetField(shrine, "_runeMask", 5);
        SetField(shrine, "_runeRound", controller.RoundNumber - 1);
        Assert.True(shrine.TryBegin(player, out _));
        var restored = Track(new ExtractionEventShrine((Serial)0x40004568));
        restored.Deserialize(new BufferReader(Snapshot(shrine)));
        Assert.Equal(5, (int)GetField(restored, "_runeMask"));
        Assert.Equal(controller.RoundNumber - 1, (int)GetField(restored, "_runeRound"));
        Assert.Equal(0, restored.ActiveChannels);
        Assert.False(((TimerExecutionToken)GetField(restored, "_validationTimer")).Running);
        Assert.Null(restored.Portal);
        restored.Components.Clear();
        Advance(shrine, 12000);
        Assert.NotNull(shrine.Portal);
    }

    [Fact]
    public void SkillsDoNotShortenTheFixedTwelveSecondFlameRitual()
    {
        var (controller, shrine) = Create();
        var player = Participant(controller);
        player.Skills[SkillName.Inscribe].Base = 100;
        player.Skills[SkillName.Lockpicking].Base = 100;
        Assert.True(shrine.TryBegin(player, out _));
        Advance(shrine, 11999);
        Assert.Null(shrine.Portal);
        Advance(shrine, 1);
        Assert.NotNull(shrine.Portal);
    }

    [Fact]
    public void SharedGate_CancelLeavesParticipantAndLootInDungeon()
    {
        var (controller, shrine) = Create();
        var player = Participant(controller);
        SummonPortal(shrine, player);
        var origin = player.Location;
        var loot = Track(new Gold(432));
        player.Backpack.DropItem(loot);
        shrine.Portal.OnDoubleClick(player);
        var gump = player.FindGump<MoongateTravelConfirmation>();
        Assert.NotNull(gump);
        Assert.Equal(origin, player.Location);
        gump.OnResponse(player.NetState, Reply(0));
        Assert.Equal(origin, player.Location);
        Assert.True(controller.IsParticipant(player));
        Assert.Same(player.Backpack, loot.Parent);
    }

    [Theory]
    [InlineData("Deadline")]
    [InlineData("Round")]
    [InlineData("NotParticipant")]
    [InlineData("MovedAway")]
    [InlineData("Deleted")]
    public void SharedGate_ConfirmationRevalidatesEventAndPlayer(string invalidation)
    {
        var (controller, shrine) = Create();
        var player = Participant(controller);
        SummonPortal(shrine, player);
        Assert.True(shrine.Portal.OnMoveOver(player));
        var gump = player.FindGump<MoongateTravelConfirmation>();
        Assert.NotNull(gump);
        switch (invalidation)
        {
            case "Deadline":
            {
                Core._now = controller.MatchDeadline;
                break;
            }
            case "Round":
            {
                SetField(controller, "_roundNumber", controller.RoundNumber + 1);
                break;
            }
            case "NotParticipant":
            {
                ((HashSet<Mobile>)GetField(controller, "_participantSet")).Remove(player);
                break;
            }
            case "MovedAway":
            {
                player.MoveToWorld(new Point3D(shrine.X + 3, shrine.Y, shrine.Z), Map.Felucca);
                break;
            }
            case "Deleted":
            {
                shrine.Portal.Delete();
                break;
            }
        }
        var origin = player.Location;
        gump.OnResponse(player.NetState, Reply(1));
        Assert.Equal(origin, player.Location);
        Assert.False(ExtractionDungeonLayout.LobbyBounds.Contains(player.Location));
    }

    private static void EnterPortal(ExtractionShrinePortal portal, Mobile player)
    {
        var origin = player.Location;
        Assert.True(portal.OnMoveOver(player));
        Assert.Equal(origin, player.Location);
        var gump = player.FindGump<MoongateTravelConfirmation>();
        Assert.NotNull(gump);
        gump.OnResponse(player.NetState, Reply(1));
    }

    private static RelayInfo Reply(int buttonId) =>
        new(buttonId, ReadOnlySpan<int>.Empty, ReadOnlySpan<ushort>.Empty, ReadOnlySpan<Range>.Empty, ReadOnlySpan<byte>.Empty);

    private static void SummonPortal(ExtractionEventShrine shrine, Mobile player)
    {
        Assert.True(shrine.TryBegin(player, out var error), error);
        Advance(shrine, ExtractionEventShrine.ChannelMilliseconds);
        Assert.NotNull(shrine.Portal);
    }

    private (ExtractionEventController Controller, ExtractionEventShrine Shrine) Create(bool cairn = false)
    {
        var controller = (ExtractionEventController)typeof(ExtractionEventRuntimeTests)
            .GetMethod("BuildLobby", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_runtimeFixture, ["ModularDungeonV17"]);
        var shrine = Track(new ExtractionEventShrine(controller));
        shrine.MoveToWorld(cairn ? ExtractionShrinePrototype.CairnPoint : ExtractionShrinePrototype.Point, Map.Felucca);
        controller.AddManagedItem(shrine);
        typeof(ExtractionEventRuntime).GetField(cairn ? "_cairnShrine" : "_prototypeShrine", BindingFlags.Static | BindingFlags.NonPublic)
            .SetValue(null, shrine);
        SetField(controller, "_phase", ExtractionEventPhase.Active);
        SetField(controller, "_matchDeadline", Core.Now + TimeSpan.FromMinutes(8));
        shrine.OnEventPhaseChanged(controller);
        Assert.True(shrine.ExactAppearance);
        return (controller, shrine);
    }

    private PlayerMobile Participant(ExtractionEventController controller, Point3D? point = null)
    {
        var player = Player(point ?? ExtractionShrinePrototype.Point);
        ((List<Mobile>)GetField(controller, "_participantReferences")).Add(player);
        ((HashSet<Mobile>)GetField(controller, "_participantSet")).Add(player);
        return player;
    }

    private PlayerMobile Player(Point3D? point = null)
    {
        var state = PacketTestUtilities.CreateTestNetState();
        _states.Add(state);
        state.Account = new MockAccount();
        var player = Track(new PlayerMobile { Body = 0x190, Player = true, Name = "Shrine Tester" });
        player.AddItem(Track(new Backpack()));
        player.NetState = state;
        state.Mobile = player;
        player.MoveToWorld(point ?? ExtractionShrinePrototype.Point, Map.Felucca);
        return player;
    }

    private static void Advance(ExtractionEventShrine shrine, long milliseconds)
    {
        Core._tickCount = unchecked(Core.TickCount + milliseconds);
        Core._now += TimeSpan.FromMilliseconds(milliseconds);
        shrine.Advance();
    }

    private static int CountComponents(ExtractionEventShrine shrine, Func<AddonComponent, bool> predicate)
    {
        var count = 0;
        foreach (var component in shrine.Components)
        {
            if (predicate(component))
            {
                count++;
            }
        }
        return count;
    }

    private static bool HasText(Mobile player, string text) =>
        player.NetState.SendBuffer.GetReadSpan().IndexOf(Encoding.BigEndianUnicode.GetBytes(text)) >= 0;

    private static bool HasSound(Mobile player, int sound, Point3D point)
    {
        Span<byte> packet = stackalloc byte[OutgoingEffectPackets.SoundPacketLength];
        OutgoingEffectPackets.CreateSoundEffect(packet, sound, point);
        return player.NetState.SendBuffer.GetReadSpan().IndexOf(packet) >= 0;
    }

    private static bool HasEffect(Mobile player, int itemId, Point3D point)
    {
        Span<byte> packet = stackalloc byte[OutgoingEffectPackets.HuedEffectLength];
        OutgoingEffectPackets.CreateLocationHuedEffect(packet, point, itemId, 10, 3, ExtractionEventShrine.RedFlameHue, 0);
        return player.NetState.SendBuffer.GetReadSpan().IndexOf(packet) >= 0;
    }

    private static byte[] Snapshot(Item item)
    {
        var writer = new BufferWriter(true);
        item.Serialize(writer);
        return writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();
    }

    private static object GetField(object target, string name) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

    private static void SetField(object target, string name, object value) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }
}
