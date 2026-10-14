using System;
using System.Collections.Generic;
using System.IO;
using Server;
using Server.Engines.Labrynth;
using Server.Items;
using Server.Mobiles;
using Server.Tests;
using Xunit;
using Cave = Server.Engines.Labrynth.NewbieCityUndergroundCave;

namespace UOContent.Tests.Engines.NewbieCity;

[Collection("Sequential UOContent Tests")]
public class NewbieCityUndergroundCaveTests : IDisposable
{
    private readonly List<IEntity> _created = [];
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NewbieCityUndergroundCaveTests-" + Guid.NewGuid());
    private string ArchivePath => Path.Combine(_directory, "before.json");

    public void Dispose()
    {
        for (var i = _created.Count - 1; i >= 0; i--)
        {
            _created[i].Delete();
        }
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Fact]
    public void Ensure_MovesTheExactExistingGateAndCreatesOnlyTwoLinks_RepeatedEnsureReusesAllThreeSerials()
    {
        var gate = OldGate();
        Directory.CreateDirectory(_directory);
        var result = Ensure(ArchivePath);
        var archive = File.ReadAllText(ArchivePath);
        var repeated = Cave.Ensure(ArchivePath, false);

        Assert.Equal(2, result.Created);
        Assert.True(result.Moved);
        Assert.Same(gate, result.Gate);
        Assert.Equal(Cave.GateSerial, result.Gate.Serial.Value);
        Assert.Equal(Cave.GatePosition, gate.Location);
        Assert.Equal(NewbieCityGalvenGate.Destination, gate.PointDest);
        Assert.Equal(0, repeated.Created);
        Assert.False(repeated.Moved);
        Assert.Same(gate, repeated.Gate);
        Assert.Equal(archive, File.ReadAllText(ArchivePath));
        for (var i = 0; i < result.Links.Length; i++)
        {
            Assert.Same(result.Links[i], repeated.Links[i]);
            AssertLink(result.Links[i], Cave.Routes[i]);
        }
        Assert.Equal(Cave.GatePosition, Cave.DescribeGate(gate).Source is { } source ? new Point3D(source.X, source.Y, source.Z) : Point3D.Zero);
        AssertNoTravelAt(Cave.Routes[0].Destination);
        AssertNoTravelAt(Cave.Routes[1].Destination);
        AssertNoTravelAt(NewbieCityGalvenGate.Destination);
    }

    [Theory]
    [InlineData("ReturnConflict")]
    [InlineData("ForeignItem")]
    [InlineData("Player")]
    [InlineData("ControlledPet")]
    [InlineData("GateFacet")]
    public void LateConflictOrInteriorOccupant_RefusesBeforeArchiveMovementOrLinkCreation(string conflict)
    {
        var gate = OldGate();
        Directory.CreateDirectory(_directory);
        switch (conflict)
        {
            case "ReturnConflict":
            {
                var wrong = Track(new Teleporter(Point3D.Zero, Map.Felucca));
                wrong.MoveToWorld(Cave.Routes[1].Source, Map.Felucca);
                break;
            }
            case "ForeignItem":
            {
                var item = Track(new Item(0xEED));
                item.MoveToWorld(new Point3D(6028, 3448, -20), Map.Felucca);
                break;
            }
            case "Player":
            {
                var player = Track(new PlayerMobile { Player = true, Body = 0x190 });
                player.MoveToWorld(Cave.Routes[0].Destination, Map.Felucca);
                break;
            }
            case "ControlledPet":
            {
                var player = Track(new PlayerMobile { Player = true, Body = 0x190 });
                player.MoveToWorld(Cave.Routes[1].Destination, Map.Felucca);
                var sheep = Track(new Sheep());
                Assert.True(sheep.SetControlMaster(player));
                sheep.MoveToWorld(Cave.Routes[0].Destination, Map.Felucca);
                break;
            }
            case "GateFacet":
            {
                gate.MapDest = Map.Trammel;
                break;
            }
        }

        Assert.Throws<InvalidOperationException>(() => Cave.Ensure(ArchivePath, false));

        Assert.Equal(NewbieCityGalvenGate.Source, gate.Location);
        Assert.Same(gate, World.FindEntity((Serial)Cave.GateSerial));
        Assert.False(gate.Deleted);
        Assert.False(File.Exists(ArchivePath));
        Assert.Equal(1, CountTeleportersAt(Cave.Routes[0].Source));
        Assert.Equal(conflict == "ReturnConflict" ? 1 : 0, CountTeleportersAt(Cave.Routes[1].Source));
    }

