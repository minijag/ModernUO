using System;
using Server;
using Server.Mobiles;
using Server.Tests;
using Xunit;

namespace UOContent.Tests.Engines.NewbieCity;

[Collection("Sequential UOContent Tests")]
public class NewbieCityBankTreasuryTests
{
    [Fact]
    public void NativeBankerAndThreeWideCustomerAisle_RemainClearAtGroundLevel()
    {
        Assert.True(TestServerInitializer.TileDataLoaded);
        for (var x = 5591; x <= 5593; x++)
        {
            for (var y = 1333; y <= 1337; y++)
            {
                Assert.True(Map.Felucca.CanFit(x, y, 0, 16, true, false), $"Bank approach obstructed at {x},{y},0.");
            }
        }
        foreach (var x in new[] { 5591, 5592 })
        {
            Assert.True(Map.Felucca.CanFit(x, 1338, 0, 16, true, false));
            Assert.True(Map.Felucca.CanFit(x, 1339, 0, 16, true, false));
        }
    }

    [Theory]
    [InlineData(5591)]
    [InlineData(5592)]
    public void PlayerWalksThroughEitherDoorAndAcrossTheRugToBankingRange(int entranceX)
    {
        Assert.True(TestServerInitializer.TileDataLoaded);
        var player = new PlayerMobile { Player = true, Body = 0x190, Direction = Direction.North };
        if (!ReferenceEquals(World.FindEntity(player.Serial), player))
        {
            World.AddEntity(player);
        }
        try
        {
            player.MoveToWorld(new Point3D(entranceX, 1339, 0), Map.Felucca);
            for (var y = 1338; y >= 1334; y--)
            {
                Assert.True(player.Move(Direction.North));
                Assert.Equal(new Point3D(entranceX, y, 0), player.Location);
            }
            Assert.True(player.InRange(new Point3D(5592, 1333, 0), 1));
            player.Direction = Direction.South;
            for (var y = 1335; y <= 1339; y++)
            {
                Assert.True(player.Move(Direction.South));
                Assert.Equal(new Point3D(entranceX, y, 0), player.Location);
            }
        }
        finally
        {
            player.Delete();
        }
    }
}
