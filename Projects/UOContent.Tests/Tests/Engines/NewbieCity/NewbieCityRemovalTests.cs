using System;
using System.Collections.Generic;
using System.IO;
using Server;
using Server.Engines.Labrynth;
using Server.Engines.Spawners;
using Server.Items;
using Server.Mobiles;
using Server.Tests;
using Xunit;

namespace UOContent.Tests.Engines.NewbieCity;

[Collection("Sequential UOContent Tests")]
public class NewbieCityRemovalTests : IDisposable
{
    private const uint FirstSerial = 1073772319;
    private readonly List<IEntity> _created = [];

    public void Dispose()
    {
        for (var index = _created.Count - 1; index >= 0; index--)
        {
            _created[index].Delete();
        }
    }

    [SkippableFact]
    public void RetainedReceipts_ReconcileOriginalCreationWithCurrentRoutesAndSheepPen()
    {
        var data = Environment.GetEnvironmentVariable("MODERNUO_NEWBIE_CITY_DATA") ??
            Path.GetFullPath("../Distribution/Live/Data");
        Skip.If(!File.Exists(Path.Combine(data, "NewbieCity", "before.json")), "Requires retained Newbie City receipts.");
        NewbieCityRemoval.ValidateReceipts(data);
    }

    [SkippableFact]
    public void Apply_ArchivesBeforeRemovingOnlyNineteenStockObjectsAndTheirTwentyTwoOwnedMobiles()
    {
        TileDataRequirement.SkipIfMissing();
        var installed = SeedCity();
        var player = Track(new Mobile { Player = true, Body = 0x190 });
        player.MoveToWorld(new Point3D(5592, 1352, 0), Map.Felucca);
        var bank = Track(player.BankBox);
        var bankGold = Track(new Gold(1234));
        bank.DropItem(bankGold);
        var worldItem = Track(new Item(0xEED));
        worldItem.MoveToWorld(new Point3D(5593, 1352, 0), Map.Felucca);
        var before = CaptureState();
        NewbieCityRemoval.Archive archive = null;

        var result = NewbieCityRemoval.Apply(archive: snapshot =>
        {
            AssertStateUnchanged(before);
            archive = snapshot;
        });

        Assert.NotNull(archive);
        Assert.Equal(13, result.DeletedSpawners);
        Assert.Equal(4, result.DeletedTeleporters);
        Assert.Equal(2, result.DeletedDoors);
        Assert.Equal(22, result.DeletedMobiles);
        Assert.True(result.NoObjectsRemaining);
        Assert.False(result.AlreadyRemoved);
        foreach (var spawner in installed.Spawners)
        {
            Assert.True(spawner.Deleted);
            Assert.False(spawner.Running);
            Assert.Empty(spawner.Spawned);
            Assert.Empty(Assert.Single(spawner.Entries).Spawned);
        }
        foreach (var pad in installed.Teleporters)
        {
            Assert.True(pad.Deleted);
        }
        foreach (var gate in installed.Gates)
        {
            Assert.True(gate.Deleted);
        }
        foreach (var mobile in installed.Mobiles)
        {
            Assert.True(mobile.Deleted);
            Assert.Null(World.FindEntity(mobile.Serial));
        }
        AssertStockSerialsAbsent();
        Assert.False(player.Deleted);
        Assert.Equal(new Point3D(5592, 1352, 0), player.Location);
        Assert.Same(Map.Felucca, player.Map);
        Assert.False(bank.Deleted);
        Assert.False(bankGold.Deleted);
        Assert.Same(player, bank.Parent);
        Assert.Same(bank, bankGold.Parent);
        Assert.Equal(1234, bankGold.Amount);
        Assert.False(worldItem.Deleted);
        Assert.Equal(new Point3D(5593, 1352, 0), worldItem.Location);

        var repeated = NewbieCityRemoval.Apply(archive);

        Assert.Equal(0, repeated.DeletedSpawners);
        Assert.Equal(0, repeated.DeletedTeleporters);
        Assert.Equal(0, repeated.DeletedDoors);
        Assert.Equal(0, repeated.DeletedMobiles);
        Assert.True(repeated.NoObjectsRemaining);
        Assert.True(repeated.AlreadyRemoved);
        Assert.False(player.Deleted);
        Assert.False(bankGold.Deleted);
        Assert.Equal(1234, bankGold.Amount);
        Assert.False(worldItem.Deleted);
    }

