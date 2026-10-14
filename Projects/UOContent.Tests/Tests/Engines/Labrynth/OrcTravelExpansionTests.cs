using System;
using System.Collections.Generic;
using Server;
using Server.Engines.Labrynth;
using Server.Items;
using Server.Tests;
using Xunit;
using Movement = Server.Movement.Movement;

namespace UOContent.Tests.Engines.Labrynth;

[Collection("Sequential UOContent Tests")]
public class OrcTravelExpansionTests : IDisposable
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
    public void Plans_PinAllExistingObjectsAndOnlyTheRequestedTravelChanges()
    {
        (uint Serial, Point3D OriginalSource, Point3D OriginalDestination, Point3D Source, Point3D Destination, string Name)[] expected =
        [
            (0x4000198E, new(1404, 1462, 0), new(5279, 1082, 2), new(1404, 1460, 0), new(5279, 1082, 2), "Galven Cave - Orc Fort"),
            (0x4000198F, new(1405, 1462, 0), new(5280, 1082, 2), new(1405, 1460, 0), new(5280, 1082, 2), "Galven Cave - Orc Fort"),
            (0x40007517, new(5279, 1084, 0), new(1404, 1463, 0), new(5279, 1084, 0), new(1404, 1461, 0), "Orc Fort - Galven Cave"),
            (0x40007518, new(5280, 1084, 0), new(1405, 1463, 0), new(5280, 1084, 0), new(1405, 1461, 0), "Orc Fort - Galven Cave"),
            (0x4000750F, new(1666, 1247, 0), new(5300, 981, 0), new(1666, 1247, 0), new(5284, 985, 20), "Orc Fort - Dungeon Entrance"),
            (0x40007510, new(1667, 1247, 0), new(5300, 981, 0), new(1667, 1247, 0), new(5284, 985, 20), "Orc Fort - Dungeon Entrance"),
            (0x40007511, new(5297, 981, 0), new(1666, 1247, 0), new(5284, 984, 20), new(1666, 1247, 0), "Dungeon - Return to Orc Fort"),
            (0x40007512, new(5298, 981, 0), new(1666, 1247, 0), new(5285, 984, 20), new(1666, 1247, 0), "Dungeon - Return to Orc Fort"),
            (0x40007513, new(5299, 981, 0), new(1666, 1247, 0), new(5284, 983, 20), new(1666, 1247, 0), "Dungeon - Return to Orc Fort"),
            (0x40007514, new(5300, 981, 0), new(1666, 1247, 0), new(5285, 983, 20), new(1666, 1247, 0), "Dungeon - Return to Orc Fort")
        ];
        Assert.Equal(expected.Length, OrcTravelExpansion.Routes.Length);
        foreach (var item in expected)
        {
            var route = FindRoute(item.Serial);
            Assert.Equal(item.OriginalSource, route.OriginalSource);
            Assert.Equal(item.OriginalDestination, route.OriginalDestination);
            Assert.Equal(item.Source, route.Source);
            Assert.Equal(item.Destination, route.Destination);
            Assert.Equal(item.Name, route.Name);
        }
        Point3D[] positions =
        [
            new(1405, 1469, 1), new(1404, 1468, 1), new(1405, 1466, 1), new(1404, 1465, 1),
            new(1405, 1463, 1), new(1404, 1462, 1), new(1405, 1461, 1)
        ];
        Assert.Equal(positions.Length, OrcTravelExpansion.Footprints.Length);
        for (var index = 0; index < positions.Length; index++)
        {
            var footprint = OrcTravelExpansion.Footprints[index];
            Assert.Equal(0x40007519u + (uint)index, footprint.Serial.Value);
            Assert.Equal(new Point3D(1405, 1469 - index, 1), footprint.OriginalPosition);
            Assert.Equal(positions[index], footprint.Position);
        }
    }

    [SkippableFact]
    public void CompleteMigration_PreservesEverySerialAndUnrelatedItems_AndIsIdempotent()
    {
        TileDataRequirement.SkipIfMissing();
        var original = SeedOriginal();
        var unrelated = Track(new Item(0xEED));
        unrelated.MoveToWorld(OrcTravelExpansion.Routes[0].Source, Map.Felucca);
        var neighbor = Track(new Teleporter(new Point3D(100, 100, 0), Map.Felucca));
        var neighborPosition = new Point3D(5286, 985, 20);
        neighbor.MoveToWorld(neighborPosition, Map.Felucca);

        var result = OrcTravelExpansion.Ensure();

        Assert.Equal(10, result.MovedTeleporters);
        Assert.Equal(6, result.MovedFootprints);
        AssertInstalled(result, original.Teleporters, original.Footprints);
        var reused = OrcTravelExpansion.Ensure();
        Assert.Equal(0, reused.MovedTeleporters);
        Assert.Equal(0, reused.MovedFootprints);
        AssertInstalled(reused, original.Teleporters, original.Footprints);
        Assert.False(unrelated.Deleted);
        Assert.Equal(OrcTravelExpansion.Routes[0].Source, unrelated.Location);
        Assert.False(neighbor.Deleted);
        Assert.Equal(neighborPosition, neighbor.Location);
        Assert.Equal(new Point3D(100, 100, 0), neighbor.PointDest);
    }

    [SkippableTheory]
    [InlineData("LateTeleporter")]
    [InlineData("LateFootprint")]
    [InlineData("WrongDestination")]
    [InlineData("Mixed")]
    [InlineData("Missing")]
    public void FullPreflightFailure_DoesNotMoveRepointDeleteOrRegenerateAnyObject(string conflict)
    {
        TileDataRequirement.SkipIfMissing();
        var original = SeedOriginal();
        switch (conflict)
        {
            case "LateTeleporter":
            {
                var unexpected = Track(new Teleporter(new Point3D(10, 10, 0), Map.Felucca));
                unexpected.MoveToWorld(OrcTravelExpansion.Routes[^1].Source, Map.Felucca);
                break;
            }
            case "LateFootprint":
            {
                var unexpected = Track(new Static(0x1E04) { Name = "Unrelated footprints" });
                unexpected.MoveToWorld(OrcTravelExpansion.Footprints[^1].Position, Map.Felucca);
                break;
            }
            case "WrongDestination":
            {
                original.Teleporters[^1].PointDest = new Point3D(10, 10, 0);
                break;
            }
            case "Mixed":
            {
                original.Teleporters[0].MoveToWorld(OrcTravelExpansion.Routes[0].Source, Map.Felucca);
                break;
            }
            case "Missing":
            {
                original.Footprints[^1].Delete();
                break;
            }
        }
        var before = Capture();

        Assert.Throws<InvalidOperationException>(() => OrcTravelExpansion.Ensure());

        AssertUnchanged(before);
        foreach (var route in OrcTravelExpansion.Routes)
        {
            Assert.Same(original.Teleporters[FindRouteIndex(route.Serial)], World.FindItem(route.Serial));
        }
        if (conflict == "Missing")
        {
            Assert.Null(World.FindItem(OrcTravelExpansion.Footprints[^1].Serial));
        }
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void NativeWalking_AllTenRoutesArriveAtTheirExactDestinationWithoutImmediateBounce(int index)
    {
        TileDataRequirement.SkipIfMissing();
        SeedOriginal();
        OrcTravelExpansion.Ensure();
        var route = OrcTravelExpansion.Routes[index];
        var player = Track(new Mobile { Player = true, Body = 0x190 });
        var direction = Approach(player, route.Source);

        Assert.False(player.Move(direction));

        Assert.Equal(route.Destination, player.Location);
        Assert.Same(Map.Felucca, player.Map);
        Assert.NotEqual(route.Source, player.Location);
        Assert.True(Map.Felucca.CanFit(route.Destination, 16, true, false));
        if (route.Serial == (Serial)0x4000750Fu || route.Serial == (Serial)0x40007510u)
        {
            foreach (var item in Map.Felucca.GetItemsAt<Teleporter>(player.Location))
            {
                Assert.False(item.Active);
            }
        }
    }

    [SkippableFact]
    public void NativeSouthRamp_IsWalkableDownAndBackUpWithTheExpectedHeights()
    {
        TileDataRequirement.SkipIfMissing();
        Point3D[] path =
        [
            new(5284, 985, 20), new(5284, 986, 17), new(5284, 987, 12), new(5284, 988, 7),
            new(5284, 989, 2), new(5284, 990, 0), new(5284, 991, 0), new(5284, 992, 0),
            new(5284, 993, 0), new(5284, 994, 0)
        ];
        var player = Track(new Mobile { Player = true, Body = 0x190, Direction = Direction.South });
        player.MoveToWorld(path[0], Map.Felucca);
        for (var index = 1; index < path.Length; index++)
        {
            Assert.True(Map.Felucca.CanFit(path[index], 16, true, false));
            Assert.True(player.Move(Direction.South));
            Assert.Equal(path[index], player.Location);
        }
        player.Direction = Direction.North;
        for (var index = path.Length - 2; index >= 0; index--)
        {
            Assert.True(player.Move(Direction.North));
            Assert.Equal(path[index], player.Location);
        }
    }

    [SkippableFact]
    public void StaggeredFootprintTrail_HasNativeSupportAndDoesNotBlockWalking()
    {
        TileDataRequirement.SkipIfMissing();
        SeedOriginal();
        var result = OrcTravelExpansion.Ensure();
        var player = Track(new Mobile { Player = true, Body = 0x190 });
        for (var index = 0; index < result.FootprintItems.Length; index++)
        {
            var footprint = result.FootprintItems[index];
            var target = new Point3D(footprint.X, footprint.Y, 0);
            Assert.Equal(0, footprint.ItemData.CalcHeight);
            Assert.False(footprint.ItemData.Surface);
            Assert.False(footprint.ItemData.Impassable);
            Assert.True(Map.Felucca.CanFit(target, 16, true, false));
            player.MoveToWorld(new Point3D(target.X, target.Y + 1, 0), Map.Felucca);
            player.Direction = Direction.North;
            Assert.True(player.Move(Direction.North));
            Assert.Equal(target, player.Location);
        }
    }

    [SkippableFact]
    public void OrdinarySerialization_PreservesAllPinnedObjects_WithoutRegenerationOrAnotherMove()
    {
        TileDataRequirement.SkipIfMissing();
        var original = SeedOriginal();
        OrcTravelExpansion.Ensure();
        var teleporters = new Teleporter[original.Teleporters.Length];
        var footprints = new Static[original.Footprints.Length];
        for (var index = 0; index < teleporters.Length; index++)
        {
            teleporters[index] = Restore(original.Teleporters[index], new Teleporter(original.Teleporters[index].Serial));
        }
        for (var index = 0; index < footprints.Length; index++)
        {
            footprints[index] = Restore(original.Footprints[index], new Static(original.Footprints[index].Serial));
        }

        var result = OrcTravelExpansion.Ensure();

        Assert.Equal(0, result.MovedTeleporters);
        Assert.Equal(0, result.MovedFootprints);
        AssertInstalled(result, teleporters, footprints);
    }

    private (Teleporter[] Teleporters, Static[] Footprints) SeedOriginal()
    {
        var teleporters = new Teleporter[OrcTravelExpansion.Routes.Length];
        var footprints = new Static[OrcTravelExpansion.Footprints.Length];
        for (var index = 0; index < teleporters.Length; index++)
        {
            var route = OrcTravelExpansion.Routes[index];
            Assert.Null(World.FindItem(route.Serial));
            var item = Track(new Teleporter(route.Serial)
            {
                ItemID = 0x1BC3, Amount = 1, Name = route.Name, PointDest = route.OriginalDestination,
                MapDest = Map.Felucca, Flags = TeleporterFlags.Active, Delay = TimeSpan.Zero,
                SoundID = 0, Visible = false, Movable = false
            });
            World.AddEntity(item);
            item.MoveToWorld(route.OriginalSource, Map.Felucca);
            teleporters[index] = item;
        }
        for (var index = 0; index < footprints.Length; index++)
        {
            var plan = OrcTravelExpansion.Footprints[index];
            Assert.Null(World.FindItem(plan.Serial));
            var item = Track(new Static(plan.Serial)
            {
                ItemID = 0x1E04, Amount = 1, Name = "Cave entrance footprints", Hue = 0,
                Visible = true, Movable = false
            });
            World.AddEntity(item);
            item.MoveToWorld(plan.OriginalPosition, Map.Felucca);
            footprints[index] = item;
        }
        return (teleporters, footprints);
    }

    private static void AssertInstalled(OrcTravelExpansion.Result result, Teleporter[] teleporters, Static[] footprints)
    {
        Assert.Equal(10, result.Teleporters.Length);
        Assert.Equal(7, result.FootprintItems.Length);
        for (var index = 0; index < teleporters.Length; index++)
        {
            var route = OrcTravelExpansion.Routes[index];
            var item = Assert.IsType<Teleporter>(result.Teleporters[index]);
            Assert.Same(teleporters[index], item);
            Assert.Same(item, World.FindItem(route.Serial));
            Assert.Equal(route.Source, item.Location);
            Assert.Equal(route.Destination, item.PointDest);
            Assert.Equal(route.Name, item.Name);
            Assert.Same(Map.Felucca, item.Map);
            Assert.Same(Map.Felucca, item.MapDest);
            Assert.Equal(TeleporterFlags.Active, item.Flags);
            Assert.Equal(TimeSpan.Zero, item.Delay);
            Assert.Equal(0, item.SoundID);
            Assert.False(item.Visible);
            Assert.False(item.Movable);
            Assert.False(item.Deleted);
        }
        for (var index = 0; index < footprints.Length; index++)
        {
            var plan = OrcTravelExpansion.Footprints[index];
            var item = Assert.IsType<Static>(result.FootprintItems[index]);
            Assert.Same(footprints[index], item);
            Assert.Same(item, World.FindItem(plan.Serial));
            Assert.Equal(plan.Position, item.Location);
            Assert.Equal(0x1E04, item.ItemID);
            Assert.Equal("Cave entrance footprints", item.Name);
            Assert.Equal(0, item.Hue);
            Assert.True(item.Visible);
            Assert.False(item.Movable);
            Assert.False(item.Deleted);
            Assert.Same(Map.Felucca, item.Map);
        }
    }

    private (IEntity Entity, Point3D Location, bool Deleted, Point3D? Destination)[] Capture()
    {
        var snapshot = new (IEntity Entity, Point3D Location, bool Deleted, Point3D? Destination)[_created.Count];
        for (var index = 0; index < _created.Count; index++)
        {
            var item = _created[index];
            snapshot[index] = (item, item.Location, item.Deleted, item is Teleporter pad ? pad.PointDest : null);
        }
        return snapshot;
    }

    private static void AssertUnchanged((IEntity Entity, Point3D Location, bool Deleted, Point3D? Destination)[] snapshot)
    {
        foreach (var item in snapshot)
        {
            Assert.Equal(item.Location, item.Entity.Location);
            Assert.Equal(item.Deleted, item.Entity.Deleted);
            if (item.Destination.HasValue)
            {
                Assert.Equal(item.Destination.Value, ((Teleporter)item.Entity).PointDest);
            }
        }
    }

    private T Restore<T>(T original, T restored) where T : Item
    {
        var writer = new BufferWriter(true);
        original.Serialize(writer);
        var bytes = writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();
        original.Delete();
        Track(restored);
        var reader = new BufferReader(bytes);
        restored.Deserialize(reader);
        World.AddEntity(restored);
        Assert.Equal(bytes.Length, reader.Position);
        return restored;
    }

    private static OrcTravelExpansion.Route FindRoute(uint serial)
    {
        foreach (var route in OrcTravelExpansion.Routes)
        {
            if (route.Serial.Value == serial)
            {
                return route;
            }
        }
        throw new InvalidOperationException($"Missing planned route 0x{serial:X8}.");
    }

    private static int FindRouteIndex(Serial serial)
    {
        for (var index = 0; index < OrcTravelExpansion.Routes.Length; index++)
        {
            if (OrcTravelExpansion.Routes[index].Serial == serial)
            {
                return index;
            }
        }
        throw new InvalidOperationException($"Missing planned route {serial}.");
    }

    private static Direction Approach(Mobile player, Point3D source)
    {
        (int X, int Y, Direction Direction)[] candidates =
        [
            (0, 1, Direction.North), (0, -1, Direction.South),
            (1, 0, Direction.West), (-1, 0, Direction.East)
        ];
        foreach (var candidate in candidates)
        {
            var point = new Point3D(source.X + candidate.X, source.Y + candidate.Y, source.Z);
            if (!Map.Felucca.CanFit(point, 16, true, false))
            {
                continue;
            }
            player.MoveToWorld(point, Map.Felucca);
            player.Direction = candidate.Direction;
            if (Movement.CheckMovement(player, candidate.Direction, out var z) && z == source.Z)
            {
                return candidate.Direction;
            }
        }
        throw new InvalidOperationException($"No native walkable approach to travel pad at {source}.");
    }

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }
}
