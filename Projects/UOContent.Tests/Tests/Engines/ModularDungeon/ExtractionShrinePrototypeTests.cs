using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Server;
using Server.Engines.ModularDungeon;
using Server.Gumps;
using Server.Items;
using Server.Mobiles;
using Server.Network;
using Server.Tests;
using Server.Tests.Network;
using Xunit;

namespace UOContent.Tests.Engines.ModularDungeon;

[Collection("Sequential UOContent Tests")]
public class ExtractionShrinePrototypeTests : IDisposable
{
    private readonly List<IEntity> _created = [];
    private readonly List<NetState> _states = [];
    private readonly DateTime _now = Core.Now;
    private readonly long _ticks = Core.TickCount;
    private readonly Expansion _expansion = Core.Expansion;

    public ExtractionShrinePrototypeTests()
    {
        Timer.Init(Core.TickCount);
        Core.Expansion = Expansion.T2A;
        SetRuntimeField("_deploymentSuspended", false);
    }

    public void Dispose()
    {
        if (ExtractionEventRuntime.Controller is { } controller)
        {
            foreach (var managed in controller.ManagedItems)
            {
                _created.Add(managed);
            }
            foreach (var creature in controller.OwnedCreatures)
            {
                _created.Add(creature);
            }
            foreach (var loot in controller.OwnedLoot)
            {
                _created.Add(loot);
            }
        }
        InvokeRuntime("Shutdown");
        typeof(ExtractionEventRuntime).GetProperty(nameof(ExtractionEventRuntime.Controller)).SetValue(null, null);
        SetRuntimeField("_deploymentSuspended", false);
        for (var index = _created.Count - 1; index >= 0; index--)
        {
            if (_created[index] is Mobile mobile)
            {
                mobile.NetState = null;
            }
            _created[index]?.Delete();
        }
        foreach (var state in _states)
        {
            state.Dispose();
        }
        Core._now = _now;
        Core._tickCount = _ticks;
        Core.Expansion = _expansion;
        Timer.Init(Core.TickCount);
    }

    [Fact]
    public void NativeEchoCourtFootprint_CanBePlacedAndPreservesFourStandingTiles()
    {
        Assert.True(TestServerInitializer.TileDataLoaded);
        Assert.True(ExtractionShrinePrototype.CanPlace(out var error), error);
        var shrine = Track(new ExtractionEventShrine());
        shrine.MoveToWorld(ExtractionShrinePrototype.Point, Map.Felucca);
        Assert.True(shrine.ExactAppearance);
        Assert.True(ExtractionShrinePrototype.CheckStandingTiles());
        Assert.False(ExtractionShrinePrototype.CanPlace(out _));
    }

    [Fact]
    public void NativeCairnMinesFootprint_CanBePlacedAndPreservesFourStandingTiles()
    {
        Assert.True(TestServerInitializer.TileDataLoaded);
        var point = ExtractionShrinePrototype.CairnPoint;
        Assert.True(ExtractionShrinePrototype.CanPlace(point, out var error), error);
        var shrine = Track(new ExtractionEventShrine());
        shrine.MoveToWorld(point, Map.Felucca);
        Assert.True(shrine.ExactAppearance);
        Assert.True(ExtractionShrinePrototype.CheckStandingTiles(point));
        Assert.False(ExtractionShrinePrototype.CanPlace(point, out _));
    }

    [Fact]
    public void ExistingWorldProp_RefusesPlacementWithoutDeletingOrMovingIt()
    {
        var prop = Track(new Gold(93));
        prop.MoveToWorld(ExtractionShrinePrototype.Point, Map.Felucca);
        Assert.False(ExtractionShrinePrototype.CanPlace(out var error));
        Assert.Contains("existing world item", error);
        Assert.False(prop.Deleted);
        Assert.Equal(93, prop.Amount);
        Assert.Equal(ExtractionShrinePrototype.Point, prop.Location);
    }

