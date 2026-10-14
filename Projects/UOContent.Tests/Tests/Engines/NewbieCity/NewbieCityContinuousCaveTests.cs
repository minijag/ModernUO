using System;
using System.IO;
using System.Text.Json;
using Server;
using Server.Mobiles;
using Server.Tests;
using Xunit;

namespace UOContent.Tests.Engines.NewbieCity;

[Collection("Sequential UOContent Tests")]
public class NewbieCityContinuousCaveTests
{
    [Theory]
    [InlineData(5590, false)]
    [InlineData(5590, true)]
    [InlineData(5589, false)]
    [InlineData(5589, true)]
    public void PlayerAndSheepWalkDirectlyFromCityThroughMouthIntoCaveAndBack(int x, bool sheep)
    {
        Assert.True(TestServerInitializer.TileDataLoaded);
        Mobile mobile = sheep ? new Sheep() : new PlayerMobile { Player = true, Young = true, Body = 0x190 };
        if (!ReferenceEquals(World.FindEntity(mobile.Serial), mobile))
        {
            World.AddEntity(mobile);
        }
        try
        {
            mobile.MoveToWorld(new Point3D(x, 1325, 0), Map.Felucca);
            mobile.Direction = Direction.North;
            for (var y = 1324; y >= 1314; y--)
            {
                Assert.True(mobile.Move(Direction.North), $"Cannot walk directly north into cave at {x},{y},0.");
                Assert.Equal(new Point3D(x, y, 0), mobile.Location);
                Assert.Same(Map.Felucca, mobile.Map);
            }
            mobile.Direction = Direction.South;
            for (var y = 1315; y <= 1325; y++)
            {
                Assert.True(mobile.Move(Direction.South), $"Cannot walk directly south out of cave at {x},{y},0.");
                Assert.Equal(new Point3D(x, y, 0), mobile.Location);
            }
        }
        finally
        {
            mobile.Delete();
        }
    }

    [Fact]
    public void EveryRequiredNativeCaveFloorTileHasStandingClearance()
    {
        Assert.True(TestServerInitializer.TileDataLoaded);
        var fixture = Environment.GetEnvironmentVariable("MODERNUO_TEST_DATA_DIR");
        Assert.False(string.IsNullOrEmpty(fixture));
        using var report = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixture, "verification.json")));
        var stageId = report.RootElement.GetProperty("stageId").GetString();
        Assert.True(stageId is "newbie-city-continuous-cave-v1" or "newbie-city-roofed-cave-v1" or "newbie-city-cave-mouth-v1" or "newbie-city-cave-galven-v1");
        foreach (var tile in report.RootElement.GetProperty("requiredStandingTiles").EnumerateArray())
        {
            var point = new Point3D(tile[0].GetInt32(), tile[1].GetInt32(), tile[2].GetInt32());
            Assert.True(Map.Felucca.CanFit(point, 16, true, false), $"Cave floor lacks clearance at {point}.");
            if (stageId == "newbie-city-roofed-cave-v1" && point.Y <= 1323)
            {
                Assert.True(Map.Felucca.Tiles.GetLandTile(point.X, point.Y).Z >= 16,
                    $"The inside height cutoff must reveal the cave floor and mouth at {point}.");
            }
            if (stageId == "newbie-city-roofed-cave-v1" && point.Y <= 1321)
            {
                var land = Map.Felucca.Tiles.GetLandTile(point.X, point.Y);
                Map.Felucca.GetAverageZ(point.X, point.Y, out var low, out var average, out _);
                Assert.False(land.Ignored);
                Assert.True(low >= point.Z + 20 && average >= point.Z + 16,
                    $"Cave floor needs intact overhead terrain and client cutaway clearance at {point}.");
            }
        }
        if (stageId is "newbie-city-cave-mouth-v1" or "newbie-city-cave-galven-v1")
        {
            foreach (var roof in report.RootElement.GetProperty("roofCoverage").GetProperty("entries").EnumerateArray())
            {
                var x = roof.GetProperty("x").GetInt32();
                var y = roof.GetProperty("y").GetInt32();
                var land = Map.Felucca.Tiles.GetLandTile(x, y);
                Map.Felucca.GetAverageZ(x, y, out var low, out var average, out _);
                Assert.False(land.Ignored);
                Assert.True(low >= 20 && average >= 16, $"Intact mountain clearance required at {x},{y}.");
            }
            foreach (var tile in report.RootElement.GetProperty("mouthWalkingTiles").EnumerateArray())
            {
                var land = Map.Felucca.Tiles.GetLandTile(tile[0].GetInt32(), tile[1].GetInt32());
                Assert.True(land.Ignored && land.Z >= 20);
            }
        }
    }
}
