using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Server;
using Server.Items;
using Server.Mobiles;
using Xunit;
using Retirement = Server.Engines.Labrynth.NewbieCityCaveTravelRetirement;

namespace UOContent.Tests.Engines.NewbieCity;

[Collection("Sequential UOContent Tests")]
public class NewbieCityCaveTravelRetirementTests : IDisposable
{
    private readonly List<IEntity> _created = [];
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NewbieCityCaveRetirementTests-" + Guid.NewGuid());
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
    public void ExactTravelIsArchivedAndDeleted_RepeatedRunIsIdempotent_UnrelatedTravelSurvives()
    {
        var travel = CreateTravel();
        var unrelated = Track(new Teleporter(Point3D.Zero, Map.Felucca));
        unrelated.MoveToWorld(new Point3D(5592, 1324, 0), Map.Felucca);

        var result = Retirement.Ensure(ArchivePath, [], false);
        var before = File.ReadAllText(ArchivePath);
        var archive = JsonSerializer.Deserialize<Retirement.Archive>(before);
        var repeated = Retirement.Ensure(ArchivePath, [], false);

        Assert.Equal(3, result.Deleted);
        Assert.Equal(0, result.AlreadyAbsent);
        Assert.Equal(Retirement.InstallationId, archive.InstallationId);
        Assert.Equal(Retirement.ExpectedTravel, archive.Travel);
        Assert.Empty(archive.Occupants);
        Assert.All(travel, item => Assert.True(item.Deleted));
        Assert.False(unrelated.Deleted);
        Assert.Equal(0, repeated.Deleted);
        Assert.Equal(3, repeated.AlreadyAbsent);
        Assert.Equal(before, File.ReadAllText(ArchivePath));
    }

    [Theory]
    [InlineData("Destination")]
    [InlineData("Appearance")]
    [InlineData("Facet")]
    [InlineData("Missing")]
    [InlineData("ForeignItem")]
    public void FailedPreflightDoesNotArchiveDeleteOrRelocate(string conflict)
    {
        var travel = CreateTravel();
        var player = Track(new PlayerMobile());
        var original = new Point3D(6028, 3448, -20);
        player.MoveToWorld(original, Map.Felucca);
        switch (conflict)
        {
            case "Destination":
                travel[2].PointDest = Point3D.Zero;
                break;
            case "Appearance":
                travel[2].Hue = 9;
                break;
            case "Facet":
                travel[2].MapDest = Map.Trammel;
                break;
            case "Missing":
                travel[2].Delete();
                break;
            case "ForeignItem":
                Track(new Item(0xEED)).MoveToWorld(original, Map.Felucca);
                break;
        }

        Assert.Throws<InvalidOperationException>(() => Retirement.Ensure(ArchivePath, [], false));

        Assert.False(File.Exists(ArchivePath));
        Assert.False(travel[0].Deleted);
        Assert.False(travel[1].Deleted);
        Assert.Equal(original, player.Location);
    }

