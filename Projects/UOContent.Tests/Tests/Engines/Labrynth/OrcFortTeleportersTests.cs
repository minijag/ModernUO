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
public class OrcFortTeleportersTests : IDisposable
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
    public void Routes_AreTheTwoRequestedEntranceTilesAndFourReturnTiles()
    {
        var routes = OrcFortTeleporters.Routes;
        Assert.Equal(6, routes.Length);
        for (var index = 0; index < routes.Length; index++)
        {
            var entrance = index < 2;
            Assert.Equal(entrance ? new Point3D(1666 + index, 1247, 0) : new Point3D(5295 + index, 981, 0),
                routes[index].Source);
            Assert.Equal(entrance ? new Point3D(5300, 981, 0) : new Point3D(1666, 1247, 0),
                routes[index].Destination);
            Assert.Equal(entrance ? "Orc Fort - Dungeon Entrance" : "Dungeon - Return to Orc Fort", routes[index].Name);
        }
    }

    [SkippableFact]
    public void Ensure_CreatesSixOrdinaryPadsOnce_AndPreservesNeighborsAndUnrelatedItems()
    {
        TileDataRequirement.SkipIfMissing();
        Assert.Equal(6, OrcFortTeleporters.Routes.Length);
        var neighbor = Track(new Teleporter(new Point3D(100, 100, 0), Map.Felucca));
        neighbor.MoveToWorld(new Point3D(5296, 981, 0), Map.Felucca);
        var unrelated = Track(new Item(0xEED));
        unrelated.MoveToWorld(OrcFortTeleporters.Routes[0].Source, Map.Felucca);
        var pads = EnsureAndTrack(out var created);

        Assert.Equal(6, created);
        var reused = OrcFortTeleporters.Ensure(out created);
        Assert.Equal(0, created);
        for (var index = 0; index < pads.Length; index++)
        {
            var route = OrcFortTeleporters.Routes[index];
            var pad = Assert.IsType<Teleporter>(pads[index]);
            Assert.Same(pad, reused[index]);
            Assert.Equal(route.Source, pad.Location);
            Assert.Equal(route.Destination, pad.PointDest);
            Assert.Equal(route.Name, pad.Name);
            Assert.Same(Map.Felucca, pad.Map);
            Assert.Same(Map.Felucca, pad.MapDest);
            Assert.Equal(TeleporterFlags.Active, pad.Flags);
            Assert.False(pad.Visible);
            Assert.False(pad.Movable);
            Assert.Equal(TimeSpan.Zero, pad.Delay);
        }
        Assert.False(neighbor.Deleted);
        Assert.Equal(new Point3D(5296, 981, 0), neighbor.Location);
        Assert.Equal(new Point3D(100, 100, 0), neighbor.PointDest);
        Assert.False(unrelated.Deleted);
        Assert.Equal(OrcFortTeleporters.Routes[0].Source, unrelated.Location);
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void NativeWalkingAcrossEachPad_ReachesExactDestination_WithoutImmediateBounce(int index)
    {
        TileDataRequirement.SkipIfMissing();
        var pads = EnsureAndTrack(out _);
        var route = OrcFortTeleporters.Routes[index];
        Assert.True(Map.Felucca.CanFit(route.Source, 16, true, false));
        Assert.True(Map.Felucca.CanFit(route.Destination, 16, true, false));
        Assert.Contains(pads, pad => pad.Location == route.Destination && pad.Active);
        var player = Track(new Mobile { Player = true, Body = 0x190 });
        var direction = Approach(player, route.Source);

        Assert.False(player.Move(direction));

        Assert.Same(Map.Felucca, player.Map);
        Assert.Equal(route.Destination, player.Location);
        Assert.NotEqual(route.Source, player.Location);
    }

    [SkippableTheory]
    [InlineData("WrongDestination")]
    [InlineData("Inactive")]
    [InlineData("Subclass")]
    [InlineData("Duplicate")]
    public void ConflictAtLastPad_IsRejectedBeforeCreatingAnyEarlierMissingPads(string conflict)
    {
        TileDataRequirement.SkipIfMissing();
        var route = OrcFortTeleporters.Routes[^1];
        var conflicting = Track(conflict == "Subclass"
            ? new SkillTeleporter()
            : new Teleporter(route.Destination, Map.Felucca));
        conflicting.Name = route.Name;
        conflicting.MoveToWorld(route.Source, Map.Felucca);
        switch (conflict)
        {
            case "WrongDestination":
            {
                conflicting.PointDest = new Point3D(10, 10, 0);
                break;
            }
            case "Inactive":
            {
                conflicting.Active = false;
                break;
            }
            case "Duplicate":
            {
                var duplicate = Track(new Teleporter(route.Destination, Map.Felucca) { Name = route.Name });
                duplicate.MoveToWorld(route.Source, Map.Felucca);
                break;
            }
        }
        var originalDestination = conflicting.PointDest;
        var originalFlags = conflicting.Flags;
        var unrelated = Track(new Item(0xEED));
        unrelated.MoveToWorld(OrcFortTeleporters.Routes[0].Source, Map.Felucca);

        var exception = Assert.Throws<InvalidOperationException>(() => OrcFortTeleporters.Ensure(out _));
        Assert.Contains("Conflicting", exception.Message);

        for (var index = 0; index < OrcFortTeleporters.Routes.Length - 1; index++)
        {
            foreach (var pad in Map.Felucca.GetItemsAt<Teleporter>(OrcFortTeleporters.Routes[index].Source))
            {
                Assert.Fail($"A missing pad was created before full preflight: {pad.Location}");
            }
        }
        Assert.False(conflicting.Deleted);
        Assert.Equal(route.Source, conflicting.Location);
        Assert.Equal(originalDestination, conflicting.PointDest);
        Assert.Equal(originalFlags, conflicting.Flags);
        Assert.False(unrelated.Deleted);
    }

    [SkippableFact]
    public void OrdinarySaveLoad_PreservesAllRoutes_AndEnsureReusesRestoredPads()
    {
        TileDataRequirement.SkipIfMissing();
        var pads = EnsureAndTrack(out _);
        var restored = new Teleporter[pads.Length];
        for (var index = 0; index < pads.Length; index++)
        {
            var writer = new BufferWriter(true);
            pads[index].Serialize(writer);
            var bytes = writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();
            pads[index].Delete();
            restored[index] = Track(new Teleporter(World.NewItem));
            var reader = new BufferReader(bytes);
            restored[index].Deserialize(reader);
            Assert.Equal(bytes.Length, reader.Position);
        }
        var reused = OrcFortTeleporters.Ensure(out var created);

        Assert.Equal(0, created);
        for (var index = 0; index < restored.Length; index++)
        {
            Assert.Same(restored[index], reused[index]);
            Assert.Equal(OrcFortTeleporters.Routes[index].Source, reused[index].Location);
            Assert.Equal(OrcFortTeleporters.Routes[index].Destination, reused[index].PointDest);
        }
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
        throw new InvalidOperationException($"No native walkable approach to teleporter at {source}.");
    }

    private Teleporter[] EnsureAndTrack(out int created)
    {
        var pads = OrcFortTeleporters.Ensure(out created);
        foreach (var pad in pads)
        {
            Track(pad);
        }
        return pads;
    }

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }
}
