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
public class ArborOrcDensityTests : IDisposable
{
    private readonly List<Spawner> _created = [];

    public void Dispose()
    {
        for (var index = _created.Count - 1; index >= 0; index--)
        {
            _created[index].Delete();
        }
    }

    [SkippableFact]
    public void Tripling_Fills105Orcs_WithoutReplacingSpawnersOrResettingExistingOrcs()
    {
        TileDataRequirement.SkipIfMissing();
        var spawners = Seed();
        var originalOrcs = CaptureOrcs(spawners);
        Assert.Equal(35, originalOrcs.Count);
        originalOrcs[0].Hits = 1;

        var result = ArborOrcDensity.Ensure(spawners);

        Assert.Equal(3, ArborOrcDensity.Multiplier);
        Assert.Equal(33, result.ChangedSpawners);
        Assert.Equal(70, result.AddedOrcs);
        Assert.Equal(105, result.Population);
        Assert.Equal(105, CaptureOrcs(spawners).Count);
        for (var index = 0; index < spawners.Length; index++)
        {
            var pin = ArborOrcOccupation.Pins[index];
            var spawner = spawners[index];
            Assert.Same(spawner, result.Spawners[index]);
            Assert.Same(spawner, World.FindItem(spawner.Serial));
            Assert.Equal(pin.Count * 3, spawner.Count);
            Assert.Equal(pin.Count * 3, Assert.Single(spawner.Entries).SpawnedMaxCount);
            Assert.Equal(pin.Count * 3, spawner.Spawned.Count);
            Assert.Equal(pin.Position, spawner.Location);
            Assert.Same(Map.Felucca, spawner.Map);
            Assert.Equal(ArborOrcOccupation.ReplacementGuid(pin.Guid), spawner.Guid);
            Assert.Equal(TimeSpan.FromMinutes(pin.MinimumMinutes), spawner.MinDelay);
            Assert.Equal(TimeSpan.FromMinutes(10), spawner.MaxDelay);
            Assert.Equal(pin.HomeRange, spawner.HomeRange);
        }
        AssertPreserved(originalOrcs);
        Assert.Equal(1, originalOrcs[0].Hits);
    }

