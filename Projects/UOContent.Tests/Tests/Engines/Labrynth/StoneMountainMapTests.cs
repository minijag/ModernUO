using System;
using Server;
using Server.Tests;
using Xunit;

namespace UOContent.Tests.Engines.Labrynth;

[Collection("Sequential UOContent Tests")]
public class StoneMountainMapTests : IDisposable
{
    private Mobile _walker;

    public void Dispose()
    {
        _walker?.Delete();
        _walker = null;
    }

    [SkippableFact]
    public void RemovedMountain_RestoresNativeGrassAtGroundLevel()
    {
        TileDataRequirement.SkipIfMissing();
        var land = Map.Felucca.Tiles.GetLandTile(1848, 992);

        Assert.Equal(0x5, land.ID);
        Assert.Equal(0, land.Z);
        var staticCount = 0;
        foreach (var tile in Map.Felucca.Tiles.GetStaticTiles(1848, 992))
        {
            staticCount++;
        }
        Assert.Equal(0, staticCount);
        Assert.True(Map.Felucca.CanFit(1848, 992, 0, 16, true, false));
    }

    [SkippableFact]
    public void Neighbors_PreserveTheMazeHedgesAndRiverbank()
    {
        TileDataRequirement.SkipIfMissing();
        Assert.True(HasNativeStatic(1848, 991, 0xDB9, 0));
        Assert.True(HasNativeStatic(1847, 992, 0xDB9, 0));
        var riverbank = Map.Felucca.Tiles.GetLandTile(1848, 993);

        Assert.Equal(0x91, riverbank.ID);
        Assert.Equal(-10, riverbank.Z);
        Assert.True(HasNativeStatic(1848, 993, 0x17B2, -5));
    }

    [SkippableFact]
    public void NativeMovement_WalksOntoTheRestoredGroundAndBackWithoutClimbing()
    {
        TileDataRequirement.SkipIfMissing();
        var start = new Point3D(1849, 992, 0);
        Assert.True(Map.Felucca.CanFit(start, 16, true, false));
        _walker = new Mobile { Player = true, Body = 0x190 };
        _walker.MoveToWorld(start, Map.Felucca);
        _walker.Direction = Direction.West;

        Assert.True(_walker.Move(Direction.West));
        Assert.Equal(new Point3D(1848, 992, 0), _walker.Location);
        _walker.Direction = Direction.East;
        Assert.True(_walker.Move(Direction.East));
        Assert.Equal(start, _walker.Location);
    }

    private static bool HasNativeStatic(int x, int y, int itemId, int z)
    {
        foreach (var tile in Map.Felucca.Tiles.GetStaticTiles(x, y))
        {
            if (tile.ID == itemId && tile.Z == z)
            {
                return true;
            }
        }
        return false;
    }
}
