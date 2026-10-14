using System;
using System.Collections.Generic;
using Server;
using Server.Engines.Labrynth;
using Server.Items;
using Server.Tests;
using Xunit;

namespace UOContent.Tests.Engines.Labrynth;

[Collection("Sequential UOContent Tests")]
public class OrcFortEastRampTests : IDisposable
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
    public void Cleanup_RemovesOnlyTwoSurplusPads_AndIsIdempotent()
    {
        TileDataRequirement.SkipIfMissing();
        var pads = Seed();
        var neighbor = Track(new Teleporter(new Point3D(1404, 1461, 0), Map.Felucca));
        neighbor.MoveToWorld(new Point3D(5279, 1085, 0), Map.Felucca);
        Assert.Equal(2, OrcFortEastRamp.Ensure());
        Assert.Equal(0, OrcFortEastRamp.Ensure());
        for (var index = 0; index < 2; index++)
        {
            Assert.Same(pads[index], World.FindItem(OrcFortEastRamp.PadSerials[index]));
            Assert.Equal(OrcFortEastRamp.PadPositions[index], pads[index].Location);
            Assert.Equal(new Point3D(1666, 1247, 0), pads[index].PointDest);
        }
        Assert.True(pads[2].Deleted);
        Assert.True(pads[3].Deleted);
        Assert.Null(World.FindItem(OrcFortEastRamp.PadSerials[2]));
        Assert.Null(World.FindItem(OrcFortEastRamp.PadSerials[3]));
        Assert.False(neighbor.Deleted);
        Assert.Equal(new Point3D(5279, 1085, 0), neighbor.Location);
    }

    [SkippableFact]
    public void LateConflict_RejectsCleanupWithoutDeletingOrMovingAnyPad()
    {
        TileDataRequirement.SkipIfMissing();
        var pads = Seed();
        pads[^1].PointDest = new Point3D(10, 10, 0);
        Assert.Throws<InvalidOperationException>(() => OrcFortEastRamp.Ensure());
        foreach (var item in pads)
        {
            Assert.False(item.Deleted);
            Assert.Same(item, World.FindItem(item.Serial));
        }
    }

    [SkippableTheory]
    [InlineData(984)]
    [InlineData(985)]
    public void BothNativeRampLanes_WalkEastDownToFortGroundAndBackWest(int y)
    {
        TileDataRequirement.SkipIfMissing();
        var player = Track(new Mobile { Player = true, Body = 0x190, Direction = Direction.East });
        Point3D[] path =
        [
            new(5285, y, 20), new(5286, y, 17), new(5287, y, 12), new(5288, y, 7),
            new(5289, y, 2), new(5290, y, 0), new(5291, y, 0)
        ];
        player.MoveToWorld(path[0], Map.Felucca);
        for (var index = 1; index < path.Length; index++)
        {
            Assert.True(Map.Felucca.CanFit(path[index], 16, true, false));
            Assert.True(player.Move(Direction.East));
            Assert.Equal(path[index], player.Location);
        }
        // The ground-level route passes beside the existing native ladder.
        player.Direction = Direction.South;
        if (y == 984)
        {
            Assert.True(player.Move(Direction.South));
        }
        player.Direction = Direction.East;
        for (var x = 5292; x <= 5296; x++)
        {
            Assert.True(player.Move(Direction.East));
            Assert.Equal(new Point3D(x, 985, 0), player.Location);
        }
        player.MoveToWorld(path[^1], Map.Felucca);
        player.Direction = Direction.West;
        for (var index = path.Length - 2; index >= 0; index--)
        {
            Assert.True(player.Move(Direction.West));
            Assert.Equal(path[index], player.Location);
        }
    }

    [SkippableFact]
    public void NorthExtension_IsConnectedToOriginalPlatform_AndSouthRampIsGone()
    {
        TileDataRequirement.SkipIfMissing();
        var player = Track(new Mobile { Player = true, Body = 0x190, Direction = Direction.North });
        player.MoveToWorld(new Point3D(5284, 982, 20), Map.Felucca);
        for (var y = 981; y >= 978; y--)
        {
            Assert.True(player.Move(Direction.North));
            Assert.Equal(new Point3D(5284, y, 20), player.Location);
        }
        player.Direction = Direction.South;
        for (var y = 979; y <= 982; y++)
        {
            Assert.True(player.Move(Direction.South));
            Assert.Equal(new Point3D(5284, y, 20), player.Location);
        }
        for (var x = 5284; x <= 5285; x++)
        {
            for (var y = 987; y <= 993; y++)
            {
                foreach (var tile in Map.Felucca.Tiles.GetStaticTiles(x, y))
                {
                    Assert.DoesNotContain(tile.ID, new[] { 0x740, 0x73E, 0x4A9, 0x4AC });
                }
            }
        }
    }

    private Teleporter[] Seed()
    {
        var pads = new Teleporter[OrcFortEastRamp.PadSerials.Length];
        for (var index = 0; index < pads.Length; index++)
        {
            Assert.Null(World.FindItem(OrcFortEastRamp.PadSerials[index]));
            var item = Track(new Teleporter(OrcFortEastRamp.PadSerials[index])
            {
                ItemID = 0x1BC3, Amount = 1, Name = "Dungeon - Return to Orc Fort",
                PointDest = new Point3D(1666, 1247, 0), MapDest = Map.Felucca,
                Flags = TeleporterFlags.Active, Delay = TimeSpan.Zero, SoundID = 0,
                Visible = false, Movable = false
            });
            World.AddEntity(item);
            item.MoveToWorld(OrcFortEastRamp.PadPositions[index], Map.Felucca);
            pads[index] = item;
        }
        return pads;
    }

    private T Track<T>(T item) where T : IEntity
    {
        _created.Add(item);
        return item;
    }
}
