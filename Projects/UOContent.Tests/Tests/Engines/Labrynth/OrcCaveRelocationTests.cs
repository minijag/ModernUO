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
public class OrcCaveRelocationTests : IDisposable
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
    public void Routes_AreTheTwoCaveEntrancesAndTwoFortReturns()
    {
        Assert.Equal(4, OrcCaveRelocation.Routes.Length);
        for (var index = 0; index < 4; index++)
        {
            var entrance = index < 2;
            var offset = index % 2;
            var route = OrcCaveRelocation.Routes[index];
            Assert.Equal(entrance ? new Point3D(1404 + offset, 1462, 0) : new Point3D(5279 + offset, 1084, 0), route.Source);
            Assert.Equal(entrance ? new Point3D(5279 + offset, 1082, 2) : new Point3D(1404 + offset, 1463, 0), route.Destination);
            Assert.Equal(entrance ? "Galven Cave - Orc Fort" : "Orc Fort - Galven Cave", route.Name);
        }
    }

    [SkippableFact]
    public void Relocation_PreservesEntranceSerials_RemovesOnlyOldReturns_AndIsIdempotent()
    {
        TileDataRequirement.SkipIfMissing();
        var original = SeedOriginal();
        var neighbor = Track(new Teleporter(new Point3D(100, 100, 0), Map.Felucca));
        neighbor.MoveToWorld(new Point3D(1406, 1463, 0), Map.Felucca);
        var unrelated = Track(new Item(0xEED));
        unrelated.MoveToWorld(OrcCaveRelocation.Routes[0].Source, Map.Felucca);
        var result = EnsureAndTrack();

        Assert.True(result.Changed);
        Assert.Same(original[0], result.Teleporters[0]);
        Assert.Same(original[1], result.Teleporters[1]);
        for (var index = 0; index < 4; index++)
        {
            var pad = Assert.IsType<Teleporter>(result.Teleporters[index]);
            var route = OrcCaveRelocation.Routes[index];
            Assert.Equal(route.Source, pad.Location);
            Assert.Equal(route.Destination, pad.PointDest);
            Assert.Equal(route.Name, pad.Name);
            Assert.Equal(Map.Felucca, pad.Map);
            Assert.Equal(Map.Felucca, pad.MapDest);
            Assert.Equal(TeleporterFlags.Active, pad.Flags);
            Assert.False(pad.Visible);
            Assert.False(pad.Movable);
            Assert.Equal(TimeSpan.Zero, pad.Delay);
            Assert.True(original[index + 2].Deleted);
            Assert.Equal(original[index + 2].Serial.Value, result.RemovedSerials[index]);
        }
        var reused = OrcCaveRelocation.Ensure();
        Assert.False(reused.Changed);
        Assert.Empty(reused.RemovedSerials);
        for (var index = 0; index < 4; index++)
        {
            Assert.Same(result.Teleporters[index], reused.Teleporters[index]);
        }
        Assert.False(neighbor.Deleted);
        Assert.Equal(new Point3D(1406, 1463, 0), neighbor.Location);
        Assert.Equal(new Point3D(100, 100, 0), neighbor.PointDest);
        Assert.False(unrelated.Deleted);
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NativeWalking_AllFourNewPadsReachSupportedArrivalWithoutBounce(int index)
    {
        TileDataRequirement.SkipIfMissing();
        SeedOriginal();
        EnsureAndTrack();
        var route = OrcCaveRelocation.Routes[index];
        Assert.True(Map.Felucca.CanFit(route.Source, 16, true, false));
        Assert.True(Map.Felucca.CanFit(route.Destination, 16, true, false));
        var player = Track(new Mobile { Player = true, Body = 0x190 });
        var direction = Approach(player, route.Source);

        Assert.False(player.Move(direction));

        Assert.Equal(route.Destination, player.Location);
        Assert.Same(Map.Felucca, player.Map);
        foreach (var arrival in Map.Felucca.GetItemsAt<Teleporter>(player.Location))
        {
            Assert.False(arrival.Active);
        }
    }

    [Theory]
    [InlineData("NewConflict")]
    [InlineData("OldConflict")]
    [InlineData("DuplicateOld")]
    [InlineData("Mixed")]
    [InlineData("MissingOld")]
    public void PreflightConflict_PreservesEveryExistingItemAndEntrance(string conflict)
    {
        var original = SeedOriginal();
        switch (conflict)
        {
            case "NewConflict":
            {
                var unexpected = Track(new Teleporter(new Point3D(10, 10, 0), Map.Felucca));
                unexpected.MoveToWorld(OrcCaveRelocation.Routes[^1].Source, Map.Felucca);
                break;
            }
            case "OldConflict":
            {
                original[^1].Active = false;
                break;
            }
            case "DuplicateOld":
            {
                AddRoute(OrcCaveRelocation.OriginalReturns[^1]);
                break;
            }
            case "Mixed":
            {
                AddRoute(OrcCaveRelocation.Routes[0]);
                break;
            }
            case "MissingOld":
            {
                original[^1].Delete();
                break;
            }
        }
        var locations = new Point3D[_created.Count];
        var deleted = new bool[_created.Count];
        for (var index = 0; index < _created.Count; index++)
        {
            locations[index] = _created[index].Location;
            deleted[index] = _created[index].Deleted;
        }

        Assert.Throws<InvalidOperationException>(() => OrcCaveRelocation.Ensure());

        for (var index = 0; index < _created.Count; index++)
        {
            Assert.Equal(locations[index], _created[index].Location);
            Assert.Equal(deleted[index], _created[index].Deleted);
        }
        for (var index = 0; index < 2; index++)
        {
            Assert.Equal(OrcCaveRelocation.OriginalEntrances[index].Destination, original[index].PointDest);
            Assert.Null(original[index].Name);
        }
    }

    [SkippableFact]
    public void NativeSerialization_RestoresOrdinaryRelocatedRoutesWithoutRegeneration()
    {
        TileDataRequirement.SkipIfMissing();
        SeedOriginal();
        var result = EnsureAndTrack();
        var restored = new Teleporter[4];
        for (var index = 0; index < 4; index++)
        {
            var writer = new BufferWriter(true);
            result.Teleporters[index].Serialize(writer);
            var bytes = writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();
            result.Teleporters[index].Delete();
            restored[index] = Track(new Teleporter(World.NewItem));
            var reader = new BufferReader(bytes);
            restored[index].Deserialize(reader);
            Assert.Equal(bytes.Length, reader.Position);
        }

        var reused = OrcCaveRelocation.Ensure();

        Assert.False(reused.Changed);
        Assert.Empty(reused.RemovedSerials);
        for (var index = 0; index < 4; index++)
        {
            Assert.Same(restored[index], reused.Teleporters[index]);
        }
    }

    private Teleporter[] SeedOriginal()
    {
        var original = new Teleporter[6];
        for (var index = 0; index < 2; index++)
        {
            original[index] = AddRoute(OrcCaveRelocation.OriginalEntrances[index]);
        }
        for (var index = 0; index < 4; index++)
        {
            original[index + 2] = AddRoute(OrcCaveRelocation.OriginalReturns[index]);
        }
        return original;
    }

    private Teleporter AddRoute(OrcCaveRelocation.Route route)
    {
        var pad = Track(new Teleporter(route.Destination, Map.Felucca) { Name = route.Name });
        pad.MoveToWorld(route.Source, Map.Felucca);
        return pad;
    }

    private OrcCaveRelocation.Result EnsureAndTrack()
    {
        var result = OrcCaveRelocation.Ensure();
        foreach (var pad in result.Teleporters)
        {
            if (!_created.Contains(pad))
            {
                Track(pad);
            }
        }
        return result;
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
        throw new InvalidOperationException($"No native walkable approach to orc-cave pad at {source}.");
    }

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }
}
