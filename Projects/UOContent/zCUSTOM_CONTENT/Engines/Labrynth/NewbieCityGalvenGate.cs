using System;
using System.IO;
using System.Text.Json;
using Server.Items;
using Server.Logging;

namespace Server.Engines.Labrynth;

public static class NewbieCityGalvenGate
{
    internal const string InstallationId = "newbie-city-galven-gate-v1";
    internal const string GateName = "a moongate to Galven";
    internal const int BlueHue = 0x482;
    internal static Point3D Source => new(5590, 1323, 0);
    internal static Point3D Destination => new(1317, 1380, 0);
    internal sealed record Marker(string InstallationId, bool ReturnEnabled);
    internal sealed record Endpoint(int X, int Y, int Z, string Map);
    internal sealed record Snapshot(uint? Serial, string Name, Endpoint Source, Endpoint Destination,
        int ItemId, int Hue, int SoundID, LightType Light, TeleporterFlags Flags, bool Visible, bool Movable, long DelayTicks);
    internal sealed record Archive(string InstallationId, Snapshot Gate);
    private sealed record Receipt(bool Success, string InstallationId, Snapshot Gate);
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(NewbieCityGalvenGate));
    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public static void Initialize()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data", "NewbieCityGalvenGate");
        var pending = Path.Combine(directory, "install.pending");
        if (!File.Exists(pending) || File.Exists(Path.Combine(directory, "install.completed")))
        {
            return;
        }

        var resultPath = Path.Combine(directory, "result.json");
        try
        {
            Require(new FileInfo(pending).Length is > 0 and <= 1024, "The Galven gate marker has an invalid size.");
            var marker = JsonSerializer.Deserialize<Marker>(File.ReadAllBytes(pending), _options);
            Require(marker is { InstallationId: InstallationId, ReturnEnabled: false },
                "The marker must authorize only the one-way Galven gate.");
            var existing = FindAndValidate(true);
            var archivePath = Path.Combine(directory, "before.json");
            var receiptPath = Path.Combine(directory, "installed.json");
            if (!File.Exists(archivePath))
            {
                using var stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                JsonSerializer.Serialize(stream, new Archive(InstallationId, Describe(existing)), _options);
                stream.Flush(true);
            }
            else
            {
                Require(new FileInfo(archivePath).Length is > 0 and <= 8192, "The Galven gate archive has an invalid size.");
                ValidateArchiveIdentity(JsonSerializer.Deserialize<Archive>(File.ReadAllBytes(archivePath), _options), existing, receiptPath);
            }

            var gate = Ensure(out var created);
            var result = new
            {
                Success = true,
                InstallationId,
                Created = created,
                Gate = Describe(gate),
                AwaitingWorldSave = true,
                Archive = "Data/NewbieCityGalvenGate/before.json"
            };
            WriteJson(receiptPath, result);
            WriteJson(resultPath, result);
            _logger.Information("Verified one-way Newbie City cave gate to Galven {Serial}; created={Created}; awaiting world save", gate.Serial, created);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Newbie City Galven gate installation refused");
            WriteJson(resultPath, new { Success = false, InstallationId, Error = exception.ToString() });
        }
    }

    internal static Teleporter Ensure(out bool created, bool checkSupport = true)
    {
        created = false;
        var existing = FindAndValidate(checkSupport);
        if (existing != null)
        {
            return existing;
        }

        // Stock moongates reject Young travellers to Felucca, including same-facet travel.
        var gate = new Teleporter(Destination, Map.Felucca)
        {
            Name = GateName,
            ItemID = 0xF6C,
            Hue = BlueHue,
            Visible = true,
            Light = LightType.Circle300,
            SoundID = 0x1FE
        };
        try
        {
            gate.MoveToWorld(Source, Map.Felucca);
            created = true;
            return gate;
        }
        catch
        {
            gate.Delete();
            throw;
        }
    }

    private static Teleporter FindAndValidate(bool checkSupport)
    {
        Teleporter found = null;
        foreach (var item in Map.Felucca.GetItemsInRange<Item>(Source, 0))
        {
            if (Math.Abs(item.Z - Source.Z) > 16)
            {
                continue;
            }
            if (item is Teleporter teleporter)
            {
                Require(found == null && IsExactRoute(teleporter), "A conflicting or duplicate travel trigger occupies the Galven cave gate.");
                found = teleporter;
            }
            else
            {
                Require(!IsPortal(item), "Another moongate occupies the Galven cave gate.");
            }
        }
        foreach (var item in Map.Felucca.GetItemsInRange<Item>(Destination, 0))
        {
            if (Math.Abs(item.Z - Destination.Z) <= 16)
            {
                Require(item is not Teleporter { Active: true } && !IsPortal(item), "The Galven arrival tile has another active travel trigger.");
            }
        }
        if (checkSupport)
        {
            Require(Map.Felucca.CanFit(Source, 16, true, false) && Map.Felucca.CanFit(Destination, 16, true, false),
                "The Galven cave gate and its arrival must have clear walkable support.");
        }
        return found;
    }

    private static bool IsPortal(Item item) => item is Moongate or PublicMoongate || item.ItemID == 0xF6C;

    private static bool IsExactRoute(Teleporter gate) => gate.GetType() == typeof(Teleporter) && !gate.Deleted && gate.Parent == null &&
        gate.Location == Source && gate.Map == Map.Felucca && gate.PointDest == Destination && gate.MapDest == Map.Felucca &&
        gate.Name == GateName && gate.ItemID == 0xF6C && gate.Hue == BlueHue && gate.Light == LightType.Circle300 &&
        gate.Flags == TeleporterFlags.Active && gate.Visible && !gate.Movable && gate.Delay == TimeSpan.Zero && gate.SoundID == 0x1FE;

    internal static Snapshot Describe(Teleporter gate) => new(gate?.Serial.Value, GateName,
        new(Source.X, Source.Y, Source.Z, "Felucca"), new(Destination.X, Destination.Y, Destination.Z, "Felucca"),
        0xF6C, BlueHue, 0x1FE, LightType.Circle300, TeleporterFlags.Active, true, false, 0);

    internal static void ValidateArchiveIdentity(Archive archive, Teleporter existing, string receiptPath)
    {
        Require(archive?.InstallationId == InstallationId && IsExactSnapshot(archive.Gate), "The Galven gate archive describes another installation or route.");
        Require(archive.Gate.Serial == null || existing?.Serial.Value == archive.Gate.Serial,
            "An archived existing Galven gate is missing or has another serial.");
        if (File.Exists(receiptPath))
        {
            Require(new FileInfo(receiptPath).Length is > 0 and <= 8192, "The Galven gate receipt has an invalid size.");
            var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllBytes(receiptPath), _options);
            Require(receipt is { Success: true, InstallationId: InstallationId } && IsExactSnapshot(receipt.Gate) && receipt.Gate.Serial != null,
                "The Galven gate receipt is not this successful one-way installation.");
            var serial = existing?.Serial.Value;
            Require(serial == archive.Gate.Serial || serial == receipt.Gate.Serial,
                "The Galven gate differs from both the archive and successful receipt.");
        }
    }

    private static bool IsExactSnapshot(Snapshot snapshot) => snapshot != null && (snapshot with { Serial = null }) == Describe(null);

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
