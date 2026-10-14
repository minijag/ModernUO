using System;
using System.Collections.Generic;
using System.IO;
using Server;
using Server.Engines.Spawners;
using Server.Items;
using Server.Mobiles;
using Server.Tests;
using Xunit;
using Deployment = Server.Engines.Labrynth.NewbieCityV2Deployment;

namespace UOContent.Tests.Engines.NewbieCity;

[Collection("Sequential UOContent Tests")]
public class NewbieCityV2DeploymentTests : IDisposable
{
    private readonly List<IEntity> _created = [];

    public void Dispose()
    {
        for (var index = _created.Count - 1; index >= 0; index--)
        {
            _created[index].Delete();
        }
    }

    [Fact]
    public void ApprovedLayout_HasTwelveHumanHomesTwoSixSheepPensAndFourReciprocalPadsAndGates()
    {
        var layout = LoadLayout();
        Assert.Equal("newbie-city-v2", layout.InstallationId);
        Assert.Equal("newbie-city-v2", layout.NativeStageId);
        Assert.Equal(3, layout.NativeFiles.Length);
        Assert.Equal(14, layout.Spawners.Length);
        Assert.Equal(4, layout.Routes.Length);
        Assert.Equal(4, layout.Doors.Length);
        var humans = new HashSet<string>();
        var sheep = new List<Deployment.SpawnPin>();
        foreach (var pin in layout.Spawners)
        {
            Assert.True(layout.CityBounds.Contains(pin.Position));
            Assert.True(pin.Bounds.Contains(pin.Position));
            Assert.Equal(0, pin.Position.Z);
            if (pin.Type == "Sheep")
            {
                Assert.Equal(6, pin.Count);
                sheep.Add(pin);
            }
            else
            {
                Assert.Equal(1, pin.Count);
                Assert.True(humans.Add(pin.Type));
            }
        }
        Assert.Equal(12, humans.Count);
        Assert.Contains("Banker", humans);
        Assert.Contains("Healer", humans);
        Assert.Equal(2, sheep.Count);
        Assert.NotEqual(sheep[0].Bounds, sheep[1].Bounds);
        for (var index = 0; index < layout.Routes.Length; index++)
        {
            var route = layout.Routes[index];
            var reciprocal = layout.Routes[index ^ 1];
            Assert.Equal(route.Source, reciprocal.Destination);
            Assert.Equal(route.Destination, reciprocal.Source);
            Assert.Equal(0, route.Source.Z);
            Assert.Equal(0, route.Destination.Z);
        }
        for (var index = 0; index < layout.Doors.Length; index += 2)
        {
            var north = layout.Doors[index];
            var south = layout.Doors[index + 1];
            Assert.Equal("DarkWoodGate", north.Type);
            Assert.Equal("DarkWoodGate", south.Type);
            Assert.Equal(new Point3D(north.Position.X, north.Position.Y + 1, 0), south.Position);
        }
    }

