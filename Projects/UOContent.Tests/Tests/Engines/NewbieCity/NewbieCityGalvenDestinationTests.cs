using System;
using Server;
using Server.Engines.Labrynth;
using Server.Items;
using Server.Tests;
using Xunit;

namespace UOContent.Tests.Engines.NewbieCity;

[Collection("Sequential UOContent Tests")]
public class NewbieCityGalvenDestinationTests
{
    [Fact]
    public void NativeArrivalIsClearAndOnlyExistingGateDestinationChanges()
    {
        Assert.True(TestServerInitializer.TileDataLoaded);
        var gate = NewbieCityCaveGalvenGate.Ensure(out var created);
        try
        {
            Assert.True(created);
            var before = NewbieCityCaveGalvenGate.Describe(gate);
            Assert.True(NewbieCityGalvenDestination.Apply(gate));
            Assert.False(NewbieCityGalvenDestination.Apply(gate));
            var after = NewbieCityCaveGalvenGate.Describe(gate);
            Assert.Equal(before with { Destination = new(1331, 1390, 1, "Felucca") }, after);
            Assert.True(Map.Felucca.CanFit(NewbieCityGalvenDestination.Destination, 16, true, false));
        }
        finally
        {
            gate.Delete();
        }
    }

    [Fact]
    public void ConflictingArrivalLeavesGateUnchanged()
    {
        var gate = NewbieCityCaveGalvenGate.Ensure(out _, false);
        var conflict = new Teleporter(Point3D.Zero, Map.Felucca);
        try
        {
            conflict.MoveToWorld(NewbieCityGalvenDestination.Destination, Map.Felucca);
            Assert.Throws<InvalidOperationException>(() => NewbieCityGalvenDestination.Apply(gate, false));
            Assert.Equal(NewbieCityCaveGalvenGate.Destination, gate.PointDest);
            Assert.False(conflict.Deleted);
        }
        finally
        {
            conflict.Delete();
            gate.Delete();
        }
    }
}
