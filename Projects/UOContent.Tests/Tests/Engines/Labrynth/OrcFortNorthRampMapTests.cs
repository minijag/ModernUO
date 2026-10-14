using System;
using Server;
using Server.Tests;
using Xunit;

namespace UOContent.Tests.Engines.Labrynth;

[Collection("Sequential UOContent Tests")]
public class OrcFortNorthRampMapTests
{
    [SkippableTheory]
    [InlineData(978)]
    [InlineData(979)]
    public void BothNorthRampLanes_WalkEastDownAndBackWest(int y)
    {
        TileDataRequirement.SkipIfMissing();
        var player = new Mobile { Player = true, Body = 0x190, Direction = Direction.East };
        Point3D[] path =
        [
            new(5285, y, 20), new(5286, y, 17), new(5287, y, 12), new(5288, y, 7),
            new(5289, y, 2), new(5290, y, 0), new(5291, y, 0)
        ];
        try
        {
            player.MoveToWorld(path[0], Map.Felucca);
            for (var index = 1; index < path.Length; index++)
            {
                Assert.True(Map.Felucca.CanFit(path[index], 16, true, false));
                Assert.True(player.Move(Direction.East));
                Assert.Equal(path[index], player.Location);
            }
            player.Direction = Direction.West;
            for (var index = path.Length - 2; index >= 0; index--)
            {
                Assert.True(player.Move(Direction.West));
                Assert.Equal(path[index], player.Location);
            }
        }
        finally
        {
            player.Delete();
        }
    }

    [SkippableTheory]
    [InlineData(5290)]
    [InlineData(5291)]
    public void BothUnderpassLanes_WalkFromNorthLandingToFortGroundAndBack(int x)
    {
        TileDataRequirement.SkipIfMissing();
        var player = new Mobile { Player = true, Body = 0x190, Direction = Direction.South };
        try
        {
            player.MoveToWorld(new Point3D(x, 979, 0), Map.Felucca);
            for (var y = 980; y <= 985; y++)
            {
                Assert.True(Map.Felucca.CanFit(x, y, 0, 16, true, false));
                Assert.True(player.Move(Direction.South));
                Assert.Equal(new Point3D(x, y, 0), player.Location);
            }
            player.Direction = Direction.East;
            for (var east = x + 1; east <= 5296; east++)
            {
                Assert.True(player.Move(Direction.East));
                Assert.Equal(new Point3D(east, 985, 0), player.Location);
            }
            player.MoveToWorld(new Point3D(x, 985, 0), Map.Felucca);
            player.Direction = Direction.North;
            for (var y = 984; y >= 979; y--)
            {
                Assert.True(player.Move(Direction.North));
                Assert.Equal(new Point3D(x, y, 0), player.Location);
            }
        }
        finally
        {
            player.Delete();
        }
    }

    [SkippableFact]
    public void OriginalEastRampIsGone_AndSouthPadRowRemainsSupported()
    {
        TileDataRequirement.SkipIfMissing();
        for (var x = 5286; x <= 5289; x++)
        {
            for (var y = 984; y <= 985; y++)
            {
                foreach (var tile in Map.Felucca.Tiles.GetStaticTiles(x, y))
                {
                    Assert.NotEqual(0x73A, tile.ID);
                }
            }
        }
        Assert.True(Map.Felucca.CanFit(5284, 985, 20, 16, true, false));
        Assert.True(Map.Felucca.CanFit(5285, 985, 20, 16, true, false));
        Assert.False(Map.Felucca.CanFit(5286, 984, 20, 16, true, false));
        Assert.False(Map.Felucca.CanFit(5284, 986, 20, 16, true, false));
    }

    [SkippableFact]
    public void UpperCatwalkStillCrossesTheGroundLevelUnderpass()
    {
        TileDataRequirement.SkipIfMissing();
        var player = new Mobile { Player = true, Body = 0x190, Direction = Direction.East };
        try
        {
            player.MoveToWorld(new Point3D(5289, 982, 20), Map.Felucca);
            for (var x = 5290; x <= 5292; x++)
            {
                Assert.True(player.Move(Direction.East));
                Assert.Equal(new Point3D(x, 982, 20), player.Location);
            }
        }
        finally
        {
            player.Delete();
        }
    }
}
