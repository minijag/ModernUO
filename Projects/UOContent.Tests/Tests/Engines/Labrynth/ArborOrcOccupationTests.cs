using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Server;
using Server.Engines.Labrynth;
using Server.Engines.Spawners;
using Server.Mobiles;
using Server.Tests;
using Xunit;

namespace UOContent.Tests.Engines.Labrynth;

[Collection("Sequential UOContent Tests")]
public class ArborOrcOccupationTests : IDisposable
{
    private readonly List<IEntity> _created = [];
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ArborOrcOccupationTests-" + Guid.NewGuid());
    private string ArchivePath => Path.Combine(_directory, "original-spawners.json");

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
    public void Plan_PinsAll33TownSpawners_AndStockT2ARoles()
    {
        Assert.Equal(33, ArborOrcOccupation.Pins.Length);
        var unique = new HashSet<Guid>();
        var roles = new Dictionary<string, int>();
        foreach (var pin in ArborOrcOccupation.Pins)
        {
            Assert.InRange(pin.Position.X, 1731, 1830);
            Assert.InRange(pin.Position.Y, 971, 1122);
            Assert.True(unique.Add(pin.Guid));
            Assert.NotEqual(pin.Guid, ArborOrcOccupation.ReplacementGuid(pin.Guid));
            var role = ArborOrcOccupation.Role(pin.Entry);
            roles[role] = roles.GetValueOrDefault(role) + 1;
        }
        Assert.Equal(23, roles["Orc"]);
        Assert.Equal(5, roles["OrcCaptain"]);
        Assert.Equal(5, roles["OrcishMage"]);
    }