    [Fact]
    public void MissingOriginalGate_RefusesRatherThanCreatingAReplacement()
    {
        Assert.Null(World.FindEntity((Serial)Cave.GateSerial));

        Assert.Throws<InvalidOperationException>(() => Cave.Ensure(checkSupport: false));

        AssertNoTravelAt(Cave.Routes[0].Source);
        AssertNoTravelAt(Cave.Routes[1].Source);
        AssertNoTravelAt(Cave.GatePosition);
    }

    [Fact]
    public void CommitFailure_RollsBackGateAndBothNewLinks_ThenTheArchivedOriginalCanResume()
    {
        var gate = OldGate();
        Directory.CreateDirectory(_directory);

        Assert.Throws<IOException>(() => Cave.Ensure(ArchivePath, false, _ => throw new IOException("Test commit failure.")));

        Assert.Equal(NewbieCityGalvenGate.Source, gate.Location);
        Assert.Same(gate, World.FindEntity((Serial)Cave.GateSerial));
        Assert.Equal(1, CountTeleportersAt(Cave.Routes[0].Source));
        AssertNoTravelAt(Cave.Routes[1].Source);
        AssertNoTravelAt(Cave.GatePosition);
        Assert.True(File.Exists(ArchivePath));
        var retry = Ensure(ArchivePath);
        Assert.Equal(2, retry.Created);
        Assert.Same(gate, retry.Gate);
    }

    [Fact]
    public void ExactPartialInstall_ResumesWithoutReplacingTheGateOrExistingEntryLink()
    {
        var gate = OldGate();
        gate.MoveToWorld(Cave.GatePosition, Map.Felucca);
        var entry = Track(new Teleporter(Cave.Routes[0].Destination, Map.Felucca) { Name = Cave.Routes[0].Name });
        entry.MoveToWorld(Cave.Routes[0].Source, Map.Felucca);

        var result = Ensure();

        Assert.Equal(1, result.Created);
        Assert.False(result.Moved);
        Assert.Same(gate, result.Gate);
        Assert.Same(entry, result.Links[0]);
        AssertLink(result.Links[1], Cave.Routes[1]);
    }

    [Fact]
    public void StockSerialization_RestoresAllThreeExactSerialsAndRoutes()
    {
        OldGate();
        var installed = Ensure();
        Teleporter[] originals = [installed.Gate, installed.Links[0], installed.Links[1]];
        var restored = new Teleporter[3];
        for (var i = 0; i < originals.Length; i++)
        {
            var serial = originals[i].Serial;
            var writer = new BufferWriter(true);
            originals[i].Serialize(writer);
            var bytes = writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();
            originals[i].Delete();
            restored[i] = Track(new Teleporter(serial));
            var reader = new BufferReader(bytes);
            restored[i].Deserialize(reader);
            Assert.Equal(bytes.Length, reader.Position);
        }

        var repeated = Cave.Ensure(checkSupport: false);

        Assert.Equal(0, repeated.Created);
        Assert.False(repeated.Moved);
        Assert.Same(restored[0], repeated.Gate);
        Assert.Equal(Cave.GateSerial, repeated.Gate.Serial.Value);
        Assert.Equal(Cave.GatePosition, repeated.Gate.Location);
        Assert.Equal(NewbieCityGalvenGate.BlueHue, repeated.Gate.Hue);
        Assert.True(repeated.Gate.Visible);
        for (var i = 0; i < repeated.Links.Length; i++)
        {
            Assert.Same(restored[i + 1], repeated.Links[i]);
            AssertLink(repeated.Links[i], Cave.Routes[i]);
        }
    }