    [SkippableTheory]
    [InlineData("Route")]
    [InlineData("Spawner")]
    public void LateRouteOrSpawnerMismatch_RefusesBeforeArchivingOrDeletingAnything(string mismatch)
    {
        TileDataRequirement.SkipIfMissing();
        var installed = SeedCity();
        if (mismatch == "Route")
        {
            var pad = installed.Teleporters[^1];
            pad.PointDest = new Point3D(pad.PointDest.X + 1, pad.PointDest.Y, pad.PointDest.Z);
        }
        else
        {
            installed.Spawners[^1].Count++;
        }
        var before = CaptureState();
        var archiveCalled = false;

        Assert.Throws<InvalidOperationException>(() =>
            NewbieCityRemoval.Apply(archive: _ => archiveCalled = true));

        Assert.False(archiveCalled);
        AssertStateUnchanged(before);
        AssertPopulationIntact(installed);
    }

    [SkippableTheory]
    [InlineData("Controlled")]
    [InlineData("Summoned")]
    [InlineData("Player")]
    public void ForeignPetSummonOrPlayerInSpawnOwnership_RefusesBeforeDeletingAnyCityObject(string kind)
    {
        TileDataRequirement.SkipIfMissing();
        var installed = SeedCity();
        var spawner = installed.Spawners[^1];
        var entry = Assert.Single(spawner.Entries);
        var original = installed.Mobiles[^1];
        Assert.True(spawner.Spawned.Remove(original));
        entry.RemoveFromSpawned(original);
        original.Spawner = null;
        original.Delete();
        Mobile foreign = kind == "Player"
            ? Track(new Mobile { Player = true, Body = 0x190 })
            : Track(new Sheep { Controlled = kind == "Controlled", Summoned = kind == "Summoned" });
        foreign.Spawner = spawner;
        foreign.MoveToWorld(new Point3D(5599, 1363, 0), Map.Felucca);
        spawner.Spawned.Add(foreign, entry);
        entry.AddToSpawned(foreign);
        var before = CaptureState();
        var archiveCalled = false;

        Assert.Throws<InvalidOperationException>(() =>
            NewbieCityRemoval.Apply(archive: _ => archiveCalled = true));

        Assert.False(archiveCalled);
        AssertStateUnchanged(before);
        foreach (var owner in installed.Spawners)
        {
            Assert.False(owner.Deleted);
            Assert.True(owner.Running);
        }
        Assert.False(foreign.Deleted);
        Assert.Same(spawner, foreign.Spawner);
        Assert.Same(entry, spawner.Spawned[foreign]);
        Assert.Contains(foreign, entry.Spawned);
        Assert.Equal(10, spawner.Spawned.Count);
        Assert.Equal(10, entry.Spawned.Count);
    }

    [SkippableFact]
    public void ArchiveFailure_RefusesBeforeStoppingSpawnerOrDeletingAnyOwnedEntity()
    {
        TileDataRequirement.SkipIfMissing();
        var installed = SeedCity();
        var before = CaptureState();

        Assert.Throws<InvalidOperationException>(() =>
            NewbieCityRemoval.Apply(archive: _ => throw new InvalidOperationException("Archive unavailable.")));

        AssertStateUnchanged(before);
        AssertPopulationIntact(installed);
    }

