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
public class NewbieCityRefinementTests : IDisposable
{
    private readonly List<IEntity> _created = [];

    public void Dispose()
    {
        for (var index = _created.Count - 1; index >= 0; index--)
        {
            _created[index].Delete();
        }
    }

    [SkippableFact]
    public void Apply_TranslatesLiveSheepHomesBoundsAndOriginalObjectsWithoutReplacingAnySerial()
    {
        TileDataRequirement.SkipIfMissing();
        var (spawner, gates, sheep) = SeedOldPen();
        var before = CaptureState();
        Assert.Equal(new Point3D(5603, 1363, 0), NewbieCityRefinement.OldSpawnerPosition);
        Assert.Equal(new Point3D(5599, 1363, 0), NewbieCityRefinement.SpawnerPosition);
        Assert.Equal(new Rectangle3D(5600, 1361, -128, 7, 6, 256), NewbieCityRefinement.OldSpawnBounds);
        Assert.Equal(new Rectangle3D(5596, 1361, -128, 7, 6, 256), NewbieCityRefinement.SpawnBounds);

        Assert.Equal(13, NewbieCityRefinement.Apply(spawner, gates, false));
        Assert.Equal(0, NewbieCityRefinement.Apply(spawner, gates, false));

        foreach (var state in before)
        {
            Assert.False(state.Entity.Deleted);
            Assert.Equal(state.Serial, state.Entity.Serial);
            Assert.Equal(Translate(state.Location), state.Entity.Location);
            Assert.Same(state.Map, state.Entity.Map);
        }
        Assert.Equal(1073772331u, spawner.Serial.Value);
        Assert.Equal(1073772336u, gates[0].Serial.Value);
        Assert.Equal(1073772337u, gates[1].Serial.Value);
        Assert.Equal(NewbieCityRefinement.SpawnBounds, spawner.SpawnBounds);
        AssertSpawnerConfiguration(spawner, sheep);
        foreach (var animal in sheep)
        {
            Assert.Equal(NewbieCityRefinement.SpawnerPosition, animal.Home);
            Assert.Same(Map.Felucca, animal.HomeMap);
            Assert.Equal(3, animal.RangeHome);
            Assert.False(animal.Blessed);
            Assert.False(animal.Controlled);
            Assert.False(animal.Summoned);
        }
        Assert.Equal(13, _created.Count);
    }

    [SkippableFact]
    public void Repeat_AfterSheepWanderLeavesCurrentLocationsAndHomesUntouched()
    {
        TileDataRequirement.SkipIfMissing();
        var (spawner, gates, sheep) = SeedOldPen();
        NewbieCityRefinement.Apply(spawner, gates, false);
        sheep[0].MoveToWorld(new Point3D(5602, 1366, 0), Map.Felucca);
        var before = CaptureState();

        Assert.Equal(0, NewbieCityRefinement.Apply(spawner, gates, false));

        AssertStateUnchanged(before);
        AssertSpawnerConfiguration(spawner, sheep);
        Assert.Equal(new Point3D(5602, 1366, 0), sheep[0].Location);
        Assert.Equal(NewbieCityRefinement.SpawnerPosition, sheep[0].Home);
        Assert.Equal(NewbieCityRefinement.SpawnBounds, spawner.SpawnBounds);
    }

    [SkippableFact]
    public void Apply_PreservesEachGatesOpenStateItemIdOffsetAndReciprocalLink()
    {
        TileDataRequirement.SkipIfMissing();
        var (spawner, gates, _) = SeedOldPen();
        gates[0].Open = true;
        var openedLocation = gates[0].Location;
        var openedId = gates[0].ItemID;
        var closedId = gates[1].ItemID;
        var northOffset = gates[0].Offset;
        var southOffset = gates[1].Offset;

        Assert.Equal(13, NewbieCityRefinement.Apply(spawner, gates, false));

        Assert.True(gates[0].Open);
        Assert.False(gates[1].Open);
        Assert.Equal(openedId, gates[0].ItemID);
        Assert.Equal(closedId, gates[1].ItemID);
        Assert.Equal(northOffset, gates[0].Offset);
        Assert.Equal(southOffset, gates[1].Offset);
        Assert.Same(gates[1], gates[0].Link);
        Assert.Same(gates[0], gates[1].Link);
        Assert.Equal(Translate(openedLocation), gates[0].Location);
        Assert.Equal(new Point3D(5595 + northOffset.X, 1363 + northOffset.Y, northOffset.Z), gates[0].Location);
        Assert.Equal(new Point3D(5595, 1364, 0), gates[1].Location);
        Assert.Equal(0, NewbieCityRefinement.Apply(spawner, gates, false));
        Assert.True(gates[0].Open);
        Assert.Equal(Translate(openedLocation), gates[0].Location);
    }

