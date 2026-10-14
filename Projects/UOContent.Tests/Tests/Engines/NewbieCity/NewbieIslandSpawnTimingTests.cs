using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Server;
using Server.Engines.Labrynth;
using Server.Engines.Spawners;
using Server.Tests;
using UOContent.Tests.Engines.Spawners;
using Xunit;
using Timing = Server.Engines.Labrynth.NewbieIslandSpawnTiming;

namespace UOContent.Tests.Engines.NewbieCity;

[Collection("Sequential UOContent Tests")]
public class NewbieIslandSpawnTimingTests : IDisposable
{
    private readonly List<Spawner> _created = [];
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "newbie-island-timing-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly JsonSerializerOptions _options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public NewbieIslandSpawnTimingTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        foreach (var spawner in _created)
        {
            spawner.Delete();
        }
        Directory.Delete(_directory, true);
    }

    [Fact]
    public void ExactTargets_OnlyTimingChanges_OutsideBoundaryIsUntouched()
    {
        var manifest = Populate();
        var sentinel = Create(775, 1570);
        var before = Timing.Capture(_created[0]);
        var result = Timing.Ensure(manifest, "fixture", _directory);

        Assert.Equal(43, result.Updated);
        Assert.True(result.AwaitingWorldSave);
        for (var i = 0; i < 43; i++)
        {
            Assert.Equal(TimeSpan.FromSeconds(20), _created[i].MinDelay);
            Assert.Equal(TimeSpan.FromSeconds(20), _created[i].MaxDelay);
            Assert.Equal(TimeSpan.FromSeconds(20), _created[i].NextSpawn);
        }
        Assert.Equal(TimeSpan.FromMinutes(8), sentinel.MinDelay);
        Assert.Equal(TimeSpan.FromMinutes(10), sentinel.MaxDelay);
        Assert.Equal(before.Serial, _created[0].Serial.Value);
        Assert.Equal(JsonSerializer.Serialize(before.Configuration), JsonSerializer.Serialize(Timing.Capture(_created[0]).Configuration));
        Assert.True(File.Exists(Path.Combine(_directory, "before.json")));
        Assert.True(File.Exists(Path.Combine(_directory, "receipt.json")));
    }

    [Fact]
    public void FullSpawner_PreservesSpawn_ArmsTwentySecondsOnlyAfterVacancy()
    {
        var manifest = Populate();
        var spawner = _created[0];
        spawner.Spawn();
        var entity = Assert.Single(spawner.Spawned).Key;
        Timing.Ensure(manifest, "fixture", _directory);

        Assert.Same(entity, Assert.Single(spawner.Spawned).Key);
        Assert.False(entity.Deleted);
        Assert.Equal(TimeSpan.Zero, spawner.NextSpawn);
        entity.Delete();
        Assert.Empty(spawner.Spawned);
        Assert.Equal(TimeSpan.FromSeconds(20), spawner.NextSpawn);
        spawner.OnTick();
        Assert.Single(spawner.Spawned);
    }

    [Fact]
    public void StoppedEmptySpawner_RemainsStoppedAndEmpty()
    {
        var manifest = Populate();
        var spawner = _created[42];
        spawner.Stop();
        spawner.RemoveAllEntries();
        var originalEnd = spawner.End;

        Timing.Ensure(manifest, "fixture", _directory);

        Assert.False(spawner.Running);
        Assert.Empty(spawner.Entries);
        Assert.Empty(spawner.Spawned);
        Assert.Equal(1, spawner.Count);
        Assert.Equal(originalEnd, spawner.End);
        Assert.Equal(TimeSpan.Zero, spawner.NextSpawn);
        Assert.Equal(Timing.Delay, spawner.MinDelay);
        Assert.Equal(Timing.Delay, spawner.MaxDelay);
    }

    [Fact]
    public void SecondVacancy_DoesNotResetCurrentCountdown_AndTickFillsOnlyOne()
    {
        var manifest = Populate();
        var spawner = _created[0];
        spawner.Count = 2;
        spawner.Entries[0].SpawnedMaxCount = 2;
        spawner.Spawn();
        spawner.Spawn();
        var entities = new List<ISpawnable>(spawner.Spawned.Keys);
        Timing.Ensure(manifest, "fixture", _directory);
        entities[0].Delete();
        spawner.DoTimer(TimeSpan.FromSeconds(7));
        entities[1].Delete();
        Assert.Equal(TimeSpan.FromSeconds(7), spawner.NextSpawn);
        spawner.OnTick();
        Assert.Single(spawner.Spawned);
        Assert.Equal(Timing.Delay, spawner.NextSpawn);
    }

    [Fact]
    public void ReceiptReplay_DoesNotResetTimerOrRecreateNaturalPopulationChanges()
    {
        var manifest = Populate();
        Timing.Ensure(manifest, "fixture", _directory);
        _created[0].Spawn();
        var entity = Assert.Single(_created[0].Spawned).Key;
        _created[1].DoTimer(TimeSpan.FromSeconds(6));
        var receipt = File.ReadAllBytes(Path.Combine(_directory, "receipt.json"));

        var result = Timing.Ensure(manifest, "fixture", _directory);

        Assert.Equal(0, result.Updated);
        Assert.Equal(43, result.Verified);
        Assert.False(result.AwaitingWorldSave);
        Assert.Equal(TimeSpan.FromSeconds(6), _created[1].NextSpawn);
        Assert.Same(entity, Assert.Single(_created[0].Spawned).Key);
        Assert.Equal(receipt, File.ReadAllBytes(Path.Combine(_directory, "receipt.json")));
    }

    [Fact]
    public void BinaryRoundTrip_PreservesTwentySecondSettingsAndEntryConfiguration()
    {
        var manifest = Populate();
        Timing.Ensure(manifest, "fixture", _directory);
        var original = _created[0];
        var restored = SpawnerBlob.Read<Spawner>(SpawnerBlob.Write(original), (Serial)0x40009991);
        _created.Add(restored);
        Assert.Equal(Timing.Delay, restored.MinDelay);
        Assert.Equal(Timing.Delay, restored.MaxDelay);
        Assert.Equal(original.Guid, restored.Guid);
        Assert.Equal(original.Count, restored.Count);
        Assert.Equal(original.Running, restored.Running);
        Assert.Equal(original.SpawnBounds, restored.SpawnBounds);
        Assert.Equal(original.Entries[0].SpawnedName, restored.Entries[0].SpawnedName);
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("Extra")]
    [InlineData("Guid")]
    [InlineData("Position")]
    [InlineData("Map")]
    [InlineData("Type")]
    public void ScopeMismatch_RefusesEntireBatchBeforeAnyWrites(string mismatch)
    {
        var manifest = Populate();
        var last = _created[42];
        switch (mismatch)
        {
            case "Missing":
                last.Delete();
                break;
            case "Extra":
                Create(700, 1600);
                break;
            case "Guid":
                last.Guid = Guid.NewGuid();
                break;
            case "Position":
                last.MoveToWorld(new Point3D(last.X, last.Y, 5), Map.Felucca);
                break;
            case "Map":
                last.MoveToWorld(last.Location, Map.Trammel);
                break;
            case "Type":
                var guid = last.Guid;
                var position = last.Location;
                last.Delete();
                var proximity = new ProximitySpawner { Guid = guid };
                _created.Add(proximity);
                proximity.MoveToWorld(position, Map.Felucca);
                break;
        }
        Assert.Throws<InvalidOperationException>(() => Timing.Ensure(manifest, "fixture", _directory));
        Assert.Equal(TimeSpan.FromMinutes(8), _created[0].MinDelay);
        Assert.Equal(TimeSpan.FromMinutes(10), _created[0].MaxDelay);
        Assert.False(File.Exists(Path.Combine(_directory, "before.json")));
        Assert.False(File.Exists(Path.Combine(_directory, "receipt.json")));
    }

    [Theory]
    [InlineData("DuplicateGuid")]
    [InlineData("DuplicatePosition")]
    [InlineData("Outside")]
    [InlineData("Count")]
    public void MalformedManifest_IsRefused(string mismatch)
    {
        var manifest = Populate();
        manifest = mismatch switch
        {
            "Count" => manifest with { Targets = [] },
            _ => manifest
        };
        if (mismatch == "DuplicateGuid")
        {
            manifest.Targets[42] = manifest.Targets[42] with { Guid = manifest.Targets[0].Guid };
        }
        else if (mismatch == "DuplicatePosition")
        {
            manifest.Targets[42] = manifest.Targets[42] with { X = manifest.Targets[0].X };
        }
        else if (mismatch == "Outside")
        {
            manifest.Targets[42] = manifest.Targets[42] with { X = 775 };
        }
        Assert.Throws<InvalidOperationException>(() => Timing.Ensure(manifest, "fixture", _directory));
        Assert.Equal(TimeSpan.FromMinutes(8), _created[0].MinDelay);
    }

    [Fact]
    public void IncompleteArchive_FailsClosedWithoutReapplication()
    {
        var manifest = Populate();
        File.WriteAllText(Path.Combine(_directory, "before.json"), "{}");
        Assert.Throws<InvalidOperationException>(() => Timing.Ensure(manifest, "fixture", _directory));
        Assert.Equal(TimeSpan.FromMinutes(8), _created[0].MinDelay);
    }

    [Fact]
    public void NoMarker_IsInert_AndWrongHashIsRefused()
    {
        var manifest = Populate();
        File.WriteAllText(Path.Combine(_directory, "manifest.json"), JsonSerializer.Serialize(manifest, _options));
        Assert.Null(Timing.RunPending(_directory));
        File.WriteAllText(Path.Combine(_directory, "install.pending"),
            JsonSerializer.Serialize(new Timing.Marker(Timing.InstallationId, "wrong"), _options));
        Assert.Throws<InvalidOperationException>(() => Timing.RunPending(_directory));
        Assert.Equal(TimeSpan.FromMinutes(8), _created[0].MinDelay);
    }

    [Fact]
    public void ModifiedReceiptTarget_IsRefusedWithoutTimerReset()
    {
        var manifest = Populate();
        Timing.Ensure(manifest, "fixture", _directory);
        _created[42].Count = 2;
        _created[0].DoTimer(TimeSpan.FromSeconds(7));
        Assert.Throws<InvalidOperationException>(() => Timing.Ensure(manifest, "fixture", _directory));
        Assert.Equal(TimeSpan.FromSeconds(7), _created[0].NextSpawn);
    }

    [SkippableFact]
    public void ApprovedManifest_HasExactReviewedHashAndFortyThreeIslandPins()
    {
        var path = Environment.GetEnvironmentVariable("NEWBIE_ISLAND_TIMING_MANIFEST");
        Skip.If(string.IsNullOrWhiteSpace(path), "Set NEWBIE_ISLAND_TIMING_MANIFEST to validate the deployment artifact.");
        var bytes = File.ReadAllBytes(path!);
        Assert.Equal("f1b292258501cc125544130bfd3a6b20c20f0095e05465c77c1de4b9d9cc713d", Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Timing.ValidateManifest(JsonSerializer.Deserialize<Timing.Manifest>(bytes, _options));
    }

    private Timing.Manifest Populate()
    {
        var pins = new Timing.Pin[43];
        for (var i = 0; i < pins.Length; i++)
        {
            var spawner = Create(652 + i, 1570);
            pins[i] = new Timing.Pin(spawner.Guid, spawner.X, spawner.Y, spawner.Z);
        }
        return new Timing.Manifest(Timing.InstallationId, pins);
    }

    private Spawner Create(int x, int y)
    {
        var spawner = new Spawner(1, TimeSpan.FromMinutes(8), TimeSpan.FromMinutes(10), spawnedNames: "Gold");
        _created.Add(spawner);
        spawner.MoveToWorld(new Point3D(x, y, 0), Map.Felucca);
        spawner.HomeRange = 0;
        spawner.DoTimer(TimeSpan.FromMinutes(8));
        return spawner;
    }
}
