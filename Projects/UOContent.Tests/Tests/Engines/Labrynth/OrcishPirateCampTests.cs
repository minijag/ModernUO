using System;
using System.Collections.Generic;
using Server;
using Server.Engines.Labrynth;
using Server.Engines.Spawners;
using Server.Mobiles;
using Server.Tests;
using Xunit;

namespace UOContent.Tests.Engines.Labrynth;

[Collection("Sequential UOContent Tests")]
public class OrcishPirateCampTests : IDisposable
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
    public void NativeCamp_CreatesFiveRenamedOrcsAtTheCorrectedFeluccaCoordinates()
    {
        TileDataRequirement.SkipIfMissing();

        var camp = Track(OrcishPirateCamp.Ensure(out var created));

        Assert.True(created);
        Assert.Equal(new Point3D(1773, 910, 20), camp.Location);
        Assert.Equal(OrcishPirateCamp.Position, camp.Location);
        Assert.Same(Map.Felucca, camp.Map);
        Assert.Equal(new Guid("c6545783-386a-4e51-a2ca-99e185d1f172"), camp.Guid);
        Assert.Equal("Orcish pirate crew", camp.Name);
        Assert.Equal(5, camp.Count);
        Assert.Equal(TimeSpan.FromMinutes(5), camp.MinDelay);
        Assert.Equal(TimeSpan.FromMinutes(10), camp.MaxDelay);
        Assert.Equal(4, camp.HomeRange);
        Assert.Equal(new Rectangle3D(1769, 906, 20, 9, 9, 1), camp.SpawnBounds);
        Assert.Equal(4, camp.WalkingRange);
        Assert.True(camp.UseSpiralScan);
        Assert.True(camp.Running);
        var entry = Assert.Single(camp.Entries);
        Assert.Equal("Orc", entry.SpawnedName);
        Assert.Equal(100, entry.SpawnedProbability);
        Assert.Equal(5, entry.SpawnedMaxCount);
        Assert.Equal("Name \"an orcish pirate\"", entry.Properties);
        Assert.Null(entry.Parameters);
        Assert.False(entry.Disabled);
        Assert.Equal(5, camp.Spawned.Count);
        foreach (var entity in camp.Spawned.Keys)
        {
            var pirate = Assert.IsType<Orc>(entity);
            Assert.Equal("an orcish pirate", pirate.Name);
            Assert.Equal(17, pirate.Body.BodyID);
            Assert.Same(Map.Felucca, pirate.Map);
            Assert.Same(camp, pirate.Spawner);
            Assert.InRange(pirate.X, 1769, 1777);
            Assert.InRange(pirate.Y, 906, 914);
            Assert.Equal(20, pirate.Z);
            Assert.True(Map.Felucca.CanFit(pirate.Location, 16, true, false));
        }
    }

    [SkippableFact]
    public void RepeatedEnsure_ReusesTheCampAndCrew_WithoutRespawningOrResettingTheirHealth()
    {
        TileDataRequirement.SkipIfMissing();
        var camp = Track(OrcishPirateCamp.Ensure(out _));
        var crew = CaptureCrew(camp);
        crew[0].Hits = 1;

        var reused = OrcishPirateCamp.Ensure(out var created);

        Assert.False(created);
        Assert.Same(camp, reused);
        Assert.Equal(5, reused.Spawned.Count);
        Assert.Equal(1, crew[0].Hits);
        foreach (var pirate in crew)
        {
            Assert.False(pirate.Deleted);
            Assert.Same(pirate, World.FindMobile(pirate.Serial));
            Assert.Contains(pirate, reused.Spawned.Keys);
        }
    }

    [SkippableFact]
    public void ForeignSpawnerAtTheTarget_IsRefusedAndLeftUntouched()
    {
        TileDataRequirement.SkipIfMissing();
        var foreign = Track(new Spawner("Sheep"));
        foreign.MoveToWorld(OrcishPirateCamp.Position, Map.Felucca);
        foreign.Stop();
        var guid = foreign.Guid;
        var bounds = foreign.SpawnBounds;

        Assert.Throws<InvalidOperationException>(() => OrcishPirateCamp.Ensure(out _));

        Assert.False(foreign.Deleted);
        Assert.Same(Map.Felucca, foreign.Map);
        Assert.Equal(OrcishPirateCamp.Position, foreign.Location);
        Assert.Equal(guid, foreign.Guid);
        Assert.Equal(bounds, foreign.SpawnBounds);
        Assert.Equal("Sheep", Assert.Single(foreign.Entries).SpawnedName);
        Assert.False(foreign.Running);
        Assert.Empty(foreign.Spawned);
        AssertOnlyCampAtTarget(foreign);
    }

    [SkippableFact]
    public void MatchingGuidWithWrongCreatureEntry_IsRefusedWithoutReconfiguringTheSpawner()
    {
        TileDataRequirement.SkipIfMissing();
        var definition = new SpawnerDataDto
        {
            Guid = OrcishPirateCamp.CampGuid,
            Name = OrcishPirateCamp.CampName,
            Location = OrcishPirateCamp.Position,
            Map = Map.Felucca,
            Count = OrcishPirateCamp.CrewSize,
            MinDelay = TimeSpan.FromMinutes(5),
            MaxDelay = TimeSpan.FromMinutes(10),
            SpawnBounds = new Rectangle3D(1769, 906, 20, 9, 9, 1),
            WalkingRange = 4,
            Entries = [new SpawnerEntry("Sheep", 100, OrcishPirateCamp.CrewSize, OrcishPirateCamp.EntryProperties)]
        };
        var unexpected = Track((Spawner)definition.ToSpawner());
        unexpected.UseSpiralScan = true;
        unexpected.MoveToWorld(definition.Location, definition.Map);
        unexpected.Start();
        var entry = Assert.Single(unexpected.Entries);

        Assert.Throws<InvalidOperationException>(() => OrcishPirateCamp.Ensure(out _));

        Assert.False(unexpected.Deleted);
        Assert.Same(Map.Felucca, unexpected.Map);
        Assert.Equal(OrcishPirateCamp.Position, unexpected.Location);
        Assert.Equal(OrcishPirateCamp.CampGuid, unexpected.Guid);
        Assert.Same(entry, Assert.Single(unexpected.Entries));
        Assert.Equal("Sheep", entry.SpawnedName);
        Assert.True(unexpected.Running);
        Assert.Empty(unexpected.Spawned);
        AssertOnlyCampAtTarget(unexpected);
    }

    [SkippableFact]
    public void NativeSerialization_PreservesTheConfigurationAndCrew_AndAllowsIdempotentReuse()
    {
        TileDataRequirement.SkipIfMissing();
        var camp = Track(OrcishPirateCamp.Ensure(out _));
        var crew = CaptureCrew(camp);
        crew[0].Hits = 1;
        var serial = camp.Serial;
        var writer = new BufferWriter(true);
        camp.Serialize(writer);
        var bytes = writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();
        camp.Stop();
        // Native item loading enters sectors itself; detach this in-memory fixture first.
        camp.Map = Map.Internal;
        var reader = new BufferReader(bytes);
        camp.Deserialize(reader);

        var reused = OrcishPirateCamp.Ensure(out var created);

        Assert.Equal(bytes.Length, reader.Position);
        Assert.False(created);
        Assert.Same(camp, reused);
        Assert.Equal(serial, reused.Serial);
        Assert.Equal(OrcishPirateCamp.CampGuid, reused.Guid);
        Assert.Equal(OrcishPirateCamp.Position, reused.Location);
        Assert.Same(Map.Felucca, reused.Map);
        Assert.True(reused.Running);
        Assert.Equal(OrcishPirateCamp.EntryProperties, Assert.Single(reused.Entries).Properties);
        Assert.Equal(5, reused.Spawned.Count);
        Assert.Equal(1, crew[0].Hits);
        foreach (var pirate in crew)
        {
            Assert.False(pirate.Deleted);
            Assert.Same(reused, pirate.Spawner);
            Assert.Contains(pirate, reused.Spawned.Keys);
            Assert.Equal(OrcishPirateCamp.PirateName, pirate.Name);
        }
    }

    private static Orc[] CaptureCrew(Spawner camp)
    {
        var crew = new Orc[camp.Spawned.Count];
        var index = 0;
        foreach (var entity in camp.Spawned.Keys)
        {
            crew[index++] = Assert.IsType<Orc>(entity);
        }
        Assert.Equal(5, crew.Length);
        return crew;
    }

    private static void AssertOnlyCampAtTarget(Spawner expected)
    {
        var count = 0;
        foreach (var spawner in Map.Felucca.GetItemsAt<BaseSpawner>(OrcishPirateCamp.Position))
        {
            count++;
            Assert.Same(expected, spawner);
        }
        Assert.Equal(1, count);
    }

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }
}
