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
public class OrcCaveSouthShiftTests : IDisposable
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
    public void Routes_PinTheExistingSerialsAndOnlyMoveOneTileSouth()
    {
        Assert.Equal(2, OrcCaveSouthShift.Routes.Length);
        for (var index = 0; index < 2; index++)
        {
            var route = OrcCaveSouthShift.Routes[index];
            Assert.Equal(1073771799u + (uint)index, route.Serial.Value);
            Assert.Equal(new Point3D(5279 + index, 1084, 0), route.Source);
            Assert.Equal(new Point3D(1404 + index, 1463, 0), route.Destination);
        }
    }

    [SkippableFact]
    public void Shift_PreservesBothItemsAndSerials_IsIdempotent_AndLeavesUnrelatedItems()
    {
        TileDataRequirement.SkipIfMissing();
        var pads = SeedPair();
        var unrelated = Track(new Item(0xEED));
        unrelated.MoveToWorld(new Point3D(5279, 1084, 0), Map.Felucca);
        var result = OrcCaveSouthShift.Ensure();
        Assert.Equal(2, result.Moved);
        for (var index = 0; index < 2; index++)
        {
            Assert.Same(pads[index], result.Teleporters[index]);
            Assert.Equal(OrcCaveSouthShift.Routes[index].Serial, pads[index].Serial);
            Assert.Equal(OrcCaveSouthShift.Routes[index].Source, pads[index].Location);
            Assert.Equal(TeleporterFlags.Active, pads[index].Flags);
            Assert.Equal(TimeSpan.Zero, pads[index].Delay);
            Assert.False(pads[index].Visible);
            Assert.False(pads[index].Movable);
        }
        var reused = OrcCaveSouthShift.Ensure();
        Assert.Equal(0, reused.Moved);
        Assert.Same(pads[0], reused.Teleporters[0]);
        Assert.Same(pads[1], reused.Teleporters[1]);
        Assert.False(unrelated.Deleted);
        Assert.Equal(new Point3D(5279, 1084, 0), unrelated.Location);
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void BothShiftedPads_AreWalkableFromSouth_AndArriveWithoutBounce(int index)
    {
        TileDataRequirement.SkipIfMissing();
        SeedPair();
        OrcCaveSouthShift.Ensure();
        var route = OrcCaveSouthShift.Routes[index];
        var approach = new Point3D(route.Source.X, 1085, 0);
        Assert.True(Map.Felucca.CanFit(approach, 16, true, false));
        var player = Track(new Mobile { Player = true, Body = 0x190, Direction = Direction.North });
        player.MoveToWorld(approach, Map.Felucca);
        Assert.True(Movement.CheckMovement(player, Direction.North, out var z));
        Assert.Equal(0, z);

        Assert.False(player.Move(Direction.North));

        Assert.Equal(route.Destination, player.Location);
        Assert.Same(Map.Felucca, player.Map);
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConflictOrMixedPair_RejectsBeforeMovingEitherSerial(bool mixed)
    {
        TileDataRequirement.SkipIfMissing();
        var pads = SeedPair();
        if (mixed)
        {
            pads[1].MoveToWorld(OrcCaveSouthShift.Routes[1].Source, Map.Felucca);
        }
        else
        {
            var conflict = Track(new Teleporter(new Point3D(10, 10, 0), Map.Felucca));
            conflict.MoveToWorld(OrcCaveSouthShift.Routes[1].Source, Map.Felucca);
        }
        var first = pads[0].Location;
        var second = pads[1].Location;

        Assert.Throws<InvalidOperationException>(() => OrcCaveSouthShift.Ensure());

        Assert.Equal(first, pads[0].Location);
        Assert.Equal(second, pads[1].Location);
        Assert.Same(pads[0], World.FindItem(OrcCaveSouthShift.Routes[0].Serial));
        Assert.Same(pads[1], World.FindItem(OrcCaveSouthShift.Routes[1].Serial));
    }

    [SkippableFact]
    public void OrdinarySerialization_RestoresTheSamePinnedSerials_WithoutAnotherMove()
    {
        TileDataRequirement.SkipIfMissing();
        var pads = SeedPair();
        OrcCaveSouthShift.Ensure();
        var restored = new Teleporter[2];
        for (var index = 0; index < 2; index++)
        {
            var writer = new BufferWriter(true);
            pads[index].Serialize(writer);
            var bytes = writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();
            pads[index].Delete();
            restored[index] = Track(new Teleporter(OrcCaveSouthShift.Routes[index].Serial));
            var reader = new BufferReader(bytes);
            restored[index].Deserialize(reader);
            World.AddEntity(restored[index]);
            Assert.Equal(bytes.Length, reader.Position);
        }

        var result = OrcCaveSouthShift.Ensure();

        Assert.Equal(0, result.Moved);
        Assert.Same(restored[0], result.Teleporters[0]);
        Assert.Same(restored[1], result.Teleporters[1]);
    }

    private Teleporter[] SeedPair()
    {
        var pads = new Teleporter[2];
        for (var index = 0; index < 2; index++)
        {
            var route = OrcCaveSouthShift.Routes[index];
            Assert.Null(World.FindItem(route.Serial));
            var pad = Track(new Teleporter(route.Serial)
            {
                Name = OrcCaveSouthShift.RouteName,
                ItemID = 0x1BC3,
                Amount = 1,
                PointDest = route.Destination,
                MapDest = Map.Felucca,
                Flags = TeleporterFlags.Active,
                Delay = TimeSpan.Zero,
                SoundID = 0,
                Visible = false,
                Movable = false
            });
            World.AddEntity(pad);
            pad.MoveToWorld(new Point3D(route.Source.X, 1083, 0), Map.Felucca);
            pads[index] = pad;
        }
        return pads;
    }

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }
}
