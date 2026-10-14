using System;
using System.Collections.Generic;
using Server;
using Server.Engines.ModularDungeon;
using Server.Items;
using Server.Network;
using Server.Spells.First;
using Server.Tests;
using Server.Tests.Network;
using Xunit;

namespace UOContent.Tests.Engines.ModularDungeon;

[Collection("Sequential UOContent Tests")]
public class ExtractionEventInstallationTests : IDisposable
{
    private readonly List<IEntity> _created = [];
    private readonly List<NetState> _states = [];

    public void Dispose()
    {
        for (var index = _created.Count - 1; index >= 0; --index)
        {
            if (_created[index] is Mobile mobile)
            {
                mobile.Spell = null;
                mobile.NetState = null;
            }

            _created[index].Delete();
        }

        foreach (var state in _states)
        {
            state.Dispose();
        }
    }

    [Fact]
    public void Installation_IsHiddenAndImmovable_AndStartsWithAnIncompleteLobby()
    {
        var installation = Track(new ExtractionEventInstallation());
        Assert.False(installation.Visible);
        Assert.False(installation.Movable);
        Assert.False(installation.LobbyComplete);
        Assert.Null(installation.Controller);
        Assert.Empty(installation.Doors);
        Assert.Empty(installation.Chests);
    }

    [Fact]
    public void Initialize_CopiesOwnershipArrays_WithoutMovingOrChangingAdoptedObjects()
    {
        var controller = Track(new ExtractionEventController());
        var door = Track(new EventDungeonDoor());
        var chest = Track(new EventDungeonChest());
        var features = CreateFeatures();
        EventDungeonDoor[] doors = [door];
        EventDungeonChest[] chests = [chest];
        var installation = Track(new ExtractionEventInstallation());
        var gold = Track(new Gold(123));
        chest.DropItem(gold);

        installation.Initialize(controller, doors, chests, features);
        doors[0] = Track(new EventDungeonDoor());
        chests[0] = Track(new EventDungeonChest());

        Assert.Same(controller, installation.Controller);
        Assert.Same(door, Assert.Single(installation.Doors));
        Assert.Same(chest, Assert.Single(installation.Chests));
        Assert.Same(features.Cover, installation.Cover);
        Assert.Same(features.Hatch, installation.Hatch);
        Assert.Same(features.ReturnRune, installation.ReturnRune);
        Assert.Same(features.Lich, installation.Lich);
        Assert.Same(gold, Assert.Single(chest.Items));
        Assert.Same(Map.Internal, door.Map);
        Assert.Same(Map.Internal, chest.Map);
        Assert.False(installation.LobbyComplete);
    }

    [Fact]
    public void Initialize_RejectsRebindingWithoutChangingTheOriginalOwnership()
    {
        var installation = CreateInstallation(out var controller, out _, out _, out var features);
        var replacement = Track(new ExtractionEventController());
        Assert.Throws<InvalidOperationException>(() =>
            installation.Initialize(replacement, [], [], features));
        Assert.Same(controller, installation.Controller);
    }

    [Theory]
    [InlineData("NullController")]
    [InlineData("DeletedController")]
    [InlineData("NullDoors")]
    [InlineData("DuplicateDoor")]
    [InlineData("DeletedDoor")]
    [InlineData("NullDoor")]
    [InlineData("NullChests")]
    [InlineData("DuplicateChest")]
    [InlineData("DeletedChest")]
    [InlineData("NullChest")]
    [InlineData("NullFeatures")]
    [InlineData("DeletedHatch")]
    [InlineData("DeletedRune")]
    public void Initialize_RejectsInvalidOwnershipBeforeAdoptingAnything(string invalid)
    {
        var installation = Track(new ExtractionEventInstallation());
        var controller = Track(new ExtractionEventController());
        var door = Track(new EventDungeonDoor());
        var chest = Track(new EventDungeonChest());
        var features = CreateFeatures();
        EventDungeonDoor[] doors = [door];
        EventDungeonChest[] chests = [chest];

        switch (invalid)
        {
            case "NullController":
            {
                controller = null;
                break;
            }
            case "DeletedController":
            {
                controller.Delete();
                break;
            }
            case "NullDoors":
            {
                doors = null;
                break;
            }
            case "DuplicateDoor":
            {
                doors = [door, door];
                break;
            }
            case "DeletedDoor":
            {
                door.Delete();
                break;
            }
            case "NullDoor":
            {
                doors = [null];
                break;
            }
            case "NullChests":
            {
                chests = null;
                break;
            }
            case "DuplicateChest":
            {
                chests = [chest, chest];
                break;
            }
            case "DeletedChest":
            {
                chest.Delete();
                break;
            }
            case "NullChest":
            {
                chests = [null];
                break;
            }
            case "NullFeatures":
            {
                features = null;
                break;
            }
            case "DeletedHatch":
            {
                features.Hatch.Delete();
                break;
            }
            case "DeletedRune":
            {
                features.ReturnRune.Delete();
                break;
            }
        }

        Assert.ThrowsAny<ArgumentException>(() => installation.Initialize(controller, doors, chests, features));
        Assert.Null(installation.Controller);
        Assert.Empty(installation.Doors);
        Assert.Empty(installation.Chests);
    }

