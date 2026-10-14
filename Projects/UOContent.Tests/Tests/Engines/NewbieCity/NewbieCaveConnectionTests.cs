using System;
using System.Collections.Generic;
using Server;
using Server.Engines.Labrynth;
using Server.Items;
using Server.Tests;
using Xunit;

namespace UOContent.Tests.Engines.NewbieCity;

[Collection("Sequential UOContent Tests")]
public class NewbieCaveConnectionTests : IDisposable
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
    public void Apply_RelocatesOnlyTheFourOriginalStockPads_AndRepeatPreservesTheirSerials()
    {
        TileDataRequirement.SkipIfMissing();
        var pads = SeedOldPads();
        uint[] expectedSerials = [1073772332, 1073772333, 1073772334, 1073772335];
        Assert.Equal(4, NewbieCaveConnection.Routes.Length);

        Assert.Equal(4, NewbieCaveConnection.Apply(pads, false));
        Assert.Equal(0, NewbieCaveConnection.Apply(pads, false));

        for (var index = 0; index < pads.Length; index++)
        {
            var route = NewbieCaveConnection.Routes[index];
            var pad = pads[index];
            Assert.Equal(expectedSerials[index], route.Serial);
            Assert.Equal(route.Serial, pad.Serial.Value);
            Assert.IsType<Teleporter>(pad);
            Assert.False(pad.Deleted);
            Assert.Equal(route.Source, pad.Location);
            Assert.Equal(route.Destination, pad.PointDest);
            Assert.Equal(0, pad.Location.Z);
            Assert.Equal(0, pad.PointDest.Z);
            Assert.Equal("Newbie City - " + route.Id, pad.Name);
            Assert.Equal(TeleporterFlags.Active, pad.Flags);
            Assert.False(pad.Visible);
            Assert.False(pad.Movable);
            Assert.Same(Map.Felucca, pad.Map);
            Assert.Same(Map.Felucca, pad.MapDest);
            AssertOnlyPadAt(route.Source, pad);
        }
        Assert.Equal(4, _created.Count);
    }

    [SkippableFact]
    public void LateDestinationConflict_RefusesBeforeMovingAnyOriginalPad()
    {
        TileDataRequirement.SkipIfMissing();
        var pads = SeedOldPads();
        var route = NewbieCaveConnection.Routes[^1];
        var conflict = Track(new Teleporter(new Point3D(1000, 1000, 0), Map.Felucca));
        conflict.MoveToWorld(route.Source, Map.Felucca);

        Assert.Throws<InvalidOperationException>(() => NewbieCaveConnection.Apply(pads, false));

        AssertOldPadsUnchanged(pads);
        Assert.False(conflict.Deleted);
        Assert.Equal(route.Source, conflict.Location);
    }

    [SkippableFact]
    public void WrongLastSerial_RefusesBeforeUpdatingAnyEarlierPad()
    {
        TileDataRequirement.SkipIfMissing();
        var pads = SeedOldPads();
        var route = NewbieCaveConnection.Routes[^1];
        pads[^1].Delete();
        pads[^1] = CreateOldPad(route, route.Serial + 100);

        Assert.Throws<InvalidOperationException>(() => NewbieCaveConnection.Apply(pads, false));

        AssertOldPadsUnchanged(pads);
        Assert.Equal(route.Serial + 100, pads[^1].Serial.Value);
    }

    [SkippableFact]
    public void NativeEndpoints_HaveGroundZeroClearanceUnderTheRestoredIgnoredCaveLand()
    {
        TileDataRequirement.SkipIfMissing();
        foreach (var route in NewbieCaveConnection.Routes)
        {
            Assert.True(Map.Felucca.CanFit(route.Source, 16, true, false));
            Assert.True(Map.Felucca.CanFit(route.Destination, 16, true, false));
        }
        var floor = TileData.ItemTable[0x053B];
        Assert.Equal(0, floor.Height);
        Assert.NotEqual(TileFlag.None, floor.Flags & TileFlag.Surface);
        foreach (var y in new[] { 1612, 1613 })
        {
            var land = Map.Felucca.Tiles.GetLandTile(721, y);
            Assert.Equal(0x1DB, land.ID);
            Assert.Equal(10, land.Z);
            var count = 0;
            foreach (var tile in Map.Felucca.Tiles.GetStaticTiles(721, y))
            {
                if (tile.ID == 0x053B && tile.Z == 0 && tile.Hue == 0)
                {
                    count++;
                }
            }
            Assert.Equal(1, count);
            var approach = Map.Felucca.Tiles.GetLandTile(722, y);
            Assert.Equal(0x0078, approach.ID);
            Assert.Equal(0, approach.Z);
            Assert.True(Map.Felucca.CanFit(new Point3D(722, y, 0), 16, true, false));
        }
        var pads = SeedOldPads();
        Assert.Equal(4, NewbieCaveConnection.Apply(pads));
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(2)]
    public void NativeWalking_CityToGroundZeroCaveAndBackUsesBothPadsWithoutAutomaticBounce(int index)
    {
        TileDataRequirement.SkipIfMissing();
        var pads = SeedOldPads();
        NewbieCaveConnection.Apply(pads);
        var route = NewbieCaveConnection.Routes[index];
        var player = Track(new Mobile { Player = true, Body = 0x190, Direction = Direction.West });
        player.MoveToWorld(new Point3D(5579, route.Source.Y, 0), Map.Felucca);

        Assert.False(player.Move(Direction.West));

        Assert.Equal(route.Destination, player.Location);
        Assert.Equal(new Point3D(721, index == 0 ? 1612 : 1613, 0), player.Location);
        player.Direction = Direction.East;
        Assert.True(player.Move(Direction.East));
        Assert.Equal(new Point3D(722, route.Destination.Y, 0), player.Location);
        player.Direction = Direction.West;

        Assert.False(player.Move(Direction.West));

        Assert.Equal(route.Source, player.Location);
        Assert.Same(Map.Felucca, player.Map);
    }

    private Teleporter[] SeedOldPads()
    {
        var pads = new Teleporter[NewbieCaveConnection.Routes.Length];
        for (var index = 0; index < pads.Length; index++)
        {
            var route = NewbieCaveConnection.Routes[index];
            pads[index] = CreateOldPad(route, route.Serial);
        }
        return pads;
    }

    private Teleporter CreateOldPad(NewbieCaveConnection.Route route, uint serial)
    {
        var pad = Track(new Teleporter((Serial)serial)
        {
            ItemID = 0x1BC3,
            Flags = TeleporterFlags.Active,
            Name = "Newbie City - " + route.Id,
            PointDest = route.OldDestination,
            MapDest = Map.Felucca,
            Visible = false,
            Movable = false
        });
        pad.MoveToWorld(route.OldSource, Map.Felucca);
        return pad;
    }

    private static void AssertOldPadsUnchanged(Teleporter[] pads)
    {
        for (var index = 0; index < pads.Length; index++)
        {
            var route = NewbieCaveConnection.Routes[index];
            Assert.False(pads[index].Deleted);
            Assert.Equal(route.OldSource, pads[index].Location);
            Assert.Equal(route.OldDestination, pads[index].PointDest);
            Assert.Same(Map.Felucca, pads[index].Map);
            Assert.Same(Map.Felucca, pads[index].MapDest);
        }
    }

    private static void AssertOnlyPadAt(Point3D point, Teleporter expected)
    {
        var count = 0;
        foreach (var pad in Map.Felucca.GetItemsAt<Teleporter>(point))
        {
            if (pad.Location == point)
            {
                Assert.Same(expected, pad);
                count++;
            }
        }
        Assert.Equal(1, count);
    }

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }
}
