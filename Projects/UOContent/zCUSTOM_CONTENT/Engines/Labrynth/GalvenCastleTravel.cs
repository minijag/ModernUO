using System;
using System.IO;
using System.Text.Json;
using Server.Items;
using Server.Logging;

namespace Server.Engines.Labrynth;

public static class GalvenCastleTravel
{
    internal const string InstallationId = "galven-castle-travel-v1";
    internal readonly record struct Route(Point3D Source, Point3D Destination, string Name);
    internal static readonly Route[] Routes =
    [
        new(new(1281, 1326, 24), new(5474, 1010, 5), "Galven - King's Castle"),
        new(new(5474, 1010, 5), new(1281, 1326, 24), "King's Castle - Galven")
    ];
    internal sealed record Marker(string InstallationId, bool ReturnEnabled);
    internal sealed record Endpoint(int X, int Y, int Z, string Map);
    internal sealed record RouteSnapshot(uint? Serial, string Name, Endpoint Source, Endpoint Destination);
    internal sealed record Archive(string InstallationId, RouteSnapshot[] Routes);
    private sealed record Receipt(bool Success, string InstallationId, RouteSnapshot[] Routes);
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(GalvenCastleTravel));
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public static void Initialize()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data", "GalvenCastleTravel");
        var pending = Path.Combine(directory, "install.pending");
        if (!File.Exists(pending) || File.Exists(Path.Combine(directory, "install.completed")))
        {
            return;
        }
        var resultPath = Path.Combine(directory, "result.json");
        var receiptPath = Path.Combine(directory, "installed.json");
        try
        {
            Require(new FileInfo(pending).Length is > 0 and <= 1024, "The castle-travel installation marker has an invalid size.");
            var marker = JsonSerializer.Deserialize<Marker>(File.ReadAllBytes(pending), _jsonOptions);
            Require(marker is { InstallationId: InstallationId, ReturnEnabled: true },
                "The marker must authorize the two-way Galven castle route.");
            var existing = FindAndValidate(true);
            var archivePath = Path.Combine(directory, "before.json");
            if (!File.Exists(archivePath))
            {
                using var stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                JsonSerializer.Serialize(stream, new Archive(InstallationId, Describe(existing)), _jsonOptions);
                stream.Flush(true);
            }
            else
            {
                Require(new FileInfo(archivePath).Length is > 0 and <= 8192, "The castle-travel archive has an invalid size.");
                var archive = JsonSerializer.Deserialize<Archive>(File.ReadAllBytes(archivePath), _jsonOptions);
                ValidateArchiveIdentity(archive, existing, receiptPath);
            }
            var teleporters = Ensure(out var created);
            var result = new
            {
                Success = true,
                InstallationId,
                Created = created,
                Routes = Describe(teleporters),
                AwaitingWorldSave = true,
                Archive = "Data/GalvenCastleTravel/before.json"
            };
            WriteJson(receiptPath, result);
            WriteJson(resultPath, result);
            _logger.Information("Verified two Galven castle teleporters; created={Created}; awaiting world save", created);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Galven castle travel installation refused");
            WriteJson(resultPath, new { Success = false, InstallationId, Error = exception.ToString() });
        }
    }

    internal static Teleporter[] Ensure(out int created, bool checkSupport = true)
    {
        created = 0;
        var teleporters = FindAndValidate(checkSupport);
        var added = new bool[Routes.Length];
        try
        {
            for (var index = 0; index < Routes.Length; index++)
            {
                if (teleporters[index] != null)
                {
                    continue;
                }
                var route = Routes[index];
                var teleporter = new Teleporter(route.Destination, Map.Felucca) { Name = route.Name };
                teleporters[index] = teleporter;
                added[index] = true;
                teleporter.MoveToWorld(route.Source, Map.Felucca);
                created++;
            }
            return teleporters;
        }
        catch
        {
            for (var index = 0; index < teleporters.Length; index++)
            {
                if (added[index])
                {
                    teleporters[index].Delete();
                }
            }
            created = 0;
            throw;
        }
    }

    private static Teleporter[] FindAndValidate(bool checkSupport)
    {
        var found = new Teleporter[Routes.Length];
        for (var index = 0; index < Routes.Length; index++)
        {
            var route = Routes[index];
            foreach (var teleporter in Map.Felucca.GetItemsInRange<Teleporter>(route.Source, 0))
            {
                if (Math.Abs(teleporter.Z - route.Source.Z) > 16)
                {
                    continue;
                }
                Require(found[index] == null && IsExactRoute(teleporter, route),
                    $"Conflicting or duplicate castle-travel teleporter at {route.Source}.");
                found[index] = teleporter;
            }
            if (checkSupport)
            {
                Require(Map.Felucca.CanFit(route.Source, 16, true, false) &&
                    Map.Felucca.CanFit(route.Destination, 16, true, false),
                    $"The castle-travel route at {route.Source} needs clear supported source and arrival tiles.");
            }
            foreach (var teleporter in Map.Felucca.GetItemsInRange<Teleporter>(route.Destination, 0))
            {
                if (!teleporter.Active || Math.Abs(teleporter.Z - route.Destination.Z) > 16)
                {
                    continue;
                }
                Require(IsExactRoute(teleporter, Routes[1 - index]),
                    $"An unexpected active teleporter occupies the castle arrival at {route.Destination}.");
            }
        }
        return found;
    }

    private static bool IsExactRoute(Teleporter teleporter, Route route) =>
        teleporter.GetType() == typeof(Teleporter) && !teleporter.Deleted && teleporter.Parent == null &&
        teleporter.Location == route.Source && teleporter.Map == Map.Felucca &&
        teleporter.PointDest == route.Destination && teleporter.MapDest == Map.Felucca && teleporter.Name == route.Name &&
        teleporter.ItemID == 0x1BC3 && teleporter.Hue == 0 && teleporter.Flags == TeleporterFlags.Active &&
        !teleporter.Visible && !teleporter.Movable && teleporter.Delay == TimeSpan.Zero && teleporter.SoundID == 0;

    internal static void ValidateArchiveIdentity(Archive archive, Teleporter[] existing, string receiptPath)
    {
        Require(archive?.InstallationId == InstallationId, "The existing archive belongs to another installation.");
        ValidateSnapshots(archive.Routes);
        for (var index = 0; index < Routes.Length; index++)
        {
            Require(archive.Routes[index].Serial == null || existing[index]?.Serial.Value == archive.Routes[index].Serial,
                "An archived existing castle teleporter is missing or has another serial.");
        }
        if (File.Exists(receiptPath))
        {
            Require(new FileInfo(receiptPath).Length is > 0 and <= 8192, "The castle-travel receipt has an invalid size.");
            var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllBytes(receiptPath), _jsonOptions);
            Require(receipt is { Success: true, InstallationId: InstallationId },
                "The castle-travel receipt does not describe this successful installation.");
            ValidateSnapshots(receipt.Routes);
            var original = true;
            var installed = true;
            for (var index = 0; index < Routes.Length; index++)
            {
                var serial = existing[index]?.Serial.Value;
                original &= serial == archive.Routes[index].Serial;
                installed &= receipt.Routes[index].Serial != null && serial == receipt.Routes[index].Serial;
            }
            Require(original || installed, "The castle teleporters differ from both the archive and successful receipt.");
        }
    }

    private static void ValidateSnapshots(RouteSnapshot[] snapshots)
    {
        Require(snapshots?.Length == Routes.Length, "The castle-travel journal has an unexpected route count.");
        for (var index = 0; index < Routes.Length; index++)
        {
            var route = Routes[index];
            Require(snapshots[index]?.Name == route.Name && snapshots[index].Source == Point(route.Source) &&
                snapshots[index].Destination == Point(route.Destination), "The castle-travel journal describes another route.");
        }
    }

    private static Endpoint Point(Point3D point) => new(point.X, point.Y, point.Z, "Felucca");

    private static RouteSnapshot[] Describe(Teleporter[] teleporters)
    {
        var result = new RouteSnapshot[Routes.Length];
        for (var index = 0; index < Routes.Length; index++)
        {
            var route = Routes[index];
            result[index] = new RouteSnapshot(teleporters[index] is { } teleporter ? teleporter.Serial.Value : null,
                route.Name, Point(route.Source), Point(route.Destination));
        }
        return result;
    }

    private static void WriteJson<T>(string path, T value)
    {
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(value, _jsonOptions));
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
