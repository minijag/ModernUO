using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Server.Engines.Spawners;
using Server.Items;
using Server.Json;
using Server.Logging;
using Server.Mobiles;

namespace Server.Engines.Labrynth;

public static class NewbieCityDeployment
{
    internal const string InstallationId = "newbie-city-v1";
    internal sealed record Layout(string InstallationId, string Map, Rectangle3D CityBounds,
        SpawnPin[] Spawners, Route[] Routes, DoorPin[] Doors);
    internal sealed record SpawnPin(string Id, string Type, Point3D Position, int Count, int HomeRange,
        Rectangle3D Bounds, Direction Facing);
    internal sealed record Route(string Id, Point3D Source, Point3D Destination);
    internal sealed record DoorPin(string Id, string Type, Point3D Position, DoorFacing Facing);
    internal sealed record Result(int Created, Spawner[] Spawners, Teleporter[] Teleporters, BaseDoor[] Doors);
    private sealed record Marker(string InstallationId);
    private sealed record Journal(bool Success, string InstallationId, string LayoutHash, uint?[] StockSerials);
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(NewbieCityDeployment));
    private static readonly JsonSerializerOptions _options = JsonConfig.GetOptions();
    private static readonly TimeSpan _minDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan _maxDelay = TimeSpan.FromMinutes(5);
    private static readonly Rectangle3D _cityBounds = new(5576, 1336, -128, 33, 33, 256);

    public static void Initialize()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data", "NewbieCity");
        var pending = Path.Combine(directory, "install.pending");
        if (!File.Exists(pending) || File.Exists(Path.Combine(directory, "install.completed")))
        {
            return;
        }
        var resultPath = Path.Combine(directory, "result.json");
        try
        {
            Require(new FileInfo(pending).Length is > 0 and <= 1024, "Invalid Newbie City authorization marker.");
            Require(JsonSerializer.Deserialize<Marker>(File.ReadAllBytes(pending), _options)?.InstallationId == InstallationId,
                "The marker does not authorize this Newbie City installation.");
            var layout = LoadLayout(Path.Combine(directory, "layout.json"));
            var before = FindAndValidate(layout, true);
            var archivePath = Path.Combine(directory, "before.json");
            var receiptPath = Path.Combine(directory, "installed.json");
            ValidateJournal(layout, before, archivePath, receiptPath);
            var installed = Ensure(layout, archivePath);
            var receipt = Describe(layout, installed);
            WriteJson(receiptPath, receipt);
            WriteJson(resultPath, receipt);
            _logger.Information("Newbie City ready: 13 spawners, 12 vendors/healer, 10 sheep, 4 routes; created={Created}; awaiting world save",
                installed.Created);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Newbie City installation refused");
            WriteJson(resultPath, new { Success = false, InstallationId, Error = exception.ToString() });
        }
    }

    internal static Layout LoadLayout(string path)
    {
        Require(File.Exists(path) && new FileInfo(path).Length is > 0 and <= 32768,
            "Missing or oversized Newbie City layout manifest.");
        var layout = JsonSerializer.Deserialize<Layout>(File.ReadAllBytes(path), _options);
        ValidateLayout(layout);
        return layout;
    }

    internal static void ValidateLayout(Layout layout)
    {
        Require(layout is { InstallationId: InstallationId, Map: "Felucca" } && layout.CityBounds == _cityBounds,
            "The manifest must describe the approved 33-by-33 Felucca city.");
        Require(layout.Spawners?.Length == 13 && layout.Routes?.Length == 4 && layout.Doors is { Length: <= 4 },
            "The manifest must contain 13 spawners, four routes, and at most four doors.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var types = new HashSet<string>(StringComparer.Ordinal);
        var positions = new HashSet<Point3D>();
        foreach (var pin in layout.Spawners)
        {
            Require(pin != null && ValidId(pin.Id) && ids.Add(pin.Id) && positions.Add(pin.Position),
                "Duplicate or invalid Newbie City spawn pin.");
            Require(IsAllowedNpc(pin.Type) && types.Add(pin.Type) && pin.Count == (pin.Type == "Sheep" ? 10 : 1) &&
                pin.HomeRange is >= 0 and <= 8 && pin.Position.Z == 0 && layout.CityBounds.Contains(pin.Position) &&
                pin.Bounds.Width > 0 && pin.Bounds.Height > 0 && pin.Bounds.Depth > 0 && pin.Bounds.Contains(pin.Position) &&
                ContainsBounds(layout.CityBounds, pin.Bounds) && Enum.IsDefined(pin.Facing),
                $"Invalid count, type, position, or bounds for {pin.Id}.");
            Require(pin.Type != "Sheep" || pin.Bounds.Start.X == 5600 && pin.Bounds.Start.Y == 1361 &&
                pin.Bounds.End.X == 5607 && pin.Bounds.End.Y == 1367,
                "Sheep must spawn strictly inside the approved fenced pen.");
        }
        Require(types.Count == 13, "The city requires one of every approved vendor, a banker, a healer, and sheep.");
        foreach (var route in layout.Routes)
        {
            Require(route != null && ValidId(route.Id) && ids.Add(route.Id) && positions.Add(route.Source) &&
                IsEndpoint(route.Source) && IsEndpoint(route.Destination) && route.Source != route.Destination,
                "A city route has an unapproved or duplicate endpoint.");
            var reverse = false;
            foreach (var other in layout.Routes)
            {
                reverse |= other != null && other.Source == route.Destination && other.Destination == route.Source;
            }
            Require(reverse && (layout.CityBounds.Contains(route.Source) != layout.CityBounds.Contains(route.Destination)),
                "Each city pad must have its exact reciprocal island route.");
        }
        foreach (var door in layout.Doors)
        {
            Require(door != null && ValidId(door.Id) && ids.Add(door.Id) && positions.Add(door.Position) &&
                door.Type is "DarkWoodGate" or "DarkWoodDoor" && Enum.IsDefined(door.Facing) &&
                door.Position.Z == 0 && layout.CityBounds.Contains(door.Position), "Invalid city door pin.");
        }
    }

    internal static Result Ensure(Layout layout, string archivePath = null, bool checkSupport = true)
    {
        var found = FindAndValidate(layout, checkSupport);
        if (archivePath != null && !File.Exists(archivePath))
        {
            using var stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(stream, new
            {
                Success = false, InstallationId, LayoutHash = Hash(layout), StockSerials = Serials(found), Layout = layout
            }, _options);
            stream.Flush(true);
        }
        var added = new List<Item>();
        var newSpawners = new List<(Spawner Spawner, SpawnPin Pin)>();
        try
        {
            for (var i = 0; i < layout.Spawners.Length; i++)
            {
                if (found.Spawners[i] != null)
                {
                    continue;
                }
                var pin = layout.Spawners[i];
                var spawner = new Spawner(pin.Count, _minDelay, _maxDelay, spawnBounds: pin.Bounds, spawnedNames: pin.Type)
                {
                    Name = Name(pin.Id), Guid = SpawnGuid(pin.Id), WalkingRange = pin.HomeRange
                };
                added.Add(spawner);
                found.Spawners[i] = spawner;
                spawner.Entries[0].Properties = SpawnProperties(pin);
                spawner.MoveToWorld(pin.Position, Map.Felucca);
                newSpawners.Add((spawner, pin));
            }
            for (var i = 0; i < layout.Routes.Length; i++)
            {
                if (found.Teleporters[i] != null)
                {
                    continue;
                }
                var route = layout.Routes[i];
                var teleporter = new Teleporter(route.Destination, Map.Felucca) { Name = Name(route.Id) };
                added.Add(teleporter);
                found.Teleporters[i] = teleporter;
                teleporter.MoveToWorld(route.Source, Map.Felucca);
            }
            for (var i = 0; i < layout.Doors.Length; i++)
            {
                if (found.Doors[i] != null)
                {
                    continue;
                }
                var pin = layout.Doors[i];
                BaseDoor door = pin.Type == "DarkWoodGate" ? new DarkWoodGate(pin.Facing) : new DarkWoodDoor(pin.Facing);
                added.Add(door);
                found.Doors[i] = door;
                door.Name = Name(pin.Id);
                door.MoveToWorld(pin.Position, Map.Felucca);
            }
            foreach (var (spawner, pin) in newSpawners)
            {
                spawner.Respawn();
                Require(spawner.Spawned.Count == pin.Count, $"Initial {pin.Type} spawn did not fill its approved count.");
                foreach (var entity in spawner.Spawned.Keys)
                {
                    Require(entity is BaseCreature creature && creature.Map == Map.Felucca && pin.Bounds.Contains(creature.Location),
                        $"Initial {pin.Type} spawn escaped its approved bounds.");
                }
            }
            if (found.Doors.Length == 2 && layout.Doors[0].Type == "DarkWoodGate" && layout.Doors[1].Type == "DarkWoodGate")
            {
                found.Doors[0].Link = found.Doors[1];
                found.Doors[1].Link = found.Doors[0];
            }
            return found with { Created = added.Count };
        }
        catch
        {
            for (var i = added.Count - 1; i >= 0; i--)
            {
                added[i].Delete();
            }
            throw;
        }
    }

    private static Result FindAndValidate(Layout layout, bool checkSupport)
    {
        ValidateLayout(layout);
        var result = new Result(0, new Spawner[13], new Teleporter[4], new BaseDoor[layout.Doors.Length]);
        var allowed = new HashSet<IEntity>();
        for (var i = 0; i < layout.Spawners.Length; i++)
        {
            var pin = layout.Spawners[i];
            foreach (var spawner in Map.Felucca.GetItemsInRange<BaseSpawner>(pin.Position, 0))
            {
                Require(result.Spawners[i] == null && spawner is Spawner actual && Matches(actual, pin),
                    $"Unexpected or duplicate spawner at {pin.Position}.");
                result.Spawners[i] = (Spawner)spawner;
                allowed.Add(spawner);
                foreach (var entity in spawner.Spawned.Keys)
                {
                    Require(entity is BaseCreature creature && !creature.Player && !creature.Controlled && !creature.Summoned &&
                        creature.GetType().Name == pin.Type && creature.Spawner == spawner,
                        $"Unexpected player, pet, or NPC owned by {pin.Id}.");
                    allowed.Add(entity);
                }
            }
            if (checkSupport)
            {
                Require(Map.Felucca.CanFit(pin.Position, 16, true, false), $"Unsupported or obstructed spawn pin {pin.Id}.");
                var capacity = 0;
                for (var x = pin.Bounds.Start.X; x < pin.Bounds.End.X; x++)
                {
                    for (var y = pin.Bounds.Start.Y; y < pin.Bounds.End.Y; y++)
                    {
                        if (Map.Felucca.CanFit(new Point3D(x, y, pin.Position.Z), 16, true, false))
                        {
                            capacity++;
                        }
                    }
                }
                Require(capacity >= pin.Count, $"Insufficient supported spawn tiles inside {pin.Id} bounds.");
            }
        }
        for (var i = 0; i < layout.Routes.Length; i++)
        {
            var route = layout.Routes[i];
            foreach (var teleporter in Map.Felucca.GetItemsInRange<Teleporter>(route.Source, 0))
            {
                Require(result.Teleporters[i] == null && Matches(teleporter, route),
                    $"Unexpected or duplicate teleporter at {route.Source}.");
                result.Teleporters[i] = teleporter;
                allowed.Add(teleporter);
            }
            if (checkSupport)
            {
                Require(Map.Felucca.CanFit(route.Source, 16, true, false) && Map.Felucca.CanFit(route.Destination, 16, true, false),
                    $"The {route.Id} route needs clear supported activation and arrival tiles.");
            }
        }
        for (var i = 0; i < layout.Doors.Length; i++)
        {
            var pin = layout.Doors[i];
            foreach (var door in Map.Felucca.GetItemsInRange<BaseDoor>(pin.Position, 1))
            {
                var closed = door.Open ? new Point3D(door.X - door.Offset.X, door.Y - door.Offset.Y, door.Z - door.Offset.Z) : door.Location;
                if (closed != pin.Position && door.Location != pin.Position)
                {
                    continue;
                }
                Require(result.Doors[i] == null && closed == pin.Position && Matches(door, pin),
                    $"Unexpected or duplicate door at {pin.Position}.");
                result.Doors[i] = door;
                allowed.Add(door);
            }
            if (checkSupport && result.Doors[i] == null)
            {
                Require(Map.Felucca.CanFit(pin.Position, 16, true, false), $"Unsupported or obstructed door pin {pin.Id}.");
            }
        }
        var flat = new Rectangle2D(layout.CityBounds.Start.X, layout.CityBounds.Start.Y, layout.CityBounds.Width, layout.CityBounds.Height);
        foreach (var item in Map.Felucca.GetItemsInBounds<Item>(flat))
        {
            Require(item.Parent != null || !layout.CityBounds.Contains(item.Location) || allowed.Contains(item),
                $"Unexpected existing city item {item.Serial} ({item.GetType().Name}); no world edits were made.");
        }
        foreach (var mobile in Map.Felucca.GetMobilesInBounds<Mobile>(flat))
        {
            Require(!layout.CityBounds.Contains(mobile.Location) || allowed.Contains(mobile),
                $"Unexpected existing city mobile {mobile.Serial} ({mobile.GetType().Name}); no world edits were made.");
        }
        return result;
    }

    private static bool Matches(Spawner spawner, SpawnPin pin) => spawner.GetType() == typeof(Spawner) &&
        spawner.Parent == null && spawner.Location == pin.Position && spawner.Map == Map.Felucca && spawner.Name == Name(pin.Id) &&
        spawner.Guid == SpawnGuid(pin.Id) && spawner.Count == pin.Count && spawner.SpawnBounds == pin.Bounds &&
        spawner.WalkingRange == (pin.HomeRange > 0 ? pin.HomeRange : spawner.HomeRange) && spawner.Running &&
        spawner.MinDelay == _minDelay && spawner.MaxDelay == _maxDelay && spawner.Entries.Count == 1 &&
        spawner.Entries[0].SpawnedName == pin.Type && spawner.Entries[0].SpawnedMaxCount == pin.Count &&
        spawner.Entries[0].SpawnedProbability == 100 && spawner.Entries[0].Enabled &&
        spawner.Entries[0].Properties == SpawnProperties(pin) && string.IsNullOrEmpty(spawner.Entries[0].Parameters);

    private static bool Matches(Teleporter teleporter, Route route) => teleporter.GetType() == typeof(Teleporter) &&
        teleporter.Parent == null && teleporter.Map == Map.Felucca && teleporter.Location == route.Source &&
        teleporter.Name == Name(route.Id) && teleporter.PointDest == route.Destination && teleporter.MapDest == Map.Felucca &&
        teleporter.ItemID == 0x1BC3 && teleporter.Hue == 0 && teleporter.Flags == TeleporterFlags.Active &&
        !teleporter.Visible && !teleporter.Movable && teleporter.Delay == TimeSpan.Zero && teleporter.SoundID == 0;

    private static bool Matches(BaseDoor door, DoorPin pin) => door.GetType().Name == pin.Type && door.Name == Name(pin.Id) &&
        door.Map == Map.Felucca && door.Parent == null && !door.Movable && !door.Locked &&
        door.Offset == BaseDoor.GetOffset(pin.Facing) && door.ClosedId == (pin.Type == "DarkWoodGate" ? 0x866 : 0x6A5) + 2 * (int)pin.Facing &&
        door.OpenedId == door.ClosedId + 1 && door.ItemID == (door.Open ? door.OpenedId : door.ClosedId);

    private static object Describe(Layout layout, Result result)
    {
        var spawners = new object[result.Spawners.Length];
        var routes = new object[result.Teleporters.Length];
        var doors = new object[result.Doors.Length];
        var vendors = 0;
        var sheep = 0;
        for (var i = 0; i < spawners.Length; i++)
        {
            var pin = layout.Spawners[i];
            var spawner = result.Spawners[i];
            var live = new List<uint>();
            foreach (var entity in spawner.Spawned.Keys)
            {
                if (!entity.Deleted)
                {
                    live.Add(entity.Serial.Value);
                }
            }
            if (pin.Type == "Sheep")
            {
                sheep += live.Count;
            }
            else
            {
                vendors += live.Count;
            }
            spawners[i] = new { pin.Id, Serial = spawner.Serial.Value, Type = pin.Type, SerializedType = spawner.GetType().FullName,
                pin.Position, pin.Bounds, pin.Count, pin.HomeRange, SpawnerGuid = spawner.Guid, SpawnedSerialCount = live.Count,
                SpawnedSerials = live.ToArray(), Properties = spawner.Entries[0].Properties };
        }
        for (var i = 0; i < routes.Length; i++)
        {
            routes[i] = new { layout.Routes[i].Id, Serial = result.Teleporters[i].Serial.Value,
                layout.Routes[i].Source, layout.Routes[i].Destination, Map = "Felucca" };
        }
        for (var i = 0; i < doors.Length; i++)
        {
            doors[i] = new { layout.Doors[i].Id, Serial = result.Doors[i].Serial.Value, layout.Doors[i].Type,
                layout.Doors[i].Position, layout.Doors[i].Facing };
        }
        return new { Success = true, InstallationId, LayoutHash = Hash(layout), StockSerials = Serials(result),
            result.Created, SpawnerCount = spawners.Length, ActiveVendors = vendors, ActiveSheep = sheep,
            Spawners = spawners, Routes = routes, Doors = doors, AwaitingWorldSave = true };
    }

    private static void ValidateJournal(Layout layout, Result current, string archivePath, string receiptPath)
    {
        if (!File.Exists(archivePath))
        {
            Require(!File.Exists(receiptPath), "An installation receipt exists without its original archive.");
            return;
        }
        var before = ReadJournal(archivePath, layout);
        var actual = Serials(current);
        var original = SameSerials(actual, before.StockSerials);
        if (File.Exists(receiptPath))
        {
            var installed = ReadJournal(receiptPath, layout);
            Require(installed.Success && (original || SameSerials(actual, installed.StockSerials)),
                "The city objects differ from both the original archive and successful installation receipt.");
        }
        else
        {
            Require(original, "The city objects differ from the original pre-installation archive.");
        }
    }

    private static Journal ReadJournal(string path, Layout layout)
    {
        Require(new FileInfo(path).Length is > 0 and <= 32768, "Invalid Newbie City journal size.");
        var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllBytes(path), _options);
        Require(journal?.InstallationId == InstallationId && journal.LayoutHash == Hash(layout) &&
            journal.StockSerials?.Length == layout.Spawners.Length + layout.Routes.Length + layout.Doors.Length,
            "A Newbie City journal belongs to another manifest.");
        return journal;
    }

    private static uint?[] Serials(Result result)
    {
        var serials = new uint?[result.Spawners.Length + result.Teleporters.Length + result.Doors.Length];
        var i = 0;
        foreach (var item in result.Spawners)
        {
            serials[i++] = item?.Serial.Value;
        }
        foreach (var item in result.Teleporters)
        {
            serials[i++] = item?.Serial.Value;
        }
        foreach (var item in result.Doors)
        {
            serials[i++] = item?.Serial.Value;
        }
        return serials;
    }

    private static bool SameSerials(uint?[] left, uint?[] right)
    {
        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsAllowedNpc(string type) => type is "Banker" or "Blacksmith" or "Tailor" or "Carpenter" or "Bowyer" or
        "Alchemist" or "Mage" or "Provisioner" or "Farmer" or "AnimalTrainer" or "Tinker" or "Healer" or "Sheep";
    private static bool IsEndpoint(Point3D point) => point is { X: 5578, Y: 1351 or 1352, Z: 0 } or { X: 721, Y: 1612 or 1613, Z: 10 };
    private static bool ContainsBounds(Rectangle3D outer, Rectangle3D inner) => outer.Contains(inner.Start) &&
        outer.Contains(new Point3D(inner.End.X - 1, inner.End.Y - 1, inner.End.Z - 1));
    private static bool ValidId(string id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 64;
    private static string Name(string id) => $"Newbie City - {id}";
    private static string SpawnProperties(SpawnPin pin) => pin.Type == "Sheep" ? $"Direction {pin.Facing}" : $"Blessed true Direction {pin.Facing}";
    private static string Hash(Layout layout) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(layout, _options)));
    private static Guid SpawnGuid(string id) => new(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{InstallationId}/{id}")).AsSpan(0, 16));

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
