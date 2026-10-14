using System;
using System.IO;
using System.Text.Json;
using Server.Items;
using Server.Logging;
using Snapshot = Server.Engines.Labrynth.NewbieCityGalvenGate.Snapshot;

namespace Server.Engines.Labrynth;

public static class NewbieCityGalvenDestination
{
    internal const string InstallationId = "newbie-city-galven-destination-v2";
    internal const uint GateSerial = 1073772439;
    internal static Point3D Destination => new(1331, 1390, 1);
    private sealed record Marker(string InstallationId);
    private sealed record Archive(string InstallationId, Snapshot Gate);
    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(NewbieCityGalvenDestination));

    public static void Initialize()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data", "NewbieCityGalvenDestination");
        var pending = Path.Combine(directory, "install.pending");
        if (!File.Exists(pending) || File.Exists(Path.Combine(directory, "install.completed")))
        {
            return;
        }
        var resultPath = Path.Combine(directory, "result.json");
        try
        {
            Require(new FileInfo(pending).Length is > 0 and <= 1024, "Invalid Galven destination authorization size.");
            Require(JsonSerializer.Deserialize<Marker>(File.ReadAllBytes(pending), _options)?.InstallationId == InstallationId,
                "Authorize only this Galven destination change.");
            Require(File.Exists(Path.Combine(Core.BaseDirectory, "Data", "NewbieCityCaveGalvenGate", "install.completed")),
                "The original cave gate installation must remain completed.");
            var gate = World.FindItem((Serial)GateSerial) as Teleporter;
            Validate(gate, true);
            var archivePath = Path.Combine(directory, "before.json");
            if (File.Exists(archivePath))
            {
                Require(new FileInfo(archivePath).Length is > 0 and <= 8192, "Invalid Galven destination archive size.");
                var archive = JsonSerializer.Deserialize<Archive>(File.ReadAllBytes(archivePath), _options);
                Require(archive?.InstallationId == InstallationId && archive.Gate == Expected(false) with { Serial = GateSerial },
                    "The destination archive has another gate or route.");
            }
            else
            {
                Require(gate.PointDest == NewbieCityCaveGalvenGate.Destination, "An updated destination requires its original archive.");
                using var stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                JsonSerializer.Serialize(stream, new Archive(InstallationId, NewbieCityCaveGalvenGate.Describe(gate)), _options);
                stream.Flush(true);
            }
            var previous = gate.PointDest;
            try
            {
                var changed = Apply(gate);
                var temporary = resultPath + ".tmp";
                File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(new
                {
                    Success = true, InstallationId, Changed = changed, Gate = NewbieCityCaveGalvenGate.Describe(gate),
                    Created = 0, Deleted = 0, AwaitingWorldSave = true
                }, _options));
                File.Move(temporary, resultPath, true);
            }
            catch
            {
                gate.PointDest = previous;
                throw;
            }
            _logger.Information("Newbie City moongate destination verified at 1331,1390; awaiting world save");
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Newbie City Galven destination update refused");
            File.WriteAllBytes(resultPath, JsonSerializer.SerializeToUtf8Bytes(new { Success = false, InstallationId, Error = exception.ToString() }, _options));
        }
    }

    internal static bool Apply(Teleporter gate, bool checkSupport = true)
    {
        Validate(gate, checkSupport);
        var changed = gate.PointDest != Destination;
        gate.PointDest = Destination;
        return changed;
    }

    private static Snapshot Expected(bool updated) => updated
        ? NewbieCityCaveGalvenGate.Describe(null) with { Destination = new(Destination.X, Destination.Y, Destination.Z, "Felucca") }
        : NewbieCityCaveGalvenGate.Describe(null);

    private static void Validate(Teleporter gate, bool checkSupport)
    {
        Require(gate != null && gate.GetType() == typeof(Teleporter) && !gate.Deleted && gate.Parent == null,
            "The original stock cave gate must exist.");
        var actual = NewbieCityCaveGalvenGate.Describe(gate) with { Serial = null };
        Require(actual == Expected(false) || actual == Expected(true), "The cave gate differs from its authorized source, appearance or destination.");
        foreach (var other in Map.Felucca.GetItemsInRange<Item>(Destination, 0))
        {
            if (Math.Abs(other.Z - Destination.Z) <= 16)
            {
                Require(other is not Teleporter { Active: true } and not Moongate and not PublicMoongate && other.ItemID != 0xF6C,
                    "The new Galven arrival has another travel trigger.");
            }
        }
        if (checkSupport)
        {
            Require(Map.Felucca.CanFit(Destination, 16, true, false), "The new Galven arrival must have clear walkable support.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
