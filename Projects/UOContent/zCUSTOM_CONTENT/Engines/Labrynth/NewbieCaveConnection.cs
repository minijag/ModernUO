using System;
using System.IO;
using System.Text.Json;
using Server.Items;
using Server.Json;
using Server.Logging;

namespace Server.Engines.Labrynth;

public static class NewbieCaveConnection
{
    internal const string InstallationId = "newbie-cave-fix-v1";
    internal sealed record Route(string Id, uint Serial, Point3D OldSource, Point3D OldDestination,
        Point3D Source, Point3D Destination);
    internal static readonly Route[] Routes =
    [
        new("city-to-island-1", 1073772332, new(5578, 1351, 0), new(721, 1612, 10), new(5578, 1351, 0), new(721, 1612, 0)),
        new("island-to-city-1", 1073772333, new(721, 1612, 10), new(5578, 1351, 0), new(721, 1612, 0), new(5578, 1351, 0)),
        new("city-to-island-2", 1073772334, new(5578, 1352, 0), new(721, 1613, 10), new(5578, 1352, 0), new(721, 1613, 0)),
        new("island-to-city-2", 1073772335, new(721, 1613, 10), new(5578, 1352, 0), new(721, 1613, 0), new(5578, 1352, 0))
    ];
    private sealed record Marker(string InstallationId);
    private sealed record CityRoute(string Id, uint Serial, Point3D Source, Point3D Destination, string Map);
    private sealed record CityReceipt(bool Success, string InstallationId, CityRoute[] Routes);
    private sealed record Snapshot(string Id, uint Serial, string Name, string Type, Point3D Source, Point3D Destination,
        string Map, int ItemID, TeleporterFlags Flags, bool Visible, bool Movable);
    private sealed record Archive(string InstallationId, Snapshot[] Routes);
    private static readonly JsonSerializerOptions _options = JsonConfig.GetOptions();
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(NewbieCaveConnection));

    public static void Initialize()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data", "NewbieCaveConnection");
        var pending = Path.Combine(directory, "install.pending");
        if (!File.Exists(pending) || File.Exists(Path.Combine(directory, "install.completed")))
        {
            return;
        }
        var resultPath = Path.Combine(directory, "result.json");
        try
        {
            Require(new FileInfo(pending).Length is > 0 and <= 1024, "Invalid cave-connection authorization marker.");
            Require(JsonSerializer.Deserialize<Marker>(File.ReadAllBytes(pending), _options)?.InstallationId == InstallationId,
                "The marker does not authorize this cave-connection correction.");
            ValidateHistoricalReceipt(Path.Combine(Core.BaseDirectory, "Data", "NewbieCity", "result.json"));
            var teleporters = new Teleporter[Routes.Length];
            for (var i = 0; i < Routes.Length; i++)
            {
                teleporters[i] = World.FindItem((Serial)Routes[i].Serial) as Teleporter;
            }
            Validate(teleporters, true);
            var archivePath = Path.Combine(directory, "before.json");
            if (!File.Exists(archivePath))
            {
                using var stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                JsonSerializer.Serialize(stream, new Archive(InstallationId, Describe(teleporters)), _options);
                stream.Flush(true);
            }
            else
            {
                Require(new FileInfo(archivePath).Length is > 0 and <= 8192, "Invalid cave-connection archive size.");
                ValidateArchive(JsonSerializer.Deserialize<Archive>(File.ReadAllBytes(archivePath), _options));
            }
            var changed = Apply(teleporters);
            WriteJson(resultPath, new
            {
                Success = true, InstallationId, Changed = changed, Routes = Describe(teleporters),
                Created = 0, Deleted = 0, AwaitingWorldSave = true
            });
            _logger.Information("Newbie cave connection ready: four existing pads verified at island Z0; changed={Changed}; awaiting world save", changed);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Newbie cave connection correction refused");
            WriteJson(resultPath, new { Success = false, InstallationId, Error = exception.ToString() });
        }
    }

    internal static int Apply(Teleporter[] teleporters, bool checkSupport = true)
    {
        Validate(teleporters, checkSupport);
        var positions = new Point3D[Routes.Length];
        var destinations = new Point3D[Routes.Length];
        for (var i = 0; i < Routes.Length; i++)
        {
            positions[i] = teleporters[i].Location;
            destinations[i] = teleporters[i].PointDest;
        }
        var changed = 0;
        try
        {
            for (var i = 0; i < Routes.Length; i++)
            {
                var route = Routes[i];
                var teleporter = teleporters[i];
                if (teleporter.Location == route.Source && teleporter.PointDest == route.Destination)
                {
                    continue;
                }
                teleporter.PointDest = route.Destination;
                teleporter.MoveToWorld(route.Source, Map.Felucca);
                changed++;
            }
            Validate(teleporters, checkSupport);
            return changed;
        }
        catch
        {
            for (var i = Routes.Length - 1; i >= 0; i--)
            {
                teleporters[i].PointDest = destinations[i];
                teleporters[i].MoveToWorld(positions[i], Map.Felucca);
            }
            throw;
        }
    }

    private static void Validate(Teleporter[] teleporters, bool checkSupport)
    {
        Require(teleporters?.Length == Routes.Length, "The cave correction requires all four existing teleporters.");
        for (var i = 0; i < Routes.Length; i++)
        {
            var route = Routes[i];
            var teleporter = teleporters[i];
            Require(teleporter != null && teleporter.GetType() == typeof(Teleporter) && !teleporter.Deleted &&
                teleporter.Serial.Value == route.Serial && teleporter.Parent == null && teleporter.Map == Map.Felucca &&
                teleporter.Name == Name(route) && teleporter.MapDest == Map.Felucca && teleporter.ItemID == 0x1BC3 &&
                teleporter.Hue == 0 && teleporter.Flags == TeleporterFlags.Active && !teleporter.Visible && !teleporter.Movable &&
                teleporter.Delay == TimeSpan.Zero && teleporter.SoundID == 0 &&
                IsApprovedState(route, teleporter.Location, teleporter.PointDest),
                $"Cave route {route.Id} has a missing, changed, or unexpected stock teleporter.");
            foreach (var other in Map.Felucca.GetItemsInRange<Teleporter>(route.Source, 0))
            {
                if (Math.Abs(other.Z - route.Source.Z) <= 16 || Math.Abs(other.Z - route.OldSource.Z) <= 16)
                {
                    Require(other == teleporter, $"A conflicting teleporter occupies the old or corrected source for {route.Id}.");
                }
            }
            if (checkSupport)
            {
                Require(Map.Felucca.CanFit(route.Source, 16, true, false) && Map.Felucca.CanFit(route.Destination, 16, true, false),
                    $"The corrected {route.Id} route needs supported and unobstructed source and arrival tiles.");
            }
        }
    }

    internal static void ValidateHistoricalReceipt(string path)
    {
        Require(File.Exists(path) && new FileInfo(path).Length is > 0 and <= 32768,
            "The successful Newbie City receipt is required for this correction.");
        var receipt = JsonSerializer.Deserialize<CityReceipt>(File.ReadAllBytes(path), _options);
        Require(receipt is { Success: true, InstallationId: "newbie-city-v1" } && receipt.Routes?.Length == Routes.Length,
            "The original city receipt does not describe a successful four-route installation.");
        for (var i = 0; i < Routes.Length; i++)
        {
            var original = receipt.Routes[i];
            var expected = Routes[i];
            Require(original != null && original.Id == expected.Id && original.Serial == expected.Serial &&
                original.Source == expected.OldSource && original.Destination == expected.OldDestination && original.Map == "Felucca",
                "The original city receipt does not match the four authorized route serials and coordinates.");
        }
    }

    private static void ValidateArchive(Archive archive)
    {
        Require(archive?.InstallationId == InstallationId && archive.Routes?.Length == Routes.Length,
            "The cave-connection archive belongs to another installation.");
        for (var i = 0; i < Routes.Length; i++)
        {
            var saved = archive.Routes[i];
            var route = Routes[i];
            Require(saved != null && saved.Id == route.Id && saved.Serial == route.Serial && saved.Name == Name(route) &&
                saved.Type == typeof(Teleporter).FullName && saved.Map == "Felucca" && saved.ItemID == 0x1BC3 &&
                saved.Flags == TeleporterFlags.Active && !saved.Visible && !saved.Movable &&
                IsApprovedState(route, saved.Source, saved.Destination), "The cave-connection archive has unexpected route identity or state.");
        }
    }

    private static Snapshot[] Describe(Teleporter[] teleporters)
    {
        var result = new Snapshot[Routes.Length];
        for (var i = 0; i < Routes.Length; i++)
        {
            var teleporter = teleporters[i];
            result[i] = new(Routes[i].Id, teleporter.Serial.Value, teleporter.Name, teleporter.GetType().FullName,
                teleporter.Location, teleporter.PointDest, "Felucca", teleporter.ItemID, teleporter.Flags, teleporter.Visible, teleporter.Movable);
        }
        return result;
    }

    private static bool IsApprovedState(Route route, Point3D source, Point3D destination) =>
        source == route.OldSource && destination == route.OldDestination || source == route.Source && destination == route.Destination;
    private static string Name(Route route) => $"Newbie City - {route.Id}";

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