    private Installation SeedCity()
    {
        var pins = NewbieCityRemoval.Pins;
        Assert.Equal(13, pins.Length);
        var spawners = new Spawner[pins.Length];
        var mobiles = new List<BaseCreature>();
        for (var index = 0; index < pins.Length; index++)
        {
            var pin = pins[index];
            var spawner = Track(new Spawner((Serial)(FirstSerial + (uint)index))
            {
                ItemID = 0x1F13,
                Name = "Newbie City - " + pin.Id,
                Guid = NewbieCityRemoval.SpawnGuid(pin.Id),
                WalkingRange = pin.HomeRange
            });
            spawner.InitSpawn(pin.Count, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), spawnBounds: pin.Bounds);
            var properties = pin.Type == "Sheep"
                ? $"Direction {pin.Facing}"
                : $"Blessed true Direction {pin.Facing}";
            var entry = spawner.AddEntry(pin.Type, 100, pin.Count, false, properties);
            spawner.MoveToWorld(pin.Position, Map.Felucca);
            spawners[index] = spawner;
            for (var count = 0; count < pin.Count; count++)
            {
                var creature = Track(CreateCreature(pin.Type));
                creature.Spawner = spawner;
                creature.Home = pin.Position;
                creature.HomeMap = Map.Felucca;
                creature.RangeHome = spawner.WalkingRange;
                creature.Blessed = pin.Type != "Sheep";
                creature.Direction = pin.Facing;
                var location = pin.Type == "Sheep"
                    ? new Point3D(5596 + count % 5, 1361 + count / 5, 0)
                    : pin.Position;
                creature.MoveToWorld(location, Map.Felucca);
                spawner.Spawned.Add(creature, entry);
                entry.AddToSpawned(creature);
                mobiles.Add(creature);
            }
        }
        Assert.Equal(22, mobiles.Count);
        var pads = new Teleporter[4];
        for (var index = 0; index < pads.Length; index++)
        {
            var route = NewbieCityMarketTravel.Routes[index];
            var pad = Track(new Teleporter((Serial)(FirstSerial + 13 + (uint)index))
            {
                ItemID = 0x1BC3,
                Flags = TeleporterFlags.Active,
                Name = "Newbie City - " + route.Id,
                PointDest = route.Destination,
                MapDest = Map.Felucca,
                Visible = false,
                Movable = false
            });
            pad.MoveToWorld(route.Source, Map.Felucca);
            pads[index] = pad;
        }
        DarkWoodGate[] gates =
        [
            CreateGate(FirstSerial + 17, "sheep-gate-north", new Point3D(5595, 1363, 0), DoorFacing.SouthCW),
            CreateGate(FirstSerial + 18, "sheep-gate-south", new Point3D(5595, 1364, 0), DoorFacing.NorthCCW)
        ];
        gates[0].Link = gates[1];
        gates[1].Link = gates[0];
        return new Installation(spawners, pads, gates, mobiles.ToArray());
    }

    private DarkWoodGate CreateGate(uint serial, string id, Point3D location, DoorFacing facing)
    {
        var closedId = 0x866 + 2 * (int)facing;
        var gate = Track(new DarkWoodGate((Serial)serial)
        {
            Name = "Newbie City - " + id,
            ClosedId = closedId,
            OpenedId = closedId + 1,
            ClosedSound = 0xF2,
            OpenedSound = 0xEB,
            Offset = BaseDoor.GetOffset(facing),
            ItemID = closedId,
            Visible = true,
            Movable = false
        });
        gate.MoveToWorld(location, Map.Felucca);
        return gate;
    }

    private static BaseCreature CreateCreature(string type) => type switch
    {
        "Banker" => new Banker(),
        "Blacksmith" => new Blacksmith(),
        "Tailor" => new Tailor(),
        "Carpenter" => new Carpenter(),
        "Bowyer" => new Bowyer(),
        "Tinker" => new Tinker(),
        "Alchemist" => new Alchemist(),
        "Mage" => new Mage(),
        "Provisioner" => new Provisioner(),
        "Farmer" => new Farmer(),
        "AnimalTrainer" => new AnimalTrainer(),
        "Healer" => new Healer(),
        "Sheep" => new Sheep(),
        _ => throw new InvalidOperationException($"Unexpected test spawn type {type}.")
    };

    private static void AssertPopulationIntact(Installation installed)
    {
        var count = 0;
        foreach (var spawner in installed.Spawners)
        {
            Assert.False(spawner.Deleted);
            Assert.True(spawner.Running);
            var entry = Assert.Single(spawner.Entries);
            Assert.Equal(spawner.Spawned.Count, entry.Spawned.Count);
            foreach (var pair in spawner.Spawned)
            {
                Assert.False(pair.Key.Deleted);
                Assert.Same(spawner, pair.Key.Spawner);
                Assert.Same(entry, pair.Value);
                Assert.Contains(pair.Key, entry.Spawned);
                count++;
            }
        }
        Assert.Equal(22, count);
    }

    private static void AssertStockSerialsAbsent()
    {
        for (uint offset = 0; offset < 19; offset++)
        {
            Assert.Null(World.FindEntity((Serial)(FirstSerial + offset)));
        }
    }

    private EntityState[] CaptureState()
    {
        var states = new EntityState[_created.Count];
        for (var index = 0; index < states.Length; index++)
        {
            var entity = _created[index];
            states[index] = new EntityState(entity, entity.Serial, entity.Location, entity.Map, entity.Deleted);
        }
        return states;
    }

    private static void AssertStateUnchanged(EntityState[] before)
    {
        foreach (var state in before)
        {
            Assert.Equal(state.Deleted, state.Entity.Deleted);
            Assert.Equal(state.Serial, state.Entity.Serial);
            Assert.Equal(state.Location, state.Entity.Location);
            Assert.Same(state.Map, state.Entity.Map);
        }
    }

    private T Track<T>(T entity) where T : IEntity
    {
        if (!ReferenceEquals(World.FindEntity(entity.Serial), entity))
        {
            World.AddEntity(entity);
        }
        _created.Add(entity);
        return entity;
    }

    private sealed record Installation(Spawner[] Spawners, Teleporter[] Teleporters, DarkWoodGate[] Gates, BaseCreature[] Mobiles);
    private sealed record EntityState(IEntity Entity, Serial Serial, Point3D Location, Map Map, bool Deleted);
}