    [Fact]
    public void LivePlayerPetAndOfflineCharacterAreArchivedAndEvacuatedBeforeRemoteFloorRemoval()
    {
        CreateTravel();
        var original = new Point3D(6028, 3448, -20);
        var player = Track(new PlayerMobile());
        player.MoveToWorld(original, Map.Felucca);
        var sheep = Track(new Sheep());
        Assert.True(sheep.SetControlMaster(player));
        sheep.MoveToWorld(original, Map.Felucca);
        var offline = Track(new PlayerMobile { LogoutMap = Map.Felucca, LogoutLocation = original });
        offline.MoveToWorld(Point3D.Zero, Map.Internal);
        var outside = Track(new PlayerMobile { LogoutMap = Map.Felucca, LogoutLocation = new Point3D(6022, 3448, -20) });
        outside.MoveToWorld(Point3D.Zero, Map.Internal);

        var result = Retirement.Ensure(ArchivePath, [offline, outside], false);
        var archive = JsonSerializer.Deserialize<Retirement.Archive>(File.ReadAllText(ArchivePath));
        var repeated = Retirement.Ensure(ArchivePath, [offline, outside], false);

        Assert.Equal(2, result.RelocatedMobiles);
        Assert.Equal(1, result.RelocatedOfflineCharacters);
        Assert.Equal(2, result.OfflineCharactersChecked);
        Assert.Equal(3, archive.Occupants.Length);
        Assert.All(archive.Occupants, occupant => Assert.Equal(original, new Point3D(occupant.Source.X, occupant.Source.Y, occupant.Source.Z)));
        Assert.Equal(Retirement.SafeArrival, player.Location);
        Assert.Equal(Retirement.SafeArrival, sheep.Location);
        Assert.Same(player, sheep.ControlMaster);
        Assert.Equal(Retirement.SafeArrival, offline.LogoutLocation);
        Assert.Same(Map.Internal, offline.Map);
        Assert.Equal(new Point3D(6022, 3448, -20), outside.LogoutLocation);
        Assert.Equal(0, repeated.RelocatedMobiles);
        Assert.Equal(0, repeated.RelocatedOfflineCharacters);
    }

    [Fact]
    public void DurableArchiveAllowsPartialDeletionToResume_BadArchiveCannotAuthorizeDeletion()
    {
        var travel = CreateTravel();
        File.WriteAllText(ArchivePath, JsonSerializer.Serialize(new Retirement.Archive("wrong", Retirement.ExpectedTravel, [])));
        Assert.Throws<InvalidOperationException>(() => Retirement.Ensure(ArchivePath, [], false));
        Assert.All(travel, item => Assert.False(item.Deleted));

        File.WriteAllText(ArchivePath, JsonSerializer.Serialize(new Retirement.Archive(Retirement.InstallationId, Retirement.ExpectedTravel, [])));
        travel[0].Delete();
        var result = Retirement.Ensure(ArchivePath, [], false);

        Assert.Equal(2, result.Deleted);
        Assert.Equal(1, result.AlreadyAbsent);
        Assert.All(travel, item => Assert.True(item.Deleted));
    }

    [Fact]
    public void ArchiveWriteFailureLeavesTravelAndOccupantsUnchanged()
    {
        var travel = CreateTravel();
        var player = Track(new PlayerMobile());
        var original = new Point3D(6028, 3448, -20);
        player.MoveToWorld(original, Map.Felucca);

        Assert.Throws<DirectoryNotFoundException>(() => Retirement.Ensure(Path.Combine(_directory, "missing", "before.json"), [], false));

        Assert.All(travel, item => Assert.False(item.Deleted));
        Assert.Equal(original, player.Location);
    }

    private Teleporter[] CreateTravel()
    {
        Directory.CreateDirectory(_directory);
        var travel = new Teleporter[Retirement.ExpectedTravel.Length];
        for (var i = 0; i < travel.Length; i++)
        {
            var expected = Retirement.ExpectedTravel[i];
            Assert.Null(World.FindEntity((Serial)expected.Serial.Value));
            var item = Track(new Teleporter((Serial)expected.Serial.Value)
            {
                Name = expected.Name,
                ItemID = expected.ItemId,
                Hue = expected.Hue,
                Light = expected.Light,
                SoundID = expected.SoundID,
                Flags = expected.Flags,
                Visible = expected.Visible,
                Movable = expected.Movable,
                Delay = TimeSpan.FromTicks(expected.DelayTicks),
                PointDest = new Point3D(expected.Destination.X, expected.Destination.Y, expected.Destination.Z),
                MapDest = Map.Felucca
            });
            item.MoveToWorld(new Point3D(expected.Source.X, expected.Source.Y, expected.Source.Z), Map.Felucca);
            travel[i] = item;
        }
        return travel;
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
