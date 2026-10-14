using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Server;
using Server.Engines.Labrynth;
using Server.Items;
using Server.Tests;
using Xunit;
using Movement = Server.Movement.Movement;

namespace UOContent.Tests.Engines.Labrynth;

[Collection("Sequential UOContent Tests")]
public class GalvenCastleTravelTests : IDisposable
{
    private readonly List<IEntity> _created = [];
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GalvenCastleTravelTests-" + Guid.NewGuid());
    private string ReceiptPath => Path.Combine(_directory, "installed.json");

    public void Dispose()
    {
        for (var index = _created.Count - 1; index >= 0; index--)
        {
            _created[index].Delete();
        }
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Fact]
    public void Plan_HasOneStockTriggerAtEachRequestedCenter_AndExactTwoWayDestinations()
    {
        Assert.Equal(2, GalvenCastleTravel.Routes.Length);
        Assert.Equal(new Point3D(1281, 1326, 24), GalvenCastleTravel.Routes[0].Source);
        Assert.Equal(new Point3D(5474, 1010, 5), GalvenCastleTravel.Routes[0].Destination);
        for (var index = 0; index < 2; index++)
        {
            var route = GalvenCastleTravel.Routes[index];
            var opposite = GalvenCastleTravel.Routes[1 - index];
            Assert.NotEqual(route.Source, opposite.Source);
            Assert.Equal(opposite.Source, route.Destination);
        }
    }

    [SkippableFact]
    public void Ensure_CreatesExactlyTwoOrdinaryInvisibleTriggers_AndRepeatedEnsurePreservesTheirSerials()
    {
        TileDataRequirement.SkipIfMissing();
        var unrelated = Track(new Item(0xEED));
        unrelated.MoveToWorld(GalvenCastleTravel.Routes[0].Source, Map.Felucca);

        var pads = EnsureAndTrack(out var created);
        var reused = GalvenCastleTravel.Ensure(out var reusedCount, false);

        Assert.Equal(2, created);
        Assert.Equal(0, reusedCount);
        Assert.Equal(2, pads.Length);
        for (var index = 0; index < 2; index++)
        {
            Assert.Same(pads[index], reused[index]);
            Assert.Same(pads[index], World.FindItem(pads[index].Serial));
            AssertRoute(pads[index], GalvenCastleTravel.Routes[index]);
            AssertOnlyTriggerAt(GalvenCastleTravel.Routes[index].Source, pads[index]);
        }
        Assert.False(unrelated.Deleted);
        Assert.Equal(GalvenCastleTravel.Routes[0].Source, unrelated.Location);
    }

    [SkippableFact]
    public void PartialExistingPair_ReusesTheExistingSerialAndCreatesOnlyTheMissingTrigger()
    {
        TileDataRequirement.SkipIfMissing();
        var existing = AddRoute(GalvenCastleTravel.Routes[1]);

        var pads = EnsureAndTrack(out var created);

        Assert.Equal(1, created);
        Assert.Same(existing, pads[1]);
        Assert.Same(existing, World.FindItem(existing.Serial));
        AssertOnlyTriggerAt(GalvenCastleTravel.Routes[1].Source, existing);
    }

