using System.Collections.Generic;
using Server;
using Server.Tests;
using Xunit;

namespace UOContent.Tests.Engines.Labrynth;

[Collection("Sequential UOContent Tests")]
public class ArborShoreSmoothMapTests
{
    [SkippableFact]
    public void ReportedBankJoins_UseCompleteNativeDirectionalAssemblies()
    {
        TileDataRequirement.SkipIfMissing();

        AssertNativeAssembly(1792, 940, 585, 925);
        AssertNativeAssembly(1823, 973, 618, 994);
        AssertNativeAssembly(1832, 979, 649, 907);
        Assert.Equal(0x96, Map.Felucca.Tiles.GetLandTile(1792, 940).ID);
        Assert.Equal(-10, Map.Felucca.Tiles.GetLandTile(1823, 973).Z);
    }

    [SkippableFact]
    public void ShoreSmoothing_PreservesHedgesAndFlatInlandGround()
    {
        TileDataRequirement.SkipIfMissing();
        foreach (var point in new[] { (1795, 940), (1827, 972), (1834, 979) })
        {
            var land = Map.Felucca.Tiles.GetLandTile(point.Item1, point.Item2);
            Assert.Equal(0, land.Z);
            var hedgeFound = false;
            foreach (var tile in Map.Felucca.Tiles.GetStaticTiles(point.Item1, point.Item2))
            {
                if (tile.ID is 0xDB8 or 0xDB9 && tile.Z == 0)
                {
                    hedgeFound = true;
                }
            }
            Assert.True(hedgeFound);
        }
        Assert.True(Map.Felucca.CanFit(1794, 940, 0, 16, true, false));
        Assert.True(Map.Felucca.CanFit(1824, 971, 0, 16, true, false));
        Assert.True(Map.Felucca.CanFit(1833, 979, 0, 16, true, false));
    }

    private static void AssertNativeAssembly(int x, int y, int nativeX, int nativeY)
    {
        var actualLand = Map.Felucca.Tiles.GetLandTile(x, y);
        var referenceLand = Map.Felucca.Tiles.GetLandTile(nativeX, nativeY);
        Assert.Equal(referenceLand.ID, actualLand.ID);
        Assert.Equal(referenceLand.Z, actualLand.Z);
        var actualWater = GetWater(x, y);
        var referenceWater = GetWater(nativeX, nativeY);
        Assert.Equal(referenceWater, actualWater);
    }

    private static List<(int ID, int Z, int Hue)> GetWater(int x, int y)
    {
        var result = new List<(int ID, int Z, int Hue)>();
        foreach (var tile in Map.Felucca.Tiles.GetStaticTiles(x, y))
        {
            if (tile.ID is >= 0x1796 and <= 0x17B5)
            {
                result.Add((tile.ID, tile.Z, tile.Hue));
            }
        }
        result.Sort();
        return result;
    }
}
