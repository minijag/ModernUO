using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Server.Engines.Spawners;
using Server.Items;
using Server.Json;
using Server.Logging;
using Server.Mobiles;

namespace Server.Engines.Labrynth;

public static class NewbieCityRefinement
{
    internal const string InstallationId = "newbie-city-refine-v1";
    internal const uint SpawnerSerial = 1073772331;
    internal static readonly Point3D OldSpawnerPosition = new(5603, 1363, 0);
    internal static readonly Point3D SpawnerPosition = new(5599, 1363, 0);
    internal static readonly Rectangle3D OldSpawnBounds = new(5600, 1361, -128, 7, 6, 256);
    internal static readonly Rectangle3D SpawnBounds = new(5596, 1361, -128, 7, 6, 256);
    private static readonly Guid _spawnerGuid = new("663b0f3e-8f4e-59cb-d882-cd8baf9fcd7e");
    private sealed record Marker(string InstallationId);
    private sealed record SpawnerReceipt(string Id, uint Serial, string Type, Point3D Position, Rectangle3D Bounds,
        int HomeRange, int Count, Guid SpawnerGuid);
    private sealed record GateReceipt(string Id, uint Serial, string Type, Point3D Position, DoorFacing Facing);
    private sealed record CityReceipt(bool Success, string InstallationId, SpawnerReceipt[] Spawners, GateReceipt[] Doors);
    private sealed record SpawnerSnapshot(uint Serial, Point3D Position, Rectangle3D Bounds, int HomeRange, int Count, Guid Guid);
    private sealed record GateSnapshot(uint Serial, Point3D Position, Point3D ClosedPosition, uint LinkSerial, bool Open);
    private sealed record SheepSnapshot(uint Serial, Point3D Position, Point3D Home);
    private sealed record Archive(string InstallationId, SpawnerSnapshot SheepSpawner, GateSnapshot[] Gates, SheepSnapshot[] Sheep);
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(NewbieCityRefinement));
    private static readonly JsonSerializerOptions _options = JsonConfig.GetOptions();

    public static void Initialize()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data", "NewbieCityRefinement");
        var pending = Path.Combine(directory, "install.pending");
        if (!File.Exists(pending) || File.Exists(Path.Combine(directory, "install.completed")))
        {
            return;
        }
        var resultPath = Path.Combine(directory, "result.json");
        try
        {
            Require(new FileInfo(pending).Length is > 0 and <= 1024, "Invalid Newbie City refinement marker.");
            Require(JsonSerializer.Deserialize<Marker>(File.ReadAllBytes(pending), _options)?.InstallationId == InstallationId,
                "The marker does not authorize the sheep-pen relocation.");
            ValidateHistoricalReceipt(Path.Combine(Core.BaseDirectory, "Data", "NewbieCity", "result.json"));
            var spawner = World.FindItem((Serial)SpawnerSerial) as Spawner;
            BaseDoor[] gates = [World.FindItem((Serial)1073772336) as BaseDoor, World.FindItem((Serial)1073772337) as BaseDoor];
            var sheep = Validate(spawner, gates, true);
            var archivePath = Path.Combine(directory, "before.json");
            if (!File.Exists(archivePath))
            {
                using var stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                JsonSerializer.Serialize(stream, Describe(spawner, gates, sheep), _options);
                stream.Flush(true);
            }
            else
            {
                Require(new FileInfo(archivePath).Length is > 0 and <= 16384, "Invalid sheep-pen archive size.");
                ValidateArchive(JsonSerializer.Deserialize<Archive>(File.ReadAllBytes(archivePath), _options));
            }
            var changed = Apply(spawner, gates);
            var after = Describe(spawner, gates, sheep);
            WriteJson(resultPath, new
            {
                Success = true, InstallationId, Changed = changed, Created = 0, Deleted = 0, SheepCount = sheep.Length,
                after.SheepSpawner, after.Gates, after.Sheep, AwaitingWorldSave = true
            });
            _logger.Information("Newbie City refinement ready: existing sheep pen moved four tiles west; sheep={SheepCount}; changed={Changed}; awaiting world save",
                sheep.Length, changed);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Newbie City sheep-pen refinement refused");
            WriteJson(resultPath, new { Success = false, InstallationId, Error = exception.ToString() });
        }
    }

    internal static int Apply(Spawner spawner, BaseDoor[] gates, bool checkSupport = true)
    {
        var sheep = Validate(spawner, gates, checkSupport);
        if (spawner.Location == SpawnerPosition)
        {
            return 0;
        }
        var before = Describe(spawner, gates, sheep);
        try
        {
            for (var i = 0; i < sheep.Length; i++)
            {
                sheep[i].Home = West(before.Sheep[i].Home);
                sheep[i].MoveToWorld(West(before.Sheep[i].Position), Map.Felucca);
            }
            for (var i = 0; i < gates.Length; i++)
            {
                gates[i].MoveToWorld(West(before.Gates[i].Position), Map.Felucca);
            }
            spawner.SpawnBounds = SpawnBounds;
            spawner.MoveToWorld(SpawnerPosition, Map.Felucca);
            Validate(spawner, gates, checkSupport);
            return sheep.Length + 3;
        }
        catch
        {
            spawner.SpawnBounds = before.SheepSpawner.Bounds;
            spawner.MoveToWorld(before.SheepSpawner.Position, Map.Felucca);
            for (var i = 0; i < gates.Length; i++)
            {
                gates[i].MoveToWorld(before.Gates[i].Position, Map.Felucca);
            }
            for (var i = 0; i < sheep.Length; i++)
            {
                sheep[i].Home = before.Sheep[i].Home;
                sheep[i].MoveToWorld(before.Sheep[i].Position, Map.Felucca);
            }
            throw;
        }
    }

    private static Sheep[] Validate(Spawner spawner, BaseDoor[] gates, bool checkSupport)
    {
        Require(spawner != null && spawner.GetType() == typeof(Spawner) && !spawner.Deleted && spawner.Serial.Value == SpawnerSerial &&
            spawner.Parent == null && spawner.Map == Map.Felucca && spawner.Name == "Newbie City - sheep" && spawner.Guid == _spawnerGuid &&
            spawner.Count == 10 && spawner.WalkingRange == 3 && spawner.Running && spawner.Entries.Count == 1 &&
            spawner.MinDelay == TimeSpan.FromMinutes(2) && spawner.MaxDelay == TimeSpan.FromMinutes(5),
            "The sheep spawner is missing or no longer has the authorized stock identity/configuration.");
        var original = spawner.Location == OldSpawnerPosition && spawner.SpawnBounds == OldSpawnBounds;
        Require(original || spawner.Location == SpawnerPosition && spawner.SpawnBounds == SpawnBounds,
            "The sheep spawner differs from both the original and relocated state.");
        var entry = spawner.Entries[0];
        Require(entry.SpawnedName == "Sheep" && entry.SpawnedMaxCount == 10 && entry.SpawnedProbability == 100 && entry.Enabled &&
            entry.Properties == "Direction South" && string.IsNullOrEmpty(entry.Parameters), "The sheep spawn entry was changed.");
        Require(gates?.Length == 2, "Both original linked sheep gates are required.");
        for (var i = 0; i < gates.Length; i++)
        {
            var gate = gates[i];
            var facing = i == 0 ? DoorFacing.SouthCW : DoorFacing.NorthCCW;
            var closed = new Point3D(original ? 5599 : 5595, 1363 + i, 0);
            Require(gate != null && gate.GetType() == typeof(DarkWoodGate) && !gate.Deleted &&
                gate.Serial.Value == 1073772336u + (uint)i && gate.Parent == null && gate.Map == Map.Felucca &&
                gate.Name == (i == 0 ? "Newbie City - sheep-gate-north" : "Newbie City - sheep-gate-south") &&
                gate.Link == gates[1 - i] && gate.Visible && !gate.Movable && !gate.Locked && gate.Hue == 0 &&
                gate.Offset == BaseDoor.GetOffset(facing) && gate.ClosedId == 0x866 + 2 * (int)facing &&
                gate.OpenedId == gate.ClosedId + 1 && gate.ItemID == (gate.Open ? gate.OpenedId : gate.ClosedId) && Closed(gate) == closed,
                "A sheep gate is missing, unlinked, or differs from the approved state.");
            foreach (var source in new Point3D[] { new(5595, 1363 + i, 0), new(5599, 1363 + i, 0) })
            {
                foreach (var other in Map.Felucca.GetItemsInRange<BaseDoor>(source, 1))
                {
                    if (Closed(other) == source)
                    {
                        Require(other == gate, "An unexpected gate occupies an original or relocated sheep entrance.");
                    }
                }
            }
        }
        foreach (var other in Map.Felucca.GetItemsInBounds<BaseSpawner>(new Rectangle2D(5595, 1360, 13, 8)))
        {
            Require(other == spawner, "An unexpected spawner overlaps the old or relocated sheep pen.");
        }
        var owned = new List<Sheep>();
        var bounds = original ? OldSpawnBounds : SpawnBounds;
        var nearby = new Rectangle3D(bounds.X - 2, bounds.Y - 2, -128, bounds.Width + 4, bounds.Height + 4, 256);
        foreach (var entity in spawner.Spawned.Keys)
        {
            if (entity.Deleted)
            {
                continue;
            }
            Require(entity is Sheep animal && animal.GetType() == typeof(Sheep) && animal.Alive && !animal.Controlled && !animal.Summoned &&
                !animal.Player && animal.Spawner == spawner && spawner.Spawned[animal] == entry && entry.Spawned.Contains(animal) &&
                animal.Map == Map.Felucca && nearby.Contains(animal.Location) && bounds.Contains(animal.Home) &&
                animal.HomeMap == Map.Felucca && animal.RangeHome == 3,
                "The sheep spawner owns an unexpected mobile, pet, location, or home.");
            owned.Add((Sheep)entity);
        }
        Require(owned.Count is >= 8 and <= 10, "Eight to ten existing spawner-owned sheep are required; no sheep will be recreated.");
        foreach (var mobile in Map.Felucca.GetMobilesInBounds<Mobile>(new Rectangle2D(SpawnBounds.X, SpawnBounds.Y, SpawnBounds.Width, SpawnBounds.Height)))
        {
            Require(!SpawnBounds.Contains(mobile.Location) || mobile is Sheep animal && owned.Contains(animal),
                "An unexpected player, pet, or mobile occupies the relocated sheep pen.");
        }
        for (var i = 0; i < gates.Length; i++)
        {
            foreach (var mobile in Map.Felucca.GetMobilesInRange<Mobile>(new Point3D(5595, 1363 + i, 0), 0))
            {
                Require(mobile is Sheep animal && owned.Contains(animal), "An unexpected mobile occupies the relocated sheep gate.");
            }
        }
        if (checkSupport)
        {
            Require(CanFitAfterMove(SpawnerPosition, gates), "The relocated sheep-spawner tile lacks supported clearance.");
            for (var i = 0; i < gates.Length; i++)
            {
                Require(CanFitAfterMove(new Point3D(5595, 1363 + i, 0), gates), "The relocated closed-gate tile lacks supported clearance.");
                var target = original ? West(gates[i].Location) : gates[i].Location;
                Require(CanFitAfterMove(target, gates), "The relocated open gate lacks supported clearance.");
            }
            foreach (var animal in owned)
            {
                var target = original ? West(animal.Location) : animal.Location;
                Require(CanFitAfterMove(target, gates), $"The existing sheep {animal.Serial} cannot fit at its translated position.");
                for (var i = 0; i < gates.Length; i++)
                {
                    var gate = gates[i];
                    var gatePosition = original ? West(gate.Location) : gate.Location;
                    Require(target.X != gatePosition.X || target.Y != gatePosition.Y ||
                        gatePosition.Z + gate.ItemData.CalcHeight <= target.Z || target.Z + 16 <= gatePosition.Z,
                        "A translated sheep would overlap a relocated gate.");
                }
            }
        }
        return owned.ToArray();
    }

    // The old gates temporarily occupy the new pen interior; exclude only those two moving items from preflight.
    private static bool CanFitAfterMove(Point3D point, BaseDoor[] gates)
    {
        var map = Map.Felucca;
        if (map.CanFit(point, 16, true, false))
        {
            return true;
        }
        var land = map.Tiles.GetLandTile(point.X, point.Y);
        map.GetAverageZ(point.X, point.Y, out var low, out var average, out _);
        var impassableLand = (TileData.LandTable[land.ID & TileData.MaxLandValue].Flags & TileFlag.Impassable) != 0;
        if (impassableLand && average > point.Z && point.Z + 16 > low)
        {
            return false;
        }
        var supported = !impassableLand && !land.Ignored && average == point.Z;
        foreach (var tile in map.Tiles.GetStaticAndMultiTiles(point.X, point.Y))
        {
            var data = TileData.ItemTable[tile.ID & TileData.MaxItemValue];
            if ((data.Surface || data.Impassable) && tile.Z + data.CalcHeight > point.Z && point.Z + 16 > tile.Z)
            {
                return false;
            }
            supported |= data.Surface && !data.Impassable && point.Z == tile.Z + data.CalcHeight;
        }
        foreach (var item in map.GetItemsInRange<Item>(point, 0))
        {
            if (item == gates[0] || item == gates[1] || item.ItemID > TileData.MaxItemValue || item is BaseMulti)
            {
                continue;
            }
            var data = item.ItemData;
            if ((data.Surface || data.Impassable || item.BlocksFit) && item.Z + data.CalcHeight > point.Z && point.Z + 16 > item.Z)
            {
                return false;
            }
            supported |= data.Surface && !data.Impassable && !item.Movable && point.Z == item.Z + data.CalcHeight;
        }
        return supported;
    }

    internal static void ValidateHistoricalReceipt(string path)
    {
        Require(File.Exists(path) && new FileInfo(path).Length is > 0 and <= 32768, "The original Newbie City receipt is required.");
        var receipt = JsonSerializer.Deserialize<CityReceipt>(File.ReadAllBytes(path), _options);
        Require(receipt is { Success: true, InstallationId: "newbie-city-v1" } && receipt.Spawners?.Length == 13 && receipt.Doors?.Length == 2,
            "The original city receipt does not describe the authorized sheep pen.");
        var spawn = receipt.Spawners[12];
        Require(spawn is { Id: "sheep", Serial: SpawnerSerial, Type: "Sheep", HomeRange: 3, Count: 10 } &&
            spawn.Position == OldSpawnerPosition && spawn.Bounds == OldSpawnBounds && spawn.SpawnerGuid == _spawnerGuid,
            "The original receipt has another sheep-spawner identity/configuration.");
        for (var i = 0; i < receipt.Doors.Length; i++)
        {
            var gate = receipt.Doors[i];
            Require(gate != null && gate.Serial == 1073772336u + (uint)i && gate.Type == "DarkWoodGate" &&
                gate.Id == (i == 0 ? "sheep-gate-north" : "sheep-gate-south") && gate.Position == new Point3D(5599, 1363 + i, 0) &&
                gate.Facing == (i == 0 ? DoorFacing.SouthCW : DoorFacing.NorthCCW), "The original receipt has another sheep gate.");
        }
    }

    private static void ValidateArchive(Archive archive)
    {
        Require(archive?.InstallationId == InstallationId && archive.SheepSpawner is { Serial: SpawnerSerial, Count: 10, HomeRange: 3 } &&
            archive.SheepSpawner.Guid == _spawnerGuid && archive.Gates?.Length == 2 && archive.Sheep is { Length: >= 8 and <= 10 },
            "The sheep-pen archive has another identity/configuration.");
        var original = archive.SheepSpawner.Position == OldSpawnerPosition && archive.SheepSpawner.Bounds == OldSpawnBounds;
        Require(original || archive.SheepSpawner.Position == SpawnerPosition && archive.SheepSpawner.Bounds == SpawnBounds,
            "The archived sheep spawner differs from approved original/relocated states.");
        for (var i = 0; i < archive.Gates.Length; i++)
        {
            var gate = archive.Gates[i];
            var offset = BaseDoor.GetOffset(i == 0 ? DoorFacing.SouthCW : DoorFacing.NorthCCW);
            var closed = new Point3D(original ? 5599 : 5595, 1363 + i, 0);
            Require(gate != null && gate.Serial == 1073772336u + (uint)i && gate.LinkSerial == 1073772337u - (uint)i &&
                gate.ClosedPosition == closed && gate.Position == (gate.Open ? Add(closed, offset) : closed),
                "The archived sheep gates differ from approved states.");
        }
        var serials = new HashSet<uint>();
        foreach (var sheep in archive.Sheep)
        {
            Require(sheep != null && sheep.Serial is > 0 and < 0x40000000 && serials.Add(sheep.Serial) &&
                (original ? OldSpawnBounds : SpawnBounds).Contains(sheep.Home), "The sheep archive has invalid serials or homes.");
        }
    }

    private static Archive Describe(Spawner spawner, BaseDoor[] gates, Sheep[] sheep)
    {
        var gateSnapshots = new GateSnapshot[gates.Length];
        var sheepSnapshots = new SheepSnapshot[sheep.Length];
        for (var i = 0; i < gates.Length; i++)
        {
            gateSnapshots[i] = new(gates[i].Serial.Value, gates[i].Location, Closed(gates[i]), gates[i].Link.Serial.Value, gates[i].Open);
        }
        for (var i = 0; i < sheep.Length; i++)
        {
            sheepSnapshots[i] = new(sheep[i].Serial.Value, sheep[i].Location, sheep[i].Home);
        }
        return new(InstallationId, new(spawner.Serial.Value, spawner.Location, spawner.SpawnBounds, spawner.WalkingRange, spawner.Count, spawner.Guid),
            gateSnapshots, sheepSnapshots);
    }

    private static Point3D Closed(BaseDoor gate) => gate.Open
        ? new(gate.X - gate.Offset.X, gate.Y - gate.Offset.Y, gate.Z - gate.Offset.Z) : gate.Location;
    private static Point3D West(Point3D point) => new(point.X - 4, point.Y, point.Z);
    private static Point3D Add(Point3D point, Point3D offset) => new(point.X + offset.X, point.Y + offset.Y, point.Z + offset.Z);

    private static void WriteJson<T>(string path, T value)
    {
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(value, _options));
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