    [Fact]
    public void NativeYoungPlayerAndFollowingPet_WalkDownReturnUpAndUseGalvenGate_WithoutAutomaticBounce()
    {
        Assert.True(TestServerInitializer.TileDataLoaded);
        OldGate();
        var installed = Ensure(checkSupport: true);
        var player = Track(new PlayerMobile { Player = true, Young = true, Body = 0x190, Direction = Direction.North });
        player.MoveToWorld(Cave.Routes[1].Destination, Map.Felucca);
        var pet = Track(new Sheep());
        Assert.True(pet.SetControlMaster(player));
        pet.ControlOrder = OrderType.Follow;
        pet.MoveToWorld(new Point3D(player.X, player.Y + 1, player.Z), Map.Felucca);

        Assert.False(player.Move(Direction.North));
        Assert.Equal(Cave.Routes[0].Destination, player.Location);
        Assert.Equal(player.Location, pet.Location);
        Assert.NotEqual(Cave.Routes[1].Source, player.Location);
        AssertNoTravelAt(player.Location);
        player.Direction = Direction.South;
        Assert.False(player.Move(Direction.South));
        Assert.Equal(Cave.Routes[1].Destination, player.Location);
        Assert.Equal(player.Location, pet.Location);
        Assert.NotEqual(Cave.Routes[0].Source, player.Location);
        AssertNoTravelAt(player.Location);
        player.Direction = Direction.North;
        Assert.False(player.Move(Direction.North));
        Assert.Equal(Cave.Routes[0].Destination, player.Location);
        for (var y = 3449; y >= 3447; y--)
        {
            Assert.True(player.Move(Direction.North));
            Assert.Equal(new Point3D(6028, y, -20), player.Location);
        }
        Assert.False(player.Move(Direction.North));
        Assert.Equal(NewbieCityGalvenGate.Destination, player.Location);
        Assert.Equal(player.Location, pet.Location);
        Assert.True(player.Young);
        Assert.Same(Map.Felucca, player.Map);
        Assert.Equal(Cave.GateSerial, installed.Gate.Serial.Value);
        AssertNoTravelAt(player.Location);
    }

    private Teleporter OldGate()
    {
        Assert.Null(World.FindEntity((Serial)Cave.GateSerial));
        var gate = Track(new Teleporter((Serial)Cave.GateSerial)
        {
            Name = NewbieCityGalvenGate.GateName,
            ItemID = 0xF6C,
            Hue = NewbieCityGalvenGate.BlueHue,
            Light = LightType.Circle300,
            SoundID = 0x1FE,
            Flags = TeleporterFlags.Active,
            Visible = true,
            Movable = false,
            PointDest = NewbieCityGalvenGate.Destination,
            MapDest = Map.Felucca
        });
        gate.MoveToWorld(NewbieCityGalvenGate.Source, Map.Felucca);
        return gate;
    }

    private Cave.Result Ensure(string archivePath = null, bool checkSupport = false)
    {
        var result = Cave.Ensure(archivePath, checkSupport);
        foreach (var link in result.Links)
        {
            if (!_created.Contains(link))
            {
                Track(link);
            }
        }
        return result;
    }

    private static void AssertLink(Teleporter link, Cave.Route route)
    {
        Assert.IsType<Teleporter>(link);
        Assert.Equal(route.Name, link.Name);
        Assert.Equal(route.Source, link.Location);
        Assert.Equal(route.Destination, link.PointDest);
        Assert.Same(Map.Felucca, link.Map);
        Assert.Same(Map.Felucca, link.MapDest);
        Assert.Equal(TeleporterFlags.Active, link.Flags);
        Assert.Equal(0x1BC3, link.ItemID);
        Assert.Equal(0, link.Hue);
        Assert.Equal(0, link.SoundID);
        Assert.Equal(TimeSpan.Zero, link.Delay);
        Assert.False(link.Visible);
        Assert.False(link.Movable);
    }

    private static int CountTeleportersAt(Point3D point)
    {
        var count = 0;
        foreach (var _ in Map.Felucca.GetItemsAt<Teleporter>(point))
        {
            count++;
        }
        return count;
    }

    private static void AssertNoTravelAt(Point3D point) => Assert.Equal(0, CountTeleportersAt(point));

    private T Track<T>(T entity) where T : IEntity
    {
        if (!ReferenceEquals(World.FindEntity(entity.Serial), entity))
        {
            World.AddEntity(entity);
        }
        _created.Add(entity);
        return entity;
    }
}
