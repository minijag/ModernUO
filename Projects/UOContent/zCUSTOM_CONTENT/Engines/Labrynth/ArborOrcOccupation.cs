using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Server.Engines.Spawners;
using Server.Logging;
using Server.Mobiles;

namespace Server.Engines.Labrynth;

public static class ArborOrcOccupation
{
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(ArborOrcOccupation));
    internal const string ReplacementName = "Arbor - Orc Occupation";
    internal readonly record struct Pin(Guid Guid, Point3D Position, string Entry, int HomeRange, int MinimumMinutes, int Count = 1);
    internal static readonly Pin[] Pins =
    [
        new(new Guid("5510496e-1871-4ad1-abb6-09e20e415f4b"), new(1773, 1109, 0), "architect", 4, 8),
        new(new Guid("2af71223-e8de-465e-ac2f-13b411914c1f"), new(1760, 1072, 0), "banker", 4, 8),
        new(new Guid("3cc12c43-cd1b-4a29-a809-8ae3431dd9d4"), new(1791, 1050, 0), "bowyer", 4, 8),
        new(new Guid("f07ee9e8-4302-4682-ace3-0f3c6aece02a"), new(1765, 1018, 0), "baseescortable", 4, 8),
        new(new Guid("ac2517ec-1603-4b95-8c3e-fc8e90564e90"), new(1773, 976, 0), "barkeeper", 4, 5),
        new(new Guid("05c24c39-8cc4-4fbe-b5a1-19f2c2b6dedc"), new(1774, 1103, 0), "carpenter", 4, 8),
        new(new Guid("08b2a450-3319-48d5-8455-6ee510225879"), new(1750, 981, 0), "herbalist", 4, 8),
        new(new Guid("ccf10b64-b646-47af-9a46-61f212a2e2f7"), new(1800, 1082, 0), "provisioner", 4, 8),
        new(new Guid("f342369b-60ac-4e64-85ab-0b86c4abca1c"), new(1795, 1075, 0), "tinker", 4, 8),
        new(new Guid("3e63599b-8207-471c-9486-364c2083b648"), new(1764, 1078, 0), "banker", 4, 8),
        new(new Guid("93c0f34a-c231-4bae-94f9-b2b975f64a7d"), new(1738, 1052, 15), "townshiprewardvendor", 1, 8),
        new(new Guid("6abcbbc6-ad5d-4aff-a601-ea3bf7c4ea49"), new(1798, 1032, 0), "tailor", 4, 8),
        new(new Guid("b53dbce2-4bb3-400d-bce4-8394a173c48e"), new(1764, 1006, 0), "weaponsmith", 4, 8),
        new(new Guid("c9b0aa32-a116-45ac-a70f-bf4ee8166aba"), new(1802, 997, 0), "animaltrainer", 4, 8),
        new(new Guid("3ade45a4-85dd-4c8b-b548-34bb944d7a41"), new(1792, 1080, 0), "cobbler", 4, 8),
        new(new Guid("56a404c4-b1d5-4fd6-8013-40130bf2e24f"), new(1756, 981, 0), "alchemist", 4, 8),
        new(new Guid("1be58988-574a-4532-9e4f-0a874d1591b2"), new(1746, 992, 0), "mage", 4, 8),
        new(new Guid("73d21129-8656-4b2b-982d-aa1f35eed465"), new(1768, 1037, 0), "healer", 4, 8),
        new(new Guid("aa3631d3-655b-47c0-a7cd-93d00ccac16f"), new(1765, 999, 0), "blacksmith", 4, 8),
        new(new Guid("fe743112-a11a-4ce9-8eea-e1e1f57d5d4f"), new(1749, 987, 0), "scribe", 4, 8),
        new(new Guid("e1ef8e10-8dee-4f65-9e3a-744fcf6b439e"), new(1805, 1030, 0), "weaver", 4, 8),
        new(new Guid("30dc5ebf-1bcb-485f-b095-314e40d5b904"), new(1791, 1045, 0), "leatherworker", 4, 8),
        new(new Guid("00c62291-12c9-4e01-b9e9-d8edd7871ce7"), new(1791, 1063, 0), "baseescortable", 4, 5),
        new(new Guid("3af59feb-b0af-4f7a-bc5a-6d1b8fe9d8ee"), new(1746, 984, 0), "mage", 4, 8),
        new(new Guid("f8064d0c-598c-4217-a8a4-afe57149f41a"), new(1795, 1017, 0), "baseescortable", 15, 8),
        new(new Guid("7fe0b285-eff5-47a3-b98e-d6c1713c0c36"), new(1827, 1004, 0), "bird", 30, 8, 2),
        new(new Guid("1c65c635-7c1a-495b-a565-59a717aecfbf"), new(1751, 1088, 2), "rabbit,dog,cat", 25, 8, 1),
        new(new Guid("b7723729-23b1-4752-b9f0-2883839de8d2"), new(1781, 1029, 0), "rabbit,dog,cat", 25, 8, 1),
        new(new Guid("3abf3d07-96ef-4818-be39-ebf8fe487120"), new(1824, 1001, 0), "rabbit,dog,cat", 25, 8, 1),
        new(new Guid("8e4688bd-a013-4dbd-9349-50b30aa2d5c8"), new(1823, 996, 0), "sheep", 4, 8, 1),
        new(new Guid("b76b23c7-1bdf-4a0b-9093-d398570ceb8e"), new(1783, 1028, 0), "bird", 30, 8, 2),
        new(new Guid("5a4464a2-ce11-41b5-9d0e-fb9f7606d119"), new(1820, 996, 0), "sheep", 4, 8, 1),
        new(new Guid("ec90590a-6483-4588-b6dc-8a3f96adf43d"), new(1824, 1084, 0), "rabbit,dog,cat", 25, 8, 1),
    ];

    internal sealed record Plan(JsonElement[] Originals, JsonElement[] Replacements);
    internal sealed record NpcSnapshot(uint Serial, string Type, int X, int Y, int Z, string Map);
    internal sealed record SpawnerSnapshot(uint Serial, bool Running, bool UseSpiralScan, JsonElement Configuration, NpcSnapshot[] Npcs);
    internal sealed record Archive(int Version, JsonElement[] OriginalDefinitions, SpawnerSnapshot[] Spawners);
    internal sealed record Result(int Created, Spawner[] Originals, Spawner[] Replacements, int ArchivedNpcCount);
    private static readonly JsonSerializerOptions _archiveOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static void Initialize()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data", "ArborOrcOccupation");
        if (!File.Exists(Path.Combine(directory, "occupation.pending")))
        {
            return;
        }
        var resultPath = Path.Combine(directory, "result.json");
        try
        {
            var plan = JsonSerializer.Deserialize<Plan>(File.ReadAllText(Path.Combine(directory, "plan.json")), _archiveOptions);
            var result = Ensure(plan, Path.Combine(directory, "original-spawners.json"));
            var routes = new object[Pins.Length];
            for (var index = 0; index < routes.Length; index++)
            {
                routes[index] = new
                {
                    OriginalSerial = result.Originals[index].Serial.Value,
                    ReplacementSerial = result.Replacements[index].Serial.Value,
                    OriginalGuid = Pins[index].Guid,
                    ReplacementGuid = result.Replacements[index].Guid,
                    Position = new { Pins[index].Position.X, Pins[index].Position.Y, Pins[index].Position.Z, Map = "Felucca" },
                    Orc = result.Replacements[index].Entries[0].SpawnedName
                };
            }
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new
            {
                Success = true, result.Created, ArchivedSpawners = Pins.Length,
                result.ArchivedNpcCount, Routes = routes, AwaitingWorldSave = true,
                Archive = "Data/ArborOrcOccupation/original-spawners.json"
            }, _archiveOptions));
            _logger.Information("Arbor orc occupation verified; created={Created}, archived NPCs={Npcs}; awaiting world save",
                result.Created, result.ArchivedNpcCount);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Arbor orc occupation refused; authorization remains pending");
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new { Success = false, Error = exception.Message }, _archiveOptions));
        }
    }

    internal static string Role(string entry) => entry switch
    {
        "alchemist" or "herbalist" or "mage" or "scribe" => "OrcishMage",
        "banker" or "weaponsmith" or "blacksmith" or "townshiprewardvendor" => "OrcCaptain",
        _ => "Orc"
    };

    internal static Guid ReplacementGuid(Guid original) => new("0a7b0c00" + original.ToString("D")[8..]);

    internal static Result Ensure(Plan plan, string archivePath)
    {
        var definitions = ValidatePlan(plan);
        var originals = new Spawner[Pins.Length];
        var replacements = new Spawner[Pins.Length];
        Archive archive = null;
        if (File.Exists(archivePath))
        {
            archive = JsonSerializer.Deserialize<Archive>(File.ReadAllText(archivePath), _archiveOptions);
            ValidateArchive(archive, plan);
            for (var index = 0; index < Pins.Length; index++)
            {
                originals[index] = World.FindItem((Serial)archive.Spawners[index].Serial) as Spawner;
            }
        }

        var internalCount = 0;
        for (var index = 0; index < Pins.Length; index++)
        {
            foreach (var candidate in Map.Felucca.GetItemsAt<BaseSpawner>(Pins[index].Position))
            {
                if (candidate.GetType() != typeof(Spawner))
                {
                    throw new InvalidOperationException($"Unexpected spawner type at Arbor site {Pins[index].Position}.");
                }
                if (candidate.Guid == Pins[index].Guid && archive == null && originals[index] == null)
                {
                    originals[index] = (Spawner)candidate;
                }
                else if (candidate.Guid == definitions.Replacements[index].Guid && replacements[index] == null)
                {
                    replacements[index] = (Spawner)candidate;
                }
                else if (candidate != originals[index])
                {
                    throw new InvalidOperationException($"Conflicting or duplicate spawner at Arbor site {Pins[index].Position}.");
                }
            }
            var original = originals[index];
            if (original == null || original.Deleted || original.GetType() != typeof(Spawner) || original.Parent != null ||
                original.Location != Pins[index].Position || original.Map != Map.Felucca && original.Map != Map.Internal)
            {
                throw new InvalidOperationException($"Original Arbor spawner {Pins[index].Guid} is missing or unexpected.");
            }
            if (!Matches((SpawnerDataDto)original.ToDto(), definitions.Originals[index], original.Map))
            {
                throw new InvalidOperationException($"Original Arbor configuration differs at {Pins[index].Position}.");
            }
            ValidateOwnedNpcs(original);
            if (original.Map == Map.Internal)
            {
                internalCount++;
                if (original.Running || replacements[index] == null ||
                    replacements[index].Name != ReplacementName ||
                    !Matches((SpawnerDataDto)replacements[index].ToDto(), definitions.Replacements[index], Map.Felucca))
                {
                    throw new InvalidOperationException("Arbor occupation has a partial or altered installed state.");
                }
            }
            else if (replacements[index] != null)
            {
                throw new InvalidOperationException("Arbor occupation has mixed original and replacement spawners.");
            }
            if (archive != null)
            {
                ValidateSnapshot(original, archive.Spawners[index]);
            }
        }
        if (internalCount != 0 && internalCount != Pins.Length)
        {
            throw new InvalidOperationException("Arbor occupation is only partially installed.");
        }
        if (internalCount == Pins.Length)
        {
            return new Result(0, originals, replacements, CountNpcs(originals));
        }

        if (archive == null)
        {
            var snapshots = new SpawnerSnapshot[Pins.Length];
            for (var index = 0; index < Pins.Length; index++)
            {
                snapshots[index] = Capture(originals[index]);
            }
            archive = new Archive(1, plan.Originals, snapshots);
            var text = JsonSerializer.Serialize(archive, _archiveOptions);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(archivePath))!);
            // CreateNew fails closed instead of replacing the only recovery manifest.
            using var stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.Write(text);
            writer.Flush();
            stream.Flush(true);
        }

        try
        {
            for (var index = 0; index < Pins.Length; index++)
            {
                var original = originals[index];
                original.Stop();
                foreach (var spawn in original.Spawned.Keys)
                {
                    if (spawn is Mobile { Deleted: false } mobile)
                    {
                        mobile.Internalize();
                    }
                }
                // Keep coordinates so native HomeRange-style bounds remain intact while archived.
                original.MoveToWorld(original.Location, Map.Internal);
            }
            for (var index = 0; index < Pins.Length; index++)
            {
                var replacement = (Spawner)definitions.Replacements[index].ToSpawner();
                replacements[index] = replacement;
                replacement.MoveToWorld(Pins[index].Position, Map.Felucca);
                replacement.Respawn();
                if (replacement.Spawned.Count != replacement.Count)
                {
                    throw new InvalidOperationException($"Orc replacement failed to fill the approved capacity at {Pins[index].Position}.");
                }
            }
        }
        catch
        {
            for (var index = 0; index < Pins.Length; index++)
            {
                replacements[index]?.Delete();
                originals[index].MoveToWorld(Pins[index].Position, Map.Felucca);
                foreach (var npc in archive.Spawners[index].Npcs)
                {
                    World.FindMobile((Serial)npc.Serial)?.MoveToWorld(new Point3D(npc.X, npc.Y, npc.Z), Map.Parse(npc.Map));
                }
                originals[index].Running = archive.Spawners[index].Running;
            }
            throw;
        }
        return new Result(Pins.Length, originals, replacements, CountNpcs(originals));
    }

    private static (SpawnerDataDto[] Originals, SpawnerDataDto[] Replacements) ValidatePlan(Plan plan)
    {
        if (plan?.Originals?.Length != Pins.Length || plan.Replacements?.Length != Pins.Length)
        {
            throw new InvalidOperationException("Arbor occupation requires exactly 33 pinned original and replacement definitions.");
        }
        var originals = new SpawnerDataDto[Pins.Length];
        var replacements = new SpawnerDataDto[Pins.Length];
        for (var index = 0; index < Pins.Length; index++)
        {
            var pin = Pins[index];
            var original = ReadDefinition(plan.Originals[index]);
            var replacement = ReadDefinition(plan.Replacements[index]);
            if (original.Guid != pin.Guid || original.Location != pin.Position || original.Map != Map.Felucca ||
                original.Count != pin.Count || original.MinDelay != TimeSpan.FromMinutes(pin.MinimumMinutes) ||
                original.MaxDelay != TimeSpan.FromMinutes(10) || original.HomeRange != pin.HomeRange ||
                original.Team != 0 || original.WalkingRange != -1 || original.Group || original.SpawnLocationIsHome ||
                original.SpawnPositionMode != SpawnPositionMode.Automatic || original.MaxSpawnAttempts != 0 ||
                !MatchesApprovedEntries(original, pin))
            {
                throw new InvalidOperationException($"Arbor plan differs from the approved original definition {pin.Guid}.");
            }
            var expected = original with
            {
                Guid = ReplacementGuid(pin.Guid), Name = ReplacementName,
                Entries = [new SpawnerEntry(Role(pin.Entry), 100, pin.Count)]
            };
            if (replacement.Name != ReplacementName || !Matches(replacement, expected, Map.Felucca))
            {
                throw new InvalidOperationException($"Arbor replacement differs from its approved role/configuration {pin.Guid}.");
            }
            originals[index] = original;
            replacements[index] = replacement;
        }
        return (originals, replacements);
    }

    private static SpawnerDataDto ReadDefinition(JsonElement element, bool requireType = true)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            requireType && (!element.TryGetProperty("type", out var type) || type.GetString() != "Spawner"))
        {
            throw new InvalidOperationException("Arbor definitions must be ordinary native Spawners.");
        }
        return JsonSerializer.Deserialize<SpawnerDataDto>(element.GetRawText(), SpawnerJsonSerializer.Options)
            ?? throw new InvalidOperationException("Missing Arbor spawner definition.");
    }

    private static bool Matches(SpawnerDataDto actual, SpawnerDataDto expected, Map map)
    {
        if (actual.Guid != expected.Guid || actual.Location != expected.Location || actual.Map != map ||
            actual.Count != expected.Count || actual.MinDelay != expected.MinDelay || actual.MaxDelay != expected.MaxDelay ||
            actual.Team != expected.Team || actual.WalkingRange != expected.WalkingRange || actual.HomeRange != expected.HomeRange ||
            actual.Group != expected.Group || actual.SpawnLocationIsHome != expected.SpawnLocationIsHome ||
            actual.SpawnPositionMode != expected.SpawnPositionMode || actual.MaxSpawnAttempts != expected.MaxSpawnAttempts ||
            actual.SpawnBounds != expected.SpawnBounds || actual.Entries?.Count != expected.Entries?.Count)
        {
            return false;
        }
        for (var index = 0; index < actual.Entries.Count; index++)
        {
            var a = actual.Entries[index];
            var e = expected.Entries[index];
            if (!string.Equals(a.SpawnedName, e.SpawnedName, StringComparison.OrdinalIgnoreCase) ||
                a.SpawnedProbability != e.SpawnedProbability || a.SpawnedMaxCount != e.SpawnedMaxCount ||
                a.Properties != e.Properties || a.Parameters != e.Parameters || a.Disabled != e.Disabled)
            {
                return false;
            }
        }
        return true;
    }

    private static bool MatchesApprovedEntries(SpawnerDataDto definition, Pin pin)
    {
        var names = pin.Entry.Split(',');
        if (definition.Entries?.Count != names.Length)
        {
            return false;
        }
        for (var index = 0; index < names.Length; index++)
        {
            var entry = definition.Entries[index];
            if (entry.SpawnedName != names[index] || entry.SpawnedProbability != 100 ||
                entry.SpawnedMaxCount != pin.Count || entry.Disabled || entry.Parameters != null || entry.Properties != null)
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsApprovedNpc(Spawner spawner, Mobile mobile)
    {
        foreach (var entry in spawner.Entries)
        {
            var type = AssemblyHandler.FindTypeByName(entry.SpawnedName);
            if (type?.IsInstanceOfType(mobile) == true)
            {
                return true;
            }
        }
        return false;
    }

    private static void ValidateOwnedNpcs(Spawner spawner)
    {
        foreach (var spawn in spawner.Spawned.Keys)
        {
            if (spawn.Deleted)
            {
                continue;
            }
            if (spawn is not Mobile mobile || mobile.Player || !IsApprovedNpc(spawner, mobile) || mobile.Spawner != spawner ||
                mobile is BaseCreature { Controlled: true } or BaseCreature { Summoned: true } or BaseCreature { IsStabled: true })
            {
                throw new InvalidOperationException($"Arbor spawner {spawner.Guid} owns a protected or unexpected spawn.");
            }
        }
    }

    private static SpawnerSnapshot Capture(Spawner spawner)
    {
        var npcs = new List<NpcSnapshot>();
        foreach (var spawn in spawner.Spawned.Keys)
        {
            if (spawn is Mobile { Deleted: false } mobile)
            {
                npcs.Add(new NpcSnapshot(mobile.Serial.Value, mobile.GetType().FullName, mobile.X, mobile.Y, mobile.Z, mobile.Map.Name));
            }
        }
        return new SpawnerSnapshot(spawner.Serial.Value, spawner.Running, spawner.UseSpiralScan,
            JsonSerializer.SerializeToElement((SpawnerDataDto)spawner.ToDto(), SpawnerJsonSerializer.Options), npcs.ToArray());
    }

    private static void ValidateArchive(Archive archive, Plan plan)
    {
        if (archive?.Version != 1 || archive.OriginalDefinitions?.Length != Pins.Length || archive.Spawners?.Length != Pins.Length)
        {
            throw new InvalidOperationException("Invalid Arbor recovery archive.");
        }
        for (var index = 0; index < Pins.Length; index++)
        {
            if (!JsonElement.DeepEquals(archive.OriginalDefinitions[index], plan.Originals[index]))
            {
                throw new InvalidOperationException("Arbor recovery definitions differ from the authorized plan.");
            }
        }
    }

    private static void ValidateSnapshot(Spawner spawner, SpawnerSnapshot snapshot)
    {
        var expected = ReadDefinition(snapshot.Configuration, false);
        if (spawner.Serial.Value != snapshot.Serial || spawner.UseSpiralScan != snapshot.UseSpiralScan ||
            !Matches((SpawnerDataDto)spawner.ToDto(), expected, spawner.Map) ||
            CountNpcs([spawner]) != snapshot.Npcs.Length)
        {
            throw new InvalidOperationException("Archived Arbor native spawner differs from its recovery snapshot.");
        }
        foreach (var npc in snapshot.Npcs)
        {
            var mobile = World.FindMobile((Serial)npc.Serial);
            if (mobile == null || mobile.Deleted || mobile.Spawner != spawner ||
                mobile.GetType().FullName != npc.Type || mobile.Location != new Point3D(npc.X, npc.Y, npc.Z) ||
                mobile.Map != (spawner.Map == Map.Internal ? Map.Internal : Map.Parse(npc.Map)) ||
                !spawner.Spawned.ContainsKey(mobile))
            {
                throw new InvalidOperationException("Archived Arbor NPC differs from its recovery snapshot.");
            }
        }
    }

    private static int CountNpcs(Spawner[] spawners)
    {
        var count = 0;
        foreach (var spawner in spawners)
        {
            foreach (var spawn in spawner.Spawned.Keys)
            {
                if (spawn is Mobile { Deleted: false })
                {
                    count++;
                }
            }
        }
        return count;
    }
}