    [Fact]
    public void SyncFeatures_TracksReplacementCoverAndLichWithoutExchangingTransports()
    {
        var installation = CreateInstallation(out _, out _, out _, out var features);
        var replacementCover = Track(new WoodenBox());
        var replacementLich = Track(new RootboundCellarLich());
        var updated = new RootboundCellarFeature.Features
        {
            Cover = replacementCover,
            Hatch = features.Hatch,
            ReturnRune = features.ReturnRune,
            Lich = replacementLich
        };

        installation.SyncFeatures(updated);
        var snapshot = installation.Features;
        Assert.Same(replacementCover, installation.Cover);
        Assert.Same(replacementLich, installation.Lich);
        Assert.Same(updated.Cover, snapshot.Cover);
        Assert.Same(updated.Lich, snapshot.Lich);
        Assert.Same(features.Hatch, snapshot.Hatch);
        Assert.Same(features.ReturnRune, snapshot.ReturnRune);
        Assert.False(features.Cover.Deleted);
        Assert.False(features.Lich.Deleted);
    }

    [Fact]
    public void SyncFeatures_RejectsAReplacementTransportBeforeChangingCoverOrLich()
    {
        var installation = CreateInstallation(out _, out _, out _, out var features);
        var invalid = new RootboundCellarFeature.Features
        {
            Cover = Track(new WoodenBox()),
            Hatch = Track(new EventDungeonHiddenHatch()),
            ReturnRune = features.ReturnRune,
            Lich = null
        };

        Assert.Throws<ArgumentException>(() => installation.SyncFeatures(invalid));
        Assert.Same(features.Cover, installation.Cover);
        Assert.Same(features.Hatch, installation.Hatch);
        Assert.Same(features.Lich, installation.Lich);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SaveLoad_PreservesOwnershipAndLobbyCompletionWithoutRespawningAnything(bool complete)
    {
        var installation = CreateInstallation(out var controller, out var door, out var chest, out var features);
        if (complete)
        {
            installation.MarkLobbyComplete();
            installation.MarkLobbyComplete();
        }

        var bytes = Snapshot(installation);
        installation.Delete();
        var restored = Track(new ExtractionEventInstallation(World.NewItem));
        var reader = new BufferReader(bytes);
        restored.Deserialize(reader);

        Assert.Equal(bytes.Length, reader.Position);
        Assert.Same(controller, restored.Controller);
        Assert.Same(door, Assert.Single(restored.Doors));
        Assert.Same(chest, Assert.Single(restored.Chests));
        Assert.Same(features.Cover, restored.Cover);
        Assert.Same(features.Hatch, restored.Hatch);
        Assert.Same(features.ReturnRune, restored.ReturnRune);
        Assert.Same(features.Lich, restored.Lich);
        Assert.Equal(complete, restored.LobbyComplete);
        Assert.False(restored.Visible);
        Assert.False(restored.Movable);
    }

    [Fact]
    public void MissingOrDeadLich_DoesNotPreventPersistingTheRemainingCellarFeatures()
    {
        var controller = Track(new ExtractionEventController());
        var features = CreateFeatures();
        features.Lich.Delete();
        var installation = Track(new ExtractionEventInstallation());
        installation.Initialize(controller, [], [], features);
        Assert.Null(installation.Lich);

        var snapshot = installation.Features;
        Assert.Null(snapshot.Lich);
        Assert.Same(features.Hatch, snapshot.Hatch);
        Assert.Same(features.ReturnRune, snapshot.ReturnRune);
        installation.SyncFeatures(snapshot);
        Assert.Null(installation.Lich);
    }

    [Fact]
    public void DeletingInstallation_ClearsReferencesWithoutDeletingAdoptedWorldObjects()
    {
        var installation = CreateInstallation(out var controller, out var door, out var chest, out var features);
        installation.Delete();

        Assert.Null(installation.Controller);
        Assert.Empty(installation.Doors);
        Assert.Empty(installation.Chests);
        Assert.Null(installation.Cover);
        Assert.Null(installation.Hatch);
        Assert.Null(installation.ReturnRune);
        Assert.Null(installation.Lich);
        Assert.False(controller.Deleted);
        Assert.False(door.Deleted);
        Assert.False(chest.Deleted);
        Assert.False(features.Cover.Deleted);
        Assert.False(features.Hatch.Deleted);
        Assert.False(features.ReturnRune.Deleted);
        Assert.False(features.Lich.Deleted);
    }

    [Fact]
    public void IncompleteOrDeletedInstallation_CannotBeMarkedAsACompletedLobby()
    {
        var incomplete = Track(new ExtractionEventInstallation());
        Assert.Throws<InvalidOperationException>(incomplete.MarkLobbyComplete);
        var installation = CreateInstallation(out _, out _, out _, out _);
        installation.Delete();
        Assert.Throws<InvalidOperationException>(installation.MarkLobbyComplete);
    }

    [Theory]
    [InlineData(ExtractionEventGateKind.Entry, MoongateAppearance.Blue, 0x3F)]
    [InlineData(ExtractionEventGateKind.LobbyReturn, MoongateAppearance.Blue, 0x482)]
    [InlineData(ExtractionEventGateKind.Extraction, 0x1822, 0x3F)]
    public void GateKinds_HaveDistinctIntendedArtworkAndColors(ExtractionEventGateKind kind, int artId, int hue)
    {
        var controller = Track(new ExtractionEventController());
        var gate = Track(new ExtractionEventGate(controller, kind));
        Assert.Equal(kind, gate.Kind);
        Assert.Same(controller, gate.Controller);
        Assert.Equal(artId, gate.ItemID);
        Assert.Equal(hue, gate.Hue);
        Assert.False(gate.Movable);
        Assert.True(gate.Visible);
        Assert.Equal(LightType.Circle300, gate.Light);
    }

    [Theory]
    [InlineData(ExtractionEventGateKind.Entry)]
    [InlineData(ExtractionEventGateKind.LobbyReturn)]
    [InlineData(ExtractionEventGateKind.Extraction)]
    public void GateSaveLoad_PreservesItsControllerKindAndIntendedAppearance(ExtractionEventGateKind kind)
    {
        var controller = Track(new ExtractionEventController());
        var gate = Track(new ExtractionEventGate(controller, kind));
        var bytes = Snapshot(gate);
        gate.Delete();
        Assert.Null(gate.Controller);

        var restored = Track(new ExtractionEventGate(World.NewItem));
        var reader = new BufferReader(bytes);
        restored.Deserialize(reader);

        Assert.Equal(bytes.Length, reader.Position);
        Assert.Same(controller, restored.Controller);
        Assert.Equal(kind, restored.Kind);
        Assert.Equal(kind == ExtractionEventGateKind.Extraction ? 0x1822 : MoongateAppearance.Blue, restored.ItemID);
        Assert.Equal(kind == ExtractionEventGateKind.LobbyReturn ? 0x482 : 0x3F, restored.Hue);
        Assert.True(restored.Visible);
        Assert.False(restored.Movable);
        Assert.False(controller.Deleted);
    }

    [Theory]
    [InlineData("Dead")]
    [InlineData("Disconnected")]
    [InlineData("Creature")]
    [InlineData("Deleted")]
    [InlineData("Distant")]
    [InlineData("OtherMap")]
    [InlineData("OtherFloor")]
    [InlineData("Casting")]
    [InlineData("GateParent")]
    [InlineData("DeletedController")]
    public void GateValidation_RejectsIneligibleUseWithoutMovingTheActor(string invalid)
    {
        var controller = Track(new ExtractionEventController());
        var gate = Track(new ExtractionEventGate(controller, ExtractionEventGateKind.Entry));
        gate.MoveToWorld(new Point3D(100, 100, 0), Map.Felucca);
        var actor = CreateActor();

        switch (invalid)
        {
            case "Dead":
            {
                actor.Body = 0x192;
                break;
            }
            case "Disconnected":
            {
                actor.NetState = null;
                break;
            }
            case "Creature":
            {
                actor.Player = false;
                break;
            }
            case "Deleted":
            {
                actor.Delete();
                break;
            }
            case "Distant":
            {
                actor.MoveToWorld(new Point3D(102, 100, 0), Map.Felucca);
                break;
            }
            case "OtherMap":
            {
                actor.MoveToWorld(new Point3D(100, 100, 0), Map.Trammel);
                break;
            }
            case "OtherFloor":
            {
                actor.MoveToWorld(new Point3D(100, 100, -25), Map.Felucca);
                break;
            }
            case "Casting":
            {
                actor.Spell = new MagicArrowSpell(actor, null);
                break;
            }
            case "GateParent":
            {
                Track(new Backpack()).DropItem(gate);
                break;
            }
            case "DeletedController":
            {
                controller.Delete();
                break;
            }
        }

        var location = actor.Location;
        var map = actor.Map;
        Assert.False(gate.CanUseGate(actor, out var error));
        Assert.False(string.IsNullOrEmpty(error));
        gate.OnDoubleClick(actor);
        Assert.True(gate.OnMoveOver(actor));
        Assert.Equal(location, actor.Location);
        Assert.Same(map, actor.Map);
    }

    [Fact]
    public void GateValidation_DoesNotRejectCombatantsOrCriminalsBeforeRuntimePolicy()
    {
        var controller = Track(new ExtractionEventController());
        var gate = Track(new ExtractionEventGate(controller, ExtractionEventGateKind.Extraction));
        gate.MoveToWorld(new Point3D(100, 100, 0), Map.Felucca);
        var actor = CreateActor();
        actor.Criminal = true;
        actor.Combatant = Track(new Mobile());

        Assert.True(gate.CanUseGate(actor, out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData(ExtractionEventGateKind.Entry, false)]
    [InlineData(ExtractionEventGateKind.Extraction, false)]
    [InlineData(ExtractionEventGateKind.LobbyReturn, true)]
    public void Ghosts_CanOnlyUseTheBlueLobbyReturnRoute(ExtractionEventGateKind kind, bool expected)
    {
        var controller = Track(new ExtractionEventController());
        var gate = Track(new ExtractionEventGate(controller, kind));
        gate.MoveToWorld(new Point3D(100, 100, 0), Map.Felucca);
        var ghost = CreateActor();
        ghost.Body = 0x192;

        Assert.False(ghost.Alive);
        Assert.Equal(expected, gate.CanUseGate(ghost, out _));
    }

    [Fact]
    public void UnboundGate_AndUnknownKindRemainInactive()
    {
        var unbound = Track(new ExtractionEventGate());
        var actor = CreateActor();
        Assert.False(unbound.CanUseGate(actor, out _));
        Assert.True(unbound.OnMoveOver(actor));
        var controller = Track(new ExtractionEventController());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExtractionEventGate(controller, (ExtractionEventGateKind)99));
    }

    private ExtractionEventInstallation CreateInstallation(
        out ExtractionEventController controller,
        out EventDungeonDoor door,
        out EventDungeonChest chest,
        out RootboundCellarFeature.Features features
    )
    {
        controller = Track(new ExtractionEventController());
        door = Track(new EventDungeonDoor());
        chest = Track(new EventDungeonChest());
        features = CreateFeatures();
        var installation = Track(new ExtractionEventInstallation());
        installation.Initialize(controller, [door], [chest], features);
        return installation;
    }

    private RootboundCellarFeature.Features CreateFeatures() => new()
    {
        Cover = Track(new WoodenBox()),
        Hatch = Track(new EventDungeonHiddenHatch()),
        ReturnRune = Track(new RootboundCellarReturnRune()),
        Lich = Track(new RootboundCellarLich())
    };

    private Mobile CreateActor()
    {
        var state = PacketTestUtilities.CreateTestNetState();
        _states.Add(state);
        state.Account = new MockAccount();
        var player = Track(new Mobile { Player = true, Body = 0x190, AccessLevel = AccessLevel.GameMaster });
        player.NetState = state;
        state.Mobile = player;
        player.MoveToWorld(new Point3D(100, 100, 0), Map.Felucca);
        return player;
    }

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }

    private static byte[] Snapshot(ISerializable entity)
    {
        var writer = new BufferWriter(true);
        entity.Serialize(writer);
        return writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();
    }
}