    [SkippableFact]
    public void AlreadyTripled_IsIdempotent_AndLeavesMissingOrcsForNormalRespawning()
    {
        TileDataRequirement.SkipIfMissing();
        var spawners = Seed();
        ArborOrcDensity.Ensure(spawners);
        var orcs = CaptureOrcs(spawners);
        orcs[0].Hits = 1;

        var again = ArborOrcDensity.Ensure(spawners);

        Assert.Equal(0, again.ChangedSpawners);
        Assert.Equal(0, again.AddedOrcs);
        Assert.Equal(105, again.Population);
        AssertPreserved(orcs);
        Assert.Equal(1, orcs[0].Hits);
        orcs[^1].Delete();
        foreach (var spawner in spawners)
        {
            spawner.Defrag();
        }

        var depleted = ArborOrcDensity.Ensure(spawners);

        Assert.Equal(0, depleted.ChangedSpawners);
        Assert.Equal(0, depleted.AddedOrcs);
        Assert.Equal(104, depleted.Population);
        Assert.Equal(104, CaptureOrcs(spawners).Count);
        Assert.Equal(1, orcs[0].Hits);
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void LateWrongRoleOrGuid_RejectsTheWholeSetBeforeAnyMutation(bool wrongGuid)
    {
        TileDataRequirement.SkipIfMissing();
        var spawners = Seed(wrongGuid ? 1 : 0);
        var originals = CaptureOrcs(spawners);
        originals[0].Hits = 1;

        Assert.Throws<InvalidOperationException>(() => ArborOrcDensity.Ensure(spawners));

        Assert.Equal(35, CaptureOrcs(spawners).Count);
        AssertPreserved(originals);
        Assert.Equal(1, originals[0].Hits);
        for (var index = 0; index < spawners.Length; index++)
        {
            Assert.Equal(ArborOrcOccupation.Pins[index].Count, spawners[index].Count);
            Assert.Equal(ArborOrcOccupation.Pins[index].Count, spawners[index].Entries[0].SpawnedMaxCount);
        }
    }

    [SkippableFact]
    public void NativeSerialization_PreservesTripledCapacitiesAndOwnedOrcs_ForIdempotentReuse()
    {
        TileDataRequirement.SkipIfMissing();
        var spawners = Seed();
        ArborOrcDensity.Ensure(spawners);
        var orcs = CaptureOrcs(spawners);
        orcs[0].Hits = 1;
        foreach (var spawner in spawners)
        {
            var serial = spawner.Serial;
            var writer = new BufferWriter(true);
            spawner.Serialize(writer);
            var bytes = writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();
            spawner.Stop();
            // Native item loading enters sectors itself; detach this in-memory fixture first.
            spawner.Map = Map.Internal;
            var reader = new BufferReader(bytes);
            spawner.Deserialize(reader);
            Assert.Equal(bytes.Length, reader.Position);
            Assert.Equal(serial, spawner.Serial);
            Assert.Same(Map.Felucca, spawner.Map);
        }

        var reused = ArborOrcDensity.Ensure(spawners);

        Assert.Equal(0, reused.ChangedSpawners);
        Assert.Equal(0, reused.AddedOrcs);
        Assert.Equal(105, reused.Population);
        for (var index = 0; index < spawners.Length; index++)
        {
            var capacity = ArborOrcOccupation.Pins[index].Count * 3;
            Assert.Equal(capacity, spawners[index].Count);
            Assert.Equal(capacity, spawners[index].Entries[0].SpawnedMaxCount);
            Assert.Equal(capacity, spawners[index].Spawned.Count);
        }
        AssertPreserved(orcs);
        Assert.Equal(1, orcs[0].Hits);
    }

    private Spawner[] Seed(int lastSiteError = -1)
    {
        var spawners = new Spawner[ArborOrcOccupation.Pins.Length];
        for (var index = 0; index < spawners.Length; index++)
        {
            var pin = ArborOrcOccupation.Pins[index];
            var guid = ArborOrcOccupation.ReplacementGuid(pin.Guid);
            var role = ArborOrcOccupation.Role(pin.Entry);
            if (index == spawners.Length - 1)
            {
                if (lastSiteError == 0)
                {
                    role = "OrcCaptain";
                }
                else if (lastSiteError == 1)
                {
                    guid = Guid.NewGuid();
                }
            }
            var definition = new SpawnerDataDto
            {
                Guid = guid,
                Name = ArborOrcOccupation.ReplacementName,
                Location = pin.Position,
                Map = Map.Felucca,
                Count = pin.Count,
                MinDelay = TimeSpan.FromMinutes(pin.MinimumMinutes),
                MaxDelay = TimeSpan.FromMinutes(10),
                Team = 0,
                HomeRange = pin.HomeRange,
                WalkingRange = -1,
                Entries = [new SpawnerEntry(role, 100, pin.Count)]
            };
            var spawner = (Spawner)definition.ToSpawner();
            _created.Add(spawner);
            spawner.MoveToWorld(pin.Position, Map.Felucca);
            spawner.Respawn();
            Assert.Equal(pin.Count, spawner.Spawned.Count);
            spawners[index] = spawner;
        }
        return spawners;
    }

    private static List<BaseCreature> CaptureOrcs(Spawner[] spawners)
    {
        var orcs = new List<BaseCreature>();
        foreach (var spawner in spawners)
        {
            foreach (var spawn in spawner.Spawned.Keys)
            {
                var orc = Assert.IsAssignableFrom<BaseCreature>(spawn);
                Assert.True(orc is Orc or OrcCaptain or OrcishMage);
                Assert.False(orc.Deleted);
                Assert.Same(spawner, orc.Spawner);
                orcs.Add(orc);
            }
        }
        return orcs;
    }

    private static void AssertPreserved(List<BaseCreature> orcs)
    {
        foreach (var orc in orcs)
        {
            Assert.False(orc.Deleted);
            Assert.Same(orc, World.FindMobile(orc.Serial));
            Assert.Same(Map.Felucca, orc.Map);
            Assert.Contains(orc, ((Spawner)orc.Spawner).Spawned.Keys);
        }
    }
}
