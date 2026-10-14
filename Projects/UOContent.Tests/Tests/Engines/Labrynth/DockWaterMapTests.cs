using Server;
using Server.Tests;
using Xunit;

namespace UOContent.Tests.Engines.Labrynth;

[Collection("Sequential UOContent Tests")]
public class DockWaterMapTests
{
    [SkippableFact]
    public void DockCornerWater_HasLevelNativeOceanAndNoObsoleteShoreOverlays()
    {
        TileDataRequirement.SkipIfMissing();

        for (var x = 1780; x <= 1783; x++)
        {
            for (var y = 925; y <= 928; y++)
            {
                AssertCleanWater(x, y);
            }
        }
        AssertCleanWater(1784, 928);
    }

    [SkippableFact]
    public void DockCorner_PreservesItsNativePlanksPierAndContinuousRope()
    {
        TileDataRequirement.SkipIfMissing();
        Assert.True(HasNativeStatic(1780, 927, 0x7CE, 0));
        Assert.True(HasNativeStatic(1780, 927, 0x3AE, -5));
        Assert.True(HasNativeStatic(1780, 927, 0x3AC, 1));
        Assert.True(HasNativeStatic(1780, 928, 0x7D0, 0));
        Assert.True(HasNativeStatic(1780, 928, 0x3A9, 1));
    }

    [SkippableFact]
    public void RemovedRockyShore_IsWaterNotHumanWalkableGround()
    {
        TileDataRequirement.SkipIfMissing();
        var land = Map.Felucca.Tiles.GetLandTile(1781, 926);
        Assert.True((TileData.LandTable[land.ID].Flags & TileFlag.Wet) != 0);
        Assert.False(Map.Felucca.CanFit(1781, 926, -5, 16, true, false));
    }

    [SkippableFact]
    public void RemovedGrayRock_RestoresGrassWithoutRemovingTheNeighboringHedge()
    {
        TileDataRequirement.SkipIfMissing();
        var land = Map.Felucca.Tiles.GetLandTile(1786, 928);
        Assert.Equal(0x5, land.ID);
        Assert.Equal(0, land.Z);
        Assert.True(Map.Felucca.CanFit(1786, 928, 0, 16, true, false));
        Assert.True(HasNativeStatic(1787, 928, 0xDB9, 0));
    }

    private static void AssertCleanWater(int x, int y)
    {
        var land = Map.Felucca.Tiles.GetLandTile(x, y);
        Assert.InRange(land.ID, 0xA8, 0xAB);
        Assert.Equal(-5, land.Z);
        foreach (var tile in Map.Felucca.Tiles.GetStaticTiles(x, y))
        {
            if (x == 1784 && y == 928 && tile.ID == 0x17A9 && tile.Z == -5)
            {
                continue;
            }
            Assert.False(tile.ID is >= 0x1796 and <= 0x17B5, $"Obsolete shore overlay remains at {x},{y},{tile.Z}.");
        }
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