    [SkippableTheory]
    [InlineData("Destination")]
    [InlineData("Inactive")]
    [InlineData("Visible")]
    [InlineData("Delay")]
    [InlineData("Subclass")]
    [InlineData("Duplicate")]
    public void ConflictingLastTrigger_RejectsBeforeCreatingTheMissingFirstTrigger(string conflict)
    {
        TileDataRequirement.SkipIfMissing();
        var route = GalvenCastleTravel.Routes[1];
        var existing = Track(conflict == "Subclass"
            ? new SkillTeleporter()
            : new Teleporter(route.Destination, Map.Felucca));
        existing.Name = route.Name;
        existing.PointDest = route.Destination;
        existing.MapDest = Map.Felucca;
        existing.MoveToWorld(route.Source, Map.Felucca);
        switch (conflict)
        {
            case "Destination":
            {
                existing.PointDest = new Point3D(100, 100, 0);
                break;
            }
            case "Inactive":
            {
                existing.Active = false;
                break;
            }
            case "Visible":
            {
                existing.Visible = true;
                break;
            }
            case "Delay":
            {
                existing.Delay = TimeSpan.FromSeconds(1);
                break;
            }
            case "Duplicate":
            {
                AddRoute(route);
                break;
            }
        }
        var destination = existing.PointDest;
        var flags = existing.Flags;
        var visible = existing.Visible;
        var delay = existing.Delay;

        Assert.Throws<InvalidOperationException>(() => GalvenCastleTravel.Ensure(out _, false));

        AssertNoTriggerAt(GalvenCastleTravel.Routes[0].Source);
        Assert.False(existing.Deleted);
        Assert.Equal(route.Source, existing.Location);
        Assert.Equal(destination, existing.PointDest);
        Assert.Equal(flags, existing.Flags);
        Assert.Equal(visible, existing.Visible);
        Assert.Equal(delay, existing.Delay);
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void UnexpectedActiveArrivalTrigger_RejectsBeforeCreatingEitherRoute(int index)
    {
        TileDataRequirement.SkipIfMissing();
        var arrival = GalvenCastleTravel.Routes[index].Destination;
        var conflict = Track(new Teleporter(new Point3D(100, 100, 0), Map.Felucca));
        conflict.MoveToWorld(arrival, Map.Felucca);

        Assert.Throws<InvalidOperationException>(() => GalvenCastleTravel.Ensure(out _, false));

        AssertNoTriggerAt(GalvenCastleTravel.Routes[index].Source);
        AssertOnlyTriggerAt(arrival, conflict);
        Assert.True(conflict.Active);
        Assert.False(conflict.Deleted);
    }

    [SkippableFact]
    public void UnsupportedLastSource_RejectsBeforeCreatingEitherTrigger()
    {
        TileDataRequirement.SkipIfMissing();
        var route = GalvenCastleTravel.Routes[1];
        var unsupported = new Point3D(route.Source.X, route.Source.Y, 127);
        Assert.False(Map.Felucca.CanFit(unsupported, 16, true, false));
        GalvenCastleTravel.Routes[1] = route with { Source = unsupported };
        try
        {
            Assert.Throws<InvalidOperationException>(() => GalvenCastleTravel.Ensure(out _));

            AssertNoTriggerAt(GalvenCastleTravel.Routes[0].Source);
            AssertNoTriggerAt(unsupported);
        }
        finally
        {
            GalvenCastleTravel.Routes[1] = route;
        }
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void SingleMoveOver_LandsOnTheExactOppositeTriggerWithoutAutomaticPingPong(int index)
    {
        TileDataRequirement.SkipIfMissing();
        var pads = EnsureAndTrack(out _);
        var route = GalvenCastleTravel.Routes[index];
        var player = Track(new Mobile { Player = true, Body = 0x190 });
        player.MoveToWorld(route.Source, Map.Felucca);

        Assert.False(pads[index].OnMoveOver(player));

        Assert.Same(Map.Felucca, player.Map);
        Assert.Equal(route.Destination, player.Location);
        Assert.Equal(pads[1 - index].Location, player.Location);
        AssertOnlyTriggerAt(player.Location, pads[1 - index]);
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void WalkingOntoEachTrigger_DoesNotBounceUntilThePlayerStepsOffAndBack(int index)
    {
        TileDataRequirement.SkipIfMissing();
        var pads = EnsureAndTrack(out _);
        var route = GalvenCastleTravel.Routes[index];
        var player = Track(new Walker { Player = true, Body = 0x190, Direction = Direction.North });
        player.MoveToWorld(new Point3D(route.Source.X, route.Source.Y + 1, route.Source.Z), Map.Felucca);

        Assert.False(player.Move(Direction.North));

        Assert.Equal(route.Destination, player.Location);
        Assert.Same(Map.Felucca, player.Map);
        AssertOnlyTriggerAt(player.Location, pads[1 - index]);
        player.Direction = Direction.South;
        Assert.True(player.Move(Direction.South));
        Assert.Equal(new Point3D(route.Destination.X, route.Destination.Y + 1, route.Destination.Z), player.Location);
        player.Direction = Direction.North;

        Assert.False(player.Move(Direction.North));

        Assert.Equal(route.Source, player.Location);
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void NativeWalkingOntoEachPlatformCenter_ReachesTheOppositeCenterWithoutAutomaticBounce(int index)
    {
        TileDataRequirement.SkipIfMissing();
        var pads = EnsureAndTrack(out _);
        var route = GalvenCastleTravel.Routes[index];
        Assert.True(Map.Felucca.CanFit(route.Source, 16, true, false));
        Assert.True(Map.Felucca.CanFit(route.Destination, 16, true, false));
        var player = Track(new Mobile { Player = true, Body = 0x190 });
        var direction = NativeApproach(player, route.Source);

        Assert.False(player.Move(direction));

        Assert.Equal(route.Destination, player.Location);
        Assert.Same(Map.Felucca, player.Map);
        AssertOnlyTriggerAt(player.Location, pads[1 - index]);
    }

    [SkippableFact]
    public void OrdinarySerialization_RestoresBothRoutesAndEnsureReusesTheirSerials()
    {
        TileDataRequirement.SkipIfMissing();
        var pads = EnsureAndTrack(out _);
        var restored = new Teleporter[2];
        for (var index = 0; index < 2; index++)
        {
            var serial = pads[index].Serial;
            var writer = new BufferWriter(true);
            pads[index].Serialize(writer);
            var bytes = writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();
            pads[index].Delete();
            restored[index] = Track(new Teleporter(serial));
            var reader = new BufferReader(bytes);
            restored[index].Deserialize(reader);
            World.AddEntity(restored[index]);
            Assert.Equal(bytes.Length, reader.Position);
        }

        var reused = GalvenCastleTravel.Ensure(out var created, false);

        Assert.Equal(0, created);
        for (var index = 0; index < 2; index++)
        {
            Assert.Same(restored[index], reused[index]);
            Assert.Same(restored[index], World.FindItem(restored[index].Serial));
            AssertRoute(restored[index], GalvenCastleTravel.Routes[index]);
            AssertOnlyTriggerAt(restored[index].Location, restored[index]);
        }
    }

    [Theory]
    [InlineData("Identity")]
    [InlineData("Route")]
    public void InvalidArchiveIdentityOrRoute_CannotAuthorizeRecovery(string conflict)
    {
        var archive = new GalvenCastleTravel.Archive(GalvenCastleTravel.InstallationId, Snapshots([null, null]));
        if (conflict == "Identity")
        {
            archive = archive with { InstallationId = "another-installation" };
        }
        else
        {
            archive.Routes[1] = archive.Routes[1] with { Destination = new(100, 100, 0, "Felucca") };
        }

        Assert.Throws<InvalidOperationException>(() => GalvenCastleTravel.ValidateArchiveIdentity(archive, [null, null], ReceiptPath));

        Assert.False(File.Exists(ReceiptPath));
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArchivedExistingSerial_RefusesMissingOrReplacementTrigger(bool replacement)
    {
        TileDataRequirement.SkipIfMissing();
        var original = AddRoute(GalvenCastleTravel.Routes[0]);
        var archive = new GalvenCastleTravel.Archive(GalvenCastleTravel.InstallationId, Snapshots([original, null]));
        original.Delete();
        var existing = replacement ? AddRoute(GalvenCastleTravel.Routes[0]) : null;

        Assert.Throws<InvalidOperationException>(() => GalvenCastleTravel.ValidateArchiveIdentity(archive, [existing, null], ReceiptPath));

        Assert.False(File.Exists(ReceiptPath));
        if (existing != null)
        {
            Assert.False(existing.Deleted);
            AssertRoute(existing, GalvenCastleTravel.Routes[0]);
        }
    }

    [SkippableFact]
    public void SuccessfulReceipt_AllowsOriginalOrInstalledPair_ButRejectsMixedSerialsWithoutChangingTheJournal()
    {
        TileDataRequirement.SkipIfMissing();
        var archive = new GalvenCastleTravel.Archive(GalvenCastleTravel.InstallationId, Snapshots([null, null]));
        var pads = EnsureAndTrack(out _);
        Directory.CreateDirectory(_directory);
        var receipt = JsonSerializer.Serialize(new
        {
            Success = true, GalvenCastleTravel.InstallationId, Routes = Snapshots(pads)
        });
        File.WriteAllText(ReceiptPath, receipt);

        GalvenCastleTravel.ValidateArchiveIdentity(archive, [null, null], ReceiptPath);
        GalvenCastleTravel.ValidateArchiveIdentity(archive, pads, ReceiptPath);
        Assert.Throws<InvalidOperationException>(() => GalvenCastleTravel.ValidateArchiveIdentity(archive, [pads[0], null], ReceiptPath));

        Assert.Equal(receipt, File.ReadAllText(ReceiptPath));
        AssertRoute(pads[0], GalvenCastleTravel.Routes[0]);
        AssertRoute(pads[1], GalvenCastleTravel.Routes[1]);
    }

    private static GalvenCastleTravel.RouteSnapshot[] Snapshots(Teleporter[] pads)
    {
        var snapshots = new GalvenCastleTravel.RouteSnapshot[2];
        for (var index = 0; index < 2; index++)
        {
            var route = GalvenCastleTravel.Routes[index];
            snapshots[index] = new GalvenCastleTravel.RouteSnapshot(pads[index]?.Serial.Value, route.Name,
                new(route.Source.X, route.Source.Y, route.Source.Z, "Felucca"),
                new(route.Destination.X, route.Destination.Y, route.Destination.Z, "Felucca"));
        }
        return snapshots;
    }

    private static Direction NativeApproach(Mobile player, Point3D source)
    {
        (int X, int Y, Direction Direction)[] candidates =
        [
            (0, 1, Direction.North), (0, -1, Direction.South),
            (1, 0, Direction.West), (-1, 0, Direction.East)
        ];
        foreach (var candidate in candidates)
        {
            for (var z = source.Z; z >= source.Z - 5; z--)
            {
                var position = new Point3D(source.X + candidate.X, source.Y + candidate.Y, z);
                if (!Map.Felucca.CanFit(position, 16, true, false))
                {
                    continue;
                }
                player.MoveToWorld(position, Map.Felucca);
                player.Direction = candidate.Direction;
                if (Movement.CheckMovement(player, candidate.Direction, out var targetZ) && targetZ == source.Z)
                {
                    return candidate.Direction;
                }
            }
        }
        throw new InvalidOperationException($"No supported native approach to castle platform at {source}.");
    }

    private static void AssertRoute(Teleporter pad, GalvenCastleTravel.Route route)
    {
        Assert.IsType<Teleporter>(pad);
        Assert.Equal(route.Source, pad.Location);
        Assert.Equal(route.Destination, pad.PointDest);
        Assert.Equal(route.Name, pad.Name);
        Assert.Same(Map.Felucca, pad.Map);
        Assert.Same(Map.Felucca, pad.MapDest);
        Assert.Equal(TeleporterFlags.Active, pad.Flags);
        Assert.False(pad.Visible);
        Assert.False(pad.Movable);
        Assert.Equal(TimeSpan.Zero, pad.Delay);
        Assert.Equal(0, pad.SoundID);
    }

    private static void AssertNoTriggerAt(Point3D position)
    {
        foreach (var pad in Map.Felucca.GetItemsAt<Teleporter>(position))
        {
            Assert.Fail($"Unexpected teleporter {pad.Serial} at {position}.");
        }
    }

    private static void AssertOnlyTriggerAt(Point3D position, Teleporter expected)
    {
        var count = 0;
        foreach (var pad in Map.Felucca.GetItemsAt<Teleporter>(position))
        {
            Assert.Same(expected, pad);
            count++;
        }
        Assert.Equal(1, count);
    }

    private Teleporter AddRoute(GalvenCastleTravel.Route route)
    {
        var pad = Track(new Teleporter(route.Destination, Map.Felucca) { Name = route.Name });
        pad.MoveToWorld(route.Source, Map.Felucca);
        return pad;
    }

    private Teleporter[] EnsureAndTrack(out int created)
    {
        var pads = GalvenCastleTravel.Ensure(out created, false);
        foreach (var pad in pads)
        {
            if (!_created.Contains(pad))
            {
                Track(pad);
            }
        }
        return pads;
    }

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }

    private sealed class Walker : Mobile
    {
        public override bool CheckMovement(Direction direction, out int newZ)
        {
            newZ = Z;
            return true;
        }
    }
}
