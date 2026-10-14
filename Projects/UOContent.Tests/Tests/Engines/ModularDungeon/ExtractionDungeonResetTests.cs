using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Server;
using Server.Engines.ModularDungeon;
using Server.Items;
using Server.Mobiles;
using Server.Network;
using Server.Tests;
using Server.Tests.Network;
using Xunit;

namespace UOContent.Tests.Engines.ModularDungeon;

[Collection("Sequential UOContent Tests")]
public class ExtractionDungeonResetTests : IDisposable
{
    private readonly List<IEntity> _created = [];
    private readonly List<NetState> _states = [];
    private readonly Expansion _expansion = Core.Expansion;
    private readonly long _tickCount = Core.TickCount;
    private readonly DateTime _now = Core.Now;

    public ExtractionDungeonResetTests()
    {
        Core.Expansion = Expansion.T2A;
        Timer.Init(Core.TickCount);
    }

    public void Dispose()
    {
        ExtractionDungeonReset.ManagedChestLookup = null;
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

        Core.Expansion = _expansion;
        Core._tickCount = _tickCount;
        Core._now = _now;
        Timer.Init(Core.TickCount);
    }

    [Fact]
    public void DoorReset_CancelsOpeningAndRestoresClosedLockedKeylessGeometry()
    {
        var door = Track(new EventDungeonDoor());
        var point = new Point3D(100, 100, 0);
        door.MoveToWorld(point, Map.Felucca);
        var player = Actor(point);
        door.OnDoubleClick(player);
        Assert.True(OpeningTimer(door).Running);
        door.Open = true;
        door.Locked = false;
        door.KeyValue = 123;
        var offset = BaseDoor.GetOffset(DoorFacing.WestCW);
        var definition = new ModularDungeonDeployment.DoorDefinition("door", new(100, 100, 0),
            new(100 + offset.X, 100 + offset.Y, offset.Z), "WestCW", door.ClosedId, door.OpenedId, []);

        door.ResetForMatch(Map.Felucca, definition);
        Advance(3000);

        Assert.False(door.Open);
        Assert.True(door.Locked);
        Assert.Equal(0u, door.KeyValue);
        Assert.Equal(point, door.Location);
        Assert.Equal(definition.ClosedId, door.ItemID);
        Assert.False(door.AutoClose);
        Assert.False(door.UseLocks());
        Assert.False(OpeningTimer(door).Running);
        Assert.Null(OpeningMobile(door));
    }

