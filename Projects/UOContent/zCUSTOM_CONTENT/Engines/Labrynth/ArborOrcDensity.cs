using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Server.Engines.Spawners;
using Server.Logging;
using Server.Mobiles;

namespace Server.Engines.Labrynth;

public static class ArborOrcDensity
{
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(ArborOrcDensity));
    private static readonly JsonSerializerOptions _resultOptions = new() { WriteIndented = true };
    internal const int Multiplier = 3;
    internal sealed record Result(int ChangedSpawners, int AddedOrcs, int Population, Spawner[] Spawners);

    public static void Initialize()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data", "ArborOrcOccupation");
        if (!File.Exists(Path.Combine(directory, "density.pending")))
        {
            return;
        }

        var resultPath = Path.Combine(directory, "density-result.json");
        try
        {
            var result = Ensure(Resolve(Path.Combine(directory, "result.json")));
            var definitions = new SpawnerDto[result.Spawners.Length];
            var sites = new object[result.Spawners.Length];
            var capacity = 0;
            for (var index = 0; index < result.Spawners.Length; index++)
            {
                var spawner = result.Spawners[index];
                definitions[index] = spawner.ToDto();
                capacity += spawner.Count;
                sites[index] = new
                {
                    Serial = spawner.Serial.Value, spawner.Guid,
                    spawner.X, spawner.Y, spawner.Z, Map = "Felucca",
                    Orc = spawner.Entries[0].SpawnedName, spawner.Count,
                    EntryMaximum = spawner.Entries[0].SpawnedMaxCount,
                    Population = CountLivingSpawns(spawner)
                };
            }
            File.WriteAllText(Path.Combine(directory, "density-spawners.json"),
                JsonSerializer.Serialize(definitions, SpawnerJsonSerializer.Options));
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new
            {
                Success = true, result.ChangedSpawners, result.AddedOrcs, result.Population,
                Capacity = capacity, Multiplier, Spawners = sites, AwaitingWorldSave = true
            }, _resultOptions));
            _logger.Information("Arbor orc density verified: changed spawners={Changed}, added orcs={Added}, population={Population}; awaiting world save",
                result.ChangedSpawners, result.AddedOrcs, result.Population);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Arbor orc density refused; authorization remains pending");
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new { Success = false, Error = exception.Message }, _resultOptions));
        }
    }

    private static Spawner[] Resolve(string receiptPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(receiptPath));
        var root = document.RootElement;
        var routes = root.GetProperty("Routes");
        if (!root.GetProperty("Success").GetBoolean() || routes.GetArrayLength() != ArborOrcOccupation.Pins.Length)
        {
            throw new InvalidOperationException("Arbor density requires the successful 33-site occupation receipt.");
        }

        var spawners = new Spawner[ArborOrcOccupation.Pins.Length];
        var serials = new HashSet<uint>();
        for (var index = 0; index < spawners.Length; index++)
        {
            var pin = ArborOrcOccupation.Pins[index];
            var route = routes[index];
            var position = route.GetProperty("Position");
            var serial = route.GetProperty("ReplacementSerial").GetUInt32();
            if (route.GetProperty("OriginalGuid").GetGuid() != pin.Guid ||
                route.GetProperty("ReplacementGuid").GetGuid() != ArborOrcOccupation.ReplacementGuid(pin.Guid) ||
                route.GetProperty("Orc").GetString() != ArborOrcOccupation.Role(pin.Entry) ||
                position.GetProperty("X").GetInt32() != pin.Position.X ||
                position.GetProperty("Y").GetInt32() != pin.Position.Y ||
                position.GetProperty("Z").GetInt32() != pin.Position.Z ||
                position.GetProperty("Map").GetString() != "Felucca" || !serials.Add(serial))
            {
                throw new InvalidOperationException("Arbor occupation receipt differs from the pinned town sites.");
            }
            spawners[index] = World.FindItem((Serial)serial) as Spawner;
        }
        return spawners;
    }

    internal static Result Ensure(Spawner[] spawners)
    {
        if (spawners?.Length != ArborOrcOccupation.Pins.Length)
        {
            throw new InvalidOperationException("Arbor density requires all 33 occupation spawners.");
        }

        var originalSpawns = new HashSet<ISpawnable>[spawners.Length];
        var running = new bool[spawners.Length];
        var tripled = 0;
        var population = 0;
        for (var index = 0; index < spawners.Length; index++)
        {
            var spawner = spawners[index];
            var pin = ArborOrcOccupation.Pins[index];
            Validate(spawner, pin);
            originalSpawns[index] = new HashSet<ISpawnable>(spawner.Spawned.Keys);
            running[index] = spawner.Running;
            population += CountLivingSpawns(spawner);
            if (spawner.Count == pin.Count * Multiplier)
            {
                tripled++;
            }
        }
        if (tripled != 0 && tripled != spawners.Length)
        {
            throw new InvalidOperationException("Arbor density is only partially installed.");
        }
        if (tripled == spawners.Length)
        {
            // Ordinary deaths remain governed by the existing respawn timers.
            return new Result(0, 0, population, spawners);
        }

        var added = 0;
        try
        {
            for (var index = 0; index < spawners.Length; index++)
            {
                var spawner = spawners[index];
                var target = ArborOrcOccupation.Pins[index].Count * Multiplier;
                spawner.Entries[0].SpawnedMaxCount = target;
                spawner.Count = target;
                var current = CountLivingSpawns(spawner);
                var missing = target - current;
                for (var spawnIndex = 0; spawnIndex < missing; spawnIndex++)
                {
                    // Respawn replaces the current creatures; Spawn fills only the new capacity.
                    spawner.Spawn();
                    var after = CountLivingSpawns(spawner);
                    if (after != current + 1)
                    {
                        throw new InvalidOperationException($"Arbor could not fill the increased capacity at {spawner.Location}.");
                    }
                    current = after;
                    added++;
                }
                Validate(spawner, ArborOrcOccupation.Pins[index]);
                foreach (var spawn in spawner.Spawned.Keys)
                {
                    if (!originalSpawns[index].Contains(spawn) &&
                        spawn is Mobile mobile && !Map.Felucca.CanFit(mobile.Location, 16, true, false))
                    {
                        throw new InvalidOperationException($"An added Arbor orc has no supported position at {mobile.Location}.");
                    }
                }
                spawner.DoTimer();
            }
        }
        catch
        {
            for (var index = 0; index < spawners.Length; index++)
            {
                var spawner = spawners[index];
                var addedSpawns = new List<ISpawnable>();
                foreach (var spawn in spawner.Spawned.Keys)
                {
                    if (!originalSpawns[index].Contains(spawn))
                    {
                        addedSpawns.Add(spawn);
                    }
                }
                foreach (var spawn in addedSpawns)
                {
                    spawn.Delete();
                }
                spawner.Entries[0].SpawnedMaxCount = ArborOrcOccupation.Pins[index].Count;
                spawner.Count = ArborOrcOccupation.Pins[index].Count;
                spawner.Running = running[index];
                spawner.DoTimer();
            }
            throw;
        }
        return new Result(spawners.Length, added, population + added, spawners);
    }

    private static void Validate(Spawner spawner, ArborOrcOccupation.Pin pin)
    {
        if (spawner == null || spawner.Deleted || spawner.GetType() != typeof(Spawner) ||
            spawner.Parent != null || spawner.Map != Map.Felucca || spawner.Location != pin.Position ||
            spawner.Name != ArborOrcOccupation.ReplacementName ||
            spawner.Guid != ArborOrcOccupation.ReplacementGuid(pin.Guid))
        {
            throw new InvalidOperationException($"Arbor occupation spawner is missing or unexpected at {pin.Position}.");
        }

        var definition = (SpawnerDataDto)spawner.ToDto();
        if (definition.Count != pin.Count && definition.Count != pin.Count * Multiplier ||
            definition.MinDelay != TimeSpan.FromMinutes(pin.MinimumMinutes) ||
            definition.MaxDelay != TimeSpan.FromMinutes(10) || definition.Team != 0 ||
            definition.WalkingRange != -1 || definition.HomeRange != pin.HomeRange ||
            definition.SpawnBounds != default || definition.Group || definition.SpawnLocationIsHome ||
            spawner.SpawnPositionMode != SpawnPositionMode.Automatic || definition.MaxSpawnAttempts != 0 ||
            spawner.UseSpiralScan || spawner.Entries.Count != 1)
        {
            throw new InvalidOperationException($"Arbor occupation configuration differs at {pin.Position}.");
        }

        var entry = spawner.Entries[0];
        var role = ArborOrcOccupation.Role(pin.Entry);
        if (entry.SpawnedName != role || entry.SpawnedMaxCount != spawner.Count ||
            entry.SpawnedProbability != 100 || entry.Properties != null || entry.Parameters != null || entry.Disabled ||
            spawner.Spawned == null || entry.Spawned.Count != spawner.Spawned.Count)
        {
            throw new InvalidOperationException($"Arbor occupation entry differs at {pin.Position}.");
        }

        var count = 0;
        foreach (var pair in spawner.Spawned)
        {
            var spawn = pair.Key;
            if (pair.Value != entry || !entry.Spawned.Contains(spawn))
            {
                throw new InvalidOperationException("An Arbor spawn has inconsistent native ownership.");
            }
            if (spawn.Deleted)
            {
                continue;
            }
            if (spawn is not BaseCreature creature || creature.Player || creature.Controlled || creature.Summoned ||
                creature.IsStabled || creature.Spawner != spawner || creature.Map != Map.Felucca ||
                creature.GetType() != AssemblyHandler.FindTypeByName(role))
            {
                throw new InvalidOperationException("An Arbor occupation spawner owns a protected or unexpected creature.");
            }
            count++;
        }
        if (count > spawner.Count)
        {
            throw new InvalidOperationException("An Arbor occupation spawner exceeds its approved capacity.");
        }
        foreach (var candidate in Map.Felucca.GetItemsAt<BaseSpawner>(pin.Position))
        {
            if (candidate != spawner)
            {
                throw new InvalidOperationException($"A conflicting spawner occupies Arbor site {pin.Position}.");
            }
        }
    }

    private static int CountLivingSpawns(Spawner spawner)
    {
        var count = 0;
        foreach (var spawn in spawner.Spawned.Keys)
        {
            if (!spawn.Deleted)
            {
                count++;
            }
        }
        return count;
    }
}