    [Fact]
    public void Ensure_CreatesTwentyTwoStockObjectsAndTwentyFourBoundedMobiles_RepeatPreservesAllIdentities()
    {
        var layout = LoadLayout();
        var first = EnsureAndTrack(layout);
        Assert.Equal(22, first.Created);
        AssertInstalled(layout, first);
        var originalMobiles = new HashSet<ISpawnable>();
        foreach (var spawner in first.Spawners)
        {
            foreach (var entity in spawner.Spawned.Keys)
            {
                Assert.True(originalMobiles.Add(entity));
            }
        }
        Assert.Equal(24, originalMobiles.Count);

        var repeated = EnsureAndTrack(layout);

        Assert.Equal(0, repeated.Created);
        AssertInstalled(layout, repeated);
        for (var index = 0; index < first.Spawners.Length; index++)
        {
            Assert.Same(first.Spawners[index], repeated.Spawners[index]);
            foreach (var entity in repeated.Spawners[index].Spawned.Keys)
            {
                Assert.Contains(entity, originalMobiles);
            }
        }
        for (var index = 0; index < first.Teleporters.Length; index++)
        {
            Assert.Same(first.Teleporters[index], repeated.Teleporters[index]);
        }
        for (var index = 0; index < first.Doors.Length; index++)
        {
            Assert.Same(first.Doors[index], repeated.Doors[index]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LateUnexpectedWorldItemOrPet_RefusesBeforeCreatingAnyStockObject(bool pet)
    {
        var layout = LoadLayout();
        var location = new Point3D(5592, 1352, 0);
        IEntity foreign;
        if (pet)
        {
            var animal = Track(new Sheep { Controlled = true });
            animal.MoveToWorld(location, Map.Felucca);
            foreign = animal;
        }
        else
        {
            var item = Track(new Item(0xEED));
            item.MoveToWorld(location, Map.Felucca);
            foreign = item;
        }

        Assert.Throws<InvalidOperationException>(() => Deployment.Ensure(layout, checkSupport: false));

        Assert.False(foreign.Deleted);
        Assert.Equal(location, foreign.Location);
        Assert.Same(Map.Felucca, foreign.Map);
        AssertMissingExcept(layout, null);
    }

    [Fact]
    public void LastSpawnerConfigurationMismatch_RefusesBeforeRecreatingEarlierMissingHomesRoutesOrGates()
    {
        var layout = LoadLayout();
        var installed = EnsureAndTrack(layout);
        var retained = installed.Spawners[^1];
        foreach (var spawner in installed.Spawners)
        {
            if (!ReferenceEquals(spawner, retained))
            {
                spawner.Delete();
            }
        }
        foreach (var pad in installed.Teleporters)
        {
            pad.Delete();
        }
        foreach (var gate in installed.Doors)
        {
            gate.Delete();
        }
        retained.Count++;
        var serial = retained.Serial;
        var count = retained.Count;
        var owned = new HashSet<ISpawnable>(retained.Spawned.Keys);

        Assert.Throws<InvalidOperationException>(() => Deployment.Ensure(layout, checkSupport: false));

        Assert.False(retained.Deleted);
        Assert.Equal(serial, retained.Serial);
        Assert.Equal(count, retained.Count);
        Assert.True(retained.Running);
        Assert.Equal(6, retained.Spawned.Count);
        foreach (var entity in retained.Spawned.Keys)
        {
            Assert.Contains(entity, owned);
            Assert.False(entity.Deleted);
            Assert.Same(retained, entity.Spawner);
        }
        AssertMissingExcept(layout, retained);
    }

    [Fact]
    public void NativeHomesFrontsBankEntrancesGatesPensAndBroadStreets_HaveConnectedGroundZeroClearance()
    {
        var layout = LoadLayout();
        var connected = Flood(layout.CityBounds, new Point3D(5592, 1352, 0));
        foreach (var pin in layout.Spawners)
        {
            AssertConnected(pin.Position, connected);
            if (pin.Type == "Sheep")
            {
                for (var x = pin.Bounds.Start.X; x < pin.Bounds.End.X; x++)
                {
                    for (var y = pin.Bounds.Start.Y; y < pin.Bounds.End.Y; y++)
                    {
                        AssertConnected(new Point3D(x, y, 0), connected);
                    }
                }
            }
            else if (pin.Type != "Banker")
            {
                AssertConnected(Front(pin), connected);
            }
        }
        foreach (var gate in layout.Doors)
        {
            AssertConnected(gate.Position, connected);
            var offset = BaseDoor.GetOffset(gate.Facing);
            AssertConnected(new Point3D(gate.Position.X + offset.X, gate.Position.Y + offset.Y, offset.Z), connected);
        }
        foreach (var route in layout.Routes)
        {
            AssertFits(route.Source);
            AssertFits(route.Destination);
            if (layout.CityBounds.Contains(route.Source))
            {
                AssertConnected(route.Source, connected);
            }
        }
        for (var x = 5591; x <= 5592; x++)
        {
            AssertConnected(new Point3D(x, 1338, 0), connected);
            AssertConnected(new Point3D(x, 1339, 0), connected);
        }
        for (var x = 5574; x <= 5608; x++)
        {
            for (var y = 1350; y <= 1353; y++)
            {
                AssertConnected(new Point3D(x, y, 0), connected);
            }
        }
        for (var x = 5590; x <= 5593; x++)
        {
            for (var y = 1339; y <= 1378; y++)
            {
                AssertConnected(new Point3D(x, y, 0), connected);
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NativeWalking_EntersCityPadFromWestAndReturnsThroughItsExactIslandPartner(int index)
    {
        var layout = LoadLayout();
        EnsureAndTrack(layout);
        var route = layout.Routes[index];
        var player = Track(new Mobile { Player = true, Body = 0x190, Direction = Direction.East });
        var approach = new Point3D(route.Source.X - 1, route.Source.Y, 0);
        AssertFits(approach);
        player.MoveToWorld(approach, Map.Felucca);

        Assert.False(player.Move(Direction.East));

        Assert.Equal(route.Destination, player.Location);
        Assert.Same(Map.Felucca, player.Map);
        player.Direction = Direction.East;
        Assert.True(player.Move(Direction.East));
        Assert.Equal(new Point3D(route.Destination.X + 1, route.Destination.Y, 0), player.Location);
        player.Direction = Direction.West;

        Assert.False(player.Move(Direction.West));

        Assert.Equal(route.Source, player.Location);
        Assert.Same(Map.Felucca, player.Map);
    }

    private static Deployment.Layout LoadLayout()
    {
        Assert.True(TestServerInitializer.TileDataLoaded, "Requires the staged native UO tile data.");
        var path = Environment.GetEnvironmentVariable("MODERNUO_NEWBIE_CITY_V2_LAYOUT") ??
            Path.GetFullPath("artifacts/modular-dungeon/newbie-city-v2-install-20261012-r1/staged-map/layout.json");
        Assert.True(File.Exists(path), $"Requires the complete staged V2 manifest: {path}.");
        return Deployment.LoadLayout(path);
    }

    private Deployment.Result EnsureAndTrack(Deployment.Layout layout)
    {
        var result = Deployment.Ensure(layout);
        foreach (var spawner in result.Spawners)
        {
            Track(spawner);
            foreach (var entity in spawner.Spawned.Keys)
            {
                Track(entity);
            }
        }
        foreach (var pad in result.Teleporters)
        {
            Track(pad);
        }
        foreach (var gate in result.Doors)
        {
            Track(gate);
        }
        return result;
    }

    private static void AssertInstalled(Deployment.Layout layout, Deployment.Result result)
    {
        Assert.Equal(14, result.Spawners.Length);
        Assert.Equal(4, result.Teleporters.Length);
        Assert.Equal(4, result.Doors.Length);
        var sheep = 0;
        var humans = 0;
        for (var index = 0; index < result.Spawners.Length; index++)
        {
            var pin = layout.Spawners[index];
            var spawner = result.Spawners[index];
            Assert.IsType<Spawner>(spawner);
            Assert.False(spawner.Deleted);
            Assert.Same(spawner, World.FindEntity(spawner.Serial));
            Assert.Equal("Newbie City V2 - " + pin.Id, spawner.Name);
            Assert.Equal(Deployment.SpawnGuid(pin.Id), spawner.Guid);
            Assert.Equal(pin.Position, spawner.Location);
            Assert.Same(Map.Felucca, spawner.Map);
            Assert.Equal(pin.Bounds, spawner.SpawnBounds);
            Assert.Equal(pin.Count, spawner.Count);
            Assert.True(spawner.Running);
            Assert.Equal(TimeSpan.FromMinutes(2), spawner.MinDelay);
            Assert.Equal(TimeSpan.FromMinutes(5), spawner.MaxDelay);
            var entry = Assert.Single(spawner.Entries);
            Assert.Equal(pin.Type, entry.SpawnedName);
            Assert.Equal(pin.Count, entry.SpawnedMaxCount);
            Assert.Equal(100, entry.SpawnedProbability);
            Assert.Equal(pin.Count, spawner.Spawned.Count);
            Assert.Equal(pin.Count, entry.Spawned.Count);
            foreach (var pair in spawner.Spawned)
            {
                var creature = Assert.IsAssignableFrom<BaseCreature>(pair.Key);
                Assert.Equal(pin.Type, creature.GetType().Name);
                Assert.False(creature.Deleted);
                Assert.False(creature.Controlled);
                Assert.False(creature.Summoned);
                Assert.Equal(pin.Type != "Sheep", creature.Blessed);
                Assert.Same(spawner, creature.Spawner);
                Assert.Same(entry, pair.Value);
                Assert.Contains(creature, entry.Spawned);
                Assert.True(pin.Bounds.Contains(creature.Location));
                Assert.Equal(0, creature.Z);
                Assert.Equal(pin.Position, creature.Home);
                Assert.Same(Map.Felucca, creature.HomeMap);
                Assert.Equal(spawner.WalkingRange, creature.RangeHome);
                AssertFits(creature.Location);
                if (pin.Type == "Sheep")
                {
                    sheep++;
                }
                else
                {
                    humans++;
                }
            }
        }
        Assert.Equal(12, humans);
        Assert.Equal(12, sheep);
        for (var index = 0; index < result.Teleporters.Length; index++)
        {
            var route = layout.Routes[index];
            var pad = Assert.IsType<Teleporter>(result.Teleporters[index]);
            Assert.Equal(route.Source, pad.Location);
            Assert.Equal(route.Destination, pad.PointDest);
            Assert.Same(Map.Felucca, pad.MapDest);
            Assert.Equal(TeleporterFlags.Active, pad.Flags);
            Assert.Equal(0x1BC3, pad.ItemID);
            Assert.False(pad.Visible);
            Assert.False(pad.Movable);
            Assert.Equal(TimeSpan.Zero, pad.Delay);
        }
        for (var index = 0; index < result.Doors.Length; index++)
        {
            var gate = Assert.IsType<DarkWoodGate>(result.Doors[index]);
            Assert.Equal(layout.Doors[index].Position, gate.Location);
            Assert.False(gate.Open);
            Assert.False(gate.Locked);
            Assert.Same(result.Doors[index ^ 1], gate.Link);
        }
    }

    private static void AssertMissingExcept(Deployment.Layout layout, Spawner retained)
    {
        foreach (var pin in layout.Spawners)
        {
            var count = 0;
            foreach (var spawner in Map.Felucca.GetItemsInRange<BaseSpawner>(pin.Position, 0))
            {
                Assert.Same(retained, spawner);
                count++;
            }
            Assert.Equal(retained != null && retained.Location == pin.Position ? 1 : 0, count);
        }
        foreach (var route in layout.Routes)
        {
            foreach (var pad in Map.Felucca.GetItemsAt<Teleporter>(route.Source))
            {
                Assert.Fail($"Unexpected route object {pad.Serial}.");
            }
        }
        foreach (var gate in layout.Doors)
        {
            foreach (var door in Map.Felucca.GetItemsInRange<BaseDoor>(gate.Position, 1))
            {
                Assert.Fail($"Unexpected gate object {door.Serial}.");
            }
        }
    }

    private static HashSet<Point3D> Flood(Rectangle3D bounds, Point3D anchor)
    {
        AssertFits(anchor);
        var connected = new HashSet<Point3D> { anchor };
        var queue = new Queue<Point3D>();
        queue.Enqueue(anchor);
        while (queue.TryDequeue(out var point))
        {
            Point3D[] neighbors =
            [
                new(point.X - 1, point.Y, 0), new(point.X + 1, point.Y, 0),
                new(point.X, point.Y - 1, 0), new(point.X, point.Y + 1, 0)
            ];
            foreach (var next in neighbors)
            {
                if (bounds.Contains(next) && !connected.Contains(next) && Map.Felucca.CanFit(next, 16, true, false))
                {
                    connected.Add(next);
                    queue.Enqueue(next);
                }
            }
        }
        return connected;
    }

    private static Point3D Front(Deployment.SpawnPin pin) => pin.Facing switch
    {
        Direction.North => new(pin.Position.X, pin.Position.Y - 1, 0),
        Direction.South => new(pin.Position.X, pin.Position.Y + 1, 0),
        Direction.East => new(pin.Position.X + 1, pin.Position.Y, 0),
        Direction.West => new(pin.Position.X - 1, pin.Position.Y, 0),
        _ => throw new InvalidOperationException("V2 human vendors must face a cardinal approach.")
    };

    private static void AssertConnected(Point3D point, HashSet<Point3D> connected)
    {
        AssertFits(point);
        Assert.Contains(point, connected);
    }

    private static void AssertFits(Point3D point) =>
        Assert.True(Map.Felucca.CanFit(point, 16, true, false), $"Native V2 standing clearance at {point}.");

    private T Track<T>(T entity) where T : IEntity
    {
        if (!_created.Contains(entity))
        {
            if (!ReferenceEquals(World.FindEntity(entity.Serial), entity))
            {
                World.AddEntity(entity);
            }
            _created.Add(entity);
        }
        return entity;
    }
}
