using System;
using System.Collections.Generic;
using Server;
using Server.Engines.Labrynth;
using Server.Items;
using Server.Mobiles;
using Server.Tests;
using Xunit;

namespace UOContent.Tests.Engines.NewbieCity;

[Collection("Sequential UOContent Tests")]
public class NewbieCityGalvenGateTests : IDisposable
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
    public void Ensure_CreatesOneBlueStockGate_RepeatedEnsurePreservesItsExactSerialAndNeighbors()
    {
        var decoration = Track(new Item(0xEED));
        decoration.MoveToWorld(NewbieCityGalvenGate.Source, Map.Felucca);
        var neighbor = Track(new Teleporter(Point3D.Zero, Map.Felucca));
        neighbor.MoveToWorld(new Point3D(1317, 1379, 0), Map.Felucca);
        var gate = Track(NewbieCityGalvenGate.Ensure(out var created, false));
        var serial = gate.Serial;

        Assert.True(created);
        AssertGate(gate);
        Assert.Same(gate, NewbieCityGalvenGate.Ensure(out created, false));
        Assert.False(created);
        Assert.Equal(serial, gate.Serial);
        Assert.Same(gate, World.FindItem(serial));
        AssertOnlyGate(gate);
        AssertNoArrivalTrigger();
        Assert.False(decoration.Deleted);
        Assert.False(neighbor.Deleted);
        var writer = new BufferWriter(true);
        gate.Serialize(writer);
        var bytes = writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();
        gate.Delete();
        var restored = Track(new Teleporter(serial));
        var reader = new BufferReader(bytes);
        restored.Deserialize(reader);
        Assert.Equal(bytes.Length, reader.Position);
        Assert.Same(restored, NewbieCityGalvenGate.Ensure(out created, false));
        Assert.False(created);
        Assert.Equal(serial, restored.Serial);
        AssertGate(restored);
    }

    [Theory]
    [InlineData("Destination")]
    [InlineData("Delay")]
    [InlineData("Hue")]
    [InlineData("Invisible")]
    [InlineData("Inactive")]
    [InlineData("Duplicate")]
    public void ConflictingSource_IsRefusedWithoutDeletingOrChangingExistingObjects(string conflict)
    {
        var gate = ExactGate();
        switch (conflict)
        {
            case "Destination":
                gate.PointDest = new Point3D(1, 1, 0);
                break;
            case "Delay":
                gate.Delay = TimeSpan.FromSeconds(1);
                break;
            case "Hue":
                gate.Hue = 0;
                break;
            case "Invisible":
                gate.Visible = false;
                break;
            case "Inactive":
                gate.Active = false;
                break;
            case "Duplicate":
                ExactGate();
                break;
        }
        var target = gate.PointDest;
        var hue = gate.Hue;
        var flags = gate.Flags;

        Assert.Throws<InvalidOperationException>(() => NewbieCityGalvenGate.Ensure(out _, false));

        Assert.False(gate.Deleted);
        Assert.Equal(NewbieCityGalvenGate.Source, gate.Location);
        Assert.Equal(target, gate.PointDest);
        Assert.Equal(hue, gate.Hue);
        Assert.Equal(flags, gate.Flags);
        AssertNoArrivalTrigger();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnexpectedArrivalTrigger_IsRefusedBeforeCreatingTheCaveGate(bool moongate)
    {
        var conflict = Track<Item>(moongate
            ? new Moongate(NewbieCityGalvenGate.Source, Map.Felucca, false)
            : new Teleporter(NewbieCityGalvenGate.Source, Map.Felucca));
        conflict.MoveToWorld(NewbieCityGalvenGate.Destination, Map.Felucca);

        Assert.Throws<InvalidOperationException>(() => NewbieCityGalvenGate.Ensure(out _, false));

        Assert.False(conflict.Deleted);
        Assert.Equal(NewbieCityGalvenGate.Destination, conflict.Location);
        foreach (var gate in Map.Felucca.GetItemsAt<Teleporter>(NewbieCityGalvenGate.Source))
        {
            Assert.Fail($"Unexpected cave gate {gate.Serial}.");
        }
    }

    [Fact]
    public void NativeCityApproachReachesCave_YoungPlayerWalksThroughWithFollowingPet_WithoutReturnGate()
    {
        Assert.True(TestServerInitializer.TileDataLoaded);
        Assert.True(Map.Felucca.CanFit(NewbieCityGalvenGate.Source, 16, true, false));
        Assert.True(Map.Felucca.CanFit(NewbieCityGalvenGate.Destination, 16, true, false));
        var reachable = ReachableFromCity();
        Assert.Contains(new Point2D(5590, 1324), reachable);
        Assert.Contains(new Point2D(5590, 1323), reachable);
        var gate = Track(NewbieCityGalvenGate.Ensure(out var created));
        Assert.True(created);
        var player = Track(new PlayerMobile { Player = true, Young = true, Body = 0x190, Direction = Direction.North });
        player.MoveToWorld(new Point3D(5590, 1324, 0), Map.Felucca);
        var pet = Track(new Sheep());
        Assert.True(pet.SetControlMaster(player));
        pet.ControlOrder = OrderType.Follow;
        pet.MoveToWorld(new Point3D(player.X, player.Y + 1, player.Z), Map.Felucca);
        var wild = Track(new Sheep());
        wild.MoveToWorld(new Point3D(player.X + 1, player.Y, player.Z), Map.Felucca);
        var wildLocation = wild.Location;

        Assert.True(gate.CanTeleport(player));
        Assert.False(player.Move(Direction.North));

        Assert.True(player.Young);
        Assert.Equal(new Point3D(1317, 1380, 0), player.Location);
        Assert.Same(Map.Felucca, player.Map);
        Assert.Equal(player.Location, pet.Location);
        Assert.Same(player.Map, pet.Map);
        Assert.Equal(wildLocation, wild.Location);
        Assert.Equal(new Point3D(5590, 1323, 0), gate.Location);
        AssertNoArrivalTrigger();
        Assert.True(gate.OnMoveOver(wild));
    }

    private static HashSet<Point2D> ReachableFromCity()
    {
        var start = new Point2D(5592, 1352);
        var reached = new HashSet<Point2D> { start };
        var examined = new HashSet<Point2D> { start };
        var queue = new Queue<Point2D>();
        queue.Enqueue(start);
        (int X, int Y)[] directions = [(-1, 0), (1, 0), (0, -1), (0, 1)];
        Assert.True(Map.Felucca.CanFit(new Point3D(start.X, start.Y, 0), 16, true, false));
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var direction in directions)
            {
                var next = new Point2D(current.X + direction.X, current.Y + direction.Y);
                if (next.X is < 5574 or > 5610 || next.Y is < 1318 or > 1380 || !examined.Add(next) ||
                    !Map.Felucca.CanFit(new Point3D(next.X, next.Y, 0), 16, true, false))
                {
                    continue;
                }
                reached.Add(next);
                queue.Enqueue(next);
            }
        }
        return reached;
    }
    private Teleporter ExactGate()
    {
        var gate = Track(new Teleporter(NewbieCityGalvenGate.Destination, Map.Felucca)
        {
            Name = NewbieCityGalvenGate.GateName,
            ItemID = 0xF6C,
            Hue = NewbieCityGalvenGate.BlueHue,
            Visible = true,
            Light = LightType.Circle300,
            SoundID = 0x1FE
        });
        gate.MoveToWorld(NewbieCityGalvenGate.Source, Map.Felucca);
        return gate;
    }

    private static void AssertGate(Teleporter gate)
    {
        Assert.IsType<Teleporter>(gate);
        Assert.Equal(NewbieCityGalvenGate.Source, gate.Location);
        Assert.Equal(NewbieCityGalvenGate.Destination, gate.PointDest);
        Assert.Equal(NewbieCityGalvenGate.GateName, gate.Name);
        Assert.Same(Map.Felucca, gate.Map);
        Assert.Same(Map.Felucca, gate.MapDest);
        Assert.Equal(0xF6C, gate.ItemID);
        Assert.Equal(NewbieCityGalvenGate.BlueHue, gate.Hue);
        Assert.Equal(LightType.Circle300, gate.Light);
        Assert.Equal(TeleporterFlags.Active, gate.Flags);
        Assert.Equal(0x1FE, gate.SoundID);
        Assert.Equal(TimeSpan.Zero, gate.Delay);
        Assert.True(gate.Visible);
        Assert.False(gate.Movable);
    }

    private static void AssertOnlyGate(Teleporter expected)
    {
        var count = 0;
        foreach (var gate in Map.Felucca.GetItemsAt<Teleporter>(NewbieCityGalvenGate.Source))
        {
            Assert.Same(expected, gate);
            count++;
        }
        Assert.Equal(1, count);
    }

    private static void AssertNoArrivalTrigger()
    {
        foreach (var gate in Map.Felucca.GetItemsAt<Teleporter>(NewbieCityGalvenGate.Destination))
        {
            Assert.Fail($"Unexpected Galven arrival gate {gate.Serial}.");
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
}
