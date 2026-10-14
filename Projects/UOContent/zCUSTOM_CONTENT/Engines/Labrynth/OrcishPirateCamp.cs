using System;
using System.IO;
using System.Text.Json;
using Server.Engines.Spawners;
using Server.Logging;
using Server.Mobiles;

namespace Server.Engines.Labrynth;

public static class OrcishPirateCamp
{
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(OrcishPirateCamp));
    internal const int CrewSize = 5;
    internal const string CampName = "Orcish pirate crew";
    internal const string PirateName = "an orcish pirate";
    internal const string EntryProperties = "Name \"an orcish pirate\"";
    internal static readonly Guid CampGuid = new("c6545783-386a-4e51-a2ca-99e185d1f172");
    internal static readonly Point3D Position = new(1773, 910, 20);
    // The ship deck sits above both terrain and a lower cabin; restrict spawning to the deck.
    internal static readonly Rectangle3D Bounds = new(1769, 906, 20, 9, 9, 1);

    public static void Initialize()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data", "OrcishPirates");
        if (!File.Exists(Path.Combine(directory, "install.pending")))
        {
            return;
        }

        var resultPath = Path.Combine(directory, "result.json");
        try
        {
            var spawner = Ensure(out var created);
            File.WriteAllText(Path.Combine(directory, "spawners.json"),
                JsonSerializer.Serialize(new SpawnerDto[] { spawner.ToDto() }, SpawnerJsonSerializer.Options));
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new
            {
                Success = true, Created = created, SpawnerSerial = spawner.Serial.Value,
                Guid = spawner.Guid, spawner.X, spawner.Y, spawner.Z, Map = "Felucca",
                CrewSize, ActivePirates = spawner.Spawned.Count, AwaitingWorldSave = true
            }, new JsonSerializerOptions { WriteIndented = true }));
            _logger.Information("Orcish pirate crew verified: spawner={Serial}, location={Position}, pirates={Count}; awaiting world save",
                spawner.Serial, spawner.Location, spawner.Spawned.Count);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Orcish pirate installation refused; authorization remains pending");
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new { Success = false, Error = exception.Message }));
        }
    }

    internal static Spawner Ensure(out bool created)
    {
        created = false;
        var position = Position;
        Spawner existing = null;
        foreach (var candidate in Map.Felucca.GetItemsAt<BaseSpawner>(position))
        {
            if (candidate.GetType() != typeof(Spawner) || candidate.Guid != CampGuid || existing != null)
            {
                throw new InvalidOperationException("An unrelated or duplicate spawner occupies the requested pirate site.");
            }
            existing = (Spawner)candidate;
        }

        if (existing != null)
        {
            var entries = existing.Entries;
            if (existing.Location != position || existing.Parent != null || existing.Name != CampName ||
                existing.Count != CrewSize || existing.SpawnBounds != Bounds || existing.HomeRange != 4 || existing.WalkingRange != 4 ||
                existing.MinDelay != TimeSpan.FromMinutes(5) || existing.MaxDelay != TimeSpan.FromMinutes(10) ||
                existing.Team != 0 || existing.Group || existing.SpawnLocationIsHome || !existing.UseSpiralScan ||
                entries.Count != 1 || entries[0].SpawnedName != "Orc" || entries[0].SpawnedMaxCount != CrewSize ||
                entries[0].SpawnedProbability != 100 || entries[0].Properties != EntryProperties ||
                entries[0].Parameters != null || entries[0].Disabled)
            {
                throw new InvalidOperationException("The existing pirate crew configuration differs from the approved site.");
            }
            return existing;
        }

        var supportedTiles = 0;
        for (var x = position.X - 4; x <= position.X + 4; x++)
        {
            for (var y = position.Y - 4; y <= position.Y + 4; y++)
            {
                if (Map.Felucca.CanFit(x, y, position.Z, 16, true, false))
                {
                    supportedTiles++;
                }
            }
        }
        if (supportedTiles < CrewSize)
        {
            throw new InvalidOperationException("The pirate camp needs five supported walking tiles within its local range.");
        }

        var definition = new SpawnerDataDto
        {
            Guid = CampGuid, Name = CampName, Location = position, Map = Map.Felucca,
            Count = CrewSize, MinDelay = TimeSpan.FromMinutes(5), MaxDelay = TimeSpan.FromMinutes(10),
            SpawnBounds = Bounds, WalkingRange = 4,
            Entries = [new SpawnerEntry("Orc", 100, CrewSize, EntryProperties)]
        };
        var spawner = (Spawner)definition.ToSpawner();
        try
        {
            spawner.UseSpiralScan = true;
            spawner.MoveToWorld(position, Map.Felucca);
            spawner.Respawn();
            if (spawner.Spawned.Count != CrewSize)
            {
                throw new InvalidOperationException("The requested pirate crew could not be fully spawned.");
            }
            foreach (var spawn in spawner.Spawned.Keys)
            {
                if (spawn is not Orc pirate || pirate.Name != PirateName || pirate.Map != Map.Felucca || pirate.Z != position.Z ||
                    !Map.Felucca.CanFit(pirate.Location, 16, true, false))
                {
                    throw new InvalidOperationException("A pirate did not spawn with the approved type, name or supported position.");
                }
            }
            created = true;
            return spawner;
        }
        catch
        {
            spawner.Delete();
            throw;
        }
    }
}
