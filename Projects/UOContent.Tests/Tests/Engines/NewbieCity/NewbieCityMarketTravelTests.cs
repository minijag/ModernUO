using System;
using System.Collections.Generic;
using System.IO;
using Server;
using Server.Engines.Labrynth;
using Server.Items;
using Server.Tests;
using Xunit;

namespace UOContent.Tests.Engines.NewbieCity;

[Collection("Sequential UOContent Tests")]
public class NewbieCityMarketTravelTests : IDisposable
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
    public void Apply_MovesOnlyOriginalPadsToExactMarketRoutes_AndRepeatRetainsAllFourSerials()
    {
        TileDataRequirement.SkipIfMissing();
        var pads = SeedOldPads();
        uint[] serials = [1073772332, 1073772333, 1073772334, 1073772335];
        Point3D[] sources = [new(5608, 1352, 0), new(721, 1612, 0), new(5608, 1353, 0), new(721, 1613, 0)];
        Point3D[] destinations = [sources[1], sources[0], sources[3], sources[2]];
        Assert.Equal(4, NewbieCityMarketTravel.Routes.Length);

        Assert.Equal(4, NewbieCityMarketTravel.Apply(pads, false));
        Assert.Equal(0, NewbieCityMarketTravel.Apply(pads, false));

        for (var index = 0; index < pads.Length; index++)
        {
            var route = NewbieCityMarketTravel.Routes[index];
            var pad = pads[index];
            Assert.Equal(serials[index], route.Serial);
            Assert.Equal(sources[index], route.Source);
            Assert.Equal(destinations[index], route.Destination);
            Assert.Equal(route.Serial, pad.Serial.Value);
            Assert.IsType<Teleporter>(pad);
            Assert.False(pad.Deleted);
            Assert.Equal(route.Source, pad.Location);
            Assert.Equal(route.Destination, pad.PointDest);
            Assert.Equal("Newbie City - " + route.Id, pad.Name);
            Assert.Equal(TeleporterFlags.Active, pad.Flags);
            Assert.False(pad.Visible);
            Assert.False(pad.Movable);
            Assert.Equal(0x1BC3, pad.ItemID);
            Assert.Same(Map.Felucca, pad.Map);
            Assert.Same(Map.Felucca, pad.MapDest);
            AssertOnlyPadAt(route.Source, pad);
            if (route.OldSource != route.Source)
            {
                AssertOnlyPadAt(route.OldSource, null);
            }
        }
        Assert.Equal(4, _created.Count);
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void LateConflictAtOldOrNewCitySource_RefusesBeforeMovingAnyPad(bool oldSource)
    {
        TileDataRequirement.SkipIfMissing();
        var pads = SeedOldPads();
        var route = NewbieCityMarketTravel.Routes[2];
        var location = oldSource ? route.OldSource : route.Source;
        var conflict = Track(new Teleporter(new Point3D(1000, 1000, 0), Map.Felucca));
        conflict.MoveToWorld(location, Map.Felucca);

        Assert.Throws<InvalidOperationException>(() => NewbieCityMarketTravel.Apply(pads, false));

        AssertOldPadsUnchanged(pads);
        Assert.False(conflict.Deleted);
        Assert.Equal(location, conflict.Location);
    }

    [SkippableFact]
    public void WrongLastSerial_RefusesBeforeChangingAnyEarlierRoute()
    {
        TileDataRequirement.SkipIfMissing();
        var pads = SeedOldPads();
        var route = NewbieCityMarketTravel.Routes[^1];
        pads[^1].Delete();
        pads[^1] = CreateOldPad(route, route.Serial + 100);

        Assert.Throws<InvalidOperationException>(() => NewbieCityMarketTravel.Apply(pads, false));

        AssertOldPadsUnchanged(pads);
        Assert.Equal(route.Serial + 100, pads[^1].Serial.Value);
    }

    [SkippableFact]
    public void NativeMarket_AllNpcPinsBankEntriesPenAndMarketRoutesHaveStandingClearance()
    {
        TileDataRequirement.SkipIfMissing();
        var manifest = Environment.GetEnvironmentVariable("MODERNUO_NEWBIE_CITY_LAYOUT") ??
            Path.GetFullPath("../Distribution/Live/Data/NewbieCity/layout.json");
        Skip.If(!File.Exists(manifest), "Requires the retained Newbie City NPC layout manifest.");
        var layout = NewbieCityDeployment.LoadLayout(manifest);
        Assert.Equal(13, layout.Spawners.Length);
        foreach (var pin in layout.Spawners)
        {
            AssertFits(pin.Position);
            if (pin.Type is not ("Banker" or "Healer" or "Sheep"))
            {
                AssertFits(new Point3D(pin.Position.X, pin.Position.Y + 1, 0));
                var roofCount = 0;
                foreach (var tile in Map.Felucca.Tiles.GetStaticTiles(pin.Position.X, pin.Position.Y))
                {
                    if (TileData.ItemTable[tile.ID].Roof)
                    {
                        Assert.True(tile.Z >= 16, $"Roof headroom above {pin.Type}.");
                        roofCount++;
                    }
                }
                Assert.True(roofCount > 0, $"Native canopy above {pin.Type}.");
            }
        }
        foreach (var route in NewbieCityMarketTravel.Routes)
        {
            AssertFits(route.Source);
            AssertFits(route.Destination);
        }
        AssertFits(new Point3D(5591, 1345, 0));
        AssertFits(new Point3D(5592, 1345, 0));
        AssertFits(new Point3D(5591, 1346, 0));
        AssertFits(new Point3D(5592, 1346, 0));
        var sheep = Assert.Single(layout.Spawners, pin => pin.Type == "Sheep");
        for (var x = sheep.Bounds.Start.X; x < sheep.Bounds.End.X; x++)
        {
            for (var y = sheep.Bounds.Start.Y; y < sheep.Bounds.End.Y; y++)
            {
                AssertFits(new Point3D(x, y, sheep.Position.Z));
            }
        }
        var pads = SeedOldPads();
        Assert.Equal(4, NewbieCityMarketTravel.Apply(pads));
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(2)]
    public void NativeWalking_EntersMarketPadFromWestAndReturnsFromGroundZeroIslandWithoutBounce(int index)
    {
        TileDataRequirement.SkipIfMissing();
        var pads = SeedOldPads();
        NewbieCityMarketTravel.Apply(pads);
        var route = NewbieCityMarketTravel.Routes[index];
        var player = Track(new Mobile { Player = true, Body = 0x190, Direction = Direction.East });
        var approach = new Point3D(5607, route.Source.Y, 0);
        AssertFits(approach);
        player.MoveToWorld(approach, Map.Felucca);

        Assert.False(player.Move(Direction.East));

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
        var pads = new Teleporter[NewbieCityMarketTravel.Routes.Length];
        for (var index = 0; index < pads.Length; index++)
        {
            var route = NewbieCityMarketTravel.Routes[index];
            pads[index] = CreateOldPad(route, route.Serial);
        }
        return pads;
    }

    private Teleporter CreateOldPad(NewbieCityMarketTravel.Route route, uint serial)
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
            var route = NewbieCityMarketTravel.Routes[index];
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
        Assert.Equal(expected == null ? 0 : 1, count);
    }

    private static void AssertFits(Point3D point) =>
        Assert.True(Map.Felucca.CanFit(point, 16, true, false), $"Native market standing clearance at {point}.");

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }
}
