using Server;
using Server.Tests;
using Xunit;

namespace UOContent.Tests.Engines.Labrynth;

[Collection("Sequential UOContent Tests")]
public class DockNorthShoreMapTests
{
    [SkippableFact]
    public void NorthBank_UsesExactDirectionalNativeShoreAssemblies()
    {
        TileDataRequirement.SkipIfMissing();
        foreach (var join in new[]
        {
            (1784, 925, 579, 911), (1784, 926, 579, 912),
            (1784, 927, 579, 913), (1785, 927, 580, 913),
            (1785, 928, 580, 914), (1784, 929, 578, 912),
            (1785, 929, 579, 912)
        })
        {
            var target = Map.Felucca.Tiles.GetLandTile(join.Item1, join.Item2);
            var reference = Map.Felucca.Tiles.GetLandTile(join.Item3, join.Item4);
            Assert.Equal(reference.ID, target.ID);
            Assert.Equal(reference.Z, target.Z);
            var expectedWaterCount = 0;
            foreach (var tile in Map.Felucca.Tiles.GetStaticTiles(join.Item3, join.Item4))
            {
                if (tile.ID is >= 0x1796 and <= 0x17B5)
                {
                    ++expectedWaterCount;
                    Assert.True(HasStatic(join.Item1, join.Item2, tile.ID, tile.Z));
                }
            }
            Assert.Equal(expectedWaterCount, WaterCount(join.Item1, join.Item2));
        }
        Assert.Equal(0xA2, Map.Felucca.Tiles.GetLandTile(1785, 927).ID);
        Assert.True(HasStatic(1784, 928, 0x17A9, -5));
        Assert.Equal(1, WaterCount(1784, 928));
    }

    [SkippableFact]
    public void NorthBank_PreservesTheSeventeenOceanCellsAndDockCorner()
    {
        TileDataRequirement.SkipIfMissing();
        for (var x = 1780; x <= 1783; ++x)
        {
            for (var y = 925; y <= 928; ++y)
            {
                AssertOcean(x, y);
                Assert.Equal(0, WaterCount(x, y));
            }
        }
        AssertOcean(1784, 928);
        Assert.True(HasStatic(1780, 927, 0x7CE, 0));
        Assert.True(HasStatic(1780, 927, 0x3AE, -5));
        Assert.True(HasStatic(1780, 927, 0x3AC, 1));
        Assert.True(HasStatic(1780, 928, 0x7D0, 0));
        Assert.True(HasStatic(1780, 928, 0x3A9, 1));
        Assert.True(HasStatic(1787, 928, 0xDB9, 0));
    }

    [SkippableFact]
    public void GrassBehindNorthBank_HasSupportedLevelWalkableGround()
    {
        TileDataRequirement.SkipIfMissing();
        foreach (var point in new[] { (1785, 924), (1786, 924), (1785, 925), (1785, 926) })
        {
            var land = Map.Felucca.Tiles.GetLandTile(point.Item1, point.Item2);
            Assert.InRange(land.ID, 3, 6);
            Assert.Equal(0, land.Z);
        }
        Assert.True(Map.Felucca.CanFit(1786, 925, 0, 16, true, false));
        Assert.True(Map.Felucca.CanFit(1786, 926, 0, 16, true, false));
    }

    private static void AssertOcean(int x, int y)
    {
        var land = Map.Felucca.Tiles.GetLandTile(x, y);
        Assert.InRange(land.ID, 0xA8, 0xAB);
        Assert.Equal(-5, land.Z);
    }

    private static int WaterCount(int x, int y)
    {
        var count = 0;
        foreach (var tile in Map.Felucca.Tiles.GetStaticTiles(x, y))
        {
            if (tile.ID is >= 0x1796 and <= 0x17B5)
            {
                ++count;
            }
        }
        return count;
    }

    private static bool HasStatic(int x, int y, int id, int z)
    {
        foreach (var tile in Map.Felucca.Tiles.GetStaticTiles(x, y))
        {
            if (tile.ID == id && tile.Z == z)
            {
                return true;
            }
        }
        return false;
    }
}
