using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Server.Engines.Spawners;
using Server.Logging;

namespace Server.Engines.Labrynth;

public static class NewbieIslandSpawnTiming
{
    internal const string InstallationId = "newbie-island-spawn-timing-20s-v1";
    internal const int TargetCount = 43;
    internal static readonly Rectangle2D Bounds = new(652, 1570, 123, 148);
    internal static readonly TimeSpan Delay = TimeSpan.FromSeconds(20);
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(NewbieIslandSpawnTiming));
    private static readonly JsonSerializerOptions _options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private static TimerExecutionToken _timer;

    internal sealed record Pin(Guid Guid, int X, int Y, int Z);
    internal sealed record Manifest(string InstallationId, Pin[] Targets);
    internal sealed record Marker(string InstallationId, string ManifestSha256);
    internal sealed record Entry(string Name, int Probability, int MaxCount, string Properties, string Parameters, bool Disabled);
    internal sealed record Configuration(string Name, int Count, bool Group, int Team, int WalkingRange,
        int BoundsX, int BoundsY, int BoundsZ, int BoundsWidth, int BoundsHeight, int BoundsDepth,
        bool SpawnLocationIsHome, bool ReturnOnDeactivate, uint WayPointSerial, bool UseSpiralScan,
        int SpawnPositionMode, int MaxSpawnAttempts, Entry[] Entries);
    internal sealed record Spawn(uint Serial, int EntryIndex);
    internal sealed record Snapshot(uint Serial, Pin Pin, TimeSpan MinDelay, TimeSpan MaxDelay, DateTime End,
        TimeSpan NextSpawn, bool Running, Configuration Configuration, Spawn[] Spawned);
    internal sealed record Archive(string InstallationId, string ManifestSha256, Snapshot[] Targets);
    internal sealed record Receipt(string InstallationId, string ManifestSha256, string Stage, Snapshot[] Targets);
    internal sealed record Result(bool Success, string InstallationId, int Updated, int Verified, bool AwaitingWorldSave, string Error);

    public static void Initialize()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data", "NewbieIslandSpawnTiming");
        if (!File.Exists(Path.Combine(directory, "install.pending")))
        {
            return;
        }
        EventSink.Shutdown += () => _timer.Cancel();
        Timer.StartTimer(TimeSpan.FromSeconds(1), () => Begin(directory), out _timer);
    }

    private static void Begin(string directory)
    {
        if (World.Saving || World.WorldState is WorldState.PendingSave or WorldState.WritingSave)
        {
            Timer.StartTimer(TimeSpan.FromSeconds(1), () => Begin(directory), out _timer);
            return;
        }
        try
        {
            var result = RunPending(directory);
            if (result != null)
            {
                WriteJson(Path.Combine(directory, "result.json"), result);
                _logger.Information("Newbie island spawn timing updated {Updated}, verified {Verified}; awaiting save {AwaitingSave}",
                    result.Updated, result.Verified, result.AwaitingWorldSave);
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Newbie island spawn timing installation refused");
            try
            {
                WriteJson(Path.Combine(directory, "result.json"), new Result(false, InstallationId, 0, 0, false, exception.Message));
            }
            catch (Exception writeException)
            {
                _logger.Error(writeException, "Could not write newbie island spawn timing refusal");
            }
        }
    }

    internal static Result RunPending(string directory)
    {
        var markerPath = Path.Combine(directory, "install.pending");
        if (!File.Exists(markerPath))
        {
            return null;
        }
        var marker = JsonSerializer.Deserialize<Marker>(ReadBounded(markerPath, 1024), _options);
        var bytes = ReadBounded(Path.Combine(directory, "manifest.json"), 32768);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Require(marker is { InstallationId: InstallationId } && marker.ManifestSha256 == hash,
            "The pending marker must authorize this exact manifest.");
        var manifest = JsonSerializer.Deserialize<Manifest>(bytes, _options);
        return Ensure(manifest, hash, directory);
    }

    internal static void ValidateManifest(Manifest manifest)
    {
        Require(manifest is { InstallationId: InstallationId, Targets.Length: TargetCount }, "The manifest must pin exactly 43 island spawners.");
        var guids = new HashSet<Guid>();
        var positions = new HashSet<(int, int, int)>();
        foreach (var pin in manifest.Targets)
        {
            Require(pin != null && pin.Guid != Guid.Empty && guids.Add(pin.Guid) &&
                    pin.X is >= 652 and < 775 && pin.Y is >= 1570 and < 1718 &&
                    positions.Add((pin.X, pin.Y, pin.Z)), "Invalid, duplicate or out-of-island spawner pin.");
        }
    }

    internal static Result Ensure(Manifest manifest, string hash, string directory)
    {
        ValidateManifest(manifest);
        var spawners = Survey(manifest);
        var beforePath = Path.Combine(directory, "before.json");
        var receiptPath = Path.Combine(directory, "receipt.json");
        if (File.Exists(receiptPath))
        {
            var before = JsonSerializer.Deserialize<Archive>(ReadBounded(beforePath, 1048576), _options);
            var receipt = JsonSerializer.Deserialize<Receipt>(ReadBounded(receiptPath, 1048576), _options);
            Require(before is { InstallationId: InstallationId, Targets.Length: TargetCount } && before.ManifestSha256 == hash &&
                    receipt is { InstallationId: InstallationId, Stage: "installed", Targets.Length: TargetCount } &&
                    receipt.ManifestSha256 == hash, "The existing receipt or archive has a different identity or is incomplete.");
            for (var i = 0; i < spawners.Length; i++)
            {
                var current = Capture(spawners[i]);
                ValidatePreserved(current, before.Targets[i], false);
                ValidatePreserved(current, receipt.Targets[i], false);
                Require(current.MinDelay == Delay && current.MaxDelay == Delay, "A recorded spawner no longer has 20-second delays.");
            }
            return new Result(true, InstallationId, 0, TargetCount, false, null);
        }
        Require(!File.Exists(beforePath), "An incomplete installation archive exists; automatic reapplication is refused.");
        var snapshots = new Snapshot[spawners.Length];
        for (var i = 0; i < spawners.Length; i++)
        {
            snapshots[i] = Capture(spawners[i]);
        }
        WriteNewJson(beforePath, new Archive(InstallationId, hash, snapshots));

        // The bounded batch cannot interleave with creature deaths or other timer callbacks.
        for (var i = 0; i < spawners.Length; i++)
        {
            var spawner = spawners[i];
            spawner.MinDelay = Delay;
            spawner.MaxDelay = Delay;
            if (spawner.Running)
            {
                spawner.DoTimer(Delay);
            }
        }
        var after = new Snapshot[spawners.Length];
        for (var i = 0; i < spawners.Length; i++)
        {
            after[i] = Capture(spawners[i]);
            ValidatePreserved(after[i], snapshots[i], true);
            Require(after[i].MinDelay == Delay && after[i].MaxDelay == Delay, "Spawner delay verification failed.");
        }
        WriteJson(receiptPath, new Receipt(InstallationId, hash, "installed", after));
        return new Result(true, InstallationId, TargetCount, TargetCount, true, null);
    }

    private static Spawner[] Survey(Manifest manifest)
    {
        var found = new Dictionary<Guid, Spawner>();
        foreach (var item in Map.Felucca.GetItemsInBounds<Item>(Bounds))
        {
            if (item is not ISpawner)
            {
                continue;
            }
            Require(item.GetType() == typeof(Spawner) && found.Count < 128, "Unexpected spawner type or excessive island spawner count.");
            var spawner = (Spawner)item;
            Require(!spawner.Deleted && spawner.Parent == null && spawner.Map == Map.Felucca &&
                    ReferenceEquals(World.FindItem(spawner.Serial), spawner) && found.TryAdd(spawner.Guid, spawner),
                "Conflicting or unregistered island spawner identity.");
        }
        Require(found.Count == TargetCount, "The island spawner set has missing or additional members.");
        var ordered = new Spawner[TargetCount];
        for (var i = 0; i < ordered.Length; i++)
        {
            var pin = manifest.Targets[i];
            Require(found.TryGetValue(pin.Guid, out var spawner) && spawner.Location == new Point3D(pin.X, pin.Y, pin.Z),
                "An island spawner GUID or position differs from its approved pin.");
            ordered[i] = spawner;
        }
        return ordered;
    }

    internal static Snapshot Capture(Spawner spawner)
    {
        Require(spawner.Entries.Count <= 128 && spawner.Spawned is { Count: <= 512 }, "A spawner exceeds bounded snapshot capacity.");
        var entries = new Entry[spawner.Entries.Count];
        var entryIndexes = new Dictionary<SpawnerEntry, int>();
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = spawner.Entries[i];
            entries[i] = new Entry(entry.SpawnedName, entry.SpawnedProbability, entry.SpawnedMaxCount,
                entry.Properties, entry.Parameters, entry.Disabled);
            entryIndexes.Add(entry, i);
        }
        var spawned = new Spawn[spawner.Spawned.Count];
        var index = 0;
        foreach (var (entity, entry) in spawner.Spawned)
        {
            Require(entity is { Deleted: false } && ReferenceEquals(entity.Spawner, spawner) && entryIndexes.ContainsKey(entry),
                "The existing spawn registry is inconsistent.");
            spawned[index++] = new Spawn(entity.Serial.Value, entryIndexes[entry]);
        }
        Array.Sort(spawned, (left, right) => left.Serial.CompareTo(right.Serial));
        var bounds = spawner.SpawnBounds;
        var configuration = new Configuration(spawner.Name, spawner.Count, spawner.Group, spawner.Team, spawner.WalkingRange,
            bounds.Start.X, bounds.Start.Y, bounds.Start.Z, bounds.Width, bounds.Height, bounds.Depth,
            spawner.SpawnLocationIsHome, spawner.ReturnOnDeactivate, spawner.WayPoint?.Serial.Value ?? 0, spawner.UseSpiralScan,
            (int)spawner.SpawnPositionMode, spawner.MaxSpawnAttempts, entries);
        return new Snapshot(spawner.Serial.Value, new Pin(spawner.Guid, spawner.X, spawner.Y, spawner.Z),
            spawner.MinDelay, spawner.MaxDelay, spawner.End, spawner.NextSpawn, spawner.Running, configuration, spawned);
    }

    private static void ValidatePreserved(Snapshot current, Snapshot original, bool checkSpawned)
    {
        Require(original != null && current.Serial == original.Serial && current.Pin == original.Pin &&
                current.Running == original.Running &&
                JsonSerializer.Serialize(current.Configuration, _options) == JsonSerializer.Serialize(original.Configuration, _options),
            "An unrelated spawner property or identity changed.");
        if (checkSpawned)
        {
            Require(original.Spawned != null && current.Spawned.AsSpan().SequenceEqual(original.Spawned),
                "Existing spawned entities or their entry ownership changed.");
        }
    }

    private static byte[] ReadBounded(string path, int maximum)
    {
        Require(new FileInfo(path).Length is > 0 && new FileInfo(path).Length <= maximum, "Invalid deployment file size.");
        var bytes = File.ReadAllBytes(path);
        Require(bytes.Length <= maximum, "The deployment file grew while reading.");
        return bytes;
    }

    private static void WriteNewJson<T>(string path, T value)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, value, _options);
        stream.Flush(true);
    }

    private static void WriteJson<T>(string path, T value)
    {
        var temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, value, _options);
            stream.Flush(true);
        }
        File.Move(temporary, path, true);
    }

    private static void Require(bool condition, string error)
    {
        if (!condition)
        {
            throw new InvalidOperationException(error);
        }
    }
}
