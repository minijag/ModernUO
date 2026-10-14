using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Server.Engines.Spawners;
using Server.Items;
using Server.Json;
using Server.Logging;
using Server.Mobiles;

namespace Server.Engines.Labrynth;

public static class NewbieCityRemoval
{
    internal const string InstallationId = "newbie-city-removal-v1";
    internal const uint StockSerialBase = 1073772319;
    private const string LayoutHash = "98A1944F42E3F0F0BC4AAD9C8BC73C091D38273BD75256D93D5791E4FB2E4AE0";
    internal static readonly NewbieCityDeployment.SpawnPin[] Pins =
    [
        Pin("banker", "Banker", 5592, 1340, 5591, 1340, 3, 1, 0),
        Pin("blacksmith", "Blacksmith", 5582, 1339, 5581, 1339, 3, 1),
        Pin("tailor", "Tailor", 5582, 1344, 5581, 1344, 3, 1),
        Pin("carpenter", "Carpenter", 5582, 1349, 5581, 1349, 3, 1),
        Pin("bowyer", "Bowyer", 5582, 1358, 5581, 1358, 3, 1),
        Pin("tinker", "Tinker", 5582, 1363, 5581, 1363, 3, 1),
        Pin("alchemist", "Alchemist", 5604, 1339, 5603, 1339, 3, 1),
        Pin("mage", "Mage", 5604, 1344, 5603, 1344, 3, 1),
        Pin("provisioner", "Provisioner", 5604, 1349, 5603, 1349, 3, 1),
        Pin("farmer", "Farmer", 5604, 1358, 5603, 1358, 3, 1),
        Pin("animaltrainer", "AnimalTrainer", 5590, 1364, 5589, 1364, 3, 1),
        Pin("healer", "Healer", 5594, 1354, 5593, 1354, 3, 2, facing: Direction.West),
        Pin("sheep", "Sheep", 5599, 1363, 5596, 1361, 7, 6, 3, 10)
    ];
    internal sealed record SpawnerSnapshot(string Id, uint Serial, string Type, string Name, Guid Guid,
        Point3D Position, Rectangle3D Bounds, int Count, int HomeRange, string Properties);
    internal sealed record RouteSnapshot(string Id, uint Serial, string Name, string Type, Point3D Source,
        Point3D Destination, string Map, int ItemID, TeleporterFlags Flags, bool Visible, bool Movable);
    internal sealed record GateSnapshot(uint Serial, string Name, string Type, Point3D Position,
        Point3D ClosedPosition, uint LinkSerial, bool Open);
    internal sealed record MobileSnapshot(uint Serial, uint SpawnerSerial, string Type, string Name, Point3D Position,
        Point3D Home, int RangeHome, bool Blessed);
    internal sealed record Archive(string InstallationId, SpawnerSnapshot[] Spawners, RouteSnapshot[] Routes,
        GateSnapshot[] Doors, MobileSnapshot[] Mobiles);
    internal sealed record Result(bool Success, string InstallationId, int DeletedSpawners, int DeletedTeleporters,
        int DeletedDoors, int DeletedMobiles, bool NoObjectsRemaining, bool AlreadyRemoved, uint[] StockSerials,
        uint[] OwnedMobileSerials, bool AwaitingWorldSave);
    private sealed record Marker(string InstallationId);
    private sealed record Original(bool Success, string InstallationId, string LayoutHash, uint?[] StockSerials,
        NewbieCityDeployment.Layout Layout);
    private sealed record SpawnReceipt(string Id, uint Serial, string Type, string SerializedType, Point3D Position,
        Rectangle3D Bounds, int Count, int HomeRange, Guid SpawnerGuid, string Properties);
    private sealed record DoorReceipt(string Id, uint Serial, string Type, Point3D Position, DoorFacing Facing);
    private sealed record OriginalRoute(string Id, uint Serial, Point3D Source, Point3D Destination, string Map);
    private sealed record Installed(bool Success, string InstallationId, string LayoutHash, uint?[] StockSerials,
        int Created, SpawnReceipt[] Spawners, OriginalRoute[] Routes, DoorReceipt[] Doors);
    private sealed record TravelReceipt(bool Success, string InstallationId, RouteSnapshot[] Routes);
    private sealed record PenSpawner(uint Serial, Point3D Position, Rectangle3D Bounds, int HomeRange, int Count, Guid Guid);
    private sealed record PenGate(uint Serial, Point3D Position, Point3D ClosedPosition, uint LinkSerial, bool Open);
    private sealed record PenReceipt(bool Success, string InstallationId, PenSpawner SheepSpawner, PenGate[] Gates);
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(NewbieCityRemoval));
    private static readonly JsonSerializerOptions _options = JsonConfig.GetOptions();

    public static void Initialize()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data", "NewbieCityRemoval");
        var pending = Path.Combine(directory, "remove.pending");
        if (!File.Exists(pending))
        {
            return;
        }
        var resultPath = Path.Combine(directory, "result.json");
        try
        {
            Require(Read<Marker>(pending, 1024)?.InstallationId == InstallationId, "The marker does not authorize Newbie City removal.");
            ValidateReceipts(Path.Combine(Core.BaseDirectory, "Data"));
            var archivePath = Path.Combine(directory, "before.json");
            var previous = File.Exists(archivePath) ? Read<Archive>(archivePath, 65536) : null;
            var result = Apply(previous, snapshot =>
            {
                if (previous != null)
                {
                    return;
                }
                using var stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                JsonSerializer.Serialize(stream, snapshot, _options);
                stream.Flush(true);
            });
            WriteJson(resultPath, result);
            _logger.Information("Newbie City removal: stock objects={Objects}; owned mobiles={Mobiles}; already removed={AlreadyRemoved}; awaiting world save",
                result.DeletedSpawners + result.DeletedTeleporters + result.DeletedDoors, result.DeletedMobiles, result.AlreadyRemoved);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Newbie City removal refused");
            WriteJson(resultPath, new { Success = false, InstallationId, Error = exception.ToString() });
        }
    }

    internal static Result Apply(Archive previous = null, Action<Archive> archive = null)
    {
        var items = new Item[19];
        var present = 0;
        for (var i = 0; i < items.Length; i++)
        {
            var entity = World.FindEntity((Serial)(StockSerialBase + (uint)i));
            Require(entity == null || entity is Item, "A city stock serial now belongs to a non-item entity.");
            items[i] = entity as Item;
            if (entity != null)
            {
                present++;
            }
        }
        if (previous != null)
        {
            ValidateArchive(previous);
        }
        if (present == 0)
        {
            Require(previous != null && NoneRemain(previous), "Missing city objects require a verified successful removal snapshot.");
            return DescribeResult(previous, true);
        }
        Require(present == items.Length, "The city stock identities are partially missing; no objects were deleted.");
        var mobiles = new List<BaseCreature>(22);
        var spawnSnapshots = new SpawnerSnapshot[13];
        var mobileSnapshots = new List<MobileSnapshot>(22);
        var seen = new HashSet<uint>();
        for (var i = 0; i < Pins.Length; i++)
        {
            var pin = Pins[i];
            Require(items[i].GetType() == typeof(Spawner), $"The {pin.Id} stock serial has another type.");
            var spawner = (Spawner)items[i];
            Require(!spawner.Deleted && spawner.Parent == null && spawner.Map == Map.Felucca && spawner.Location == pin.Position &&
                spawner.Name == Name(pin.Id) && spawner.Guid == SpawnGuid(pin.Id) && spawner.Count == pin.Count &&
                spawner.SpawnBounds == pin.Bounds && spawner.WalkingRange == pin.HomeRange && spawner.Running &&
                spawner.MinDelay == TimeSpan.FromMinutes(2) && spawner.MaxDelay == TimeSpan.FromMinutes(5) &&
                spawner.ItemID == 0x1F13 && !spawner.Visible && !spawner.Movable && spawner.Hue == 0 && spawner.Entries.Count == 1,
                $"The {pin.Id} spawner differs from its authorized current identity/configuration.");
            var entry = spawner.Entries[0];
            Require(entry.SpawnedName == pin.Type && entry.SpawnedMaxCount == pin.Count && entry.SpawnedProbability == 100 &&
                entry.Enabled && entry.Properties == Properties(pin) && string.IsNullOrEmpty(entry.Parameters) &&
                spawner.Spawned.Count == pin.Count && entry.Spawned.Count == pin.Count,
                $"The {pin.Id} spawn entry or owned count differs from the authorized city.");
            foreach (var entity in entry.Spawned)
            {
                Require(entity is BaseCreature creature && creature.GetType() == NpcType(pin.Type) && !creature.Deleted &&
                    creature.Alive && !creature.Player && !creature.Controlled && !creature.Summoned && !creature.IsStabled &&
                    !creature.IsBonded && !creature.IsDeadPet && creature.ControlMaster == null && creature.SummonMaster == null &&
                    creature.LastOwner == null && creature.Spawner == spawner && spawner.Spawned.TryGetValue(creature, out var owner) &&
                    owner == entry && World.FindEntity(creature.Serial) == creature && seen.Add(creature.Serial.Value) &&
                    creature.Map == Map.Felucca && creature.HomeMap == Map.Felucca && creature.Home == pin.Position &&
                    creature.RangeHome == pin.HomeRange && creature.Blessed == (pin.Type != "Sheep"),
                    $"The {pin.Id} spawner owns a changed, foreign, player-owned, summoned, or protected entity.");
                var mobile = (BaseCreature)entity;
                mobiles.Add(mobile);
                mobileSnapshots.Add(new(mobile.Serial.Value, spawner.Serial.Value, mobile.GetType().FullName, mobile.Name,
                    mobile.Location, mobile.Home, mobile.RangeHome, mobile.Blessed));
            }
            spawnSnapshots[i] = new(pin.Id, spawner.Serial.Value, spawner.GetType().FullName, spawner.Name, spawner.Guid,
                spawner.Location, spawner.SpawnBounds, spawner.Count, spawner.WalkingRange, entry.Properties);
        }
        var routeSnapshots = new RouteSnapshot[4];
        for (var i = 0; i < routeSnapshots.Length; i++)
        {
            var route = NewbieCityMarketTravel.Routes[i];
            Require(items[13 + i].GetType() == typeof(Teleporter), "A city route stock serial has another type.");
            var pad = (Teleporter)items[13 + i];
            Require(!pad.Deleted && pad.Parent == null && pad.Map == Map.Felucca && pad.MapDest == Map.Felucca &&
                pad.Location == route.Source && pad.PointDest == route.Destination && pad.Name == Name(route.Id) &&
                pad.ItemID == 0x1BC3 && pad.Hue == 0 && pad.Flags == TeleporterFlags.Active && !pad.Visible && !pad.Movable &&
                pad.Delay == TimeSpan.Zero && pad.SoundID == 0, $"The {route.Id} teleporter differs from its current authorized route.");
            routeSnapshots[i] = new(route.Id, pad.Serial.Value, pad.Name, pad.GetType().FullName, pad.Location,
                pad.PointDest, "Felucca", pad.ItemID, pad.Flags, pad.Visible, pad.Movable);
        }
        var gateSnapshots = new GateSnapshot[2];
        for (var i = 0; i < gateSnapshots.Length; i++)
        {
            var id = i == 0 ? "sheep-gate-north" : "sheep-gate-south";
            var facing = i == 0 ? DoorFacing.SouthCW : DoorFacing.NorthCCW;
            Require(items[17 + i].GetType() == typeof(DarkWoodGate), "A sheep gate stock serial has another type.");
            var gate = (BaseDoor)items[17 + i];
            var closed = Closed(gate);
            Require(!gate.Deleted && gate.Parent == null && gate.Map == Map.Felucca && gate.Name == Name(id) &&
                gate.Link == items[18 - i] && closed == new Point3D(5595, 1363 + i, 0) && gate.Visible && !gate.Movable &&
                !gate.Locked && gate.Hue == 0 && gate.Offset == BaseDoor.GetOffset(facing) &&
                gate.ClosedId == 0x866 + 2 * (int)facing && gate.OpenedId == gate.ClosedId + 1 &&
                gate.ItemID == (gate.Open ? gate.OpenedId : gate.ClosedId), "A sheep gate differs from its authorized closed pin, art, or reciprocal link.");
            gateSnapshots[i] = new(gate.Serial.Value, gate.Name, gate.GetType().FullName, gate.Location, closed, gate.Link.Serial.Value, gate.Open);
        }
        var snapshot = new Archive(InstallationId, spawnSnapshots, routeSnapshots, gateSnapshots, mobileSnapshots.ToArray());
        ValidateArchive(snapshot);
        if (previous != null)
        {
            Require(SameOwned(previous, snapshot), "The current owned spawn serials differ from the existing removal snapshot.");
        }
        archive?.Invoke(snapshot);
        // Stopping every source first prevents any deletion callback from scheduling a replacement spawn.
        for (var i = 0; i < Pins.Length; i++)
        {
            ((Spawner)items[i]).Stop();
        }
        foreach (var mobile in mobiles)
        {
            mobile.Delete();
        }
        foreach (var item in items)
        {
            item.Delete();
        }
        Require(NoneRemain(snapshot), "A city stock object or owned spawn remains after removal.");
        return DescribeResult(snapshot, false);
    }

    internal static void ValidateReceipts(string dataDirectory)
    {
        var before = Read<Original>(Path.Combine(dataDirectory, "NewbieCity", "before.json"), 32768);
        var installed = Read<Installed>(Path.Combine(dataDirectory, "NewbieCity", "installed.json"), 32768);
        Require(before is { Success: false, InstallationId: "newbie-city-v1", LayoutHash: LayoutHash } && before.StockSerials?.Length == 19 &&
            installed is { Success: true, InstallationId: "newbie-city-v1", LayoutHash: LayoutHash, Created: 19 } &&
            installed.StockSerials?.Length == 19 && installed.Spawners?.Length == 13 && installed.Routes?.Length == 4 && installed.Doors?.Length == 2,
            "The original city creation receipts are missing or have another identity.");
        Require(before.Layout != null && Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(before.Layout, _options))) == LayoutHash,
            "The original city archive has another layout.");
        for (var i = 0; i < 19; i++)
        {
            Require(before.StockSerials[i] == null && installed.StockSerials[i] == StockSerialBase + (uint)i,
                "The original receipt does not prove that all 19 exact stock objects were newly created.");
        }
        for (var i = 0; i < Pins.Length; i++)
        {
            var pin = OriginalPin(i);
            var saved = installed.Spawners[i];
            Require(saved != null && saved.Serial == StockSerialBase + (uint)i && saved.Id == pin.Id && saved.Type == pin.Type &&
                saved.SerializedType == typeof(Spawner).FullName && saved.SpawnerGuid == SpawnGuid(pin.Id) && saved.Position == pin.Position &&
                saved.Bounds == pin.Bounds && saved.Count == pin.Count && saved.HomeRange == pin.HomeRange && saved.Properties == Properties(pin),
                "An original city spawner receipt differs from its exact approved identity.");
        }
        var travel = Read<TravelReceipt>(Path.Combine(dataDirectory, "NewbieCityMarketTravel", "result.json"), 8192);
        Require(travel is { Success: true, InstallationId: "newbie-city-market-v1" } && travel.Routes?.Length == 4,
            "The successful current market-route receipt is required.");
        for (var i = 0; i < 4; i++)
        {
            var route = NewbieCityMarketTravel.Routes[i];
            var original = installed.Routes[i];
            var saved = travel.Routes[i];
            Require(original != null && original.Id == route.Id && original.Serial == route.Serial && original.Map == "Felucca" &&
                original.Source == OriginalEndpoint(route.OldSource) && original.Destination == OriginalEndpoint(route.OldDestination) &&
                MatchesRoute(saved, i), "The original and current city route receipts do not reconcile.");
        }
        var pen = Read<PenReceipt>(Path.Combine(dataDirectory, "NewbieCityRefinement", "result.json"), 16384);
        Require(pen is { Success: true, InstallationId: "newbie-city-refine-v1" } && pen.SheepSpawner != null && pen.Gates?.Length == 2 &&
            pen.SheepSpawner.Serial == StockSerialBase + 12 && pen.SheepSpawner.Position == Pins[12].Position &&
            pen.SheepSpawner.Bounds == Pins[12].Bounds && pen.SheepSpawner.Count == 10 && pen.SheepSpawner.HomeRange == 3 &&
            pen.SheepSpawner.Guid == SpawnGuid("sheep"), "The successful current sheep-pen receipt is required.");
        for (var i = 0; i < 2; i++)
        {
            var original = installed.Doors[i];
            var saved = pen.Gates[i];
            var facing = i == 0 ? DoorFacing.SouthCW : DoorFacing.NorthCCW;
            Require(original != null && original.Serial == StockSerialBase + 17u + (uint)i &&
                original.Id == (i == 0 ? "sheep-gate-north" : "sheep-gate-south") && original.Type == "DarkWoodGate" &&
                original.Position == new Point3D(5599, 1363 + i, 0) && original.Facing == facing && saved != null &&
                saved.Serial == original.Serial && saved.ClosedPosition == new Point3D(5595, 1363 + i, 0) &&
                saved.LinkSerial == StockSerialBase + 18u - (uint)i &&
                saved.Position == (saved.Open ? Add(saved.ClosedPosition, BaseDoor.GetOffset(facing)) : saved.ClosedPosition),
                "The original and current sheep-gate receipts do not reconcile.");
        }
    }

    private static void ValidateArchive(Archive saved)
    {
        Require(saved?.InstallationId == InstallationId && saved.Spawners?.Length == 13 && saved.Routes?.Length == 4 &&
            saved.Doors?.Length == 2 && saved.Mobiles?.Length == 22, "The removal snapshot has another identity or object count.");
        for (var i = 0; i < Pins.Length; i++)
        {
            var pin = Pins[i];
            var spawn = saved.Spawners[i];
            Require(spawn != null && spawn.Id == pin.Id && spawn.Serial == StockSerialBase + (uint)i && spawn.Type == typeof(Spawner).FullName &&
                spawn.Name == Name(pin.Id) && spawn.Guid == SpawnGuid(pin.Id) && spawn.Position == pin.Position && spawn.Bounds == pin.Bounds &&
                spawn.Count == pin.Count && spawn.HomeRange == pin.HomeRange && spawn.Properties == Properties(pin), "Invalid archived city spawner.");
        }
        for (var i = 0; i < 4; i++)
        {
            Require(MatchesRoute(saved.Routes[i], i), "Invalid archived current city route.");
        }
        for (var i = 0; i < 2; i++)
        {
            var gate = saved.Doors[i];
            var facing = i == 0 ? DoorFacing.SouthCW : DoorFacing.NorthCCW;
            Require(gate != null && gate.Serial == StockSerialBase + 17u + (uint)i && gate.Type == typeof(DarkWoodGate).FullName &&
                gate.Name == Name(i == 0 ? "sheep-gate-north" : "sheep-gate-south") && gate.ClosedPosition == new Point3D(5595, 1363 + i, 0) &&
                gate.LinkSerial == StockSerialBase + 18u - (uint)i &&
                gate.Position == (gate.Open ? Add(gate.ClosedPosition, BaseDoor.GetOffset(facing)) : gate.ClosedPosition), "Invalid archived city gate.");
        }
        var counts = new int[13];
        var unique = new HashSet<uint>();
        foreach (var mobile in saved.Mobiles)
        {
            Require(mobile != null && mobile.Serial is > 0 and < 0x40000000 && unique.Add(mobile.Serial) &&
                mobile.SpawnerSerial >= StockSerialBase && mobile.SpawnerSerial < StockSerialBase + 13, "Invalid archived city mobile serial/owner.");
            var index = (int)(mobile.SpawnerSerial - StockSerialBase);
            var pin = Pins[index];
            Require(mobile.Type == NpcType(pin.Type).FullName && mobile.Home == pin.Position && mobile.RangeHome == pin.HomeRange &&
                mobile.Blessed == (pin.Type != "Sheep"), "Invalid archived city mobile type/home.");
            counts[index]++;
        }
        for (var i = 0; i < counts.Length; i++)
        {
            Require(counts[i] == Pins[i].Count, "The archived city mobile counts differ from the approved spawners.");
        }
    }

    private static bool MatchesRoute(RouteSnapshot saved, int index)
    {
        var route = NewbieCityMarketTravel.Routes[index];
        return saved != null && saved.Id == route.Id && saved.Serial == route.Serial && saved.Name == Name(route.Id) &&
            saved.Type == typeof(Teleporter).FullName && saved.Source == route.Source && saved.Destination == route.Destination &&
            saved.Map == "Felucca" && saved.ItemID == 0x1BC3 && saved.Flags == TeleporterFlags.Active && !saved.Visible && !saved.Movable;
    }

    private static bool SameOwned(Archive left, Archive right)
    {
        var serials = new HashSet<uint>();
        foreach (var mobile in left.Mobiles)
        {
            serials.Add(mobile.Serial);
        }
        foreach (var mobile in right.Mobiles)
        {
            if (!serials.Remove(mobile.Serial))
            {
                return false;
            }
        }
        return serials.Count == 0;
    }

    private static bool NoneRemain(Archive snapshot)
    {
        for (var i = 0; i < 19; i++)
        {
            if (World.FindEntity((Serial)(StockSerialBase + (uint)i)) != null)
            {
                return false;
            }
        }
        foreach (var mobile in snapshot.Mobiles)
        {
            if (World.FindEntity((Serial)mobile.Serial) != null)
            {
                return false;
            }
        }
        return true;
    }

    private static Result DescribeResult(Archive snapshot, bool repeat)
    {
        var stock = new uint[19];
        var mobiles = new uint[snapshot.Mobiles.Length];
        for (var i = 0; i < stock.Length; i++)
        {
            stock[i] = StockSerialBase + (uint)i;
        }
        for (var i = 0; i < mobiles.Length; i++)
        {
            mobiles[i] = snapshot.Mobiles[i].Serial;
        }
        return new(true, InstallationId, repeat ? 0 : 13, repeat ? 0 : 4, repeat ? 0 : 2, repeat ? 0 : mobiles.Length,
            true, repeat, stock, mobiles, !repeat);
    }

    internal static Guid SpawnGuid(string id) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"newbie-city-v1/{id}")).AsSpan(0, 16));
    private static string Name(string id) => $"Newbie City - {id}";
    private static string Properties(NewbieCityDeployment.SpawnPin pin) => pin.Type == "Sheep"
        ? $"Direction {pin.Facing}" : $"Blessed true Direction {pin.Facing}";
    private static NewbieCityDeployment.SpawnPin Pin(string id, string type, int x, int y, int bx, int by, int width, int height,
        int range = 1, int count = 1, Direction facing = Direction.South) =>
        new(id, type, new(x, y, 0), count, range, new(bx, by, -128, width, height, 256), facing);
    private static NewbieCityDeployment.SpawnPin OriginalPin(int index) => index == 12
        ? Pins[index] with { Position = new(5603, 1363, 0), Bounds = new(5600, 1361, -128, 7, 6, 256) } : Pins[index];
    private static Point3D OriginalEndpoint(Point3D point) => point.X == 721 ? new(point.X, point.Y, 10) : point;
    private static Point3D Closed(BaseDoor gate) => gate.Open
        ? new(gate.X - gate.Offset.X, gate.Y - gate.Offset.Y, gate.Z - gate.Offset.Z) : gate.Location;
    private static Point3D Add(Point3D point, Point3D offset) => new(point.X + offset.X, point.Y + offset.Y, point.Z + offset.Z);
    private static Type NpcType(string name) => name switch
    {
        "Banker" => typeof(Banker), "Blacksmith" => typeof(Blacksmith), "Tailor" => typeof(Tailor),
        "Carpenter" => typeof(Carpenter), "Bowyer" => typeof(Bowyer), "Tinker" => typeof(Tinker),
        "Alchemist" => typeof(Alchemist), "Mage" => typeof(Mage), "Provisioner" => typeof(Provisioner),
        "Farmer" => typeof(Farmer), "AnimalTrainer" => typeof(AnimalTrainer), "Healer" => typeof(Healer),
        "Sheep" => typeof(Sheep), _ => throw new InvalidOperationException("Unapproved city NPC type.")
    };

    private static T Read<T>(string path, int maximumBytes)
    {
        Require(File.Exists(path) && new FileInfo(path).Length > 0 && new FileInfo(path).Length <= maximumBytes,
            $"Missing or oversized city removal input: {path}.");
        return JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), _options);
    }

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