    [Fact]
    public void ChestReset_CancelsPendingOpeningAndInvalidatesExistingOpenersWithoutDeletingContents()
    {
        var chest = Track(new EventDungeonChest());
        var point = new Point3D(100, 100, 0);
        chest.MoveToWorld(point, Map.Felucca);
        var player = Actor(point);
        var marker = Track(new Gold(17));
        chest.DropItem(marker);
        chest.OnDoubleClick(player);
        chest.Openers = [player];
        Assert.True(OpeningTimer(chest).Running);

        chest.ResetForMatch(Map.Felucca, point, 0xE43);
        Advance(6000);

        Assert.Null(chest.Openers);
        Assert.False(OpeningTimer(chest).Running);
        Assert.Null(OpeningMobile(chest));
        Assert.Same(marker, Assert.Single(chest.Items));
        Assert.False(marker.Deleted);
        Assert.False(chest.Movable);
        Assert.Equal(0xE43, chest.ItemID);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RegisteredChest_RejectsEveryPlayerDepositRoute(int route)
    {
        var chest = Track(new EventDungeonChest());
        chest.MoveToWorld(new Point3D(100, 100, 0), Map.Felucca);
        var player = Actor(chest.Location);
        chest.Openers = [player];
        var playerGold = Track(new Gold(33));
        var loot = Track(new Gold(12));
        chest.DropItem(loot);
        ExtractionDungeonReset.ManagedChestLookup = candidate => candidate == chest;

        var accepted = route switch
        {
            0 => chest.TryDropItem(player, playerGold, false),
            1 => chest.TryDropItem(player, playerGold, false, false),
            2 => chest.OnDragDrop(player, playerGold),
            3 => chest.OnDragDropInto(player, playerGold, Point3D.Zero),
            _ => chest.OnStackAttempt(player, loot, playerGold)
        };

        Assert.False(accepted);
        Assert.False(playerGold.Deleted);
        Assert.Null(playerGold.Parent);
        Assert.Equal(12, loot.Amount);
        Assert.Same(loot, Assert.Single(chest.Items));
    }

    [Fact]
    public void UnregisteredChest_DepositBehaviorIsUnchanged()
    {
        var chest = Track(new EventDungeonChest());
        chest.MoveToWorld(new Point3D(100, 100, 0), Map.Felucca);
        var player = Actor(chest.Location);
        chest.Openers = [player];
        var gold = Track(new Gold(33));
        ExtractionDungeonReset.ManagedChestLookup = _ => false;

        Assert.True(chest.TryDropItem(player, gold, false));
        Assert.Same(chest, gold.Parent);
    }

    [Theory]
    [InlineData("Skeleton")]
    [InlineData("Wraith")]
    [InlineData("Mummy")]
    [InlineData("GiantSpider")]
    [InlineData("DireWolf")]
    [InlineData("TimberWolf")]
    [InlineData("SilverSerpent")]
    [InlineData("Gazer")]
    [InlineData("RootboundCellarLich")]
    public void CreaturePreparation_DoublesTheResolvedT2AMaximumExactly_WithoutSpawner(string type)
    {
        var creature = Track(ExtractionDungeonReset.CreateCreature(type));
        var originalMaximum = creature.HitsMax;
        var home = new Point3D(100, 100, 0);

        ExtractionDungeonReset.PrepareCreature(creature, Map.Felucca, home, 3);

        Assert.Equal(originalMaximum * 2, creature.HitsMax);
        Assert.Equal(creature.HitsMax, creature.Hits);
        Assert.Equal(originalMaximum * 2, creature.HitsMaxSeed);
        Assert.True(creature.Frozen);
        Assert.False(creature.Tamable);
        Assert.Null(creature.Spawner);
        Assert.Equal(home, creature.Home);
        Assert.Equal(Map.Felucca, creature.HomeMap);
        Assert.Equal(3, creature.RangeHome);
        var bytes = Snapshot(creature);
        var restored = Track((BaseCreature)Activator.CreateInstance(creature.GetType(), World.NewMobile));
        var reader = new BufferReader(bytes);
        restored.Deserialize(reader);
        Assert.Equal(bytes.Length, reader.Position);
        Assert.Equal(originalMaximum * 2, restored.HitsMax);
        Assert.False(restored.Tamable);
        Assert.Null(restored.Spawner);
    }

    [Fact]
    public void UnsupportedCreatureType_IsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => ExtractionDungeonReset.CreateCreature("Dragon"));
    }

    [Fact]
    public void CompletedReset_RefillsEveryOwnedChestOnce_PreservesUntrackedContentsAndPlayerLoot()
    {
        var state = CreateState();
        var context = state.Context;
        var previousMonster = Track(new Skeleton());
        previousMonster.MoveToWorld(context.Spawns[0].Position, Map.Felucca);
        state.Creatures.Add(previousMonster);
        var ownedResidual = Track(new Gold(17));
        context.Chests[0].DropItem(ownedResidual);
        state.Loot.Add(ownedResidual);
        var playerBag = Track(new Backpack());
        var extractedLoot = Track(new Gold(18));
        playerBag.DropItem(extractedLoot);
        state.Loot.Add(extractedLoot);
        var unknown = Track(new Gold(19));
        context.Chests[1].DropItem(unknown);
        var boxContents = Track(new Gold(20));
        context.Features.Cover.DropItem(boxContents);
        playerBag.DropItem(context.Features.Cover);
        var oldLich = context.Features.Lich;
        var session = ExtractionDungeonReset.CreateSession(context);

        RunToCompletion(session);

        Assert.True(previousMonster.Deleted);
        Assert.True(oldLich.Deleted);
        Assert.True(ownedResidual.Deleted);
        Assert.False(extractedLoot.Deleted);
        Assert.Same(playerBag, extractedLoot.Parent);
        Assert.False(unknown.Deleted);
        Assert.Same(context.RecoveryStorage, unknown.Parent);
        Assert.All(context.Chests, chest => Assert.True(chest.GetAmount(typeof(Gold)) >= 402));
        Assert.Equal(111, state.Creatures.Count);
        Assert.All(state.Creatures, creature => Assert.Null(creature.Spawner));
        Assert.All(state.Creatures, creature => Assert.True(creature.Frozen));
        Assert.True(context.Features.Hatch.IsCovered);
        Assert.Same(boxContents, Assert.Single(context.Features.Cover.Items));
        Assert.False(boxContents.Deleted);
        Assert.Contains(context.Features.Lich, state.Creatures);
        var lootCount = state.Loot.Count;
        var creatureCount = state.Creatures.Count;
        session.Process(int.MaxValue);
        Advance(60000);
        Assert.Equal(lootCount, state.Loot.Count);
        Assert.Equal(creatureCount, state.Creatures.Count);
    }

    [Fact]
    public void MissingConsumedCover_IsRecreatedAndReattachedToThePersistentHatch()
    {
        var state = CreateState();
        var oldCover = state.Context.Features.Cover;
        oldCover.Delete();
        var session = ExtractionDungeonReset.CreateSession(state.Context);

        RunToCompletion(session);

        Assert.NotSame(oldCover, state.Context.Features.Cover);
        Assert.False(state.Context.Features.Cover.Deleted);
        Assert.Same(state.Context.Features.Cover, state.Context.Features.Hatch.CoverBox);
        Assert.Contains(state.Context.Features.Cover, state.Managed);
        Assert.True(state.Context.Features.Hatch.IsCovered);
    }

    [Fact]
    public void ASecondExplicitRound_ReplacesOnlyResidualOwnedLootAndPreviousRoundCreatures()
    {
        var state = CreateState();
        RunToCompletion(ExtractionDungeonReset.CreateSession(state.Context));
        var previousCreatures = state.Creatures.ToArray();
        var previousResidual = state.Context.Chests[0].Items[0];
        var takenLoot = state.Context.Chests[1].Items[0];
        var bag = Track(new Backpack());
        bag.DropItem(takenLoot);

        RunToCompletion(ExtractionDungeonReset.CreateSession(state.Context));

        Assert.All(previousCreatures, creature => Assert.True(creature.Deleted));
        Assert.True(previousResidual.Deleted);
        Assert.False(takenLoot.Deleted);
        Assert.Same(bag, takenLoot.Parent);
        Assert.DoesNotContain(takenLoot, state.Loot);
        Assert.Equal(111, state.Creatures.Count);
        Assert.All(state.Context.Chests, chest => Assert.NotEmpty(chest.Items));
    }

    [Fact]
    public void UnrelatedOccupant_FailsTheEntirePreflightBeforeLootDestructionOrNewMonsters()
    {
        var state = CreateState();
        var marker = Track(new Gold(71));
        state.Context.Chests[0].DropItem(marker);
        state.Loot.Add(marker);
        var occupant = Track(new Mobile { Player = true, Body = 0x190 });
        occupant.MoveToWorld(state.Context.Spawns[^1].Position, Map.Felucca);
        var session = ExtractionDungeonReset.CreateSession(state.Context);

        Assert.Throws<InvalidOperationException>(() => RunToCompletion(session));

        Assert.False(marker.Deleted);
        Assert.Same(state.Context.Chests[0], marker.Parent);
        Assert.Empty(state.Creatures);
        Assert.False(state.Context.Features.Lich.Deleted);
        Assert.Empty(state.Context.RecoveryStorage.Items);
    }

    [Fact]
    public void NonblockingPlayerCorpseAndDroppedLoot_ArePreservedWithoutPreventingTheNextRound()
    {
        var state = CreateState();
        var owner = Track(new Mobile { Player = true, Body = 0x190 });
        var corpse = Track(new Corpse(owner, []));
        corpse.MoveToWorld(state.Context.Spawns[0].Position, Map.Felucca);
        var droppedLoot = Track(new Gold(771));
        droppedLoot.MoveToWorld(state.Context.Spawns[1].Position, Map.Felucca);
        var corpseItem = Track(new Gold(55));
        corpse.DropItem(corpseItem);

        RunToCompletion(ExtractionDungeonReset.CreateSession(state.Context));

        Assert.False(corpse.Deleted);
        Assert.False(corpseItem.Deleted);
        Assert.Same(corpse, corpseItem.Parent);
        Assert.False(droppedLoot.Deleted);
        Assert.Null(droppedLoot.Parent);
        Assert.Equal(state.Context.Spawns[1].Position, droppedLoot.Location);
    }

    [Fact]
    public void CollisionBlockingUnrelatedItem_FailsPreflightWithoutDeletion()
    {
        var state = CreateState();
        var blocker = Track(new WoodenBox());
        blocker.MoveToWorld(state.Context.Spawns[0].Position, Map.Felucca);
        var session = ExtractionDungeonReset.CreateSession(state.Context);

        Assert.Throws<InvalidOperationException>(() => RunToCompletion(session));
        Assert.False(blocker.Deleted);
        Assert.Empty(state.Creatures);
        Assert.False(state.Context.Features.Lich.Deleted);
    }

    [Fact]
    public void Process_ClampsTheBudgetToEightAndZeroDoesNothing()
    {
        var state = CreateState();
        var session = ExtractionDungeonReset.CreateSession(state.Context);
        var cursor = session.GetType().GetField("_cursor", BindingFlags.NonPublic | BindingFlags.Instance);
        session.Process(0);
        Assert.Equal(0, cursor.GetValue(session));
        session.Process(int.MaxValue);
        Assert.Equal(8, cursor.GetValue(session));
        Assert.Empty(state.Creatures);
        Assert.Empty(state.Context.RecoveryStorage.Items);
    }

    [Theory]
    [InlineData("WrongIdentity")]
    [InlineData("WrongBlueprint")]
    [InlineData("WrongMap")]
    [InlineData("MissingDoor")]
    [InlineData("DuplicateChest")]
    [InlineData("PublicRecovery")]
    public void UnsafeContext_IsRejectedWithoutMutatingOwnedLoot(string defect)
    {
        var state = CreateState();
        var context = state.Context;
        var marker = Track(new Gold(31));
        context.Chests[0].DropItem(marker);
        switch (defect)
        {
            case "WrongIdentity": context = context with { Manifest = context.Manifest with { InstallationId = "other" } }; break;
            case "WrongBlueprint": context = context with { Manifest = context.Manifest with { BlueprintSha256 = new string('a', 64) } }; break;
            case "WrongMap": context = context with { Map = Map.Trammel }; break;
            case "MissingDoor": context.Doors[0] = null; break;
            case "DuplicateChest": context.Chests[1] = context.Chests[0]; break;
            case "PublicRecovery": context.RecoveryStorage.Visible = true; break;
        }

        Assert.Throws<InvalidOperationException>(() => ExtractionDungeonReset.CreateSession(context));
        Assert.False(marker.Deleted);
    }

    private ResetState CreateState()
    {
        Assert.True(TestServerInitializer.TileDataLoaded, "Native reset tests require the configured shard assets.");
        var manifest = JsonSerializer.Deserialize<ModularDungeonDeployment.Manifest>(File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Data", "ModularDungeonV17", "install-manifest.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var doors = new EventDungeonDoor[71];
        for (var i = 0; i < doors.Length; i++)
        {
            var definition = manifest.Doors[i];
            doors[i] = Track(new EventDungeonDoor(Enum.Parse<DoorFacing>(definition.Facing)));
            doors[i].MoveToWorld(definition.Closed.Point, Map.Felucca);
        }

        var chests = new EventDungeonChest[100];
        for (var i = 0; i < chests.Length; i++)
        {
            var definition = manifest.Chests[i];
            chests[i] = Track(definition.Type == "RootboundCellarChest" ? new RootboundCellarChest(definition.ItemId) : new EventDungeonChest(definition.ItemId));
            chests[i].MoveToWorld(definition.Position.Point, Map.Felucca);
        }

        var definitionCellar = manifest.HiddenCellar;
        var reward = Array.Find(chests, chest => chest is RootboundCellarChest);
        var features = RootboundCellarFeature.Create(Map.Felucca, definitionCellar.Hatch.Point, definitionCellar.Entry.Point,
            definitionCellar.ReturnRune.Point, definitionCellar.SurfaceReturn.Point, definitionCellar.CreaturePosition.Point, reward);
        Track(features.Cover);
        Track(features.Hatch);
        Track(features.ReturnRune);
        Track(features.Lich);
        var storage = Track(new Backpack { Visible = false, Movable = false });
        storage.Internalize();
        var creatures = new List<BaseCreature>();
        var loot = new List<Item>();
        var managed = new List<Item>();
        var spawns = new List<ExtractionDungeonReset.MonsterSpawn>();
        var starts = new Point3D[25];
        foreach (var room in ExtractionDungeonLayout.Rooms)
        {
            starts[room.ModuleId] = room.PlayerStart;
            foreach (var spawn in room.Creatures)
            {
                spawns.Add(new($"M{room.ModuleId:D2}", spawn.Kind.ToString(), spawn.Position, spawn.Position, spawn.RangeHome));
            }
        }

        var context = new ExtractionDungeonReset.Context(manifest, Map.Felucca, doors, chests, features, creatures, loot,
            storage, spawns.ToArray(), starts,
            creature => { Track(creature); creatures.Add(creature); }, creature => creatures.Remove(creature),
            item => loot.Add(item), item => loot.Remove(item), item => { Track(item); managed.Add(item); });
        ExtractionDungeonReset.ManagedChestLookup = chest => Array.IndexOf(chests, chest) >= 0;
        return new(context, creatures, loot, managed);
    }

    private sealed record ResetState(ExtractionDungeonReset.Context Context, List<BaseCreature> Creatures, List<Item> Loot, List<Item> Managed);

    private static void RunToCompletion(IExtractionEventResetSession session)
    {
        for (var i = 0; i < 2000 && !session.Complete; i++)
        {
            session.Process(8);
        }

        Assert.True(session.Complete);
    }

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }

    private Mobile Actor(Point3D point)
    {
        var state = PacketTestUtilities.CreateTestNetState();
        _states.Add(state);
        state.Account = new MockAccount();
        var player = Track(new Mobile { Player = true, Body = 0x190 });
        player.NetState = state;
        state.Mobile = player;
        player.MoveToWorld(point, Map.Felucca);
        return player;
    }

    private static TimerExecutionToken OpeningTimer(Item item) => (TimerExecutionToken)item.GetType().GetField(
        "_openingTimer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(item);

    private static Mobile OpeningMobile(Item item) => (Mobile)item.GetType().GetField(
        "_openingMobile", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(item);

    private static byte[] Snapshot(ISerializable entity)
    {
        var writer = new BufferWriter(true);
        entity.Serialize(writer);
        return writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();
    }

    private static void Advance(long milliseconds)
    {
        var deadline = Core.TickCount + milliseconds;
        while (Core.TickCount - deadline < 0)
        {
            Core._tickCount += Timer.TickRate;
            Core._now += TimeSpan.FromMilliseconds(Timer.TickRate);
            Timer.Slice(Core.TickCount);
        }
    }
}