    [SkippableFact]
    public void LateGateMismatch_RefusesBeforeMovingSpawnerSheepOrEarlierGate()
    {
        TileDataRequirement.SkipIfMissing();
        var (spawner, gates, sheep) = SeedOldPen();
        gates[^1].ItemID++;
        var before = CaptureState();

        Assert.Throws<InvalidOperationException>(() => NewbieCityRefinement.Apply(spawner, gates, false));

        AssertStateUnchanged(before);
        AssertOldPenUnchanged(spawner, gates, sheep);
    }

    [SkippableFact]
    public void ForeignSheepInDestinationPen_RefusesBeforeMovingAnyOwnedObject()
    {
        TileDataRequirement.SkipIfMissing();
        var (spawner, gates, sheep) = SeedOldPen();
        var foreign = Track(new Sheep());
        foreign.MoveToWorld(new Point3D(5596, 1361, 0), Map.Felucca);
        var before = CaptureState();

        Assert.Throws<InvalidOperationException>(() => NewbieCityRefinement.Apply(spawner, gates, false));

        AssertStateUnchanged(before);
        AssertOldPenUnchanged(spawner, gates, sheep);
        Assert.Null(foreign.Spawner);
        Assert.False(spawner.Spawned.ContainsKey(foreign));
    }

    [SkippableFact]
    public void NativeRefinedCity_HumanNpcFrontsMarketRoutesAndPenHaveStandingClearance()
    {
        TileDataRequirement.SkipIfMissing();
        var manifest = Environment.GetEnvironmentVariable("MODERNUO_NEWBIE_CITY_LAYOUT") ??
            Path.GetFullPath("../Distribution/Live/Data/NewbieCity/layout.json");
        Skip.If(!File.Exists(manifest), "Requires the retained Newbie City NPC layout manifest.");
        var layout = NewbieCityDeployment.LoadLayout(manifest);
        var humans = 0;
        foreach (var pin in layout.Spawners)
        {
            if (pin.Type == "Sheep")
            {
                continue;
            }
            AssertFits(pin.Position);
            AssertFits(new Point3D(pin.Position.X, pin.Position.Y + 1, pin.Position.Z));
            humans++;
        }
        Assert.Equal(12, humans);
        foreach (var route in NewbieCityMarketTravel.Routes)
        {
            AssertFits(route.Source);
            AssertFits(route.Destination);
        }
        var cells = 0;
        for (var x = 5596; x < 5603; x++)
        {
            for (var y = 1361; y < 1367; y++)
            {
                AssertFits(new Point3D(x, y, 0));
                cells++;
            }
        }
        Assert.Equal(42, cells);
        AssertFits(new Point3D(5595, 1363, 0));
        AssertFits(new Point3D(5595, 1364, 0));
        AssertFits(NewbieCityRefinement.SpawnerPosition);
        var (spawner, gates, sheep) = SeedOldPen();

        Assert.Equal(13, NewbieCityRefinement.Apply(spawner, gates));

        foreach (var animal in sheep)
        {
            AssertFits(animal.Location);
            Assert.True(NewbieCityRefinement.SpawnBounds.Contains(animal.Location));
        }
        Assert.Equal(0, NewbieCityRefinement.Apply(spawner, gates));
    }

    [SkippableFact]
    public void NativeRiver_MainBridgeWalksAtGroundZeroAndStoneFountainHasAllSixteenComponents()
    {
        TileDataRequirement.SkipIfMissing();
        var player = Track(new Mobile { Player = true, Body = 0x190, Direction = Direction.East });
        player.MoveToWorld(new Point3D(5596, 1352, 0), Map.Felucca);
        for (var x = 5597; x <= 5602; x++)
        {
            Assert.True(player.Move(Direction.East));
            Assert.Equal(new Point3D(x, 1352, 0), player.Location);
            Assert.Same(Map.Felucca, player.Map);
        }
        var components = new HashSet<int>();
        var count = 0;
        for (var x = 5588; x <= 5591; x++)
        {
            for (var y = 1354; y <= 1357; y++)
            {
                foreach (var tile in Map.Felucca.Tiles.GetStaticTiles(x, y))
                {
                    if (tile.ID is >= 0x1731 and <= 0x173F or 0x1741)
                    {
                        Assert.Equal(0, tile.Z);
                        Assert.Equal(0, tile.Hue);
                        Assert.True(components.Add(tile.ID), $"Duplicate fountain component {tile.ID:X}.");
                        count++;
                    }
                }
            }
        }
        Assert.Equal(16, count);
        Assert.Equal(16, components.Count);
    }

