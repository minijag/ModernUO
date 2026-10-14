using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Server.Accounting;
using Server.Items;
using Server.Logging;
using Endpoint = Server.Engines.Labrynth.NewbieCityGalvenGate.Endpoint;
using Snapshot = Server.Engines.Labrynth.NewbieCityGalvenGate.Snapshot;

namespace Server.Engines.Labrynth;

public static class NewbieCityCaveTravelRetirement
{
    internal const string InstallationId = "newbie-city-cave-travel-retirement-v1";
    internal static Point3D SafeArrival => new(5590, 1324, 0);
    internal static readonly Snapshot[] ExpectedTravel =
    [
        new(1073772439, "a moongate to Galven", Point(new(6028, 3446, -20)), Point(new(1317, 1380, 0)),
            0xF6C, 0x482, 0x1FE, LightType.Circle300, TeleporterFlags.Active, true, false, 0),
        new(1073772440, "Newbie City - Underground Cave Entrance", Point(new(5590, 1323, 0)), Point(new(6028, 3450, -20)),
            0x1BC3, 0, 0, default, TeleporterFlags.Active, false, false, 0),
        new(1073772441, "Newbie City - Underground Cave Return", Point(new(6028, 3451, -20)), Point(SafeArrival),
            0x1BC3, 0, 0, default, TeleporterFlags.Active, false, false, 0)
    ];
    private sealed record Marker(string InstallationId);
    internal sealed record Occupant(uint Serial, string Type, string Name, Endpoint Source, bool Offline);
    internal sealed record Archive(string InstallationId, Snapshot[] Travel, Occupant[] Occupants);
    internal sealed record Result(int Deleted, int AlreadyAbsent, int RelocatedMobiles, int RelocatedOfflineCharacters, int OfflineCharactersChecked);
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(NewbieCityCaveTravelRetirement));
    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public static void Initialize()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data", "NewbieCityCaveTravelRetirement");
        var pending = Path.Combine(directory, "install.pending");
        if (!File.Exists(pending) || File.Exists(Path.Combine(directory, "install.completed")))
        {
            return;
        }
        var resultPath = Path.Combine(directory, "result.json");
        try
        {
            var marker = JsonSerializer.Deserialize<Marker>(ReadBytes(pending, 1024), _options);
            Require(marker is { InstallationId: InstallationId }, "The marker must authorize this exact cave travel retirement.");
            foreach (var installer in new[] { "NewbieCityGalvenGate", "NewbieCityUndergroundCave" })
            {
                Require(File.Exists(Path.Combine(Core.BaseDirectory, "Data", installer, "install.completed")),
                    "Historical cave installers must remain completed to prevent recreating retired travel.");
            }
            var result = Ensure(Path.Combine(directory, "before.json"));
            var receipt = new
            {
                Success = true,
                InstallationId,
                result.Deleted,
                result.AlreadyAbsent,
                RemovedSerials = new uint[] { 1073772439, 1073772440, 1073772441 },
                result.RelocatedMobiles,
                result.RelocatedOfflineCharacters,
                result.OfflineCharactersChecked,
                OldInteriorClear = true,
                AwaitingWorldSave = true,
                Archive = "Data/NewbieCityCaveTravelRetirement/before.json"
            };
            WriteJson(Path.Combine(directory, "installed.json"), receipt);
            WriteJson(resultPath, receipt);
            _logger.Information("Retired the three exact Newbie City cave travel items; awaiting world save");
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Newbie City cave travel retirement refused");
            WriteJson(resultPath, new { Success = false, InstallationId, Error = exception.ToString() });
        }
    }

    internal static Result Ensure(string archivePath, IEnumerable<Mobile> accountCharacters = null, bool checkSupport = true)
    {
        Archive archive = null;
        if (File.Exists(archivePath))
        {
            archive = JsonSerializer.Deserialize<Archive>(ReadBytes(archivePath, 65536), _options);
            Require(archive?.InstallationId == InstallationId && archive.Travel?.Length == ExpectedTravel.Length && archive.Occupants != null,
                "The retirement archive has another identity or incomplete snapshots.");
            for (var i = 0; i < ExpectedTravel.Length; i++)
            {
                Require(archive.Travel[i] == ExpectedTravel[i], "An archived travel item differs from the three authorized items.");
            }
        }
        var travel = new Teleporter[ExpectedTravel.Length];
        for (var i = 0; i < travel.Length; i++)
        {
            var expected = ExpectedTravel[i];
            var entity = World.FindEntity((Serial)expected.Serial.Value);
            if (entity == null)
            {
                Require(archive != null, "A travel item is missing without a durable retirement archive.");
                continue;
            }
            Require(entity is Teleporter item && item.GetType() == typeof(Teleporter) && !item.Deleted && item.Parent == null &&
                item.Map == Map.Felucca && item.MapDest == Map.Felucca && NewbieCityUndergroundCave.DescribeGate(item) == expected,
                $"Travel serial {expected.Serial} no longer matches its exact authorized identity.");
            travel[i] = (Teleporter)entity;
        }
        foreach (var item in Map.Felucca.GetItemsInBounds(NewbieCityUndergroundCave.InteriorBounds))
        {
            Require(item == travel[0] || item == travel[2], "An unrelated runtime item occupies the cave being removed.");
        }
        var occupants = new List<(Mobile Mobile, Occupant Snapshot)>();
        foreach (var mobile in Map.Felucca.GetMobilesInBounds(NewbieCityUndergroundCave.InteriorBounds))
        {
            occupants.Add((mobile, Describe(mobile, false)));
        }
        var checkedCharacters = 0;
        foreach (var mobile in accountCharacters ?? AccountCharacters())
        {
            if (mobile == null || mobile.Deleted)
            {
                continue;
            }
            checkedCharacters++;
            if (mobile.Map == Map.Internal && mobile.LogoutMap == Map.Felucca && NewbieCityUndergroundCave.InteriorBounds.Contains(mobile.LogoutLocation))
            {
                occupants.Add((mobile, Describe(mobile, true)));
            }
        }
        if (occupants.Count > 0)
        {
            Require(!checkSupport || Map.Felucca.CanFit(SafeArrival, 16, true, false), "The cave evacuation arrival is not supported and clear.");
            foreach (var item in Map.Felucca.GetItemsInRange<Item>(SafeArrival, 0))
            {
                Require(Math.Abs(item.Z - SafeArrival.Z) > 16 || item is not (Teleporter or Moongate or PublicMoongate),
                    "The cave evacuation arrival contains another travel trigger.");
            }
        }
        var snapshots = new Occupant[occupants.Count];
        for (var i = 0; i < occupants.Count; i++)
        {
            snapshots[i] = occupants[i].Snapshot;
            Require(archive == null || Array.IndexOf(archive.Occupants, snapshots[i]) >= 0,
                "A cave occupant differs from the durable pre-retirement archive.");
        }
        if (archive == null)
        {
            using var stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(stream, new Archive(InstallationId, ExpectedTravel, snapshots), _options);
            stream.Flush(true);
        }
        var relocatedMobiles = 0;
        var relocatedOfflineCharacters = 0;
        foreach (var (mobile, snapshot) in occupants)
        {
            if (snapshot.Offline)
            {
                mobile.LogoutLocation = SafeArrival;
                mobile.LogoutMap = Map.Felucca;
                mobile.MarkDirty();
                relocatedOfflineCharacters++;
            }
            else
            {
                mobile.MoveToWorld(SafeArrival, Map.Felucca);
                relocatedMobiles++;
            }
        }
        var deleted = 0;
        foreach (var item in travel)
        {
            if (item != null)
            {
                item.Delete();
                deleted++;
            }
        }
        return new Result(deleted, travel.Length - deleted, relocatedMobiles, relocatedOfflineCharacters, checkedCharacters);
    }

    private static IEnumerable<Mobile> AccountCharacters()
    {
        // Logged-out characters are internalized, so a spatial query cannot find their return locations.
        foreach (var account in Accounts.GetAccounts())
        {
            for (var i = 0; i < account.Length; i++)
            {
                if (account[i] is { } mobile)
                {
                    yield return mobile;
                }
            }
        }
    }

    private static Occupant Describe(Mobile mobile, bool offline) => new(mobile.Serial.Value, mobile.GetType().FullName, mobile.Name,
        Point(offline ? mobile.LogoutLocation : mobile.Location), offline);

    private static Endpoint Point(Point3D point) => new(point.X, point.Y, point.Z, "Felucca");

    private static byte[] ReadBytes(string path, int maximum)
    {
        Require(new FileInfo(path).Length is > 0 && new FileInfo(path).Length <= maximum, "A cave retirement journal has an invalid size.");
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
