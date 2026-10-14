using System;
using System.Collections.Generic;
using Server;
using Server.Engines.Labrynth;
using Server.Items;
using Server.Tests;
using Xunit;

namespace UOContent.Tests.Engines.Labrynth;

[Collection("Sequential UOContent Tests")]
public class OrcFortNorthTravelTests : IDisposable
{
    private readonly List<Item> _created = [];

    public void Dispose()
    {
        foreach (var item in _created)
        {
            item.Delete();
        }
    }

    [SkippableFact]
    public void MoveSouth_PreservesNativePadsAndUsesAdjacentSafeArrival_AndIsIdempotent()
    {
        TileDataRequirement.SkipIfMissing();
        var pads = Seed();
        Assert.Equal(4, OrcFortNorthRamp.Ensure());
        Assert.Equal(0, OrcFortNorthRamp.Ensure());
        for (var index = 0; index < pads.Length; index++)
        {
            var route = OrcFortNorthRamp.Routes[index];
            Assert.Same(pads[index], World.FindItem(route.Serial));
            Assert.Equal(route.Position, pads[index].Location);
            Assert.Equal(route.Destination, pads[index].PointDest);
        }
        foreach (var item in Map.Felucca.GetItemsAt<Teleporter>(new Point3D(5284, 984, 20)))
        {
            Assert.False(item.Active);
        }
    }

    [SkippableFact]
    public void LateConflict_LeavesEveryExistingRouteUntouched()
    {
        TileDataRequirement.SkipIfMissing();
        var pads = Seed();
        pads[^1].PointDest = new Point3D(1, 1, 0);
        Assert.Throws<InvalidOperationException>(() => OrcFortNorthRamp.Ensure());
        for (var index = 0; index < pads.Length; index++)
        {
            Assert.Equal(OrcFortNorthRamp.Routes[index].OldPosition, pads[index].Location);
            Assert.False(pads[index].Deleted);
        }
        Assert.Equal(OrcFortNorthRamp.Routes[0].OldDestination, pads[0].PointDest);
    }

    private Teleporter[] Seed()
    {
        var pads = new Teleporter[OrcFortNorthRamp.Routes.Length];
        for (var index = 0; index < pads.Length; index++)
        {
            var route = OrcFortNorthRamp.Routes[index];
            Assert.Null(World.FindItem(route.Serial));
            var item = new Teleporter(route.Serial)
            {
                ItemID = 0x1BC3, Amount = 1, Name = route.Name, PointDest = route.OldDestination,
                MapDest = Map.Felucca, Flags = TeleporterFlags.Active,
                Delay = TimeSpan.Zero, SoundID = 0, Visible = false, Movable = false
            };
            _created.Add(item);
            World.AddEntity(item);
            item.MoveToWorld(route.OldPosition, Map.Felucca);
            pads[index] = item;
        }
        return pads;
    }
}
