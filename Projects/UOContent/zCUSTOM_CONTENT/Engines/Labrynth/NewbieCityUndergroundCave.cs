using System;
using System.IO;
using System.Text.Json;
using Server.Items;
using Server.Logging;
using Snapshot = Server.Engines.Labrynth.NewbieCityGalvenGate.Snapshot;
using Endpoint = Server.Engines.Labrynth.NewbieCityGalvenGate.Endpoint;

namespace Server.Engines.Labrynth;

public static class NewbieCityUndergroundCave
{
    internal const string InstallationId = "newbie-city-underground-cave-v1";
    internal const uint GateSerial = 1073772439;
    internal static Point3D GatePosition => new(6028, 3446, -20);
    internal static Rectangle2D InteriorBounds => new(6023, 3443, 11, 11);
    internal sealed record Route(string Name, Point3D Source, Point3D Destination);
    internal static readonly Route[] Routes =
    [
        new("Newbie City - Underground Cave Entrance", new(5590, 1323, 0), new(6028, 3450, -20)),
        new("Newbie City - Underground Cave Return", new(6028, 3451, -20), new(5590, 1324, 0))
    ];
    private sealed record Marker(string InstallationId, uint GateSerial);
    internal sealed record Archive(string InstallationId, Snapshot Gate, Snapshot[] Routes);
    private sealed record Receipt(bool Success, string InstallationId, Snapshot Gate, Snapshot[] Routes);
    private sealed record LegacyReceipt(bool Success, string InstallationId, Snapshot Gate);
    internal sealed record Result(int Created, bool Moved, Teleporter Gate, Teleporter[] Links);
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(NewbieCityUndergroundCave));
    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public static void Initialize()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data", "NewbieCityUndergroundCave");
        var pending = Path.Combine(directory, "install.pending");
        if (!File.Exists(pending) || File.Exists(Path.Combine(directory, "install.completed")))
        {
            return;
        }
        var resultPath = Path.Combine(directory, "result.json");
        try
        {
            var marker = JsonSerializer.Deserialize<Marker>(ReadBytes(pending, 1024), _options);
            Require(marker is { InstallationId: InstallationId, GateSerial: GateSerial }, "The marker must authorize this exact underground cave and existing gate serial.");
            var legacyDirectory = Path.Combine(Core.BaseDirectory, "Data", "NewbieCityGalvenGate");
            Require(File.Exists(Path.Combine(legacyDirectory, "install.completed")), "The original Galven gate installer must remain completed.");
            var legacy = JsonSerializer.Deserialize<LegacyReceipt>(ReadBytes(Path.Combine(legacyDirectory, "installed.json"), 8192), _options);
            Require(legacy is { Success: true, InstallationId: NewbieCityGalvenGate.InstallationId } &&
                legacy.Gate == ExpectedGate(NewbieCityGalvenGate.Source), "The original Galven gate receipt differs from the exact gate being relocated.");
            Ensure(Path.Combine(directory, "before.json"), true, installed =>
            {
                var result = new
                {
                    Success = true,
                    InstallationId,
                    installed.Created,
                    installed.Moved,
                    Gate = DescribeGate(installed.Gate),
                    Routes = DescribeLinks(installed.Links),
                    AwaitingWorldSave = true,
                    Archive = "Data/NewbieCityUndergroundCave/before.json"
                };
                WriteJson(Path.Combine(directory, "installed.json"), result);
                WriteJson(resultPath, result);
            });
            _logger.Information("Verified underground Newbie City cave: two links and relocated Galven gate {Serial}; awaiting world save", GateSerial);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Newbie City underground cave installation refused");
            WriteJson(resultPath, new { Success = false, InstallationId, Error = exception.ToString() });
        }
    }

    internal static Result Ensure(string archivePath = null, bool checkSupport = true, Action<Result> commit = null)
    {
        var (gate, links) = FindAndValidate(checkSupport);
        if (archivePath != null)
        {
            if (File.Exists(archivePath))
            {
                var archive = JsonSerializer.Deserialize<Archive>(ReadBytes(archivePath, 16384), _options);
                ValidateArchive(archive, gate, links, Path.Combine(Path.GetDirectoryName(archivePath), "installed.json"));
            }
            else
            {
                using var stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                JsonSerializer.Serialize(stream, new Archive(InstallationId, DescribeGate(gate), DescribeLinks(links)), _options);
                stream.Flush(true);
            }
        }
        var originalPosition = gate.Location;
        var created = new bool[Routes.Length];
        var count = 0;
        try
        {
            for (var i = 0; i < Routes.Length; i++)
            {
                if (links[i] != null)
                {
                    continue;
                }
                links[i] = new Teleporter(Routes[i].Destination, Map.Felucca) { Name = Routes[i].Name };
                created[i] = true;
                links[i].MoveToWorld(Routes[i].Source, Map.Felucca);
                count++;
            }
            if (originalPosition != GatePosition)
            {
                gate.MoveToWorld(GatePosition, Map.Felucca);
            }
            var result = new Result(count, originalPosition != GatePosition, gate, links);
            commit?.Invoke(result);
            return result;
        }
        catch
        {
            if (!gate.Deleted && gate.Location != originalPosition)
            {
                gate.MoveToWorld(originalPosition, Map.Felucca);
            }
            for (var i = 0; i < links.Length; i++)
            {
                if (created[i])
                {
                    links[i]?.Delete();
                }
            }
            throw;
        }
    }

    private static (Teleporter Gate, Teleporter[] Links) FindAndValidate(bool checkSupport)
    {
        var entity = World.FindEntity((Serial)GateSerial);
        Require(entity is Teleporter, "The exact previously installed Galven gate is missing or has another entity type.");
        var gate = (Teleporter)entity;
        Require(gate.GetType() == typeof(Teleporter) && !gate.Deleted && gate.Parent == null && gate.Map == Map.Felucca && gate.MapDest == Map.Felucca &&
            (gate.Location == NewbieCityGalvenGate.Source || gate.Location == GatePosition) &&
            DescribeGate(gate) == ExpectedGate(gate.Location), "The existing Galven gate has unexpected identity, appearance or destination.");
        var links = new Teleporter[Routes.Length];
        for (var i = 0; i < Routes.Length; i++)
        {
            var route = Routes[i];
            foreach (var item in Map.Felucca.GetItemsInRange<Item>(route.Source, 0))
            {
                if (item == gate || Math.Abs(item.Z - route.Source.Z) > 16)
                {
                    continue;
                }
                if (item is Teleporter link)
                {
                    Require(links[i] == null && link.GetType() == typeof(Teleporter) && !link.Deleted && link.Parent == null && link.Map == Map.Felucca && link.MapDest == Map.Felucca &&
                        Describe(link) == ExpectedLink(route, link.Serial.Value), "A conflicting or duplicate cave link occupies its source.");
                    links[i] = link;
                }
                else
                {
                    Require(!IsTravel(item), "Another portal occupies a cave link source.");
                }
            }
            ValidateArrival(route.Destination);
            if (checkSupport)
            {
                Require(Map.Felucca.CanFit(route.Source, 16, true, false) && Map.Felucca.CanFit(route.Destination, 16, true, false),
                    "Cave links need clear supported source and separate arrival tiles.");
            }
        }
        foreach (var item in Map.Felucca.GetItemsInRange<Item>(GatePosition, 0))
        {
            Require(item == gate || Math.Abs(item.Z - GatePosition.Z) > 16 || !IsTravel(item), "Another portal occupies the underground Galven gate.");
        }
        ValidateArrival(NewbieCityGalvenGate.Destination);
        foreach (var mobile in Map.Felucca.GetMobilesInBounds(InteriorBounds))
        {
            throw new InvalidOperationException($"Underground cave installation refused because mobile {mobile.Serial} occupies its bounded interior.");
        }
        foreach (var item in Map.Felucca.GetItemsInBounds(InteriorBounds))
        {
            Require(item == gate || item == links[1], "A foreign runtime object occupies the underground cave interior.");
        }
        if (checkSupport)
        {
            Require(Map.Felucca.CanFit(GatePosition, 16, true, false) && Map.Felucca.CanFit(NewbieCityGalvenGate.Destination, 16, true, false),
                "The underground Galven gate and arrival need clear supported floors.");
        }
        return (gate, links);
    }

    private static void ValidateArrival(Point3D point)
    {
        foreach (var item in Map.Felucca.GetItemsInRange<Item>(point, 0))
        {
            Require(Math.Abs(item.Z - point.Z) > 16 || !IsTravel(item), "A travel trigger occupies a separate cave arrival tile.");
        }
    }

    private static bool IsTravel(Item item) => item is Teleporter { Active: true } or Moongate or PublicMoongate || item.ItemID == 0xF6C;

    internal static Snapshot DescribeGate(Teleporter gate) => Describe(gate);

    private static Snapshot Describe(Teleporter item) => new(item.Serial.Value, item.Name, Point(item.Location), Point(item.PointDest),
        item.ItemID, item.Hue, item.SoundID, item.Light, item.Flags, item.Visible, item.Movable, item.Delay.Ticks);

    private static Snapshot ExpectedGate(Point3D source) => NewbieCityGalvenGate.Describe(null) with { Serial = GateSerial, Source = Point(source) };

    private static Snapshot ExpectedLink(Route route, uint? serial) => new(serial, route.Name, Point(route.Source), Point(route.Destination),
        0x1BC3, 0, 0, default, TeleporterFlags.Active, false, false, 0);

    private static Snapshot[] DescribeLinks(Teleporter[] links) =>
        [links[0] == null ? ExpectedLink(Routes[0], null) : Describe(links[0]), links[1] == null ? ExpectedLink(Routes[1], null) : Describe(links[1])];

    internal static void ValidateArchive(Archive archive, Teleporter gate, Teleporter[] links, string receiptPath)
    {
        Require(archive?.InstallationId == InstallationId && archive.Gate != null &&
            (archive.Gate == ExpectedGate(NewbieCityGalvenGate.Source) || archive.Gate == ExpectedGate(GatePosition)) &&
            archive.Routes?.Length == Routes.Length, "The cave archive has another identity, gate or route count.");
        for (var i = 0; i < Routes.Length; i++)
        {
            Require(archive.Routes[i] != null && archive.Routes[i] == ExpectedLink(Routes[i], archive.Routes[i].Serial) &&
                (archive.Routes[i].Serial == null || archive.Routes[i].Serial == links[i]?.Serial.Value), "An archived cave link differs or its original serial is missing.");
        }
        if (File.Exists(receiptPath))
        {
            var receipt = JsonSerializer.Deserialize<Receipt>(ReadBytes(receiptPath, 16384), _options);
            Require(receipt is { Success: true, InstallationId: InstallationId } && receipt.Gate == ExpectedGate(GatePosition) &&
                receipt.Routes?.Length == Routes.Length, "The cave receipt describes another installation.");
            var current = DescribeLinks(links);
            var original = DescribeGate(gate) == archive.Gate;
            var installed = DescribeGate(gate) == receipt.Gate;
            for (var i = 0; i < Routes.Length; i++)
            {
                Require(receipt.Routes[i]?.Serial != null && receipt.Routes[i] == ExpectedLink(Routes[i], receipt.Routes[i].Serial), "The cave receipt has an unexpected link.");
                original &= current[i] == archive.Routes[i];
                installed &= current[i] == receipt.Routes[i];
            }
            Require(original || installed, "Cave link serials differ from both the archive and successful receipt.");
        }
    }

    private static Endpoint Point(Point3D point) => new(point.X, point.Y, point.Z, "Felucca");

    private static byte[] ReadBytes(string path, int maximum)
    {
        Require(new FileInfo(path).Length is > 0 && new FileInfo(path).Length <= maximum, "An underground cave installation journal has an invalid size.");
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
