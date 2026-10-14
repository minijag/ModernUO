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

public static class NewbieCityV2Deployment
{
    internal const string InstallationId = "newbie-city-v2";
    internal sealed record NativeFile(string Name, string Sha256);
    internal sealed record Layout(string InstallationId, string Map, Rectangle3D CityBounds,
        SpawnPin[] Spawners, Route[] Routes, DoorPin[] Doors, string NativeStageId, NativeFile[] NativeFiles);
    internal sealed record SpawnPin(string Id, string Type, Point3D Position, int Count, int HomeRange,
        Rectangle3D Bounds, Direction Facing);
    internal sealed record Route(string Id, Point3D Source, Point3D Destination);
    internal sealed record DoorPin(string Id, string Type, Point3D Position, DoorFacing Facing);
    internal sealed record Result(int Created, Spawner[] Spawners, Teleporter[] Teleporters, BaseDoor[] Doors);
    private sealed record Marker(string InstallationId, string LayoutSha256, string NativeStageId);
    private sealed record Journal(bool Success, string InstallationId, string LayoutHash, uint?[] StockSerials);
    private sealed record Removal(bool Success, string InstallationId, bool NoObjectsRemaining,
        uint[] StockSerials, uint[] OwnedMobileSerials);
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(NewbieCityV2Deployment));
    private static readonly JsonSerializerOptions _options = JsonConfig.GetOptions();
    private static readonly TimeSpan _minDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan _maxDelay = TimeSpan.FromMinutes(5);
    private static readonly string[] _gateIds =
        ["sheep-west-gate-north", "sheep-west-gate-south", "sheep-east-gate-north", "sheep-east-gate-south"];

    public static void Initialize()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data", "NewbieCityV2");
        var pending = Path.Combine(directory, "install.pending");
        if (!File.Exists(pending) || File.Exists(Path.Combine(directory, "install.completed")))
        {
            return;
        }
        var resultPath = Path.Combine(directory, "result.json");
        try
        {
            var marker = JsonSerializer.Deserialize<Marker>(ReadBytes(pending, 1024), _options);
            var bytes = ReadBytes(Path.Combine(directory, "layout.json"), 65536);
            Require(marker?.InstallationId == InstallationId && marker.NativeStageId == InstallationId &&
                IsHash(marker.LayoutSha256) && string.Equals(marker.LayoutSha256, Convert.ToHexString(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase),
                "The V2 marker does not authorize this exact native-layout manifest.");
            var layout = JsonSerializer.Deserialize<Layout>(bytes, _options);
            ValidateLayout(layout);
            ValidateOldRemoval(Path.Combine(Core.BaseDirectory, "Data", "NewbieCityRemoval", "result.json"));
            var installed = Ensure(layout, Path.Combine(directory, "before.json"));
            var receipt = Describe(layout, installed);
            WriteJson(Path.Combine(directory, "installed.json"), receipt);
            WriteJson(resultPath, receipt);
            _logger.Information("Newbie City V2 ready: 14 spawners, 12 humans, two six-sheep pens, four routes and four linked gates; created={Created}; awaiting world save", installed.Created);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Newbie City V2 installation refused");
            WriteJson(resultPath, new { Success = false, InstallationId, Error = exception.ToString() });
        }
    }

    internal static Layout LoadLayout(string path)
    {
        var layout = JsonSerializer.Deserialize<Layout>(ReadBytes(path, 65536), _options);
        ValidateLayout(layout);
        return layout;
    }

    internal static void ValidateLayout(Layout layout)
    {
        Require(layout is { InstallationId: InstallationId, Map: "Felucca", NativeStageId: InstallationId } &&
            layout.CityBounds.Width is > 0 and <= 100 && layout.CityBounds.Height is > 0 and <= 100 &&
            layout.CityBounds.Start.Z == -128 && layout.CityBounds.End.Z == 128 && layout.CityBounds.Contains(new Point3D(5592, 1352, 0)),
            "The V2 manifest must describe a bounded Felucca city around 5592,1352.");
        Require(layout.NativeFiles?.Length == 3, "The V2 native identity must include exactly three native map hashes.");
        string[] names = ["map0.mul", "staidx0.mul", "statics0.mul"];
        for (var i = 0; i < names.Length; i++)
        {
            Require(layout.NativeFiles[i]?.Name == names[i] && IsHash(layout.NativeFiles[i].Sha256), "Invalid V2 native file identity.");
        }
        Require(layout.Spawners?.Length == 14 && layout.Routes?.Length == 4 && layout.Doors?.Length == 4,
            "V2 requires twelve human spawners, two six-sheep spawners, four routes and four gates.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var types = new HashSet<Type>();
        var positions = new HashSet<Point3D>();
        var pens = new SpawnPin[2];
        foreach (var pin in layout.Spawners)
        {
            Require(pin != null && ValidId(pin.Id) && ids.Add(pin.Id) && positions.Add(pin.Position), "Duplicate or invalid V2 spawn pin.");
            var type = NpcType(pin.Type);
            var sheep = pin.Type == "Sheep";
            Require(sheep ? pin.Id is "sheep-west" or "sheep-east" : pin.Id == pin.Type.ToLowerInvariant() && types.Add(type),
                "V2 requires one spawner for each approved human role and two named sheep pens.");
            Require(pin.Count == (sheep ? 6 : 1) && pin.HomeRange is >= 0 and <= 8 &&
                pin.Facing is Direction.North or Direction.East or Direction.South or Direction.West &&
                pin.Position.Z == 0 && pin.Bounds.Width is > 0 and <= 12 && pin.Bounds.Height is > 0 and <= 12 &&
                pin.Bounds.Start.Z == -128 && pin.Bounds.End.Z == 128 && pin.Bounds.Contains(pin.Position) &&
                ContainsBounds(layout.CityBounds, pin.Bounds), "A V2 spawn pin has unsafe count, position, range or bounds.");
            if (sheep)
            {
                pens[pin.Id == "sheep-west" ? 0 : 1] = pin;
            }
        }
        Require(types.Count == 12 && pens[0] != null && pens[1] != null && !Overlaps(pens[0].Bounds, pens[1].Bounds),
            "The two sheep pens must be separate from one another.");
        foreach (var pin in layout.Spawners)
        {
            if (pin.Type != "Sheep")
            {
                Require(!Overlaps(pin.Bounds, pens[0].Bounds) && !Overlaps(pin.Bounds, pens[1].Bounds), "A human spawn overlaps a sheep pen.");
            }
        }
        for (var i = 0; i < layout.Routes.Length; i++)
        {
            var route = layout.Routes[i];
            var expected = NewbieCityMarketTravel.Routes[i];
            Require(route != null && route.Id == expected.Id && ids.Add(route.Id) && positions.Add(route.Source) &&
                route.Source == expected.Source && route.Destination == expected.Destination,
                "The four V2 routes must match the exact reciprocal city/island endpoints.");
        }
        for (var i = 0; i < layout.Doors.Length; i++)
        {
            var gate = layout.Doors[i];
            var pen = pens[i / 2];
            Require(gate != null && gate.Id == _gateIds[i] && ids.Add(gate.Id) && positions.Add(gate.Position) &&
                gate.Type == "DarkWoodGate" && gate.Facing == (i % 2 == 0 ? DoorFacing.SouthCW : DoorFacing.NorthCCW) &&
                gate.Position.Z == 0 && layout.CityBounds.Contains(gate.Position) && gate.Position.X == pen.Bounds.Start.X - 1 &&
                gate.Position.Y >= pen.Bounds.Start.Y && gate.Position.Y < pen.Bounds.End.Y &&
                (i % 2 == 0 || gate.Position == new Point3D(layout.Doors[i - 1].Position.X, layout.Doors[i - 1].Position.Y + 1, 0)),
                "Each sheep pen needs its approved adjacent, west-facing linked stock gate pair.");
        }
    }

    internal static Result Ensure(Layout layout, string archivePath = null, bool checkSupport = true)
    {
        var found = FindAndValidate(layout, checkSupport);
        if (archivePath != null)
        {
            ValidateJournal(layout, found, archivePath);
            if (!File.Exists(archivePath))
            {
                using var stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                JsonSerializer.Serialize(stream, new { Success = false, InstallationId, LayoutHash = Hash(layout), StockSerials = Serials(found), Layout = layout }, _options);
                stream.Flush(true);
            }
        }
        var added = new List<Item>(22);
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
                spawner.Entries[0].Properties = Properties(pin);
                spawner.MoveToWorld(pin.Position, Map.Felucca);
            }
            for (var i = 0; i < layout.Routes.Length; i++)
            {
                if (found.Teleporters[i] != null)
                {
                    continue;
                }
                var route = layout.Routes[i];
                var pad = new Teleporter(route.Destination, Map.Felucca) { Name = Name(route.Id) };
                added.Add(pad);
                found.Teleporters[i] = pad;
                pad.MoveToWorld(route.Source, Map.Felucca);
            }
            for (var i = 0; i < layout.Doors.Length; i++)
            {
                if (found.Doors[i] != null)
                {
                    continue;
                }
                var pin = layout.Doors[i];
                var gate = new DarkWoodGate(pin.Facing) { Name = Name(pin.Id) };
                added.Add(gate);
                found.Doors[i] = gate;
                gate.MoveToWorld(pin.Position, Map.Felucca);
            }
            for (var i = 0; i < found.Doors.Length; i++)
            {
                if (added.Contains(found.Doors[i]))
                {
                    found.Doors[i].Link = found.Doors[i ^ 1];
                }
            }
            for (var i = 0; i < found.Spawners.Length; i++)
            {
                var spawner = found.Spawners[i];
                if (added.Contains(spawner))
                {
                    spawner.Respawn();
                    Require(spawner.Spawned.Count == layout.Spawners[i].Count, "A V2 spawner could not fill its approved initial population.");
                    foreach (var entity in spawner.Spawned.Keys)
                    {
                        Require(entity is BaseCreature creature && creature.Map == Map.Felucca &&
                            layout.Spawners[i].Bounds.Contains(creature.Location) && creature.Z == layout.Spawners[i].Position.Z,
                            "An initial V2 spawn escaped its approved bounds or standing height.");
                    }
                }
            }
            FindAndValidate(layout, checkSupport);
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
        var found = new Result(0, new Spawner[14], new Teleporter[4], new BaseDoor[4]);
        var allowed = new HashSet<IEntity>();
        for (var i = 0; i < layout.Spawners.Length; i++)
        {
            var pin = layout.Spawners[i];
            foreach (var spawner in Map.Felucca.GetItemsInRange<BaseSpawner>(pin.Position, 0))
            {
                Require(found.Spawners[i] == null && spawner is Spawner actual && Matches(actual, pin), "A foreign or changed spawner occupies a V2 pin.");
                found.Spawners[i] = (Spawner)spawner;
                allowed.Add(spawner);
                var entry = spawner.Entries[0];
                Require(spawner.Spawned.Count <= pin.Count && entry.Spawned.Count == spawner.Spawned.Count, "A V2 spawn ownership registry is inconsistent.");
                foreach (var entity in entry.Spawned)
                {
                    Require(entity is BaseCreature creature && creature.GetType() == NpcType(pin.Type) && !creature.Deleted && creature.Alive &&
                        !creature.Player && !creature.Controlled && !creature.Summoned && !creature.IsStabled && !creature.IsBonded &&
                        creature.ControlMaster == null && creature.SummonMaster == null && creature.LastOwner == null &&
                        creature.Spawner == spawner && spawner.Spawned.TryGetValue(creature, out var owner) && owner == entry &&
                        creature.Map == Map.Felucca && creature.HomeMap == Map.Felucca && creature.Home == pin.Position &&
                        creature.RangeHome == spawner.WalkingRange && creature.Blessed == (pin.Type != "Sheep") && allowed.Add(creature),
                        "A V2 spawner owns a changed, protected or foreign entity.");
                }
            }
            if (checkSupport)
            {
                Require(Map.Felucca.CanFit(pin.Position, 16, true, false), $"Unsupported V2 home pin {pin.Id}.");
                if (pin.Type is not ("Banker" or "Sheep"))
                {
                    var front = pin.Facing switch
                    {
                        Direction.North => new Point3D(pin.Position.X, pin.Position.Y - 1, 0),
                        Direction.East => new Point3D(pin.Position.X + 1, pin.Position.Y, 0),
                        Direction.South => new Point3D(pin.Position.X, pin.Position.Y + 1, 0),
                        _ => new Point3D(pin.Position.X - 1, pin.Position.Y, 0)
                    };
                    Require(Map.Felucca.CanFit(front, 16, true, false), $"Obstructed V2 vendor approach {pin.Id}.");
                }
                var capacity = 0;
                for (var x = pin.Bounds.Start.X; x < pin.Bounds.End.X; x++)
                {
                    for (var y = pin.Bounds.Start.Y; y < pin.Bounds.End.Y; y++)
                    {
                        if (Map.Felucca.CanFit(new Point3D(x, y, 0), 16, true, false))
                        {
                            capacity++;
                        }
                    }
                }
                Require(capacity >= pin.Count, $"Insufficient standing capacity in V2 spawn bounds {pin.Id}.");
            }
        }
        for (var i = 0; i < layout.Routes.Length; i++)
        {
            var route = layout.Routes[i];
            foreach (var pad in Map.Felucca.GetItemsInRange<Teleporter>(route.Source, 0))
            {
                Require(found.Teleporters[i] == null && pad.GetType() == typeof(Teleporter) && pad.Parent == null &&
                    pad.Location == route.Source && pad.Map == Map.Felucca && pad.Name == Name(route.Id) &&
                    pad.PointDest == route.Destination && pad.MapDest == Map.Felucca && pad.ItemID == 0x1BC3 && pad.Hue == 0 &&
                    pad.Flags == TeleporterFlags.Active && !pad.Visible && !pad.Movable && pad.Delay == TimeSpan.Zero && pad.SoundID == 0,
                    "A foreign or changed teleporter occupies a V2 route.");
                found.Teleporters[i] = pad;
                allowed.Add(pad);
            }
            if (checkSupport)
            {
                Require(Map.Felucca.CanFit(route.Source, 16, true, false) && Map.Felucca.CanFit(route.Destination, 16, true, false),
                    "A V2 route endpoint lacks clear standing support.");
            }
        }
        for (var i = 0; i < layout.Doors.Length; i++)
        {
            var pin = layout.Doors[i];
            foreach (var gate in Map.Felucca.GetItemsInRange<BaseDoor>(pin.Position, 1))
            {
                if (Closed(gate) != pin.Position)
                {
                    continue;
                }
                Require(found.Doors[i] == null && gate.GetType() == typeof(DarkWoodGate) && gate.Parent == null && gate.Map == Map.Felucca &&
                    gate.Name == Name(pin.Id) && gate.Visible && !gate.Movable && !gate.Locked && gate.Hue == 0 &&
                    gate.Offset == BaseDoor.GetOffset(pin.Facing) && gate.ClosedId == 0x866 + 2 * (int)pin.Facing &&
                    gate.OpenedId == gate.ClosedId + 1 && gate.ItemID == (gate.Open ? gate.OpenedId : gate.ClosedId), "A foreign or changed gate occupies a V2 pen entrance.");
                found.Doors[i] = gate;
                allowed.Add(gate);
            }
            if (checkSupport && found.Doors[i] == null)
            {
                var offset = BaseDoor.GetOffset(pin.Facing);
                Require(Map.Felucca.CanFit(pin.Position, 16, true, false) &&
                    Map.Felucca.CanFit(new Point3D(pin.Position.X + offset.X, pin.Position.Y + offset.Y, pin.Position.Z + offset.Z), 16, true, false),
                    "A V2 gate needs unobstructed closed and open positions.");
            }
        }
        for (var i = 0; i < found.Doors.Length; i++)
        {
            Require(found.Doors[i] == null ? found.Doors[i ^ 1] == null : found.Doors[i].Link == found.Doors[i ^ 1], "A V2 gate pair is partial or has another link.");
        }
        var flat = new Rectangle2D(layout.CityBounds.X, layout.CityBounds.Y, layout.CityBounds.Width, layout.CityBounds.Height);
        foreach (var item in Map.Felucca.GetItemsInBounds<Item>(flat))
        {
            Require(item.Parent != null || !layout.CityBounds.Contains(item.Location) || allowed.Contains(item), "An unexpected world item occupies the V2 city; no installation is authorized.");
        }
        foreach (var mobile in Map.Felucca.GetMobilesInBounds<Mobile>(flat))
        {
            Require(!layout.CityBounds.Contains(mobile.Location) || allowed.Contains(mobile), "An unexpected player, pet or mobile occupies the V2 city; no installation is authorized.");
        }
        return found;
    }

    private static bool Matches(Spawner spawner, SpawnPin pin) => spawner.GetType() == typeof(Spawner) && !spawner.Deleted &&
        spawner.Parent == null && spawner.Location == pin.Position && spawner.Map == Map.Felucca && spawner.Name == Name(pin.Id) &&
        spawner.Guid == SpawnGuid(pin.Id) && spawner.Count == pin.Count && spawner.SpawnBounds == pin.Bounds &&
        spawner.ItemID == 0x1F13 && spawner.Hue == 0 && !spawner.Visible && !spawner.Movable &&
        spawner.WalkingRange == (pin.HomeRange > 0 ? pin.HomeRange : spawner.HomeRange) && spawner.Running &&
        spawner.MinDelay == _minDelay && spawner.MaxDelay == _maxDelay && spawner.Entries.Count == 1 &&
        spawner.Entries[0].SpawnedName == pin.Type && spawner.Entries[0].SpawnedMaxCount == pin.Count &&
        spawner.Entries[0].SpawnedProbability == 100 && spawner.Entries[0].Enabled &&
        spawner.Entries[0].Properties == Properties(pin) && string.IsNullOrEmpty(spawner.Entries[0].Parameters);

    internal static object Describe(Layout layout, Result result)
    {
        var spawners = new object[14];
        var routes = new object[4];
        var gates = new object[4];
        var humans = 0;
        var sheep = 0;
        for (var i = 0; i < spawners.Length; i++)
        {
            var pin = layout.Spawners[i];
            var spawner = result.Spawners[i];
            var serials = new List<uint>();
            foreach (var entity in spawner.Spawned.Keys)
            {
                if (!entity.Deleted)
                {
                    serials.Add(entity.Serial.Value);
                }
            }
            if (pin.Type == "Sheep")
            {
                sheep += serials.Count;
            }
            else
            {
                humans += serials.Count;
            }
            spawners[i] = new { pin.Id, Serial = spawner.Serial.Value, pin.Type, SerializedType = spawner.GetType().FullName,
                pin.Position, pin.Bounds, pin.Count, pin.HomeRange, SpawnerGuid = spawner.Guid, SpawnedSerialCount = serials.Count,
                SpawnedSerials = serials.ToArray(), Properties = spawner.Entries[0].Properties };
        }
        for (var i = 0; i < routes.Length; i++)
        {
            routes[i] = new { layout.Routes[i].Id, Serial = result.Teleporters[i].Serial.Value, layout.Routes[i].Source,
                layout.Routes[i].Destination, Map = "Felucca" };
            gates[i] = new { layout.Doors[i].Id, Serial = result.Doors[i].Serial.Value, layout.Doors[i].Type,
                layout.Doors[i].Position, layout.Doors[i].Facing, LinkSerial = result.Doors[i].Link.Serial.Value };
        }
        return new { Success = true, InstallationId, LayoutHash = Hash(layout), layout.NativeStageId, layout.NativeFiles,
            StockSerials = Serials(result), result.Created, SpawnerCount = 14, SheepSpawnerCount = 2, ActiveVendors = humans,
            ActiveSheep = sheep, TeleporterCount = 4, DoorCount = 4, Spawners = spawners, Routes = routes, Doors = gates, AwaitingWorldSave = true };
    }

    private static void ValidateJournal(Layout layout, Result current, string archivePath)
    {
        var receiptPath = Path.Combine(Path.GetDirectoryName(archivePath)!, "installed.json");
        if (!File.Exists(archivePath))
        {
            Require(!File.Exists(receiptPath), "A V2 receipt exists without its original archive.");
            return;
        }
        var before = ReadJournal(archivePath, layout);
        var actual = Serials(current);
        if (File.Exists(receiptPath))
        {
            var installed = ReadJournal(receiptPath, layout);
            Require(installed.Success && (SameSerials(actual, before.StockSerials) || SameSerials(actual, installed.StockSerials)),
                "The current V2 stock serials differ from the archived and installed identities.");
        }
        else
        {
            Require(SameSerials(actual, before.StockSerials), "The V2 stock serials differ from the pre-installation archive.");
        }
    }

    private static Journal ReadJournal(string path, Layout layout)
    {
        var saved = JsonSerializer.Deserialize<Journal>(ReadBytes(path, 32768), _options);
        Require(saved?.InstallationId == InstallationId && saved.LayoutHash == Hash(layout) && saved.StockSerials?.Length == 22,
            "A V2 journal belongs to another native/layout identity.");
        return saved;
    }

    internal static void ValidateOldRemoval(string path)
    {
        var removal = JsonSerializer.Deserialize<Removal>(ReadBytes(path, 16384), _options);
        Require(removal is { Success: true, InstallationId: "newbie-city-removal-v1", NoObjectsRemaining: true } &&
            removal.StockSerials?.Length == 19 && removal.OwnedMobileSerials?.Length == 22, "The successful original-city removal receipt is required.");
        for (var i = 0; i < removal.StockSerials.Length; i++)
        {
            Require(removal.StockSerials[i] == NewbieCityRemoval.StockSerialBase + (uint)i &&
                World.FindEntity((Serial)removal.StockSerials[i]) == null, "An original city stock object remains; V2 will not duplicate it.");
        }
        var unique = new HashSet<uint>();
        foreach (var serial in removal.OwnedMobileSerials)
        {
            Require(serial is > 0 and < 0x40000000 && unique.Add(serial) && World.FindEntity((Serial)serial) == null,
                "An original city-owned mobile remains; V2 will not duplicate it.");
        }
    }

    private static uint?[] Serials(Result result)
    {
        var serials = new uint?[22];
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

    private static byte[] ReadBytes(string path, int maximum)
    {
        Require(File.Exists(path) && new FileInfo(path).Length is > 0 && new FileInfo(path).Length <= maximum, "A V2 input is missing or oversized.");
        return File.ReadAllBytes(path);
    }

    private static bool IsHash(string value)
    {
        if (value?.Length != 64)
        {
            return false;
        }
        foreach (var character in value)
        {
            if (character is not (>= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F'))
            {
                return false;
            }
        }
        return true;
    }

    private static Type NpcType(string name) => name switch
    {
        "Banker" => typeof(Banker), "Blacksmith" => typeof(Blacksmith), "Tailor" => typeof(Tailor),
        "Carpenter" => typeof(Carpenter), "Bowyer" => typeof(Bowyer), "Tinker" => typeof(Tinker),
        "Alchemist" => typeof(Alchemist), "Mage" => typeof(Mage), "Provisioner" => typeof(Provisioner),
        "Farmer" => typeof(Farmer), "AnimalTrainer" => typeof(AnimalTrainer), "Healer" => typeof(Healer),
        "Sheep" => typeof(Sheep), _ => throw new InvalidOperationException("An unapproved V2 NPC type was requested.")
    };
    private static bool ContainsBounds(Rectangle3D outer, Rectangle3D inner) => outer.Contains(inner.Start) &&
        outer.Contains(new Point3D(inner.End.X - 1, inner.End.Y - 1, inner.End.Z - 1));
    private static bool Overlaps(Rectangle3D a, Rectangle3D b) => a.X < b.End.X && b.X < a.End.X && a.Y < b.End.Y && b.Y < a.End.Y;
    private static bool ValidId(string id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 64;
    private static Point3D Closed(BaseDoor gate) => gate.Open ? new(gate.X - gate.Offset.X, gate.Y - gate.Offset.Y, gate.Z - gate.Offset.Z) : gate.Location;
    internal static string Name(string id) => $"Newbie City V2 - {id}";
    internal static Guid SpawnGuid(string id) => new(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{InstallationId}/{id}")).AsSpan(0, 16));
    private static string Properties(SpawnPin pin) => pin.Type == "Sheep" ? $"Direction {pin.Facing}" : $"Blessed true Direction {pin.Facing}";
    private static string Hash(Layout layout) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(layout, _options)));

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