    [Fact]
    public void OnePrototypeInstall_IsIdempotentAndPreservesExistingOwnedObjectsAndDeadlines()
    {
        var controller = BindMinimalRealAdapter(out var chest, out var contents, out var door, out var gates);
        var originalManaged = new Item[controller.ManagedItems.Count];
        for (var index = 0; index < originalManaged.Length; index++)
        {
            originalManaged[index] = controller.ManagedItems[index];
        }
        var waiting = controller.WaitingDeadline;
        var round = controller.RoundNumber;
        var openedLocation = door.Location;
        Assert.True(ExtractionEventRuntime.TryInstallEchoShrine(out var shrine, out var created, out var error), error);
        Track(shrine);
        Assert.True(created);
        Assert.Equal(originalManaged.Length + 1, controller.ManagedItems.Count);
        for (var index = 0; index < originalManaged.Length; index++)
        {
            Assert.Same(originalManaged[index], controller.ManagedItems[index]);
        }
        Assert.Equal(waiting, controller.WaitingDeadline);
        Assert.Equal(round, controller.RoundNumber);
        Assert.Equal(openedLocation, door.Location);
        Assert.True(door.Open);
        Assert.Same(chest, contents.Parent);
        Assert.Equal(315, contents.Amount);
        Assert.Equal(3, gates.Length);
        for (var index = 0; index < gates.Length; index++)
        {
            Assert.Equal(ExtractionEventRuntime.ExitPoints[index], gates[index].Location);
            Assert.Equal(ExtractionEventGateKind.Extraction, gates[index].Kind);
            Assert.Same(controller, gates[index].Controller);
        }
        Assert.True(ExtractionEventRuntime.TryInstallEchoShrine(out var repeated, out created, out error), error);
        Assert.False(created);
        Assert.Same(shrine, repeated);
        Assert.Equal(originalManaged.Length + 1, controller.ManagedItems.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MovedOrDuplicateOwnedPrototype_RefusesReplacement(bool duplicate)
    {
        var controller = BindMinimalRealAdapter(out _, out _, out _, out _);
        Assert.True(ExtractionEventRuntime.TryInstallEchoShrine(out var shrine, out _, out var error), error);
        Track(shrine);
        if (duplicate)
        {
            var another = Track(new ExtractionEventShrine(controller));
            another.MoveToWorld(ExtractionShrinePrototype.Point, Map.Felucca);
            controller.AddManagedItem(another);
        }
        else
        {
            shrine.MoveToWorld(new Point3D(shrine.X, shrine.Y + 1, shrine.Z), Map.Felucca);
        }
        var ownedCount = controller.ManagedItems.Count;
        Assert.False(ExtractionEventRuntime.TryInstallEchoShrine(out var replacement, out var created, out error));
        Assert.Null(replacement);
        Assert.False(created);
        Assert.Contains("staff recovery", error);
        Assert.Equal(ownedCount, controller.ManagedItems.Count);
        Assert.False(shrine.Deleted);
    }

    [Theory]
    [InlineData(ExtractionEventPhase.Active)]
    [InlineData(ExtractionEventPhase.Starting)]
    [InlineData(ExtractionEventPhase.Finishing)]
    [InlineData(ExtractionEventPhase.Faulted)]
    public void InstallationOutsideWaiting_IsRefusedWithoutChangingOwnedObjects(ExtractionEventPhase phase)
    {
        var controller = BindMinimalRealAdapter(out _, out _, out _, out _);
        typeof(ExtractionEventController).GetField("_phase", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, phase);
        var ownedCount = controller.ManagedItems.Count;
        Assert.False(ExtractionEventRuntime.TryInstallEchoShrine(out var shrine, out var created, out _));
        Assert.Null(shrine);
        Assert.False(created);
        Assert.Equal(ownedCount, controller.ManagedItems.Count);
    }

    [Theory]
    [InlineData("_participantReferences")]
    [InlineData("_queuedReferences")]
    public void OccupiedParticipantOrAdmissionQueue_RefusesPrototypeInstall(string field)
    {
        var controller = BindMinimalRealAdapter(out _, out _, out _, out _);
        var players = (List<Mobile>)typeof(ExtractionEventController)
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        players.Add(Track(new Mobile()));
        var ownedCount = controller.ManagedItems.Count;
        Assert.False(ExtractionEventRuntime.TryInstallEchoShrine(out var shrine, out var created, out _));
        Assert.Null(shrine);
        Assert.False(created);
        Assert.Equal(ownedCount, controller.ManagedItems.Count);
    }

    [Fact]
    public void SuspendedDungeonDeployment_RefusesPrototypeInstall()
    {
        var controller = BindMinimalRealAdapter(out _, out _, out _, out _);
        SetRuntimeField("_deploymentSuspended", true);
        var ownedCount = controller.ManagedItems.Count;
        Assert.False(ExtractionEventRuntime.TryInstallEchoShrine(out var shrine, out var created, out _));
        Assert.Null(shrine);
        Assert.False(created);
        Assert.Equal(ownedCount, controller.ManagedItems.Count);
    }

    [Fact]
    public void NativeAddonDeserializationLightNormalization_DoesNotInvalidateShrineAppearance()
    {
        var shrine = Track(new ExtractionEventShrine());
        shrine.MoveToWorld(ExtractionShrinePrototype.Point, Map.Felucca);
        foreach (var component in shrine.Components)
        {
            AddonComponent.ApplyLightTo(component);
        }
        Assert.True(shrine.ExactAppearance);
    }

    [Theory]
    [InlineData(0x480)]
    [InlineData(0x21)]
    public void PinnedPreviousFlameUpgrade_PreservesEveryExistingObjectAndIsIdempotent(int previousHue)
    {
        var controller = BindMinimalRealAdapter(out var chest, out var contents, out var door, out _);
        Assert.True(ExtractionEventRuntime.TryInstallEchoShrine(out var shrine, out _, out var error), error);
        Track(shrine);
        MakePreviousFlame(shrine, previousHue, 0);
        Assert.True(shrine.ExactLegacyAppearance || shrine.ExactPreviousRedAppearance);
        Assert.False(shrine.ExactAppearance);
        var serials = new Serial[shrine.Components.Count];
        for (var index = 0; index < serials.Length; index++)
        {
            serials[index] = shrine.Components[index].Serial;
        }
        var managed = controller.ManagedItems.Count;
        var deadline = controller.WaitingDeadline;
        var doorLocation = door.Location;
        Assert.True(ExtractionEventRuntime.TryUpgradeEchoShrine(shrine.Serial, controller.Serial,
            out var upgraded, out var updated, out error), error);
        Assert.Same(shrine, upgraded);
        Assert.True(updated);
        Assert.True(shrine.ExactAppearance);
        Assert.False(shrine.ExactLegacyAppearance);
        Assert.Equal(18, shrine.Components.Count);
        Assert.Equal(0x19AB, shrine.Components[9].ItemID);
        Assert.Equal(0x28, shrine.Components[9].Hue);
        Assert.Equal(shrine.Z + 8, shrine.Components[9].Z);
        for (var index = 0; index < serials.Length; index++)
        {
            Assert.Equal(serials[index], shrine.Components[index].Serial);
        }
        Assert.Equal(managed, controller.ManagedItems.Count);
        Assert.Equal(deadline, controller.WaitingDeadline);
        Assert.Equal(doorLocation, door.Location);
        Assert.True(door.Open);
        Assert.Same(chest, contents.Parent);
        Assert.Equal(315, contents.Amount);
        Assert.True(ExtractionEventRuntime.TryUpgradeEchoShrine(shrine.Serial, controller.Serial,
            out upgraded, out updated, out error), error);
        Assert.Same(shrine, upgraded);
        Assert.False(updated);
    }

    [Fact]
    public void RestoringAnExactFlameAssembly_PreservesItsShapeAndExistingOwnedObjects()
    {
        var controller = BindMinimalRealAdapter(out var chest, out var contents, out var door, out _);
        Assert.True(ExtractionEventRuntime.TryInstallEchoShrine(out var shrine, out _, out var error), error);
        Track(shrine);
        MakePreviousFlame(shrine, 0x28, 8);
        Assert.True(shrine.ExactAppearance);
        var serial = shrine.Components[9].Serial;
        var managed = controller.ManagedItems.Count;
        var doorLocation = door.Location;
        var installation = Assert.IsType<ExtractionEventInstallation>(controller.ManagedItems[0]);
        InvokeRuntime("Bind", controller, installation, LoadManifest());
        Assert.True(shrine.ExactAppearance);
        Assert.Equal(18, shrine.Components.Count);
        Assert.Equal(0x19AB, shrine.Components[9].ItemID);
        Assert.Equal(0x28, shrine.Components[9].Hue);
        Assert.Equal(new Point3D(0, 0, 8), shrine.Components[9].Offset);
        Assert.Equal(serial, shrine.Components[9].Serial);
        Assert.Equal(managed, controller.ManagedItems.Count);
        Assert.Same(chest, contents.Parent);
        Assert.Equal(315, contents.Amount);
        Assert.Equal(doorLocation, door.Location);
        Assert.True(door.Open);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExactFourRuneDowngrade_PreservesOriginalEighteenSerialsAndDeletesOnlyTheThreeAppendedGlyphs(bool restore)
    {
        var controller = BindMinimalRealAdapter(out var chest, out var contents, out var door, out var gates);
        Assert.True(ExtractionEventRuntime.TryInstallEchoShrine(out var shrine, out _, out var error), error);
        Track(shrine);
        var appended = MakePreviousRunes(shrine);
        Assert.True(shrine.ExactPreviousRuneAppearance);
        Assert.False(shrine.ExactAppearance);
        var original = new AddonComponent[18];
        var serials = new Serial[18];
        for (var index = 0; index < original.Length; index++)
        {
            original[index] = shrine.Components[index];
            serials[index] = original[index].Serial;
        }
        var rootSerial = shrine.Serial;
        var managed = controller.ManagedItems.Count;
        var deadline = controller.WaitingDeadline;
        var openedLocation = door.Location;
        var untouchedProp = Track(new Gold(74));
        untouchedProp.MoveToWorld(new Point3D(shrine.X + 4, shrine.Y + 4, shrine.Z), Map.Felucca);
        if (restore)
        {
            InvokeRuntime("Bind", controller, controller.ManagedItems[0], LoadManifest());
        }
        else
        {
            Assert.True(ExtractionEventRuntime.TryUpgradeEchoShrine(rootSerial, controller.Serial,
                out var result, out var updated, out error), error);
            Assert.Same(shrine, result);
            Assert.True(updated);
        }
        Assert.False(shrine.Deleted);
        Assert.Equal(rootSerial, shrine.Serial);
        Assert.Equal(18, shrine.Components.Count);
        Assert.True(shrine.ExactAppearance);
        Assert.Equal(0x19AB, shrine.Components[9].ItemID);
        Assert.Equal(0x28, shrine.Components[9].Hue);
        Assert.Equal(new Point3D(0, 0, 8), shrine.Components[9].Offset);
        for (var index = 0; index < original.Length; index++)
        {
            Assert.Same(original[index], shrine.Components[index]);
            Assert.Equal(serials[index], shrine.Components[index].Serial);
            Assert.False(original[index].Deleted);
        }
        Assert.All(appended, component =>
        {
            Assert.True(component.Deleted);
            Assert.Null(component.Addon);
        });
        Assert.Equal(managed, controller.ManagedItems.Count);
        Assert.Equal(deadline, controller.WaitingDeadline);
        Assert.True(door.Open);
        Assert.Equal(openedLocation, door.Location);
        Assert.Same(chest, contents.Parent);
        Assert.Equal(315, contents.Amount);
        Assert.False(untouchedProp.Deleted);
        Assert.Equal(74, untouchedProp.Amount);
        Assert.All(gates, gate => Assert.False(gate.Deleted));
        Assert.True(ExtractionEventRuntime.TryUpgradeEchoShrine(rootSerial, controller.Serial,
            out var repeated, out var repeatedUpdate, out error), error);
        Assert.Same(shrine, repeated);
        Assert.False(repeatedUpdate);
    }

    [Fact]
    public void EchoAndCairnInstallations_AreDistinctIdempotentAndDoNotReplaceEitherShrine()
    {
        var controller = BindMinimalRealAdapter(out _, out var contents, out var door, out var gates);
        var managed = controller.ManagedItems.Count;
        var deadline = controller.WaitingDeadline;
        Assert.True(ExtractionEventRuntime.TryInstallEchoShrine(out var echo, out var echoCreated, out var error), error);
        Assert.True(echoCreated);
        Track(echo);
        Assert.True(ExtractionEventRuntime.TryInstallCairnShrine(out var cairn, out var cairnCreated, out error), error);
        Assert.True(cairnCreated);
        Track(cairn);
        Assert.NotSame(echo, cairn);
        Assert.NotEqual(echo.Serial, cairn.Serial);
        Assert.Equal(ExtractionShrinePrototype.Point, echo.Location);
        Assert.Equal(ExtractionShrinePrototype.CairnPoint, cairn.Location);
        Assert.True(echo.ExactAppearance);
        Assert.True(cairn.ExactAppearance);
        Assert.Equal(18, echo.Components.Count);
        Assert.Equal(18, cairn.Components.Count);
        Assert.Equal(managed + 2, controller.ManagedItems.Count);
        Assert.True(ExtractionEventRuntime.TryInstallEchoShrine(out var repeatedEcho, out echoCreated, out error), error);
        Assert.True(ExtractionEventRuntime.TryInstallCairnShrine(out var repeatedCairn, out cairnCreated, out error), error);
        Assert.False(echoCreated);
        Assert.False(cairnCreated);
        Assert.Same(echo, repeatedEcho);
        Assert.Same(cairn, repeatedCairn);
        Assert.Equal(managed + 2, controller.ManagedItems.Count);
        Assert.Equal(deadline, controller.WaitingDeadline);
        Assert.Equal(315, contents.Amount);
        Assert.True(door.Open);
        Assert.All(gates, gate => Assert.False(gate.Deleted));
    }

    [Fact]
    public void BothFlameShrines_SummonIndependentSharedPortalsAndClearThemOnMatchEnd()
    {
        var controller = BindMinimalRealAdapter(out _, out _, out _, out var gates);
        Assert.True(ExtractionEventRuntime.TryInstallEchoShrine(out var echo, out _, out var error), error);
        Assert.True(ExtractionEventRuntime.TryInstallCairnShrine(out var cairn, out _, out error), error);
        Track(echo);
        Track(cairn);
        SetControllerField(controller, "_phase", ExtractionEventPhase.Active);
        SetControllerField(controller, "_matchDeadline", Core.Now + TimeSpan.FromMinutes(8));
        echo.OnEventPhaseChanged(controller);
        cairn.OnEventPhaseChanged(controller);
        var echoPlayer = Participant(controller, new Point3D(echo.X, echo.Y + 2, echo.Z));
        var cairnPlayer = Participant(controller, new Point3D(cairn.X, cairn.Y + 2, cairn.Z));
        var visitor = Participant(controller, cairn.Location);
        var loot = Track(new Gold(456));
        visitor.Backpack.DropItem(loot);
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var floor = Track(new Static(0x519));
                floor.MoveToWorld(new Point3D(ExtractionDungeonLayout.LobbyEntry.X + dx,
                    ExtractionDungeonLayout.LobbyEntry.Y + dy, ExtractionDungeonLayout.LobbyEntry.Z), Map.Felucca);
            }
        }
        Assert.True(echo.TryBegin(echoPlayer, out error), error);
        Assert.True(cairn.TryBegin(cairnPlayer, out error), error);
        AdvanceShrines(11999, echo, cairn);
        Assert.Null(echo.Portal);
        Assert.Null(cairn.Portal);
        AdvanceShrines(1, echo, cairn);
        var echoPortal = Track(Assert.IsType<ExtractionShrinePortal>(echo.Portal));
        var cairnPortal = Track(Assert.IsType<ExtractionShrinePortal>(cairn.Portal));
        Assert.NotSame(echoPortal, cairnPortal);
        Assert.Equal(echo.Location, echoPortal.Location);
        Assert.Equal(cairn.Location, cairnPortal.Location);
        Assert.True(ExtractionEventRuntime.IsValidShrinePortal(echoPortal));
        Assert.True(ExtractionEventRuntime.IsValidShrinePortal(cairnPortal));
        Assert.True(controller.IsParticipant(echoPlayer));
        Assert.True(controller.IsParticipant(cairnPlayer));
        Assert.True(cairnPortal.OnMoveOver(visitor));
        Assert.True(controller.IsParticipant(visitor));
        var confirmation = visitor.FindGump<MoongateTravelConfirmation>();
        Assert.NotNull(confirmation);
        confirmation.OnResponse(visitor.NetState, new RelayInfo(1, ReadOnlySpan<int>.Empty,
            ReadOnlySpan<ushort>.Empty, ReadOnlySpan<Range>.Empty, ReadOnlySpan<byte>.Empty));
        Assert.False(controller.IsParticipant(visitor));
        Assert.True(ExtractionDungeonLayout.LobbyBounds.Contains(visitor.Location));
        Assert.Same(visitor.Backpack, loot.Parent);
        Assert.Equal(456, loot.Amount);
        Assert.False(echo.TryBegin(echoPlayer, out _));
        Assert.False(cairn.TryBegin(cairnPlayer, out _));
        Assert.Same(echoPortal, echo.Portal);
        Assert.Same(cairnPortal, cairn.Portal);
        SetControllerField(controller, "_phase", ExtractionEventPhase.Finishing);
        var adapter = (IExtractionEventIntegration)typeof(ExtractionEventRuntime)
            .GetField("_adapter", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        adapter.NotifyPhase(controller);
        Assert.Null(echo.Portal);
        Assert.Null(cairn.Portal);
        Assert.True(echoPortal.Deleted);
        Assert.True(cairnPortal.Deleted);
        Assert.All(gates, gate => Assert.False(gate.Deleted));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MovingAway_CancelsOnlyTheSelectedShrinesRitual(bool useCairn)
    {
        var controller = BindMinimalRealAdapter(out _, out _, out _, out _);
        Assert.True(ExtractionEventRuntime.TryInstallEchoShrine(out var echo, out _, out var error), error);
        Assert.True(ExtractionEventRuntime.TryInstallCairnShrine(out var cairn, out _, out error), error);
        Track(echo);
        Track(cairn);
        SetControllerField(controller, "_phase", ExtractionEventPhase.Active);
        SetControllerField(controller, "_matchDeadline", Core.Now + TimeSpan.FromMinutes(8));
        echo.OnEventPhaseChanged(controller);
        cairn.OnEventPhaseChanged(controller);
        var target = useCairn ? cairn : echo;
        var other = useCairn ? echo : cairn;
        var movingPlayer = Participant(controller, new Point3D(target.X, target.Y + 2, target.Z));
        var stayingPlayer = Participant(controller, new Point3D(other.X, other.Y + 2, other.Z));
        Assert.True(target.TryBegin(movingPlayer, out error), error);
        Assert.True(other.TryBegin(stayingPlayer, out error), error);
        movingPlayer.MoveToWorld(new Point3D(movingPlayer.X, movingPlayer.Y + 1, movingPlayer.Z), Map.Felucca);
        AdvanceShrines(12000, target, other);
        Assert.Equal(0, target.ActiveChannels);
        Assert.Null(target.Portal);
        Assert.NotNull(other.Portal);
        Assert.True(controller.IsParticipant(movingPlayer));
        Assert.True(controller.IsParticipant(stayingPlayer));
    }

    [Fact]
    public void RecordedDoorRemainsSealedWhenRuntimeBindingIsUnavailable_WhileUnmanagedDoorsRemainNormal()
    {
        var controller = BindMinimalRealAdapter(out _, out _, out var door, out _);
        var unmanaged = Track(new EventDungeonDoor(DoorFacing.WestCW));
        var adapter = typeof(ExtractionEventRuntime).GetField("_adapter", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        SetControllerField(controller, "_phase", ExtractionEventPhase.Active);
        SetControllerField(controller, "_matchDeadline", Core.Now + TimeSpan.FromMinutes(8));
        Assert.True(ExtractionEventRuntime.CanOpenEventDoor(door, out _));
        SetRuntimeField("_adapter", null);
        try
        {
            Assert.False(ExtractionEventRuntime.CanOpenEventDoor(door, out var error));
            Assert.Contains("7 minutes", error);
            Assert.True(ExtractionEventRuntime.CanOpenEventDoor(unmanaged, out error));
            Assert.Null(error);
        }
        finally
        {
            SetRuntimeField("_adapter", adapter);
        }
    }

    [Fact]
    public void BothFlamesHideUntilExactlySevenMinutes_AndHideAgainForTheNextMatch()
    {
        var controller = BindMinimalRealAdapter(out _, out _, out _, out _);
        Assert.True(ExtractionEventRuntime.TryInstallEchoShrine(out var echo, out _, out var error), error);
        Assert.True(ExtractionEventRuntime.TryInstallCairnShrine(out var cairn, out _, out error), error);
        Track(echo);
        Track(cairn);
        SetControllerField(controller, "_phase", ExtractionEventPhase.Active);
        SetControllerField(controller, "_matchDeadline", Core.Now + ExtractionEventController.MatchDuration);
        var adapter = (IExtractionEventIntegration)typeof(ExtractionEventRuntime)
            .GetField("_adapter", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        adapter.NotifyPhase(controller);
        var echoPlayer = Participant(controller, new Point3D(echo.X, echo.Y + 2, echo.Z));
        var cairnPlayer = Participant(controller, new Point3D(cairn.X, cairn.Y + 2, cairn.Z));
        foreach (var shrine in new[] { echo, cairn })
        {
            Assert.True(shrine.ExactAppearance);
            Assert.False(shrine.Components[9].Visible);
            for (var index = 0; index < shrine.Components.Count; index++)
            {
                if (index != 9)
                {
                    Assert.True(shrine.Components[index].Visible);
                }
            }
        }
        Assert.False(echo.TryBegin(echoPlayer, out error));
        Assert.Contains("7 minutes", error);
        Assert.False(cairn.TryBegin(cairnPlayer, out _));
        Core._now = controller.MatchDeadline - ExtractionEventController.MatchDuration +
                    ExtractionEventController.ExtractionDelay - TimeSpan.FromMilliseconds(1);
        controller.Advance();
        Assert.False(echo.Components[9].Visible);
        Assert.False(cairn.Components[9].Visible);
        Core._now += TimeSpan.FromMilliseconds(1);
        controller.Advance();
        Assert.True(echo.Components[9].Visible);
        Assert.True(cairn.Components[9].Visible);
        Assert.True(echo.TryBegin(echoPlayer, out error), error);
        Assert.True(cairn.TryBegin(cairnPlayer, out error), error);
        SetControllerField(controller, "_phase", ExtractionEventPhase.Starting);
        adapter.NotifyPhase(controller);
        Assert.False(echo.Components[9].Visible);
        Assert.False(cairn.Components[9].Visible);
        Assert.Equal(0, echo.ActiveChannels);
        Assert.Equal(0, cairn.ActiveChannels);
        SetControllerField(controller, "_phase", ExtractionEventPhase.Active);
        SetControllerField(controller, "_matchDeadline", Core.Now + ExtractionEventController.MatchDuration);
        adapter.NotifyPhase(controller);
        Assert.False(echo.Components[9].Visible);
        Assert.False(cairn.Components[9].Visible);
    }

    [Theory]
    [InlineData("WrongShrine")]
    [InlineData("WrongController")]
    [InlineData("OtherTamper")]
    [InlineData("Duplicate")]
    [InlineData("Moved")]
    [InlineData("Active")]
    public void LegacyUpgrade_RefusesUnapprovedIdentityOrShapeWithoutChangingIt(string condition)
    {
        var controller = BindMinimalRealAdapter(out _, out _, out _, out _);
        Assert.True(ExtractionEventRuntime.TryInstallEchoShrine(out var shrine, out _, out var error), error);
        Track(shrine);
        MakePreviousFlame(shrine, 0x480, 0);
        var shrineSerial = shrine.Serial;
        var controllerSerial = controller.Serial;
        switch (condition)
        {
            case "WrongShrine":
                shrineSerial = (Serial)(shrineSerial.Value + 1);
                break;
            case "WrongController":
                controllerSerial = (Serial)(controllerSerial.Value + 1);
                break;
            case "OtherTamper":
                shrine.Components[10].Hue = 0x21;
                break;
            case "Duplicate":
                var duplicate = Track(new ExtractionEventShrine(controller));
                duplicate.MoveToWorld(ExtractionShrinePrototype.Point, Map.Felucca);
                controller.AddManagedItem(duplicate);
                break;
            case "Moved":
                shrine.MoveToWorld(new Point3D(shrine.X, shrine.Y + 1, shrine.Z), Map.Felucca);
                break;
            case "Active":
                typeof(ExtractionEventController).GetField("_phase", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(controller, ExtractionEventPhase.Active);
                break;
        }
        var managed = controller.ManagedItems.Count;
        var position = shrine.Location;
        Assert.False(ExtractionEventRuntime.TryUpgradeEchoShrine(shrineSerial, controllerSerial,
            out var result, out var updated, out _));
        Assert.Null(result);
        Assert.False(updated);
        Assert.Equal(0x480, shrine.Components[9].Hue);
        Assert.Equal(managed, controller.ManagedItems.Count);
        Assert.Equal(position, shrine.Location);
        Assert.False(shrine.Deleted);
    }

    [Fact]
    public void RealV35Reset_WithInstalledShrine_PreparesAll110RoomMonstersAndCellarWithoutChangingShrine()
    {
        var controller = BindMinimalRealAdapter(out _, out var preservedGold, out _, out _, true);
        Assert.True(ExtractionEventRuntime.TryInstallEchoShrine(out var shrine, out _, out var error), error);
        Track(shrine);
        var componentSerials = new Serial[shrine.Components.Count];
        for (var index = 0; index < componentSerials.Length; index++)
        {
            componentSerials[index] = shrine.Components[index].Serial;
        }
        var adapter = (IExtractionEventIntegration)typeof(ExtractionEventRuntime)
            .GetField("_adapter", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var session = adapter.CreateResetSession(controller);
        for (var iteration = 0; iteration < 2000 && !session.Complete; iteration++)
        {
            session.Process(8);
        }
        Assert.True(session.Complete);
        Assert.Equal(111, controller.OwnedCreatures.Count);
        var installation = Assert.IsType<ExtractionEventInstallation>(controller.ManagedItems[0]);
        var features = installation.Features;
        var manifest = LoadManifest();
        var count = 0;
        foreach (var room in ExtractionDungeonLayout.RoomsFor(manifest))
        {
            foreach (var spawn in room.Creatures)
            {
                var creature = Assert.Single(controller.OwnedCreatures,
                    candidate => candidate.Home == spawn.Position && candidate.GetType().Name == spawn.Kind.ToString());
                Assert.Equal(spawn.Position, creature.Location);
                Assert.True(Map.Felucca.CanFit(creature.Location, 16, true, false));
                Assert.True(creature.Frozen);
                Assert.Null(creature.Spawner);
                count++;
            }
        }
        Assert.Equal(110, count);
        Assert.Contains(features.Lich, controller.OwnedCreatures);
        Assert.Equal(manifest.HiddenCellar.CreaturePosition.Point, features.Lich.Location);
        Assert.True(Map.Felucca.CanFit(features.Lich.Location, 16, true, false));
        Assert.All(installation.Chests, chest => Assert.NotEmpty(chest.Items));
        Assert.False(preservedGold.Deleted);
        Assert.Same(controller.RecoveryStorage, preservedGold.Parent);
        Assert.Equal(315, preservedGold.Amount);
        Assert.Same(controller, shrine.Controller);
        Assert.Equal(ExtractionShrinePrototype.Point, shrine.Location);
        Assert.True(shrine.ExactAppearance);
        Assert.True(ExtractionShrinePrototype.CheckStandingTiles());
        Assert.Equal(componentSerials.Length, shrine.Components.Count);
        for (var index = 0; index < componentSerials.Length; index++)
        {
            Assert.Equal(componentSerials[index], shrine.Components[index].Serial);
        }
        var lootCount = controller.OwnedLoot.Count;
        session.Process(int.MaxValue);
        Assert.Equal(111, controller.OwnedCreatures.Count);
        Assert.Equal(lootCount, controller.OwnedLoot.Count);
    }

    private static void MakePreviousFlame(ExtractionEventShrine shrine, int hue, int height)
    {
        for (var index = shrine.Components.Count - 1; index >= 18; index--)
        {
            var component = shrine.Components[index];
            shrine.Components.RemoveAt(index);
            component.Addon = null;
            component.Delete();
        }
        var flame = shrine.Components[9];
        flame.ItemID = 0x19AB;
        flame.Hue = hue;
        flame.Light = LightType.Circle300;
        flame.Offset = new Point3D(0, 0, height);
        flame.MoveToWorld(new Point3D(shrine.X, shrine.Y, shrine.Z + height), shrine.Map);
    }

    private AddonComponent[] MakePreviousRunes(ExtractionEventShrine shrine)
    {
        var first = shrine.Components[9];
        first.ItemID = 0xE5C;
        first.Visible = true;
        first.Hue = 0;
        first.Light = LightType.Circle150;
        first.Offset = new Point3D(-1, 0, 1);
        first.MoveToWorld(new Point3D(shrine.X - 1, shrine.Y, shrine.Z + 1), shrine.Map);
        var added = new AddonComponent[3];
        var offsets = new Point3D[] { new(0, -1, 1), new(1, 0, 1), new(0, 1, 1) };
        var itemIds = new[] { 0xE5F, 0xE62, 0xE65 };
        for (var index = 0; index < added.Length; index++)
        {
            added[index] = Track(new AddonComponent(itemIds[index]) { Hue = 0, Light = LightType.Circle150 });
            var offset = offsets[index];
            shrine.AddComponent(added[index], offset.X, offset.Y, offset.Z);
        }
        return added;
    }

    private PlayerMobile Participant(ExtractionEventController controller, Point3D point)
    {
        var state = PacketTestUtilities.CreateTestNetState();
        _states.Add(state);
        state.Account = new MockAccount();
        var player = Track(new PlayerMobile { Body = 0x190, Player = true, Name = "Shrine Prototype Tester" });
        player.AddItem(Track(new Backpack()));
        player.NetState = state;
        state.Mobile = player;
        player.MoveToWorld(point, Map.Felucca);
        ((List<Mobile>)typeof(ExtractionEventController).GetField("_participantReferences",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller)).Add(player);
        ((HashSet<Mobile>)typeof(ExtractionEventController).GetField("_participantSet",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller)).Add(player);
        return player;
    }

    private static void AdvanceShrines(long milliseconds, params ExtractionEventShrine[] shrines)
    {
        Core._tickCount = unchecked(Core.TickCount + milliseconds);
        Core._now += TimeSpan.FromMilliseconds(milliseconds);
        foreach (var shrine in shrines)
        {
            shrine.Advance();
        }
    }

    private static void SetControllerField(ExtractionEventController controller, string name, object value) =>
        typeof(ExtractionEventController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, value);

    private ExtractionEventController BindMinimalRealAdapter(
        out EventDungeonChest firstChest, out Gold contents, out EventDungeonDoor firstDoor,
        out ExtractionEventGate[] gates, bool nativeRecords = false)
    {
        var manifest = LoadManifest();
        var controller = Track(new ExtractionEventController());
        var storage = Track(new WoodenBox { Visible = false, Movable = false, MaxItems = int.MaxValue });
        storage.MoveToWorld(Point3D.Zero, Map.Internal);
        controller.SetRecoveryStorage(storage);
        var doors = new EventDungeonDoor[71];
        for (var index = 0; index < doors.Length; index++)
        {
            doors[index] = Track(new EventDungeonDoor(nativeRecords ? Enum.Parse<DoorFacing>(manifest.Doors[index].Facing) : DoorFacing.WestCW));
            if (nativeRecords)
            {
                doors[index].MoveToWorld(manifest.Doors[index].Closed.Point, Map.Felucca);
            }
        }
        firstDoor = doors[0];
        if (!nativeRecords)
        {
            firstDoor.MoveToWorld(new Point3D(5500, 700, 0), Map.Felucca);
        }
        firstDoor.Open = true;
        var chests = new EventDungeonChest[100];
        for (var index = 0; index < chests.Length; index++)
        {
            var definition = manifest.Chests[index];
            chests[index] = Track(nativeRecords && definition.Type == "RootboundCellarChest"
                ? new RootboundCellarChest(definition.ItemId)
                : new EventDungeonChest(nativeRecords ? definition.ItemId : 0xE40));
            if (nativeRecords)
            {
                chests[index].MoveToWorld(definition.Position.Point, Map.Felucca);
            }
        }
        firstChest = chests[0];
        contents = Track(new Gold(315));
        firstChest.DropItem(contents);
        RootboundCellarFeature.Features features;
        if (nativeRecords)
        {
            var cellar = manifest.HiddenCellar;
            features = RootboundCellarFeature.Create(Map.Felucca, cellar.Hatch.Point, cellar.Entry.Point,
                cellar.ReturnRune.Point, cellar.SurfaceReturn.Point, cellar.CreaturePosition.Point,
                Array.Find(chests, chest => chest is RootboundCellarChest));
            Track(features.Cover);
            Track(features.Hatch);
            Track(features.ReturnRune);
            Track(features.Lich);
        }
        else
        {
            features = new RootboundCellarFeature.Features
            {
                Hatch = Track(new EventDungeonHiddenHatch()),
                ReturnRune = Track(new RootboundCellarReturnRune())
            };
        }
        var installation = Track(new ExtractionEventInstallation());
        installation.Initialize(controller, doors, chests, features);
        installation.MarkLobbyComplete();
        controller.AddManagedItem(installation);
        gates = new ExtractionEventGate[3];
        for (var index = 0; index < gates.Length; index++)
        {
            gates[index] = Track(new ExtractionEventGate(controller, ExtractionEventGateKind.Extraction));
            gates[index].MoveToWorld(ExtractionEventRuntime.ExitPoints[index], Map.Felucca);
            controller.AddManagedItem(gates[index]);
        }
        InvokeRuntime("Bind", controller, installation, manifest);
        controller.ConfigureWaiting(TimeSpan.FromMinutes(15), true);
        Assert.True(controller.IsBound);
        return controller;
    }

    private static ModularDungeonDeployment.Manifest LoadManifest()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "CLAUDE.md")))
        {
            root = root.Parent;
        }
        Assert.NotNull(root);
        return JsonSerializer.Deserialize<ModularDungeonDeployment.Manifest>(File.ReadAllBytes(
            Path.Combine(root.FullName, "artifacts", "modular-dungeon", "install-v35-20261010-r1", "staged-map", "install-manifest.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }

    private static void InvokeRuntime(string method, params object[] arguments) => typeof(ExtractionEventRuntime)
        .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, arguments);

    private static void SetRuntimeField(string field, object value) => typeof(ExtractionEventRuntime)
        .GetField(field, BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);
}
