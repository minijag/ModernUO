using System;
using System.Collections.Generic;
using System.IO;
using Server;
using Server.Engines.Spawners;
using Server.Items;
using Server.Mobiles;
using Server.Tests;
using Xunit;
using Deployment = Server.Engines.Labrynth.NewbieCityDeployment;
using Movement = Server.Movement.Movement;

namespace UOContent.Tests.Engines.NewbieCity;

[Collection("Sequential UOContent Tests")]
public class NewbieCityDeploymentTests : IDisposable
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
    public void ApprovedLayout_HasFourReciprocalRoutesAndTwelveSingleNpcPinsPlusTenSheep()
    {
        TileDataRequirement.SkipIfMissing();
        var layout = LoadLayout();
        Deployment.ValidateLayout(layout);
        Assert.Equal("Felucca", layout.Map);
        Assert.Equal(4, layout.Routes.Length);
        Assert.Equal(13, layout.Spawners.Length);
        var expectedRoutes = new Dictionary<Point3D, Point3D>
        {
            [new(5578, 1351, 0)] = new(721, 1612, 10),
            [new(5578, 1352, 0)] = new(721, 1613, 10),
            [new(721, 1612, 10)] = new(5578, 1351, 0),
            [new(721, 1613, 10)] = new(5578, 1352, 0)
        };
        var sources = new HashSet<Point3D>();
        foreach (var route in layout.Routes)
        {
            Assert.True(sources.Add(route.Source));
            var reciprocal = Assert.Single(layout.Routes, candidate => candidate.Source == route.Destination);
            Assert.Equal(route.Source, reciprocal.Destination);
            Assert.Equal(expectedRoutes[route.Source], route.Destination);
        }
        Assert.True(sources.SetEquals(expectedRoutes.Keys));
        var expectedRoles = new HashSet<string>
        {
            "Blacksmith", "Tailor", "Carpenter", "Bowyer", "Alchemist", "Mage", "Provisioner",
            "Farmer", "AnimalTrainer", "Tinker", "Banker", "Healer", "Sheep"
        };
        Assert.True(expectedRoles.SetEquals(Array.ConvertAll(layout.Spawners, pin => pin.Type)));
        var sheep = Assert.Single(layout.Spawners, pin => pin.Type == "Sheep");
        Assert.Equal(10, sheep.Count);
        Assert.True(sheep.Bounds.Contains(sheep.Position));
        foreach (var pin in layout.Spawners)
        {
            Assert.True(layout.CityBounds.Contains(pin.Position));
            Assert.True(pin.Bounds.Contains(pin.Position));
            if (pin != sheep)
            {
                Assert.Equal(1, pin.Count);
                var type = AssemblyHandler.FindTypeByName(pin.Type);
                Assert.NotNull(type);
                Assert.True(typeof(BaseVendor).IsAssignableFrom(type) || type == typeof(Healer));
            }
        }
    }

    [SkippableFact]
    public void Ensure_CreatesOnlyStockObjectsAndInitialPopulation_AndRepeatPreservesEverySerial()
    {
        TileDataRequirement.SkipIfMissing();
        var layout = LoadLayout();
        var first = EnsureAndTrack(layout);
        Assert.Equal(17 + layout.Doors.Length, first.Created);
        Assert.Equal(13, first.Spawners.Length);
        Assert.Equal(4, first.Teleporters.Length);
        Assert.Equal(layout.Doors.Length, first.Doors.Length);
        AssertInstalled(layout, first);
        var originalMobiles = new HashSet<ISpawnable>();
        foreach (var spawner in first.Spawners)
        {
            foreach (var mobile in spawner.Spawned.Keys)
            {
                originalMobiles.Add(mobile);
            }
        }
        Assert.Equal(22, originalMobiles.Count);

        var repeated = EnsureAndTrack(layout);
        Assert.Equal(0, repeated.Created);
        for (var index = 0; index < first.Spawners.Length; index++)
        {
            Assert.Same(first.Spawners[index], repeated.Spawners[index]);
            foreach (var mobile in repeated.Spawners[index].Spawned.Keys)
            {
                Assert.Contains(mobile, originalMobiles);
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
        AssertInstalled(layout, repeated);
    }

    [SkippableFact]
    public void PartialExistingDeployment_ReusesOriginalSerialsAndCreatesOnlyMissingObjects()
    {
        TileDataRequirement.SkipIfMissing();
        var layout = LoadLayout();
        var first = EnsureAndTrack(layout);
        var spawner = first.Spawners[0];
        var pad = first.Teleporters[0];
        Assert.NotEmpty(first.Doors);
        var door = first.Doors[0];
        DeleteInstallationExcept(first, spawner, pad, door);

        var repaired = EnsureAndTrack(layout);

        Assert.Equal(14 + layout.Doors.Length, repaired.Created);
        Assert.Same(spawner, repaired.Spawners[0]);
        Assert.Same(pad, repaired.Teleporters[0]);
        Assert.Same(door, repaired.Doors[0]);
        AssertInstalled(layout, repaired);
    }

    [SkippableTheory]
    [InlineData("Destination")]
    [InlineData("Duplicate")]
    [InlineData("Flags")]
    public void LateRouteConflict_RefusesBeforeRecreatingAnyEarlierMissingObject(string conflict)
    {
        TileDataRequirement.SkipIfMissing();
        var layout = LoadLayout();
        var installed = EnsureAndTrack(layout);
        var pad = installed.Teleporters[^1];
        DeleteInstallationExcept(installed, pad);
        switch (conflict)
        {
            case "Destination":
            {
                pad.PointDest = new Point3D(pad.PointDest.X + 1, pad.PointDest.Y, pad.PointDest.Z);
                break;
            }
            case "Duplicate":
            {
                var duplicate = Track(new Teleporter(pad.PointDest, pad.MapDest) { Name = pad.Name });
                duplicate.MoveToWorld(pad.Location, pad.Map);
                break;
            }
            case "Flags":
            {
                pad.Creatures = true;
                break;
            }
        }
        var destination = pad.PointDest;

        Assert.Throws<InvalidOperationException>(() => Deployment.Ensure(layout, checkSupport: false));

        Assert.Equal(destination, pad.PointDest);
        Assert.Same(pad, World.FindItem(pad.Serial));
        AssertMissingExcept(layout, pad.Location);
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void LateSpawnerMismatchOrDuplicate_RefusesBeforeCreatingAnyMissingRouteOrSpawn(bool duplicate)
    {
        TileDataRequirement.SkipIfMissing();
        var layout = LoadLayout();
        var installed = EnsureAndTrack(layout);
        var spawner = installed.Spawners[^1];
        DeleteInstallationExcept(installed, spawner);
        if (duplicate)
        {
            var entry = spawner.Entries[0];
            var clone = Track((Spawner)new SpawnerDataDto
            {
                Guid = spawner.Guid,
                Name = spawner.Name,
                Location = spawner.Location,
                Map = spawner.Map,
                Count = spawner.Count,
                MinDelay = spawner.MinDelay,
                MaxDelay = spawner.MaxDelay,
                WalkingRange = spawner.WalkingRange,
                SpawnBounds = spawner.SpawnBounds,
                Entries = [new SpawnerEntry(entry.SpawnedName, 100, spawner.Count, entry.Properties, entry.Parameters)]
            }.ToSpawner());
            clone.MoveToWorld(spawner.Location, spawner.Map);
        }
        else
        {
            spawner.Count++;
        }
        var count = spawner.Count;

        Assert.Throws<InvalidOperationException>(() => Deployment.Ensure(layout, checkSupport: false));

        Assert.Equal(count, spawner.Count);
        Assert.Same(spawner, World.FindItem(spawner.Serial));
        AssertMissingExcept(layout, spawner.Location);
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void LateDoorMismatchOrDuplicate_RefusesBeforeCreatingMissingSpawnersAndRoutes(bool duplicate)
    {
        TileDataRequirement.SkipIfMissing();
        var layout = LoadLayout();
        var installed = EnsureAndTrack(layout);
        Assert.NotEmpty(installed.Doors);
        var door = installed.Doors[^1];
        DeleteInstallationExcept(installed, door);
        if (duplicate)
        {
            var clone = Track((BaseDoor)Activator.CreateInstance(door.GetType(), layout.Doors[^1].Facing));
            clone.Name = door.Name;
            clone.MoveToWorld(door.Location, door.Map);
        }
        else
        {
            door.ItemID++;
        }
        var itemId = door.ItemID;

        Assert.Throws<InvalidOperationException>(() => Deployment.Ensure(layout, checkSupport: false));

        Assert.Equal(itemId, door.ItemID);
        Assert.Same(door, World.FindItem(door.Serial));
        AssertMissingExcept(layout, door.Location);
    }

    [SkippableFact]
    public void NativeLayout_AllNpcPinsRoutesDoorwaysAndSheepPenHaveStandingClearance()
    {
        TileDataRequirement.SkipIfMissing();
        var layout = LoadLayout();
        foreach (var pin in layout.Spawners)
        {
            AssertFits(pin.Position);
        }
        foreach (var route in layout.Routes)
        {
            AssertFits(route.Source);
            AssertFits(route.Destination);
        }
        foreach (var door in layout.Doors)
        {
            AssertFits(door.Position);
        }
        AssertFits(new Point3D(5591, 1345, 0));
        AssertFits(new Point3D(5592, 1345, 0));
        AssertFits(new Point3D(5591, 1346, 0));
        AssertFits(new Point3D(5592, 1346, 0));
        var sheep = Assert.Single(layout.Spawners, pin => pin.Type == "Sheep");
        for (var x = sheep.Bounds.X; x < sheep.Bounds.X + sheep.Bounds.Width; x++)
        {
            for (var y = sheep.Bounds.Y; y < sheep.Bounds.Y + sheep.Bounds.Height; y++)
            {
                AssertFits(new Point3D(x, y, sheep.Position.Z));
            }
        }
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NativeWalkingOntoEachStockPad_LandsAtItsExactReciprocalWithoutAutomaticBounce(int index)
    {
        TileDataRequirement.SkipIfMissing();
        var layout = LoadLayout();
        var installed = EnsureAndTrack(layout);
        var route = layout.Routes[index];
        AssertFits(route.Source);
        AssertFits(route.Destination);
        var player = Track(new Mobile { Player = true, Body = 0x190 });
        var approach = NativeApproach(player, route.Source);

        Assert.False(player.Move(approach));

        Assert.Equal(route.Destination, player.Location);
        Assert.Same(Map.Felucca, player.Map);
        var reciprocal = Assert.Single(installed.Teleporters, pad => pad.Location == route.Destination);
        Assert.Equal(route.Source, reciprocal.PointDest);
    }

    private Deployment.Result EnsureAndTrack(Deployment.Layout layout)
    {
        var result = Deployment.Ensure(layout, checkSupport: false);
        foreach (var spawner in result.Spawners)
        {
            Track(spawner);
        }
        foreach (var pad in result.Teleporters)
        {
            Track(pad);
        }
        foreach (var door in result.Doors)
        {
            Track(door);
        }
        return result;
    }

    private static Deployment.Layout LoadLayout()
    {
        var path = Environment.GetEnvironmentVariable("MODERNUO_NEWBIE_CITY_LAYOUT") ??
            Path.GetFullPath("../Distribution/Live/Data/NewbieCity/layout.json");
        Skip.If(!File.Exists(path), "Requires the approved Newbie City layout manifest.");
        return Deployment.LoadLayout(path);
    }

    private static void AssertInstalled(Deployment.Layout layout, Deployment.Result result)
    {
        for (var index = 0; index < result.Spawners.Length; index++)
        {
            var pin = layout.Spawners[index];
            var spawner = result.Spawners[index];
            Assert.IsType<Spawner>(spawner);
            Assert.Equal(pin.Position, spawner.Location);
            Assert.Same(Map.Felucca, spawner.Map);
            Assert.Equal(pin.Count, spawner.Count);
            Assert.Equal(0, spawner.Team);
            Assert.Equal(pin.Bounds, spawner.SpawnBounds);
            var walkingRange = pin.HomeRange > 0 ? pin.HomeRange : spawner.HomeRange;
            Assert.Equal(walkingRange, spawner.WalkingRange);
            Assert.Equal(TimeSpan.FromMinutes(2), spawner.MinDelay);
            Assert.Equal(TimeSpan.FromMinutes(5), spawner.MaxDelay);
            var entry = Assert.Single(spawner.Entries);
            Assert.Equal(pin.Type, entry.SpawnedName);
            Assert.Equal(100, entry.SpawnedProbability);
            Assert.Equal(pin.Count, entry.SpawnedMaxCount);
            Assert.Equal(pin.Count, spawner.Spawned.Count);
            foreach (var pair in spawner.Spawned)
            {
                var npc = Assert.IsAssignableFrom<BaseCreature>(pair.Key);
                Assert.Same(spawner, npc.Spawner);
                Assert.Same(entry, pair.Value);
                Assert.Equal(pin.Type, npc.GetType().Name);
                Assert.Equal(pin.Type != "Sheep", npc.Blessed);
                Assert.Equal(pin.Position, npc.Home);
                Assert.Same(Map.Felucca, npc.HomeMap);
                Assert.Equal(walkingRange, npc.RangeHome);
                Assert.True(pin.Bounds.Contains(npc.Location));
            }
            Assert.Equal(1, CountAt<Spawner>(pin.Position));
            Assert.Same(spawner, World.FindItem(spawner.Serial));
        }
        for (var index = 0; index < result.Teleporters.Length; index++)
        {
            var pad = result.Teleporters[index];
            Assert.IsType<Teleporter>(pad);
            Assert.Equal(layout.Routes[index].Source, pad.Location);
            Assert.Equal(layout.Routes[index].Destination, pad.PointDest);
            Assert.Same(Map.Felucca, pad.MapDest);
            Assert.Equal(TeleporterFlags.Active, pad.Flags);
            Assert.Equal(0x1BC3, pad.ItemID);
            Assert.False(pad.Visible);
            Assert.False(pad.Movable);
            Assert.Equal(TimeSpan.Zero, pad.Delay);
            Assert.Equal(0, pad.SoundID);
            Assert.Equal(1, CountAt<Teleporter>(pad.Location));
            Assert.Same(pad, World.FindItem(pad.Serial));
        }
        for (var index = 0; index < result.Doors.Length; index++)
        {
            var door = result.Doors[index];
            Assert.Equal(layout.Doors[index].Position, door.Location);
            Assert.False(door.Locked);
            Assert.False(door.Open);
            Assert.Equal(1, CountAt<BaseDoor>(door.Location));
            Assert.Same(door, World.FindItem(door.Serial));
        }
    }

    private static void DeleteInstallationExcept(Deployment.Result result, params IEntity[] keep)
    {
        var retained = new HashSet<IEntity>(keep);
        foreach (var spawner in result.Spawners)
        {
            if (!retained.Contains(spawner))
            {
                spawner.Delete();
            }
        }
        foreach (var pad in result.Teleporters)
        {
            if (!retained.Contains(pad))
            {
                pad.Delete();
            }
        }
        foreach (var door in result.Doors)
        {
            if (!retained.Contains(door))
            {
                door.Delete();
            }
        }
    }

    private static void AssertMissingExcept(Deployment.Layout layout, Point3D retained)
    {
        foreach (var pin in layout.Spawners)
        {
            if (pin.Position != retained)
            {
                Assert.Equal(0, CountAt<Spawner>(pin.Position));
            }
        }
        foreach (var route in layout.Routes)
        {
            if (route.Source != retained)
            {
                Assert.Equal(0, CountAt<Teleporter>(route.Source));
            }
        }
        foreach (var door in layout.Doors)
        {
            if (door.Position != retained)
            {
                Assert.Equal(0, CountAt<BaseDoor>(door.Position));
            }
        }
    }

    private static void AssertFits(Point3D point) =>
        Assert.True(Map.Felucca.CanFit(point, 16, true, false), $"Native floor clearance at {point}.");

    private static int CountAt<T>(Point3D point) where T : Item
    {
        var count = 0;
        foreach (var item in Map.Felucca.GetItemsAt<T>(point))
        {
            if (item.Location == point)
            {
                count++;
            }
        }
        return count;
    }

    private static Direction NativeApproach(Mobile player, Point3D source)
    {
        var approach = source.X == 721
            ? new Point3D(722, source.Y, 9)
            : new Point3D(5579, source.Y, 0);
        AssertFits(approach);
        if (source.X == 721)
        {
            var heights = new[] { 0, 1, 3, 5, 7, 9 };
            player.MoveToWorld(new Point3D(727, source.Y, 0), Map.Felucca);
            player.Direction = Direction.West;
            for (var index = 1; index < heights.Length; index++)
            {
                Assert.True(player.Move(Direction.West));
                Assert.Equal(new Point3D(727 - index, source.Y, heights[index]), player.Location);
            }
        }
        else
        {
            player.MoveToWorld(approach, Map.Felucca);
            player.Direction = Direction.West;
        }
        Assert.True(Movement.CheckMovement(player, Direction.West, out var destinationZ));
        Assert.Equal(source.Z, destinationZ);
        return Direction.West;
    }

    private T Track<T>(T entity) where T : IEntity
    {
        if (!_created.Contains(entity))
        {
            _created.Add(entity);
        }
        return entity;
    }
}
