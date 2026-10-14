using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Server;
using Server.Items;
using Server.Gumps;
using Server.Mobiles;
using Server.Network;
using Server.Tests;
using Server.Tests.Network;
using Xunit;
using Gate = Server.Engines.Labrynth.NewbieCityCaveGalvenGate;

namespace UOContent.Tests.Engines.NewbieCity;

[Collection("Sequential UOContent Tests")]
public class NewbieCityCaveGalvenGateTests : IDisposable
{
    private readonly List<IEntity> _created = [];
    private readonly List<NetState> _states = [];
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NewbieCityCaveGalvenGateTests-" + Guid.NewGuid());
    private string ArchivePath => Path.Combine(_directory, "before.json");

    public void Dispose()
    {
        foreach (var state in _states)
        {
            if (state.Mobile != null)
            {
                state.Mobile.NetState = null;
                state.Mobile = null;
            }
            state.Dispose();
        }
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
    public void CreateArchiveAndRepeat_PreserveOneExactGateAndItsStockSerialization()
    {
        Directory.CreateDirectory(_directory);
        var decoration = Track(new Item(0xEED));
        decoration.MoveToWorld(Gate.Source, Map.Felucca);
        var neighbor = Track(new Teleporter(Point3D.Zero, Map.Felucca));
        neighbor.MoveToWorld(new Point3D(1317, 1379, 0), Map.Felucca);
        var gate = Track(Gate.Ensure(out var created, false, ArchivePath));
        var archiveText = File.ReadAllText(ArchivePath);
        var archive = JsonSerializer.Deserialize<Gate.Archive>(archiveText);

        Assert.True(created);
        Assert.Null(archive.Gate.Serial);
        Assert.Equal(Gate.Describe(null), archive.Gate);
        Assert.Same(gate, Gate.Ensure(out created, false, ArchivePath));
        Assert.False(created);
        Assert.Equal(archiveText, File.ReadAllText(ArchivePath));
        AssertGate(gate);
        Assert.False(decoration.Deleted);
        Assert.False(neighbor.Deleted);
        AssertNoArrivalGate();

        var serial = gate.Serial;
        var writer = new BufferWriter(true);
        gate.Serialize(writer);
        var bytes = writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();
        gate.Delete();
        var restored = Track(new Teleporter(serial));
        var reader = new BufferReader(bytes);
        restored.Deserialize(reader);

        Assert.Equal(bytes.Length, reader.Position);
        Assert.Same(restored, Gate.Ensure(out created, false, ArchivePath));
        Assert.False(created);
        Assert.Equal(serial, restored.Serial);
        AssertGate(restored);
    }

    [Theory]
    [InlineData("Destination")]
    [InlineData("Hue")]
    [InlineData("Duplicate")]
    public void SourceConflictRefusesBeforeArchivingOrChangingObjects(string conflict)
    {
        Directory.CreateDirectory(_directory);
        var gate = ExactGate();
        switch (conflict)
        {
            case "Destination":
                gate.PointDest = Point3D.Zero;
                break;
            case "Hue":
                gate.Hue = 7;
                break;
            case "Duplicate":
                ExactGate();
                break;
        }
        var snapshot = Gate.Describe(gate);

        Assert.Throws<InvalidOperationException>(() => Gate.Ensure(out _, false, ArchivePath));

        Assert.False(gate.Deleted);
        Assert.Equal(snapshot, Gate.Describe(gate));
        Assert.False(File.Exists(ArchivePath));
        AssertNoArrivalGate();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArrivalTravelConflictRefusesBeforeCreatingAnything(bool moongate)
    {
        Directory.CreateDirectory(_directory);
        var conflict = Track<Item>(moongate ? new Moongate(Gate.Source, Map.Felucca, false) : new Teleporter(Gate.Source, Map.Felucca));
        conflict.MoveToWorld(Gate.Destination, Map.Felucca);

        Assert.Throws<InvalidOperationException>(() => Gate.Ensure(out _, false, ArchivePath));

        Assert.False(conflict.Deleted);
        Assert.False(File.Exists(ArchivePath));
        AssertNoSourceGate();
    }

    [Fact]
    public void ForeignArchiveRefusesCreation_CommitFailureRemovesOnlyItsNewGateAndCanRetry()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(ArchivePath, JsonSerializer.Serialize(new Gate.Archive("other", Gate.Describe(null))));
        Assert.Throws<InvalidOperationException>(() => Gate.Ensure(out _, false, ArchivePath));
        AssertNoSourceGate();
        File.WriteAllText(ArchivePath, JsonSerializer.Serialize(new Gate.Archive(Gate.InstallationId, Gate.Describe(null))));

        Assert.Throws<IOException>(() => Gate.Ensure(out _, false, ArchivePath, (_, _) => throw new IOException("receipt write failed")));

        AssertNoSourceGate();
        AssertNoArrivalGate();
        var gate = Track(Gate.Ensure(out var created, false, ArchivePath));
        Assert.True(created);
        AssertGate(gate);
    }

    [Fact]
    public void ArchivedExistingSerialCannotBeReplacedByAnIdenticalLookingGate()
    {
        Directory.CreateDirectory(_directory);
        var gate = ExactGate();
        File.WriteAllText(ArchivePath, JsonSerializer.Serialize(new Gate.Archive(Gate.InstallationId,
            Gate.Describe(gate) with { Serial = gate.Serial.Value + 1000 })));

        Assert.Throws<InvalidOperationException>(() => Gate.Ensure(out _, false, ArchivePath));

        Assert.False(gate.Deleted);
        AssertGate(gate);
    }

    [Fact]
    public void NativeYoungPlayerWalksThroughWithFollowingPet_OneWayToGalven()
    {
        Assert.True(TestServerInitializer.TileDataLoaded);
        var gate = Track(Gate.Ensure(out var created));
        Assert.True(created);
        var player = Track(new PlayerMobile { Player = true, Young = true, Body = 0x190, Direction = Direction.North });
        player.MoveToWorld(new Point3D(5590, 1316, 0), Map.Felucca);
        var state = PacketTestUtilities.CreateTestNetState();
        _states.Add(state);
        player.NetState = state;
        state.Mobile = player;
        var pet = Track(new Sheep());
        Assert.True(pet.SetControlMaster(player));
        pet.ControlOrder = OrderType.Follow;
        pet.MoveToWorld(new Point3D(5589, 1316, 0), Map.Felucca);
        var wild = Track(new Sheep());
        wild.MoveToWorld(new Point3D(5591, 1316, 0), Map.Felucca);
        var wildLocation = wild.Location;

        Assert.True(gate.CanTeleport(player));
        Assert.False(player.Move(Direction.North));
        Assert.Equal(new Point3D(5590, 1316, 0), player.Location);
        var confirmation = Assert.IsType<MoongateTravelConfirmation>(player.FindGump<MoongateTravelConfirmation>());
        confirmation.OnResponse(state, new RelayInfo(1, ReadOnlySpan<int>.Empty, ReadOnlySpan<ushort>.Empty,
            ReadOnlySpan<Range>.Empty, ReadOnlySpan<byte>.Empty));

        Assert.Equal(Gate.Destination, player.Location);
        Assert.True(player.Young);
        Assert.Same(Map.Felucca, player.Map);
        Assert.Equal(player.Location, pet.Location);
        Assert.Same(Map.Felucca, pet.Map);
        Assert.Equal(wildLocation, wild.Location);
        AssertNoArrivalGate();
        AssertGate(gate);
    }

    private Teleporter ExactGate()
    {
        var gate = Track(new Teleporter(Gate.Destination, Map.Felucca)
        {
            Name = Gate.GateName, ItemID = 0xF6C, Hue = 0x482, Visible = true, Light = LightType.Circle300, SoundID = 0x1FE
        });
        gate.MoveToWorld(Gate.Source, Map.Felucca);
        return gate;
    }

    private static void AssertGate(Teleporter gate)
    {
        Assert.IsType<Teleporter>(gate);
        Assert.Equal(Gate.Describe(null), Gate.Describe(gate) with { Serial = null });
        Assert.Same(Map.Felucca, gate.Map);
        Assert.Same(Map.Felucca, gate.MapDest);
        var count = 0;
        foreach (var found in Map.Felucca.GetItemsAt<Teleporter>(Gate.Source))
        {
            Assert.Same(gate, found);
            count++;
        }
        Assert.Equal(1, count);
    }

    private static void AssertNoSourceGate()
    {
        foreach (var item in Map.Felucca.GetItemsAt<Teleporter>(Gate.Source))
        {
            Assert.Fail($"Unexpected cave gate {item.Serial}.");
        }
    }

    private static void AssertNoArrivalGate()
    {
        foreach (var item in Map.Felucca.GetItemsAt<Teleporter>(Gate.Destination))
        {
            Assert.Fail($"Unexpected return gate {item.Serial}.");
        }
    }

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
