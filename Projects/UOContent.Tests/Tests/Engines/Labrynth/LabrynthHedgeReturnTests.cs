using System;
using System.Collections.Generic;
using Server;
using Server.Engines.Labrynth;
using Server.Items;
using Server.Tests;
using Xunit;

namespace UOContent.Tests.Engines.Labrynth;

[Collection("Sequential UOContent Tests")]
public class LabrynthHedgeReturnTests : IDisposable
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
    public void Ensure_CreatesOrdinaryRouteOnce_AndPreservesNeighborAndUnrelatedItems()
    {
        TileDataRequirement.SkipIfMissing();
        var neighbor = Track(new Teleporter(new Point3D(5275, 787, 0), Map.Felucca));
        neighbor.MoveToWorld(new Point3D(5248, 784, 0), Map.Felucca);
        var unrelated = Track(new Item(0xEED));
        unrelated.MoveToWorld(LabrynthHedgeReturn.Source, Map.Felucca);

        var teleporter = Track(LabrynthHedgeReturn.Ensure(out var created));
        Assert.True(created);
        Assert.IsType<Teleporter>(teleporter);
        Assert.Equal(LabrynthHedgeReturn.Source, teleporter.Location);
        Assert.Equal(LabrynthHedgeReturn.Destination, teleporter.PointDest);
        Assert.Same(Map.Felucca, teleporter.Map);
        Assert.Same(Map.Felucca, teleporter.MapDest);
        Assert.Equal(LabrynthHedgeReturn.RouteName, teleporter.Name);
        Assert.Equal(TeleporterFlags.Active, teleporter.Flags);
        Assert.False(teleporter.Visible);
        Assert.False(teleporter.Movable);
        Assert.Equal(TimeSpan.Zero, teleporter.Delay);
        Assert.Same(teleporter, LabrynthHedgeReturn.Ensure(out created));
        Assert.False(created);
        Assert.False(neighbor.Deleted);
        Assert.Equal(new Point3D(5248, 784, 0), neighbor.Location);
        Assert.Equal(new Point3D(5275, 787, 0), neighbor.PointDest);
        Assert.False(unrelated.Deleted);
        Assert.Equal(LabrynthHedgeReturn.Source, unrelated.Location);
    }

    [SkippableFact]
    public void MoveOver_ReturnsToNativeHedgeTile_WithoutAnArrivalTeleporter()
    {
        TileDataRequirement.SkipIfMissing();
        Assert.True(Map.Felucca.CanFit(LabrynthHedgeReturn.Source, 16, true, false));
        Assert.True(Map.Felucca.CanFit(LabrynthHedgeReturn.Destination, 16, true, false));
        var teleporter = Track(LabrynthHedgeReturn.Ensure(out _));
        var player = Track(new Mobile { Player = true, Body = 0x190 });
        player.MoveToWorld(LabrynthHedgeReturn.Source, Map.Felucca);

        Assert.False(teleporter.OnMoveOver(player));

        Assert.Same(Map.Felucca, player.Map);
        Assert.Equal(LabrynthHedgeReturn.Destination, player.Location);
        foreach (var arrival in Map.Felucca.GetItemsAt<Teleporter>(player.Location))
        {
            Assert.False(arrival.Active);
        }
        Assert.Equal(6, player.Y - 908);
    }

    [Theory]
    [InlineData("WrongDestination")]
    [InlineData("Inactive")]
    [InlineData("Visible")]
    [InlineData("Movable")]
    [InlineData("Delay")]
    [InlineData("Subclass")]
    [InlineData("Duplicate")]
    public void ConflictingRoute_IsRejectedWithoutDeletingOrChangingAnything(string conflict)
    {
        var teleporter = Track(conflict == "Subclass"
            ? new SkillTeleporter()
            : new Teleporter(LabrynthHedgeReturn.Destination, Map.Felucca));
        teleporter.Name = LabrynthHedgeReturn.RouteName;
        teleporter.MoveToWorld(LabrynthHedgeReturn.Source, Map.Felucca);
        switch (conflict)
        {
            case "WrongDestination":
            {
                teleporter.PointDest = new Point3D(10, 10, 0);
                break;
            }
            case "Inactive":
            {
                teleporter.Active = false;
                break;
            }
            case "Visible":
            {
                teleporter.Visible = true;
                break;
            }
            case "Movable":
            {
                teleporter.Movable = true;
                break;
            }
            case "Delay":
            {
                teleporter.Delay = TimeSpan.FromSeconds(1);
                break;
            }
            case "Duplicate":
            {
                var duplicate = Track(new Teleporter(LabrynthHedgeReturn.Destination, Map.Felucca)
                {
                    Name = LabrynthHedgeReturn.RouteName
                });
                duplicate.MoveToWorld(LabrynthHedgeReturn.Source, Map.Felucca);
                break;
            }
        }
        var originalDestination = teleporter.PointDest;
        var originalFlags = teleporter.Flags;
        var originalDelay = teleporter.Delay;
        var unrelated = Track(new Item(0xEED));
        unrelated.MoveToWorld(LabrynthHedgeReturn.Source, Map.Felucca);

        Assert.Throws<InvalidOperationException>(() => LabrynthHedgeReturn.Ensure(out _));

        Assert.False(teleporter.Deleted);
        Assert.Equal(LabrynthHedgeReturn.Source, teleporter.Location);
        Assert.Equal(originalDestination, teleporter.PointDest);
        Assert.Equal(originalFlags, teleporter.Flags);
        Assert.Equal(originalDelay, teleporter.Delay);
        Assert.False(unrelated.Deleted);
        Assert.Equal(LabrynthHedgeReturn.Source, unrelated.Location);
    }

    [SkippableFact]
    public void OrdinaryWorldSerialization_RestoresExactRoute_AndEnsureReusesIt()
    {
        TileDataRequirement.SkipIfMissing();
        var teleporter = Track(LabrynthHedgeReturn.Ensure(out _));
        var writer = new BufferWriter(true);
        teleporter.Serialize(writer);
        var bytes = writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();
        teleporter.Delete();
        var restored = Track(new Teleporter(World.NewItem));
        var reader = new BufferReader(bytes);
        restored.Deserialize(reader);

        Assert.Equal(bytes.Length, reader.Position);
        Assert.Same(restored, LabrynthHedgeReturn.Ensure(out var created));
        Assert.False(created);
        var player = Track(new Mobile { Player = true, Body = 0x190 });
        player.MoveToWorld(LabrynthHedgeReturn.Source, Map.Felucca);
        Assert.False(restored.OnMoveOver(player));
        Assert.Equal(LabrynthHedgeReturn.Destination, player.Location);
    }

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }
}