    private (Spawner Spawner, BaseDoor[] Gates, Sheep[] Sheep) SeedOldPen()
    {
        var spawner = Track(new Spawner((Serial)1073772331)
        {
            ItemID = 0x1F13,
            Name = "Newbie City - sheep",
            Guid = new Guid("663b0f3e-8f4e-59cb-d882-cd8baf9fcd7e"),
            WalkingRange = 3
        });
        spawner.InitSpawn(10, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), spawnBounds: NewbieCityRefinement.OldSpawnBounds);
        var entry = spawner.AddEntry("Sheep", 100, 10, false, "Direction South");
        spawner.MoveToWorld(NewbieCityRefinement.OldSpawnerPosition, Map.Felucca);
        var sheep = new Sheep[10];
        for (var index = 0; index < sheep.Length; index++)
        {
            var animal = Track(new Sheep
            {
                Home = NewbieCityRefinement.OldSpawnerPosition,
                HomeMap = Map.Felucca,
                RangeHome = 3,
                Direction = Direction.South,
                Spawner = spawner
            });
            animal.MoveToWorld(new Point3D(5600 + index % 5, 1361 + index / 5, 0), Map.Felucca);
            spawner.Spawned.Add(animal, entry);
            entry.AddToSpawned(animal);
            sheep[index] = animal;
        }
        BaseDoor[] gates =
        [
            CreateGate(1073772336, "sheep-gate-north", new Point3D(5599, 1363, 0), DoorFacing.SouthCW),
            CreateGate(1073772337, "sheep-gate-south", new Point3D(5599, 1364, 0), DoorFacing.NorthCCW)
        ];
        gates[0].Link = gates[1];
        gates[1].Link = gates[0];
        return (spawner, gates, sheep);
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

    private static void AssertSpawnerConfiguration(Spawner spawner, Sheep[] sheep)
    {
        Assert.Equal("Newbie City - sheep", spawner.Name);
        Assert.Equal(new Guid("663b0f3e-8f4e-59cb-d882-cd8baf9fcd7e"), spawner.Guid);
        Assert.Equal(10, spawner.Count);
        Assert.Equal(TimeSpan.FromMinutes(2), spawner.MinDelay);
        Assert.Equal(TimeSpan.FromMinutes(5), spawner.MaxDelay);
        Assert.Equal(3, spawner.WalkingRange);
        Assert.True(spawner.Running);
        var entry = Assert.Single(spawner.Entries);
        Assert.Equal("Sheep", entry.SpawnedName);
        Assert.Equal(100, entry.SpawnedProbability);
        Assert.Equal(10, entry.SpawnedMaxCount);
        Assert.Equal("Direction South", entry.Properties);
        Assert.True(entry.Enabled);
        Assert.Equal(10, spawner.Spawned.Count);
        Assert.Equal(10, entry.Spawned.Count);
        foreach (var animal in sheep)
        {
            Assert.Same(spawner, animal.Spawner);
            Assert.Same(entry, spawner.Spawned[animal]);
            Assert.Contains(animal, entry.Spawned);
        }
    }

    private static void AssertOldPenUnchanged(Spawner spawner, BaseDoor[] gates, Sheep[] sheep)
    {
        Assert.Equal(NewbieCityRefinement.OldSpawnerPosition, spawner.Location);
        Assert.Equal(NewbieCityRefinement.OldSpawnBounds, spawner.SpawnBounds);
        Assert.Equal(new Point3D(5599, 1363, 0), gates[0].Location);
        Assert.Equal(new Point3D(5599, 1364, 0), gates[1].Location);
        foreach (var animal in sheep)
        {
            Assert.Equal(NewbieCityRefinement.OldSpawnerPosition, animal.Home);
            Assert.Same(Map.Felucca, animal.HomeMap);
        }
        AssertSpawnerConfiguration(spawner, sheep);
    }

    private EntityState[] CaptureState()
    {
        var state = new EntityState[_created.Count];
        for (var index = 0; index < state.Length; index++)
        {
            var entity = _created[index];
            state[index] = new EntityState(entity, entity.Serial, entity.Location, entity.Map);
        }
        return state;
    }

    private static void AssertStateUnchanged(EntityState[] before)
    {
        foreach (var state in before)
        {
            Assert.False(state.Entity.Deleted);
            Assert.Equal(state.Serial, state.Entity.Serial);
            Assert.Equal(state.Location, state.Entity.Location);
            Assert.Same(state.Map, state.Entity.Map);
        }
    }

    private static Point3D Translate(Point3D point) => new(point.X - 4, point.Y, point.Z);

    private static void AssertFits(Point3D point) =>
        Assert.True(Map.Felucca.CanFit(point, 16, true, false), $"Native refined sheep-pen clearance at {point}.");

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }

    private sealed record EntityState(IEntity Entity, Serial Serial, Point3D Location, Map Map);
}
