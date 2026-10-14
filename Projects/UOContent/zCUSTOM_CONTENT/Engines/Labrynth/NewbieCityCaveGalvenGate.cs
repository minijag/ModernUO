using System;
using System.IO;
using System.Text.Json;
using Server.Items;
using Server.Logging;
using Snapshot = Server.Engines.Labrynth.NewbieCityGalvenGate.Snapshot;

namespace Server.Engines.Labrynth;

public static class NewbieCityCaveGalvenGate
{
    internal const string InstallationId = "newbie-city-cave-galven-gate-v1";
    internal const string GateName = "a moongate to Galven";
    internal static Point3D Source => new(5590, 1315, 0);
    internal static Point3D Destination => new(1317, 1380, 0);
    private sealed record Marker(string InstallationId, bool ReturnEnabled);
    internal sealed record Archive(string InstallationId, Snapshot Gate);
    private sealed record Receipt(bool Success, string InstallationId, Snapshot Gate);
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(NewbieCityCaveGalvenGate));
    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public static void Initialize()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data", "NewbieCityCaveGalvenGate");
        var pending = Path.Combine(directory, "install.pending");
        if (!File.Exists(pending) || File.Exists(Path.Combine(directory, "install.completed")))
        {
            return;
        }
        var resultPath = Path.Combine(directory, "result.json");
        try
        {
            var marker = JsonSerializer.Deserialize<Marker>(ReadBytes(pending, 1024), _options);
            Require(marker is { InstallationId: InstallationId, ReturnEnabled: false }, "Authorize only this one-way cave gate to Galven.");
            Ensure(out _, archivePath: Path.Combine(directory, "before.json"), commit: (gate, created) =>
            {
                var result = new
                {
                    Success = true,
                    InstallationId,
                    Created = created,
                    Gate = Describe(gate),
                    AwaitingWorldSave = true,
                    Archive = "Data/NewbieCityCaveGalvenGate/before.json"
                };
                WriteJson(Path.Combine(directory, "installed.json"), result);
                WriteJson(resultPath, result);
            });
            _logger.Information("Verified the one-way gate inside the Newbie City cave; awaiting world save");
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Newbie City cave Galven gate installation refused");
            WriteJson(resultPath, new { Success = false, InstallationId, Error = exception.ToString() });
        }
    }

    internal static Teleporter Ensure(out bool created, bool checkSupport = true, string archivePath = null, Action<Teleporter, bool> commit = null)
    {
        created = false;
        var gate = FindAndValidate(checkSupport);
        if (archivePath != null)
        {
            if (File.Exists(archivePath))
            {
                ValidateArchive(JsonSerializer.Deserialize<Archive>(ReadBytes(archivePath, 8192), _options), gate,
                    Path.Combine(Path.GetDirectoryName(archivePath), "installed.json"));
            }
            else
            {
                using var stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                JsonSerializer.Serialize(stream, new Archive(InstallationId, Describe(gate)), _options);
                stream.Flush(true);
            }
        }
        if (gate != null)
        {
            commit?.Invoke(gate, false);
            return gate;
        }
        // A stock Teleporter allows Young players to make this same-facet journey.
        gate = new Teleporter(Destination, Map.Felucca)
        {
            Name = GateName,
            ItemID = 0xF6C,
            Hue = 0x482,
            Visible = true,
            Light = LightType.Circle300,
            SoundID = 0x1FE
        };
        try
        {
            gate.MoveToWorld(Source, Map.Felucca);
            commit?.Invoke(gate, true);
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
            if (item is Teleporter gate)
            {
                Require(found == null && gate.GetType() == typeof(Teleporter) && !gate.Deleted && gate.Parent == null &&
                    gate.Map == Map.Felucca && gate.MapDest == Map.Felucca && IsExactSnapshot(Describe(gate)),
                    "A conflicting or duplicate travel trigger occupies the cave gate.");
                found = gate;
            }
            else
            {
                Require(!IsPortal(item), "Another moongate occupies the cave gate.");
            }
        }
        foreach (var item in Map.Felucca.GetItemsInRange<Item>(Destination, 0))
        {
            Require(Math.Abs(item.Z - Destination.Z) > 16 || item is not Teleporter { Active: true } && !IsPortal(item),
                "The Galven arrival tile has another active travel trigger.");
        }
        if (checkSupport)
        {
            Require(Map.Felucca.CanFit(Source, 16, true, false) && Map.Felucca.CanFit(Destination, 16, true, false),
                "The cave gate and Galven arrival need clear walkable support.");
        }
        return found;
    }

    private static bool IsPortal(Item item) => item is Moongate or PublicMoongate || item.ItemID == 0xF6C;

    internal static Snapshot Describe(Teleporter gate) => gate == null
        ? new(null, GateName, new(Source.X, Source.Y, Source.Z, "Felucca"), new(Destination.X, Destination.Y, Destination.Z, "Felucca"),
            0xF6C, 0x482, 0x1FE, LightType.Circle300, TeleporterFlags.Active, true, false, 0)
        : NewbieCityUndergroundCave.DescribeGate(gate);

    internal static void ValidateArchive(Archive archive, Teleporter gate, string receiptPath)
    {
        Require(archive?.InstallationId == InstallationId && IsExactSnapshot(archive.Gate), "The cave gate archive has another installation or route.");
        Require(archive.Gate.Serial == null || archive.Gate.Serial == gate?.Serial.Value, "An archived cave gate is missing or has another serial.");
        if (File.Exists(receiptPath))
        {
            var receipt = JsonSerializer.Deserialize<Receipt>(ReadBytes(receiptPath, 8192), _options);
            Require(receipt is { Success: true, InstallationId: InstallationId } && IsExactSnapshot(receipt.Gate) && receipt.Gate.Serial != null,
                "The cave gate receipt is not this successful one-way installation.");
            Require(gate?.Serial.Value == archive.Gate.Serial || gate?.Serial.Value == receipt.Gate.Serial,
                "The cave gate differs from its archive and successful receipt.");
        }
    }

    private static bool IsExactSnapshot(Snapshot snapshot) => snapshot != null && (snapshot with { Serial = null }) == Describe(null);

    private static byte[] ReadBytes(string path, int maximum)
    {
        Require(new FileInfo(path).Length is > 0 && new FileInfo(path).Length <= maximum, "A cave gate installation journal has an invalid size.");
        return File.ReadAllBytes(path);
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