    [SkippableFact]
    public void Occupation_ArchivesBeforeReplacing_PreservesNativeObjectsLootAndWildlife_IsIdempotent()
    {
        TileDataRequirement.SkipIfMissing();
        var plan = MakePlan();
        var (originals, humans) = Seed(plan);
        var first = ArborOrcOccupation.Pins[0].Position;
        var wildlife = Track(new Spawner(1, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(4), 0, default, "Bird"));
        wildlife.MoveToWorld(new Point3D(first.X + 2, first.Y, first.Z), Map.Felucca);
        wildlife.Stop();
        var player = Track(new Mobile { Player = true, Body = 0x190 });
        player.MoveToWorld(first, Map.Felucca);
        var inventory = Track(new Item(0xEED));
        humans[0].AddItem(inventory);
        var bounds = originals[0].SpawnBounds;

        var result = ArborOrcOccupation.Ensure(plan, ArchivePath);
        TrackReplacements(result.Replacements);

        Assert.Equal(33, result.Created);
        Assert.Equal(32, result.ArchivedNpcCount);
        Assert.True(File.Exists(ArchivePath));
        var archiveText = File.ReadAllText(ArchivePath);
        var archive = JsonSerializer.Deserialize<ArborOrcOccupation.Archive>(archiveText);
        Assert.Equal(33, archive.OriginalDefinitions.Length);
        Assert.Equal(33, archive.Spawners.Length);
        for (var index = 0; index < 33; index++)
        {
            var pin = ArborOrcOccupation.Pins[index];
            Assert.Same(originals[index], World.FindItem((Serial)archive.Spawners[index].Serial));
            Assert.Same(Map.Internal, originals[index].Map);
            Assert.False(originals[index].Running);
            Assert.Equal(pin.Position, originals[index].Location);
            if (humans[index] != null)
            {
                Assert.Same(Map.Internal, humans[index].Map);
                Assert.Same(originals[index], humans[index].Spawner);
                Assert.Contains(humans[index], originals[index].Spawned.Keys);
                Assert.False(humans[index].Deleted);
            }
            Assert.Same(Map.Felucca, result.Replacements[index].Map);
            Assert.Equal(pin.Position, result.Replacements[index].Location);
            Assert.Equal(ArborOrcOccupation.Role(pin.Entry), result.Replacements[index].Entries[0].SpawnedName);
            Assert.Equal(ArborOrcOccupation.ReplacementGuid(pin.Guid), result.Replacements[index].Guid);
            Assert.Equal(originals[index].Count, result.Replacements[index].Count);
            Assert.Equal(originals[index].MinDelay, result.Replacements[index].MinDelay);
            Assert.Equal(originals[index].MaxDelay, result.Replacements[index].MaxDelay);
            Assert.Equal(originals[index].HomeRange, result.Replacements[index].HomeRange);
            Assert.Equal(pin.Count, result.Replacements[index].Spawned.Count);
            Assert.False(originals[index].Deleted);
        }
        Assert.Equal(bounds, originals[0].SpawnBounds);
        Assert.Same(humans[0], inventory.Parent);
        Assert.False(inventory.Deleted);
        Assert.Same(Map.Felucca, wildlife.Map);
        Assert.Equal("Bird", wildlife.Entries[0].SpawnedName);
        Assert.Same(Map.Felucca, player.Map);
        Assert.Equal(first, player.Location);

        var again = ArborOrcOccupation.Ensure(plan, ArchivePath);

        Assert.Equal(0, again.Created);
        Assert.Same(result.Replacements[0], again.Replacements[0]);
        Assert.Equal(archiveText, File.ReadAllText(ArchivePath));
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ProtectedOwnedNpc_FailsFullPreflight_BeforeAnyArchiveOrMutation(int protection)
    {
        TileDataRequirement.SkipIfMissing();
        var plan = MakePlan();
        var (originals, humans) = Seed(plan, protection);
        Assert.Throws<InvalidOperationException>(() => ArborOrcOccupation.Ensure(plan, ArchivePath));
        Assert.False(File.Exists(ArchivePath));
        for (var index = 0; index < 33; index++)
        {
            Assert.Same(Map.Felucca, originals[index].Map);
            if (humans[index] != null)
            {
                Assert.Same(Map.Felucca, humans[index].Map);
            }
        }
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void LastSiteConflictOrPlanMutation_LeavesAllOriginalsUntouched(bool planMutation)
    {
        TileDataRequirement.SkipIfMissing();
        var plan = MakePlan();
        var (originals, _) = Seed(plan);
        if (planMutation)
        {
            var changed = MakePlan();
            changed.Replacements[32] = changed.Replacements[0];
            plan = changed;
        }
        else
        {
            var conflict = Track(new Spawner("Sheep"));
            conflict.MoveToWorld(ArborOrcOccupation.Pins[32].Position, Map.Felucca);
            conflict.Stop();
        }

        Assert.Throws<InvalidOperationException>(() => ArborOrcOccupation.Ensure(plan, ArchivePath));

        Assert.False(File.Exists(ArchivePath));
        foreach (var original in originals)
        {
            Assert.Same(Map.Felucca, original.Map);
            Assert.False(original.Deleted);
        }
    }

    [SkippableFact]
    public void ExistingInvalidRecoveryArchive_IsNeverOverwritten_AndCannotAuthorizeMutation()
    {
        TileDataRequirement.SkipIfMissing();
        var plan = MakePlan();
        var (originals, _) = Seed(plan);
        Directory.CreateDirectory(_directory);
        File.WriteAllText(ArchivePath, "{}");

        Assert.Throws<InvalidOperationException>(() => ArborOrcOccupation.Ensure(plan, ArchivePath));

        Assert.Equal("{}", File.ReadAllText(ArchivePath));
        Assert.All(originals, original => Assert.Same(Map.Felucca, original.Map));
    }

    [SkippableFact]
    public void NativeSpawnerSerialization_PreservesArchivedRefs_AndReusesReplacementsAfterReload()
    {
        TileDataRequirement.SkipIfMissing();
        var plan = MakePlan();
        Seed(plan);
        var result = ArborOrcOccupation.Ensure(plan, ArchivePath);
        TrackReplacements(result.Replacements);
        for (var index = 0; index < 33; index++)
        {
            RoundTrip(result.Originals[index]);
            RoundTrip(result.Replacements[index]);
        }

        var reused = ArborOrcOccupation.Ensure(plan, ArchivePath);

        Assert.Equal(0, reused.Created);
        Assert.Equal(32, reused.ArchivedNpcCount);
        Assert.All(reused.Originals, original => Assert.Same(Map.Internal, original.Map));
        Assert.All(reused.Replacements, replacement => Assert.Same(Map.Felucca, replacement.Map));
    }

    private void RoundTrip(Spawner source)
    {
        var writer = new BufferWriter(true);
        source.Serialize(writer);
        var bytes = writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();
        var serial = source.Serial;
        var guid = source.Guid;
        var map = source.Map;
        source.Stop();
        // Native item loading enters sectors itself; detach this in-memory fixture first.
        source.Map = Map.Internal;
        var reader = new BufferReader(bytes);
        source.Deserialize(reader);
        Assert.Equal(bytes.Length, reader.Position);
        Assert.Equal(serial, source.Serial);
        Assert.Equal(guid, source.Guid);
        Assert.Same(map, source.Map);
    }

    private (Spawner[] Spawners, Mobile[] Npcs) Seed(ArborOrcOccupation.Plan plan, int protection = -1)
    {
        var originals = new Spawner[33];
        var humans = new Mobile[33];
        for (var index = 0; index < 33; index++)
        {
            var dto = JsonSerializer.Deserialize<SpawnerDataDto>(plan.Originals[index].GetRawText(), SpawnerJsonSerializer.Options);
            var spawner = Track((Spawner)dto.ToSpawner());
            spawner.MoveToWorld(dto.Location, dto.Map);
            spawner.Stop();
            var type = AssemblyHandler.FindTypeByName(dto.Entries[0].SpawnedName);
            if (type == null)
            {
                originals[index] = spawner;
                continue;
            }
            var npc = Track((Mobile)Activator.CreateInstance(type));
            if (index == 24)
            {
                npc.Player = protection == 0;
                if (npc is BaseCreature creature)
                {
                    creature.Controlled = protection == 1;
                    creature.Summoned = protection == 2;
                    creature.IsStabled = protection == 3;
                }
                if (protection == 4)
                {
                    npc.Delete();
                    npc = Track(new Mobile { Body = 0x190 });
                }
            }
            npc.MoveToWorld(dto.Location, dto.Map);
            npc.Spawner = spawner;
            spawner.Spawned.Add(npc, spawner.Entries[0]);
            spawner.Entries[0].AddToSpawned(npc);
            originals[index] = spawner;
            humans[index] = npc;
        }
        return (originals, humans);
    }

    private static ArborOrcOccupation.Plan MakePlan()
    {
        var originals = new JsonElement[33];
        var replacements = new JsonElement[33];
        for (var index = 0; index < 33; index++)
        {
            var pin = ArborOrcOccupation.Pins[index];
            originals[index] = Definition(pin, false);
            replacements[index] = Definition(pin, true);
        }
        return new ArborOrcOccupation.Plan(originals, replacements);
    }

    private static JsonElement Definition(ArborOrcOccupation.Pin pin, bool replacement)
    {
        var names = replacement ? new[] { ArborOrcOccupation.Role(pin.Entry) } : pin.Entry.Split(',');
        var entries = new object[names.Length];
        for (var index = 0; index < names.Length; index++)
        {
            entries[index] = new { name = names[index], probability = 100, maxCount = pin.Count };
        }
        return JsonSerializer.SerializeToElement(new
        {
            type = "Spawner", name = replacement ? ArborOrcOccupation.ReplacementName : "Spawner",
            guid = replacement ? ArborOrcOccupation.ReplacementGuid(pin.Guid) : pin.Guid,
            location = new { x = pin.Position.X, y = pin.Position.Y, z = pin.Position.Z }, map = "Felucca",
            count = pin.Count, minDelay = TimeSpan.FromMinutes(pin.MinimumMinutes).ToString(),
            maxDelay = TimeSpan.FromMinutes(10).ToString(), team = 0, homeRange = pin.HomeRange, walkingRange = -1,
            entries
        });
    }

    private void TrackReplacements(Spawner[] replacements)
    {
        foreach (var replacement in replacements)
        {
            _created.Add(replacement);
        }
    }

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }
}

