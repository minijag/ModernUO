using System;
using System.IO;
using System.Text.Json;
using Server.Items;
using Server.Json;
using Server.Logging;

namespace Server.Engines.Labrynth;

public static class NewbieCityMarketTravel
{
    internal const string InstallationId = "newbie-city-market-v1";
    internal sealed record Route(string Id, uint Serial, Point3D OldSource, Point3D OldDestination,
        Point3D Source, Point3D Destination);
    internal static readonly Route[] Routes =
    [
        new("city-to-island-1", 1073772332, new(5578, 1351, 0), new(721, 1612, 0), new(5608, 1352, 0), new(721, 1612, 0)),
        new("island-to-city-1", 1073772333, new(721, 1612, 0), new(5578, 1351, 0), new(721, 1612, 0), new(5608, 1352, 0)),
        new("city-to-island-2", 1073772334, new(5578, 1352, 0), new(721, 1613, 0), new(5608, 1353, 0), new(721, 1613, 0)),
        new("island-to-city-2", 1073772335, new(721, 1613, 0), new(5578, 1352, 0), new(721, 1613, 0), new(5608, 1353, 0))
    ];
    private sealed record Marker(string InstallationId);
    private sealed record Snapshot(string Id, uint Serial, string Name, string Type, Point3D Source, Point3D Destination,
        string Map, int ItemID, TeleporterFlags Flags, bool Visible, bool Movable);
    private sealed record PriorReceipt(bool Success, string InstallationId, Snapshot[] Routes);
    private sealed record Archive(string InstallationId, Snapshot[] Routes);
    private static readonly JsonSerializerOptions _options = JsonConfig.GetOptions();
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(NewbieCityMarketTravel));

    public static void Initialize()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data", "NewbieCityMarketTravel");
        var pending = Path.Combine(directory, "install.pending");
        if (!File.Exists(pending) || File.Exists(Path.Combine(directory, "install.completed")))
        {
            return;
        }
        var resultPath = Path.Combine(directory, "result.json");
        try
        {
            Require(new FileInfo(pending).Length is > 0 and <= 1024, "Invalid city-market travel authorization marker.");
            Require(JsonSerializer.Deserialize<Marker>(File.ReadAllBytes(pending), _options)?.InstallationId == InstallationId,
                "The marker does not authorize this city-market travel relocation.");
            ValidateHistoricalReceipt(Path.Combine(Core.BaseDirectory, "Data", "NewbieCaveConnection", "result.json"));
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
                Require(new FileInfo(archivePath).Length is > 0 and <= 8192, "Invalid city-market travel archive size.");
                ValidateArchive(JsonSerializer.Deserialize<Archive>(File.ReadAllBytes(archivePath), _options));
            }
            var changed = Apply(teleporters);
            WriteJson(resultPath, new
            {
                Success = true, InstallationId, Changed = changed, Routes = Describe(teleporters),
                Created = 0, Deleted = 0, AwaitingWorldSave = true
            });
            _logger.Information("Newbie City market travel ready: four existing pads verified; city pads at 5608,1352/1353; changed={Changed}; awaiting world save", changed);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Newbie City market travel relocation refused");
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
        Require(teleporters?.Length == Routes.Length, "The market relocation requires all four existing teleporters.");
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
                $"Market route {route.Id} has a missing, changed, or unexpected stock teleporter.");
            ValidateSource(route.Source, teleporter);
            if (route.Source != route.OldSource)
            {
                ValidateSource(route.OldSource, teleporter);
            }
            if (checkSupport)
            {
                Require(Map.Felucca.CanFit(route.Source, 16, true, false) && Map.Felucca.CanFit(route.Destination, 16, true, false),
                    $"The relocated {route.Id} route needs supported and unobstructed source and arrival tiles.");
            }
        }
    }

    private static void ValidateSource(Point3D source, Teleporter expected)
    {
        foreach (var other in Map.Felucca.GetItemsInRange<Teleporter>(source, 0))
        {
            if (Math.Abs(other.Z - source.Z) <= 16)
            {
                Require(other == expected, $"A conflicting teleporter occupies an old or relocated city-market source at {source}.");
            }
        }
    }

    internal static void ValidateHistoricalReceipt(string path)
    {
        Require(File.Exists(path) && new FileInfo(path).Length is > 0 and <= 8192,
            "The successful cave-connection receipt is required for this market relocation.");
        var receipt = JsonSerializer.Deserialize<PriorReceipt>(File.ReadAllBytes(path), _options);
        Require(receipt is { Success: true, InstallationId: "newbie-cave-fix-v1" } && receipt.Routes?.Length == Routes.Length,
            "The cave receipt does not describe a successful four-route correction.");
        for (var i = 0; i < Routes.Length; i++)
        {
            var original = receipt.Routes[i];
            var expected = Routes[i];
            Require(HasIdentity(original, expected) && original.Source == expected.OldSource && original.Destination == expected.OldDestination,
                "The cave receipt does not match the four authorized route serials and coordinates.");
        }
    }

    private static void ValidateArchive(Archive archive)
    {
        Require(archive?.InstallationId == InstallationId && archive.Routes?.Length == Routes.Length,
            "The city-market travel archive belongs to another installation.");
        for (var i = 0; i < Routes.Length; i++)
        {
            var saved = archive.Routes[i];
            var route = Routes[i];
            Require(HasIdentity(saved, route) && IsApprovedState(route, saved.Source, saved.Destination),
                "The city-market travel archive has unexpected route identity or state.");
        }
    }

    private static bool HasIdentity(Snapshot saved, Route route) => saved != null && saved.Id == route.Id &&
        saved.Serial == route.Serial && saved.Name == Name(route) && saved.Type == typeof(Teleporter).FullName &&
        saved.Map == "Felucca" && saved.ItemID == 0x1BC3 && saved.Flags == TeleporterFlags.Active && !saved.Visible && !saved.Movable;

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
