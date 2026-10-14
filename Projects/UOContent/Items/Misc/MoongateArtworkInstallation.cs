using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Server.Logging;

namespace Server.Items;

public static class MoongateArtworkInstallation
{
    internal const string InstallationId = "moongate-artwork-v1";
    private const uint NewbieCitySerial = 1073772439;
    private const int ScanSize = 128;
    private const int BatchSize = 32;
    private sealed record Marker(string InstallationId);
    internal sealed record Position(int X, int Y, int Z, int? Map);
    internal sealed record Route(Position Destination, int? Flags, long? DelayTicks, int? Sound, bool? Dispellable);
    internal sealed record Snapshot(uint Serial, string Type, int OldItemId, int NewItemId, Position Location,
        int Hue, string Name, bool Visible, bool Movable, int Light, Route Route);
    internal sealed record Archive(string InstallationId, Snapshot[] Items);
    internal sealed record Result(int Checked, int Changed, int AlreadyConverted, int RemainingLegacy);
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(MoongateArtworkInstallation));
    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public static void Initialize()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data", "MoongateArtwork");
        if (!File.Exists(Path.Combine(directory, "install.pending")) || File.Exists(Path.Combine(directory, "install.completed")))
        {
            return;
        }
        Timer.StartTimer(() => Run(directory));
    }

    private static async void Run(string directory)
    {
        try
        {
            var pending = Path.Combine(directory, "install.pending");
            Require(new FileInfo(pending).Length is > 0 and <= 1024, "Invalid moongate artwork marker size.");
            Require(JsonSerializer.Deserialize<Marker>(File.ReadAllBytes(pending), _options)?.InstallationId == InstallationId,
                "The marker must authorize this exact moongate artwork installation.");
            ValidateTileData();
            ValidateNewbieCityGate(false);
            var targets = await FindTargets();
            var result = await EnsureAsync(Path.Combine(directory, "before.json"), targets, true);
            ValidateNewbieCityGate(true);
            WriteJson(Path.Combine(directory, "result.json"), new
            {
                Success = true, InstallationId, result.Checked, result.Changed, result.AlreadyConverted,
                result.RemainingLegacy, Created = 0, Deleted = 0, OnlyItemIdsChanged = true,
                NewbieCityGate = Describe(World.FindItem((Serial)NewbieCitySerial)), AwaitingWorldSave = true
            });
            _logger.Information("Moongate artwork installed on {Changed} world items; {Checked} verified; awaiting world save",
                result.Changed, result.Checked);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Moongate artwork installation refused");
            WriteJson(Path.Combine(directory, "result.json"), new { Success = false, InstallationId, Error = exception.ToString() });
        }
    }

    private static async Task<List<Item>> FindTargets()
    {
        var targets = new List<Item>();
        var serials = new HashSet<uint>();
        var batches = 0;
        foreach (var map in Map.AllMaps)
        {
            if (map == null || map == Map.Internal)
            {
                continue;
            }
            for (var x = 0; x < map.Width; x += ScanSize)
            {
                for (var y = 0; y < map.Height; y += ScanSize)
                {
                    foreach (var item in map.GetItemsInBounds<Item>(new Rectangle2D(x, y,
                                 Math.Min(ScanSize, map.Width - x), Math.Min(ScanSize, map.Height - y))))
                    {
                        if (!item.Deleted && item.Parent == null && MoongateAppearance.IsMoongate(item.ItemID) && serials.Add(item.Serial.Value))
                        {
                            targets.Add(item);
                        }
                    }
                    if (++batches % BatchSize == 0)
                    {
                        await Timer.Pause(1);
                    }
                }
            }
        }
        targets.Sort((a, b) => a.Serial.Value.CompareTo(b.Serial.Value));
        return targets;
    }

    internal static Result Ensure(string archivePath, IReadOnlyList<Item> targets) =>
        EnsureAsync(archivePath, targets, false).GetAwaiter().GetResult();

    private static async Task<Result> EnsureAsync(string archivePath, IReadOnlyList<Item> targets, bool yieldTicks)
    {
        var current = new Snapshot[targets.Count];
        var serials = new HashSet<uint>();
        for (var i = 0; i < targets.Count; i++)
        {
            current[i] = Describe(targets[i]);
            Require(serials.Add(current[i].Serial), "The target set repeats a serial.");
        }
        Archive archive;
        if (File.Exists(archivePath))
        {
            Require(new FileInfo(archivePath).Length is > 0 and <= 16777216, "Invalid moongate artwork archive size.");
            archive = JsonSerializer.Deserialize<Archive>(File.ReadAllBytes(archivePath), _options);
            Require(archive is { InstallationId: InstallationId, Items: not null } && archive.Items.Length == current.Length,
                "The archive belongs to another installation or item set.");
        }
        else
        {
            archive = new Archive(InstallationId, current);
        }
        var originals = new Dictionary<uint, Snapshot>();
        foreach (var snapshot in archive.Items)
        {
            Require(snapshot != null && originals.TryAdd(snapshot.Serial, snapshot) &&
                MoongateAppearance.IsMoongate(snapshot.OldItemId) && snapshot.NewItemId == MoongateAppearance.ConvertLegacy(snapshot.OldItemId),
                "The archive contains invalid or duplicate moongate items.");
        }
        foreach (var snapshot in current)
        {
            Require(originals.TryGetValue(snapshot.Serial, out var original), "The current item set differs from the durable archive.");
            ValidateSnapshot(snapshot, original);
        }
        if (!File.Exists(archivePath))
        {
            using var stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(stream, archive, _options);
            stream.Flush(true);
        }
        var changed = 0;
        for (var i = 0; i < targets.Count; i++)
        {
            var item = targets[i];
            var original = originals[item.Serial.Value];
            ValidateSnapshot(Describe(item), original);
            if (item.ItemID != original.NewItemId)
            {
                item.ItemID = original.NewItemId;
                changed++;
            }
            ValidateSnapshot(Describe(item), original);
            if (yieldTicks && (i + 1) % BatchSize == 0)
            {
                await Timer.Pause(1);
            }
        }
        foreach (var item in targets)
        {
            Require(item.ItemID == originals[item.Serial.Value].NewItemId, "A world moongate did not retain the installed artwork.");
            ValidateSnapshot(Describe(item), originals[item.Serial.Value]);
        }
        return new Result(targets.Count, changed, targets.Count - changed, 0);
    }

    internal static Snapshot Describe(Item item)
    {
        Require(item is { Deleted: false, Parent: null } && ReferenceEquals(World.FindItem(item.Serial), item) &&
            item.Map != null && item.Map != Map.Internal && MoongateAppearance.IsMoongate(item.ItemID),
            "The target must be a registered, placed moongate artwork item.");
        var route = item switch
        {
            Teleporter gate => new Route(At(gate.PointDest, gate.MapDest), (int)gate.Flags, gate.Delay.Ticks, gate.SoundID, null),
            Moongate gate => new Route(At(gate.Target, gate.TargetMap), null, null, null, gate.Dispellable),
            _ => null
        };
        return new Snapshot(item.Serial.Value, item.GetType().FullName, item.ItemID, MoongateAppearance.ConvertLegacy(item.ItemID),
            At(item.Location, item.Map), item.Hue, item.Name, item.Visible, item.Movable, (int)item.Light, route);
    }

    private static Position At(Point3D point, Map map) => new(point.X, point.Y, point.Z, map?.MapIndex);

    private static void ValidateSnapshot(Snapshot current, Snapshot original)
    {
        Require(current.OldItemId == original.OldItemId || current.OldItemId == original.NewItemId,
            "The item's artwork differs from its authorized original or replacement.");
        Require(current with { OldItemId = original.OldItemId, NewItemId = original.NewItemId } == original,
            "A moongate's non-artwork properties differ from the durable archive.");
    }

    private static void ValidateTileData()
    {
        for (var offset = 0; offset < MoongateAppearance.FrameCount; offset++)
        {
            foreach (var pair in new[] { (MoongateAppearance.Blue, MoongateAppearance.LegacyBlue), (MoongateAppearance.Dark, MoongateAppearance.LegacyDark) })
            {
                var actual = TileData.ItemTable[pair.Item1 + offset];
                var original = TileData.ItemTable[pair.Item2];
                Require(actual.Flags == original.Flags && actual.Height == original.Height && actual.Weight == original.Weight &&
                    (actual.Flags & TileFlag.Animation) != 0, "The native animated artwork tile metadata must be installed first.");
            }
        }
    }

    private static void ValidateNewbieCityGate(bool converted)
    {
        var gate = World.FindItem((Serial)NewbieCitySerial) as Teleporter;
        Require(gate is { Deleted: false, Parent: null, Hue: 0x482, SoundID: 0x1FE, Visible: true, Movable: false } &&
            gate.Map == Map.Felucca && gate.Location == new Point3D(5590, 1315, 0) && gate.MapDest == Map.Felucca &&
            gate.PointDest == new Point3D(1331, 1390, 1) && gate.Delay == TimeSpan.Zero && gate.Flags == TeleporterFlags.Active &&
            gate.Light == LightType.Circle300 && (converted ? gate.ItemID == MoongateAppearance.Blue : MoongateAppearance.IsBlueBase(gate.ItemID)),
            "The Newbie City moongate's identity, appearance, or Galven route differs from the approved gate.");
    }

    private static void WriteJson<T>(string path, T value)
    {
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(value, _options));
        File.Move(temporary, path, true);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
