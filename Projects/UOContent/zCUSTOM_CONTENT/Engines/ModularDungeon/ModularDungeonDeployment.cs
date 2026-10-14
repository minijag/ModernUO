using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Server.Items;
using Server.Logging;
using Server.Regions;
using CalcMovement = Server.Movement.Movement;

namespace Server.Engines.ModularDungeon;

public static class ModularDungeonDeployment
{
    private const string InstallationId = "modular-dungeon-v3-final-felucca-5256-808";
    private const string RevisionId = "modular-dungeon-v4-final-felucca-5256-808";
    private const string DecoratedId = "modular-dungeon-v5-final-felucca-5256-808";
    private const string SceneryId = "modular-dungeon-v6-final-felucca-5256-808";
    private const string ChapelId = "modular-dungeon-v7-final-felucca-5256-808";
    private const string CompletedId = "modular-dungeon-v15-final-felucca-5256-808";
    private const string CompletedBlueprintSha256 = "23f92689505561a3876e75a0fb5abbb338fda5d08a45800eff978611af271a96";
    private const string FullLevelsId = "modular-dungeon-v16-final-felucca-5256-808";
    private const string FullLevelsBlueprintSha256 = "0c1e0b200db665048225ce252bf25d73cd82d6c36207b5722a9ac2b52b7c4dcf";
    private const string OssuaryId = "modular-dungeon-v17-final-felucca-5256-808";
    private const string OssuaryBlueprintSha256 = "391ba3505002ef9f20b72b4f5ea767227c049742bd23ffdfe533e7a4ffccde82";
    private const string AbundantId = "modular-dungeon-v35-final-felucca-5256-808";
    private const string AbundantBlueprintSha256 = "0ca6ec10f678a0fa130d3b60dc385ec5f9599d7e4a191374d5f7c18fbb874774";
    private const string AbundantPreviousManifestSha256 = "7966d9845810420764daaa4a68b0947e0cc87b7bb236127477fecf0b1a31ab14";
    private const string AbundantPreviousReceiptSha256 = "e7e5cd9470869a010f8f41a7ea30d870697cf08be0449766d4b1b55c60fddfad";
    private const string AbundantOriginalNativeSha256 = "157ba89e474429427b32c7a2eedfc22d5aefbd23225eea6a7a1903e3d7e98042";
    private const string AbundantInstalledNativeSha256 = "aac95fe96a55000262a41245b83b96fa93acb99743a82fc0be75141c969c08b9";
    private const int MaximumManifestBytes = 256 * 1024;
    private const int DoorCount = 71;
    private const int StaticCount = 15001;
    private const int V4StaticCount = 15508;
    private const int V5StaticCount = 16095;
    private const int V6StaticCount = 16311;
    private const int V15StaticCount = 17518;
    private const int V16StaticCount = 21195;
    private const int V17StaticCount = 21020;
    private const int V35StaticCount = 22334;
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(ModularDungeonDeployment));
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    private static Job _job;
    private static bool _revision;
    private static bool _decorated;
    private static bool _scenery;
    private static bool _chapel;
    private static bool _completed;
    private static bool _fullLevels;
    private static bool _ossuary;
    private static bool _abundant;
    private static string _status = "No deployment marker was processed this startup.";

    private static string CurrentInstallationId => _abundant ? AbundantId : _ossuary ? OssuaryId : _fullLevels ? FullLevelsId : _completed ? CompletedId : _chapel ? ChapelId : _scenery ? SceneryId : _decorated ? DecoratedId : _revision ? RevisionId : InstallationId;
    private static string DirectoryPath => Path.Combine(Core.BaseDirectory, "Data",
        _abundant ? "ModularDungeonV35" : _ossuary ? "ModularDungeonV17" : _fullLevels ? "ModularDungeonV16" : _completed ? "ModularDungeonV15" : _chapel ? "ModularDungeonV7" : _scenery ? "ModularDungeonV6" : _decorated ? "ModularDungeonV5" : _revision ? "ModularDungeonV4" : "ModularDungeon");
    private static string DataPath(string name) => Path.Combine(DirectoryPath, name);

    public sealed record Coordinate(int X, int Y, int Z)
    {
        [JsonIgnore]
        public Point3D Point => new(X, Y, Z);
    }

    public sealed record Bounds(int X, int Y, int Width, int Height)
    {
        [JsonIgnore]
        public Rectangle2D Rectangle => new(X, Y, Width, Height);
    }

    public sealed record DoorDefinition(
        string Id, Coordinate Closed, Coordinate Opened, string Facing, int ClosedId, int OpenedId,
        Coordinate[] Approaches
    );

    public sealed record Approach(string Id, Coordinate Point);
    public sealed record ModuleDefinition(string Id, string Name, Coordinate Start, Approach[] Approaches);
    public sealed record StairLane(string Id, string ModuleId, Coordinate[] Points);
    public sealed record ChestDefinition(string Id, string ModuleId, Coordinate Position, int ItemId, Coordinate Approach,
        string Type = "EventDungeonChest");
    public sealed record EscapeRoute(string Id, string ModuleId, Coordinate From, Coordinate To);
    public sealed record HiddenCellarDefinition(string ModuleId, string ChestId, Coordinate Hatch, Coordinate Entry,
        Coordinate ReturnRune, Coordinate SurfaceReturn, Coordinate CreaturePosition);

    public sealed record Manifest(
        int SchemaVersion, string InstallationId, string BlueprintSha256, string OriginalNativeSha256,
        string InstalledNativeSha256, Coordinate Origin, Bounds Bounds, int OriginalLandId, int OriginalLandZ,
        int StaticCount, Coordinate Entry, DoorDefinition[] Doors, ModuleDefinition[] Modules, StairLane[] StairLanes,
        int OriginalStaticCount = 0, int InstalledLandId = 0x244, int InstalledLandZ = 0,
        ChestDefinition[] Chests = null, EscapeRoute[] EscapeRoutes = null, HiddenCellarDefinition HiddenCellar = null,
        bool SceneryOnly = false, string SceneryModuleId = null,
        string PreviousManifestSha256 = null, string PreviousReceiptSha256 = null
    );

    public sealed record Marker(string ManifestSha256);
    public sealed record DoorSerial(string Id, uint Serial);
    public sealed record MovementResult(string Id, string Kind, bool Success);
    public sealed record Result(
        int SchemaVersion, string InstallationId, string ManifestSha256, string NativeSha256, bool Success,
        string Stage, string Error, int StaticCount, int DoorCount, int MovementChecks, int ReachablePoints,
        DateTime CompletedUtc, Bounds Bounds, DoorSerial[] Doors, MovementResult[] MovementResults,
        bool RollbackSaveConfirmed = false, DoorSerial[] Chests = null, DoorSerial[] Features = null, DoorSerial[] Creatures = null
    );

    public static void Configure()
    {
        CommandSystem.Register("ModularDungeonGo", AccessLevel.Developer, Go);
        CommandSystem.Register("ModularDungeonStatus", AccessLevel.Developer, Status);
        EventSink.WorldSave += OnWorldSave;
        EventSink.Shutdown += OnShutdown;
    }

    public static void Initialize()
    {
        var revisionPath = Path.Combine(Core.BaseDirectory, "Data", "ModularDungeonV4");
        _revision = File.Exists(Path.Combine(revisionPath, "audit.pending")) ||
                    File.Exists(Path.Combine(revisionPath, "install.pending")) ||
                    File.Exists(Path.Combine(revisionPath, "installed.json"));
        var decoratedPath = Path.Combine(Core.BaseDirectory, "Data", "ModularDungeonV5");
        _decorated = File.Exists(Path.Combine(decoratedPath, "audit.pending")) ||
                     File.Exists(Path.Combine(decoratedPath, "install.pending")) ||
                     File.Exists(Path.Combine(decoratedPath, "installed.json"));
        var sceneryPath = Path.Combine(Core.BaseDirectory, "Data", "ModularDungeonV6");
        _scenery = File.Exists(Path.Combine(sceneryPath, "audit.pending")) ||
                   File.Exists(Path.Combine(sceneryPath, "install.pending")) ||
                   File.Exists(Path.Combine(sceneryPath, "installed.json"));
        var chapelPath = Path.Combine(Core.BaseDirectory, "Data", "ModularDungeonV7");
        _chapel = File.Exists(Path.Combine(chapelPath, "audit.pending")) ||
                  File.Exists(Path.Combine(chapelPath, "install.pending")) ||
                  File.Exists(Path.Combine(chapelPath, "installed.json"));
        var completedPath = Path.Combine(Core.BaseDirectory, "Data", "ModularDungeonV15");
        _completed = File.Exists(Path.Combine(completedPath, "audit.pending")) ||
                     File.Exists(Path.Combine(completedPath, "install.pending")) ||
                     File.Exists(Path.Combine(completedPath, "installed.json"));
        var fullLevelsPath = Path.Combine(Core.BaseDirectory, "Data", "ModularDungeonV16");
        _fullLevels = File.Exists(Path.Combine(fullLevelsPath, "audit.pending")) ||
                      File.Exists(Path.Combine(fullLevelsPath, "install.pending")) ||
                      File.Exists(Path.Combine(fullLevelsPath, "installed.json"));
        var ossuaryPath = Path.Combine(Core.BaseDirectory, "Data", "ModularDungeonV17");
        _ossuary = File.Exists(Path.Combine(ossuaryPath, "audit.pending")) ||
                   File.Exists(Path.Combine(ossuaryPath, "install.pending")) ||
                   File.Exists(Path.Combine(ossuaryPath, "installed.json"));
        var abundantPath = Path.Combine(Core.BaseDirectory, "Data", "ModularDungeonV35");
        _abundant = File.Exists(Path.Combine(abundantPath, "audit.pending")) ||
                    File.Exists(Path.Combine(abundantPath, "install.pending")) ||
                    File.Exists(Path.Combine(abundantPath, "installed.json"));
        var audit = File.Exists(DataPath("audit.pending"));
        var install = File.Exists(DataPath("install.pending"));
        if (!audit && !install)
        {
            return;
        }

        try
        {
            Require(audit != install, "Exactly one deployment phase marker must be present.");
            Require(!File.Exists(DataPath("installed.json")), "An installed manifest already exists; startup never rebuilds doors.");
            Require(!File.Exists(DataPath("install-state.json")), "A deployment journal already exists; inspect it before any retry.");
            var bytes = ReadSmallFile("install-manifest.json", MaximumManifestBytes);
            var manifest = JsonSerializer.Deserialize<Manifest>(bytes, _jsonOptions);
            ValidateManifest(manifest);
            Require(manifest.InstallationId == CurrentInstallationId, "The manifest belongs to a different deployment phase.");
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var markerName = audit ? "audit.pending" : "install.pending";
            var marker = JsonSerializer.Deserialize<Marker>(ReadSmallFile(markerName, 1024), _jsonOptions);
            Require(marker != null && EqualHash(marker.ManifestSha256, hash), "The marker does not authorize this manifest.");
            Require(!File.Exists(DataPath(audit ? "audit.completed" : "install.completed")), "The phase marker was already retired.");
            if (install)
            {
                var receipt = ReadResult("audit-result.json");
                Require(receipt.Success && receipt.Stage == "audit" && receipt.InstallationId == manifest.InstallationId &&
                        EqualHash(receipt.ManifestSha256, hash) && EqualHash(receipt.NativeSha256, manifest.OriginalNativeSha256),
                    "A successful original-map runtime audit for this manifest is required.");
            }

            Require(CalcMovement.Impl != null, "The native movement implementation is not configured.");
            _job = new Job(manifest, hash, audit);
            _status = audit ? "Auditing the original map and runtime occupancy." : "Verifying the patched native map.";
            _job.Start();
        }
        catch (Exception ex)
        {
            _status = ex.Message;
            _logger.Error(ex, "Modular dungeon deployment marker was refused");
        }
    }

    [Usage("ModularDungeonGo")]
    [Description("Moves a developer to the modular dungeon's north walk-in pad after a completed installation.")]
    private static void Go(CommandEventArgs e)
    {
        if (e.Length != 0)
        {
            e.Mobile.SendMessage("Usage: [ModularDungeonGo");
            return;
        }

        try
        {
            Require(_job == null, "Deployment is still running.");
            Require(!File.Exists(DataPath("install.pending")), "The installation marker has not been retired.");
            var result = ReadResult("installed.json");
            Require(result.Success && result.Stage == "installed" && result.InstallationId == CurrentInstallationId,
                "No completed installation is recorded.");
            var pad = new Point3D(5316, 807, 0);
            Require(Map.Felucca.CanFit(pad, 16), "The north walk-in pad is currently obstructed.");
            e.Mobile.MoveToWorld(pad, Map.Felucca);
            e.Mobile.SendMessage("You are at the north walk-in pad. Doors take up to 4 seconds without keys, close on double-click, and never close automatically. Chests take up to 6 seconds. Lockpicking and Remove Trap reduce these waits.");
        }
        catch (Exception ex)
        {
            e.Mobile.SendMessage($"Modular dungeon travel refused: {ex.Message}");
        }
    }

    [Usage("ModularDungeonStatus")]
    [Description("Shows this startup's modular dungeon deployment status without changing the dungeon.")]
    private static void Status(CommandEventArgs e)
    {
        e.Mobile.SendMessage($"Modular dungeon: {_status}");
        e.Mobile.SendMessage("Event doors take up to 4 seconds without keys. Double-click an open door to close it; there is no automatic closing. Chests take up to 6 seconds. Lockpicking and Remove Trap reduce these waits.");
        if (_job != null)
        {
            e.Mobile.SendMessage($"Phase: {_job.Phase}; native cells: {_job.CellCursor}/18496; movement checks: {_job.MovementChecks}.");
        }
    }

    private static void OnWorldSave() => _job?.OnSuccessfulSnapshot();

    private static void OnShutdown()
    {
        _job?.Stop();
        _job = null;
    }

    private static byte[] ReadSmallFile(string name, int maximumBytes)
    {
        var path = DataPath(name);
        Require(File.Exists(path), $"Missing deployment file: {name}.");
        Require(new FileInfo(path).Length is > 0 && new FileInfo(path).Length <= maximumBytes,
            $"Deployment file exceeds its bounded size: {name}.");
        var bytes = File.ReadAllBytes(path);
        Require(bytes.Length <= maximumBytes, $"Deployment file grew beyond its bounded size: {name}.");
        return bytes;
    }

    private static Result ReadResult(string name)
    {
        var result = JsonSerializer.Deserialize<Result>(ReadSmallFile(name, MaximumManifestBytes), _jsonOptions);
        Require(result != null, $"Invalid deployment result: {name}.");
        return result;
    }

    private static Result ReadPreviousReceipt(bool decorated)
    {
        var path = Path.Combine(Core.BaseDirectory, "Data", decorated ? "ModularDungeonV4" : "ModularDungeon", "installed.json");
        Require(File.Exists(path) && new FileInfo(path).Length is > 0 and <= MaximumManifestBytes,
            "A bounded previous installation receipt is required for the revision.");
        var result = JsonSerializer.Deserialize<Result>(File.ReadAllBytes(path), _jsonOptions);
        Require(result is { Success: true, Stage: "installed", DoorCount: DoorCount } &&
                result.InstallationId == (decorated ? RevisionId : InstallationId) &&
                result.StaticCount == (decorated ? V4StaticCount : StaticCount),
            "The previous dungeon installation receipt is invalid.");
        return result;
    }

    private static void WriteResult(string name, Result result)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions);
        Require(bytes.Length <= MaximumManifestBytes, "Deployment result exceeds its bounded size.");
        var temporary = DataPath(name + ".tmp");
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(true);
        }

        File.Move(temporary, DataPath(name), true);
    }

    private static bool EqualHash(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void ValidateManifest(Manifest manifest)
    {
        Require(manifest != null && manifest.SchemaVersion == 1 && manifest.InstallationId is InstallationId or RevisionId or DecoratedId or SceneryId or ChapelId or CompletedId or FullLevelsId or OssuaryId or AbundantId,
            "The deployment manifest has an unsupported identity or schema.");
        Require(manifest.Origin == new Coordinate(5256, 808, 0) && manifest.Entry == new Coordinate(5316, 807, 0) &&
                manifest.Bounds == new Bounds(5248, 800, 136, 136) && manifest.StaticCount is > 0 and <= 30000,
            "The deployment manifest is outside the authorized footprint.");
        ValidateHash(manifest.BlueprintSha256);
        ValidateHash(manifest.OriginalNativeSha256);
        ValidateHash(manifest.InstalledNativeSha256);
        var chapel = manifest.InstallationId == ChapelId;
        var completed = manifest.InstallationId == CompletedId;
        var fullLevels = manifest.InstallationId == FullLevelsId;
        var ossuary = manifest.InstallationId == OssuaryId;
        var abundant = manifest.InstallationId == AbundantId;
        var scenery = manifest.InstallationId is SceneryId or ChapelId;
        var decorated = manifest.InstallationId is DecoratedId or SceneryId or ChapelId or CompletedId or FullLevelsId or OssuaryId or AbundantId;
        var revision = manifest.InstallationId is RevisionId or DecoratedId or SceneryId or ChapelId or CompletedId or FullLevelsId or OssuaryId or AbundantId;
        Require(scenery ? manifest.SceneryOnly && manifest.SceneryModuleId == (chapel ? "M00" : "M24") :
                !manifest.SceneryOnly && manifest.SceneryModuleId == null,
            "Only the authorized room scenery revisions may use static-only deployment.");
        if (chapel || completed || fullLevels || ossuary || abundant)
        {
            ValidateHash(manifest.PreviousManifestSha256);
            ValidateHash(manifest.PreviousReceiptSha256);
        }
        else
        {
            Require(manifest.PreviousManifestSha256 == null && manifest.PreviousReceiptSha256 == null,
                "Only an authorized pinned continuation may declare exact previous receipt pins.");
        }
        if (completed)
        {
            Require(manifest.StaticCount == V15StaticCount && manifest.StairLanes?.Length == 16 &&
                    EqualHash(manifest.BlueprintSha256, CompletedBlueprintSha256),
                "The full continuation requires the frozen completed-redesign blueprint and native profile.");
        }
        if (fullLevels)
        {
            Require(manifest.StaticCount == V16StaticCount && manifest.StairLanes?.Length == 24 &&
                    EqualHash(manifest.BlueprintSha256, FullLevelsBlueprintSha256),
                "The full-level continuation requires the frozen V16 blueprint and native profile.");
        }
        if (ossuary)
        {
            Require(manifest.StaticCount == V17StaticCount && manifest.StairLanes?.Length == 22 &&
                    EqualHash(manifest.BlueprintSha256, OssuaryBlueprintSha256),
                "The Ossuary continuation requires the frozen V17 blueprint and native profile.");
        }
        if (abundant)
        {
            Require(manifest.StaticCount == V35StaticCount && manifest.StairLanes?.Length == 24 &&
                    EqualHash(manifest.BlueprintSha256, AbundantBlueprintSha256) &&
                    EqualHash(manifest.OriginalNativeSha256, AbundantOriginalNativeSha256) &&
                    EqualHash(manifest.InstalledNativeSha256, AbundantInstalledNativeSha256) &&
                    EqualHash(manifest.PreviousManifestSha256, AbundantPreviousManifestSha256) &&
                    EqualHash(manifest.PreviousReceiptSha256, AbundantPreviousReceiptSha256),
                "The abundant continuation requires the frozen V35 blueprint and exact successful V17 receipt pins.");
        }
        Require(manifest.Doors?.Length == DoorCount && manifest.Modules?.Length == 25 &&
                (revision ? manifest.StairLanes?.Length >= 16 : manifest.StairLanes?.Length == 10 && manifest.StaticCount == StaticCount),
            "The manifest must contain 71 doors, 25 modules, and every physical staircase lane.");
        Require(manifest.OriginalLandId == 0x244 && manifest.OriginalLandZ == (decorated ? -40 : 0) &&
                manifest.InstalledLandId == 0x244 && manifest.InstalledLandZ == (revision ? -40 : 0) &&
                manifest.OriginalStaticCount == (abundant ? V17StaticCount : ossuary ? V16StaticCount : fullLevels ? V15StaticCount : completed || chapel ? V6StaticCount : scenery ? V5StaticCount : decorated ? V4StaticCount : revision ? StaticCount : 0), "The native terrain profile is not authorized.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var positions = new HashSet<Point3D>();
        var doorReservations = new HashSet<Point3D>();
        foreach (var door in manifest.Doors)
        {
            Require(door != null && ids.Add(door.Id) && positions.Add(door.Closed.Point), "Duplicate door identity or location.");
            ValidatePoint(manifest, door.Closed);
            ValidatePoint(manifest, door.Opened);
            Require(Enum.TryParse<DoorFacing>(door.Facing, out var facing) && Enum.IsDefined(facing), "Invalid door facing.");
            var offset = BaseDoor.GetOffset(facing);
            Require(door.ClosedId == 0x675 + 2 * (int)facing && door.OpenedId == door.ClosedId + 1 &&
                    door.Opened.Point == new Point3D(door.Closed.X + offset.X, door.Closed.Y + offset.Y, door.Closed.Z + offset.Z),
                "The door art or hinge coordinates do not match native MetalDoor geometry.");
            Require(door.Approaches?.Length == 2, "Each door needs two physical crossing approaches.");
            ValidatePoint(manifest, door.Approaches[0]);
            ValidatePoint(manifest, door.Approaches[1]);
            var first = door.Approaches[0].Point;
            var second = door.Approaches[1].Point;
            Require(first.Z == door.Closed.Z && second.Z == door.Closed.Z &&
                    (first.X + second.X == door.Closed.X * 2 && first.Y + second.Y == door.Closed.Y * 2) &&
                    CardinalAdjacent(first, door.Closed.Point) && CardinalAdjacent(second, door.Closed.Point),
                "Door approaches must directly straddle the closed doorway.");
            doorReservations.Add(door.Closed.Point);
            doorReservations.Add(door.Opened.Point);
            doorReservations.Add(first);
            doorReservations.Add(second);
        }

        ids.Clear();
        var approachIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var module in manifest.Modules)
        {
            Require(module != null && ids.Add(module.Id) && !string.IsNullOrWhiteSpace(module.Name), "Invalid named module.");
            ValidatePoint(manifest, module.Start);
            Require(module.Approaches is { Length: > 0 }, "Every module requires its room and POI approaches.");
            approachIds.Clear();
            foreach (var approach in module.Approaches)
            {
                Require(approach != null && !string.IsNullOrWhiteSpace(approach.Id) && approachIds.Add(approach.Id),
                    "Invalid or duplicate approach identity within a module.");
                ValidatePoint(manifest, approach.Point);
            }
        }

        var laneIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var lane in manifest.StairLanes)
        {
            Require(lane != null && laneIds.Add(lane.Id) && ids.Contains(lane.ModuleId) && lane.Points?.Length == 7,
                "Every staircase lane must identify a module and contain its lower landing, five treads, and upper landing.");
            for (var i = 0; i < lane.Points.Length; i++)
            {
                ValidatePoint(manifest, lane.Points[i]);
                var lowerZ = lane.Points[0].Z;
                Require(lane.Points[i].Z == (i switch { 0 => lowerZ, 6 => lowerZ + 25, _ => lowerZ + 2 + 5 * (i - 1) }),
                    "Staircase lane walking elevations do not match the audited native treads.");
                if (i > 0)
                {
                    Require(CardinalAdjacent(lane.Points[i - 1].Point, lane.Points[i].Point),
                        "Staircase lane points must be consecutive cardinal steps.");
                }
            }
        }

        if (revision)
        {
            Require(manifest.Chests is { Length: >= 75 and <= 125 } && manifest.EscapeRoutes?.Length == (fullLevels || ossuary ? 6 : 5),
                "The revision requires 3–5 chests per room and an independent descent for every gallery.");
            var chestCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var chestIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var chest in manifest.Chests)
            {
                Require(chest != null && chestIds.Add(chest.Id) && ids.Contains(chest.ModuleId) && positions.Add(chest.Position.Point),
                    "A chest has a duplicate identity, overlapping position, or unknown room.");
                ValidatePoint(manifest, chest.Position);
                ValidatePoint(manifest, chest.Approach);
                Require(!doorReservations.Contains(chest.Position.Point) && !doorReservations.Contains(chest.Approach.Point),
                    "A chest placement or opening approach overlaps a reserved doorway or hinge coordinate.");
                Require(chest.ItemId is 0xE40 or 0xE41 or 0xE42 or 0xE43 or 0x9AB or 0xE7C &&
                        chest.Type is "EventDungeonChest" or "RootboundCellarChest" &&
                        (decorated || chest.Type == "EventDungeonChest") &&
                        chest.Approach.Z == chest.Position.Z && CardinalAdjacent(chest.Approach.Point, chest.Position.Point),
                    "A chest lacks supported native art or a direct opening approach.");
                chestCounts.TryGetValue(chest.ModuleId, out var count);
                chestCounts[chest.ModuleId] = count + 1;
            }

            Require(chestCounts.Count == 25, "Every room must contain chests.");
            foreach (var count in chestCounts.Values)
            {
                Require(count is >= 3 and <= 5, "A room must contain 3–5 chests.");
            }

            var escapeIds = new HashSet<string>(StringComparer.Ordinal);
            var escapeModules = new HashSet<string>(StringComparer.Ordinal);
            foreach (var route in manifest.EscapeRoutes)
            {
                Require(route != null && escapeIds.Add(route.Id) && ids.Contains(route.ModuleId) && escapeModules.Add(route.ModuleId),
                    "Each gallery must have its own distinct escape route.");
                ValidatePoint(manifest, route.From);
                ValidatePoint(manifest, route.To);
                Require(route.From.Z == 25 && route.To.Z == 0 && CardinalAdjacent(route.From.Point, route.To.Point),
                    "A ledge escape must lead directly from the gallery to a ground landing.");
            }
        }
        if (decorated)
        {
            ValidateHiddenCellar(manifest, doorReservations);
        }
        else
        {
            Require(manifest.HiddenCellar == null, "A previous revision cannot install hidden-cellar mechanics.");
        }
    }

    private static void ValidateHiddenCellar(Manifest manifest, HashSet<Point3D> doorReservations)
    {
        var cellar = manifest.HiddenCellar;
        Require(cellar is { ModuleId: "M10", ChestId: "M10-hidden-chest" } && manifest.Chests.Length == 100,
            "The decorated revision requires one Rootbound cellar and 100 authorized containers.");
        var points = new HashSet<Point3D>();
        foreach (var point in new[] { cellar.Hatch, cellar.Entry, cellar.ReturnRune, cellar.SurfaceReturn, cellar.CreaturePosition })
        {
            ValidatePoint(manifest, point);
            Require(points.Add(point.Point) && !doorReservations.Contains(point.Point), "Hidden-cellar endpoints must be distinct and clear of doorways.");
            Require(point.X is > 5256 and < 5280 && point.Y is > 856 and < 880,
                "Hidden-cellar coordinates must stay inside the Rootbound module.");
        }
        Require(cellar.Hatch.Z == 0 && cellar.SurfaceReturn.Z == 0 && CardinalAdjacent(cellar.Hatch.Point, cellar.SurfaceReturn.Point) &&
                cellar.Entry.Z == -25 && cellar.ReturnRune.Z == -25 && cellar.CreaturePosition.Z == -25,
            "The hidden hatch and return rune must connect the surface to the sealed cellar without a teleport loop.");
        var rewards = 0;
        foreach (var chest in manifest.Chests)
        {
            Require(!points.Contains(chest.Position.Point), "A chest blocks a hidden-cellar endpoint.");
            Require(chest.Approach != cellar.Hatch && chest.Approach != cellar.ReturnRune,
                "A chest opening approach cannot be an activating transport tile.");
            if (chest.Type == "RootboundCellarChest")
            {
                rewards++;
                Require(chest.Id == cellar.ChestId && chest.ModuleId == cellar.ModuleId && chest.Position.Z == -25 &&
                        chest.Position.X is > 5256 and < 5280 && chest.Position.Y is > 856 and < 880 &&
                        chest.Approach.X is > 5256 and < 5280 && chest.Approach.Y is > 856 and < 880,
                    "Only the dedicated hidden-cellar chest may create normal T2A rewards.");
            }
        }
        Require(rewards == 1, "Exactly one creation-only hidden-cellar reward chest is required.");
    }

    private static void ValidateSceneryDefinitions(Manifest previous, Manifest next)
    {
        var chapel = next.InstallationId == ChapelId;
        Require(next.InstallationId is SceneryId or ChapelId && previous.InstallationId == (chapel ? SceneryId : DecoratedId) &&
                next.SceneryOnly && next.SceneryModuleId == (chapel ? "M00" : "M24"),
            "The scenery revision requires its exact previous installed manifest.");
        Require(EqualHash(previous.InstalledNativeSha256, next.OriginalNativeSha256) &&
                previous.StaticCount == next.OriginalStaticCount && previous.Bounds == next.Bounds &&
                previous.Origin == next.Origin && previous.Entry == next.Entry,
            "The scenery revision does not match its previous native footprint.");
        Require(SameDefinition(previous.Doors, next.Doors) && SameDefinition(previous.Chests, next.Chests) &&
                SameDefinition(previous.HiddenCellar, next.HiddenCellar) && SameDefinition(previous.StairLanes, next.StairLanes) &&
                SameDefinition(previous.EscapeRoutes, next.EscapeRoutes),
            "A static-only revision cannot alter retained event object placements or transport geometry.");
    }

    private static bool SameDefinition<T>(T previous, T next) =>
        JsonSerializer.SerializeToUtf8Bytes(previous, _jsonOptions).AsSpan().SequenceEqual(
            JsonSerializer.SerializeToUtf8Bytes(next, _jsonOptions));

    private static void ValidateCompletedDefinitions(Manifest previous, Manifest next)
    {
        Require(next.InstallationId == CompletedId && previous.InstallationId == SceneryId &&
                !next.SceneryOnly && next.SceneryModuleId == null &&
                EqualHash(previous.BlueprintSha256, "f26050070dee91d54fc5af22fe6acf728565bc7166b4291f82926ddfc71d32aa") &&
                EqualHash(previous.InstalledNativeSha256, next.OriginalNativeSha256) &&
                previous.StaticCount == next.OriginalStaticCount && previous.Bounds == next.Bounds &&
                previous.Origin == next.Origin && previous.Entry == next.Entry,
            "The completed redesign requires its exact installed V6 predecessor and native footprint.");
        Require(SameDefinition(previous.Doors, next.Doors) && SameDefinition(previous.HiddenCellar, next.HiddenCellar) &&
                previous.Chests.Length == next.Chests.Length && previous.StairLanes.Length == next.StairLanes.Length &&
                previous.EscapeRoutes.Length == next.EscapeRoutes.Length,
            "The completed redesign cannot replace event identities or hidden-cellar transports.");
        var movedChests = 0;
        for (var i = 0; i < previous.Chests.Length; i++)
        {
            var expected = CompletedChest(previous.Chests[i]);
            Require(SameDefinition(expected, next.Chests[i]), "Only the four authorized Echo Court chests may move.");
            movedChests += expected == previous.Chests[i] ? 0 : 1;
        }
        Require(movedChests == 4, "The predecessor must contain all four retained Echo Court chests.");
        var changedLanes = 0;
        for (var i = 0; i < previous.StairLanes.Length; i++)
        {
            var lane = previous.StairLanes[i];
            var laneIndex = lane.Id switch { "R22-upper-stairs-lane-0" => 0, "R22-upper-stairs-lane-1" => 1, _ => -1 };
            if (laneIndex >= 0)
            {
                Require(lane.ModuleId == "M22", "The Echo Court staircase identity belongs to another module.");
                var points = new Coordinate[7];
                for (var step = 0; step < points.Length; step++)
                {
                    points[step] = new Coordinate(5309 + step, 923 - laneIndex,
                        step switch { 0 => 0, 6 => 25, _ => 2 + 5 * (step - 1) });
                }
                lane = lane with { Points = points };
                changedLanes++;
            }
            Require(SameDefinition(lane, next.StairLanes[i]), "Only the two authorized Echo Court staircase lanes may change.");
        }
        Require(changedLanes == 2, "Both retained Echo Court staircase lanes are required.");
        var changedEscapes = 0;
        for (var i = 0; i < previous.EscapeRoutes.Length; i++)
        {
            var route = previous.EscapeRoutes[i];
            if (route.Id == "R22-gallery-escape")
            {
                Require(route.ModuleId == "M22", "The Echo Court ledge identity belongs to another module.");
                route = route with { From = new Coordinate(5324, 915, 25), To = new Coordinate(5324, 914, 0) };
                changedEscapes++;
            }
            Require(SameDefinition(route, next.EscapeRoutes[i]), "Only the authorized Echo Court ledge may change.");
        }
        Require(changedEscapes == 1, "The retained Echo Court escape route is required.");
    }

    private static ChestDefinition CompletedChest(ChestDefinition previous) => previous.Id switch
    {
        "M22-chest-1" when previous.ModuleId == "M22" => previous with
        { Position = new Coordinate(5315, 924, 25), Approach = new Coordinate(5316, 924, 25) },
        "M22-chest-2" when previous.ModuleId == "M22" => previous with
        { Position = new Coordinate(5325, 907, 0), Approach = new Coordinate(5324, 907, 0) },
        "M22-chest-3" when previous.ModuleId == "M22" => previous with
        { Position = new Coordinate(5305, 914, 0), Approach = new Coordinate(5306, 914, 0) },
        "M22-chest-4" when previous.ModuleId == "M22" => previous with
        { Position = new Coordinate(5327, 921, 0), Approach = new Coordinate(5326, 921, 0) },
        _ => previous
    };

    private static void ValidateFullLevelDefinitions(Manifest previous, Manifest next)
    {
        Require(next.InstallationId == FullLevelsId && previous.InstallationId == CompletedId &&
                EqualHash(previous.BlueprintSha256, CompletedBlueprintSha256) &&
                EqualHash(previous.InstalledNativeSha256, next.OriginalNativeSha256) &&
                previous.StaticCount == V15StaticCount && next.OriginalStaticCount == V15StaticCount &&
                previous.Bounds == next.Bounds && previous.Origin == next.Origin && previous.Entry == next.Entry,
            "The full-level continuation requires its exact installed V15 predecessor and native footprint.");
        Require(SameDefinition(previous.Doors, next.Doors) && SameDefinition(previous.Chests, next.Chests) &&
                SameDefinition(previous.HiddenCellar, next.HiddenCellar),
            "The full-level continuation cannot alter retained doors, containers, or hidden-cellar transports.");
        Require(previous.StairLanes.Length == 16 && next.StairLanes.Length == 24 &&
                previous.EscapeRoutes.Length == 5 && next.EscapeRoutes.Length == 6,
            "The full-level continuation requires all retained routes and four independent new stair flights.");
        var lanes = new Dictionary<string, StairLane>(StringComparer.Ordinal);
        foreach (var lane in next.StairLanes)
        {
            Require(lanes.TryAdd(lane.Id, lane), "A full-level staircase identity is duplicated.");
        }
        foreach (var lane in previous.StairLanes)
        {
            Require(lanes.TryGetValue(lane.Id, out var retained) && SameDefinition(lane, retained),
                "A retained staircase lane differs from its V15 definition.");
        }
        foreach (var flight in new[] { "R14-full-level-1-stairs", "R14-full-level-2-stairs",
                                      "R17-full-level-1-stairs", "R17-full-level-2-stairs" })
        {
            for (var lane = 0; lane < 2; lane++)
            {
                var points = new Coordinate[7];
                for (var step = 0; step < points.Length; step++)
                {
                    var z = step switch { 0 => 0, 6 => 25, _ => 2 + 5 * (step - 1) };
                    points[step] = flight switch
                    {
                        "R14-full-level-1-stairs" => new Coordinate(5355 + lane, 877 - step, z - 25),
                        "R14-full-level-2-stairs" => new Coordinate(5372 - step, 860 - lane, z - 25),
                        "R17-full-level-1-stairs" => new Coordinate(5321 + lane, 888 - step, z),
                        _ => new Coordinate(5311 - lane, 896 + step, z)
                    };
                }
                var expected = new StairLane($"{flight}-lane-{lane}", flight.StartsWith("R14", StringComparison.Ordinal) ? "M14" : "M17", points);
                Require(lanes.TryGetValue(expected.Id, out var actual) && SameDefinition(expected, actual),
                    "A new staircase lane differs from the authorized physical full-level geometry.");
            }
        }
        var escapes = new Dictionary<string, EscapeRoute>(StringComparer.Ordinal);
        foreach (var route in next.EscapeRoutes)
        {
            Require(escapes.TryAdd(route.Id, route), "A full-level escape identity is duplicated.");
        }
        foreach (var route in previous.EscapeRoutes)
        {
            Require(escapes.TryGetValue(route.Id, out var retained) && SameDefinition(route, retained),
                "A retained gallery escape differs from its V15 definition.");
        }
        Require(escapes.TryGetValue("R17-collapsed-floor-escape", out var collapsed) &&
                SameDefinition(new EscapeRoute("R17-collapsed-floor-escape", "M17",
                    new Coordinate(5315, 891, 25), new Coordinate(5316, 891, 0)), collapsed),
            "The Bone Wheel collapsed-floor escape differs from its authorized direct descent.");
    }

    private static void ValidateOssuaryDefinitions(Manifest previous, Manifest next)
    {
        Require(next.InstallationId == OssuaryId && previous.InstallationId == FullLevelsId &&
                EqualHash(previous.BlueprintSha256, FullLevelsBlueprintSha256) &&
                EqualHash(previous.InstalledNativeSha256, next.OriginalNativeSha256) &&
                previous.StaticCount == V16StaticCount && next.OriginalStaticCount == V16StaticCount &&
                previous.Bounds == next.Bounds && previous.Origin == next.Origin && previous.Entry == next.Entry,
            "The Ossuary continuation requires its exact installed V16 predecessor and native footprint.");
        Require(SameDefinition(previous.Doors, next.Doors) && SameDefinition(previous.HiddenCellar, next.HiddenCellar) &&
                SameDefinition(previous.EscapeRoutes, next.EscapeRoutes) && previous.EscapeRoutes.Length == 6 &&
                previous.Chests.Length == 100 && next.Chests.Length == 100 &&
                previous.StairLanes.Length == 24 && next.StairLanes.Length == 22 &&
                previous.Modules.Length == 25 && next.Modules.Length == 25,
            "The Ossuary continuation must retain every event identity, escape, and hidden-cellar transport.");

        var movedChests = 0;
        for (var i = 0; i < previous.Chests.Length; i++)
        {
            var expected = previous.Chests[i];
            if (expected.Id == "M02-chest-1")
            {
                Require(expected.ModuleId == "M02" && expected.Position == new Coordinate(5312, 818, -25) &&
                        expected.Approach == new Coordinate(5312, 819, -25) && expected.Type == "EventDungeonChest",
                    "The retained Ossuary cellar chest differs from its exact V16 placement.");
                expected = expected with { Position = new Coordinate(5316, 818, 0), Approach = new Coordinate(5316, 817, 0) };
                movedChests++;
            }
            Require(SameDefinition(expected, next.Chests[i]),
                "Only the authorized Ossuary cellar chest and its opening approach may move to the ground floor.");
        }
        Require(movedChests == 1, "Exactly one retained Ossuary cellar chest must move to the ground floor.");

        var removedLanes = 0;
        var retainedLaneIndex = 0;
        foreach (var lane in previous.StairLanes)
        {
            if (lane.Id is "R02-basement-stairs-lane-0" or "R02-basement-stairs-lane-1")
            {
                Require(lane.ModuleId == "M02", "The removable Ossuary stair lane belongs to another module.");
                removedLanes++;
                continue;
            }
            Require(retainedLaneIndex < next.StairLanes.Length && SameDefinition(lane, next.StairLanes[retainedLaneIndex++]),
                "Every non-Ossuary stair lane must retain its exact V16 geometry and identity.");
        }
        Require(removedLanes == 2 && retainedLaneIndex == next.StairLanes.Length,
            "Only the two Ossuary basement staircase lanes may be removed.");

        var removedApproaches = 0;
        for (var i = 0; i < previous.Modules.Length; i++)
        {
            var expected = previous.Modules[i];
            if (expected.Id == "M02")
            {
                var approaches = new List<Approach>(expected.Approaches.Length - 1);
                foreach (var approach in expected.Approaches)
                {
                    if (approach.Id == "R02-basement-focus")
                    {
                        Require(approach.Point == new Coordinate(5312, 820, -25),
                            "The removable Ossuary approach differs from its exact basement position.");
                        removedApproaches++;
                        continue;
                    }
                    approaches.Add(approach);
                }
                expected = expected with { Approaches = approaches.ToArray() };
            }
            Require(SameDefinition(expected, next.Modules[i]),
                "Only the Ossuary basement focus may be removed from the module approaches.");
        }
        Require(removedApproaches == 1, "Exactly one Ossuary basement focus must be removed.");
    }

    private static void ValidateAbundantDefinitions(Manifest previous, Manifest next)
    {
        Require(next.InstallationId == AbundantId && previous.InstallationId == OssuaryId &&
                EqualHash(previous.BlueprintSha256, OssuaryBlueprintSha256) &&
                EqualHash(previous.InstalledNativeSha256, next.OriginalNativeSha256) &&
                previous.StaticCount == V17StaticCount && next.OriginalStaticCount == V17StaticCount &&
                previous.Bounds == next.Bounds && previous.Origin == next.Origin && previous.Entry == next.Entry &&
                !next.SceneryOnly && next.SceneryModuleId == null,
            "The abundant continuation requires its exact installed V17 predecessor and native footprint.");
        Require(SameDefinition(previous.Doors, next.Doors) && SameDefinition(previous.Chests, next.Chests) &&
                SameDefinition(previous.HiddenCellar, next.HiddenCellar) && previous.Chests.Length == 100 &&
                previous.StairLanes.Length == 22 && next.StairLanes.Length == 24 &&
                previous.EscapeRoutes.Length == 6 && next.EscapeRoutes.Length == 5 &&
                previous.Modules.Length == 25 && next.Modules.Length == 25,
            "The abundant continuation must retain every door, container and hidden-cellar identity without relocation.");

        var expectedLanes = new List<StairLane>(24);
        var replacedLanes = 0;
        foreach (var lane in previous.StairLanes)
        {
            if (lane.ModuleId != "M00")
            {
                expectedLanes.Add(lane);
                continue;
            }
            Require(lane.Id == $"R00-upper-stairs-lane-{replacedLanes}" && replacedLanes < 2,
                "Only the two recorded chapel stair lanes may be replaced.");
            if (replacedLanes == 0)
            {
                foreach (var side in new[] { "west", "east" })
                {
                    for (var index = 0; index < 2; index++)
                    {
                        var points = new Coordinate[7];
                        var x = 5256 + (side == "west" ? 8 : 15) + index;
                        for (var step = 0; step < points.Length; step++)
                        {
                            points[step] = new Coordinate(x, 817 + step,
                                step switch { 0 => 0, 6 => 25, _ => 2 + 5 * (step - 1) });
                        }
                        expectedLanes.Add(new StairLane($"R00-{side}-choir-stairs-lane-{index}", "M00", points));
                    }
                }
            }
            replacedLanes++;
        }
        Require(replacedLanes == 2 && SameDefinition(expectedLanes.ToArray(), next.StairLanes),
            "Only the four authorized chapel choir lanes may replace the two V17 chapel lanes.");

        var expectedEscapes = new List<EscapeRoute>(5);
        var removedEscape = 0;
        foreach (var route in previous.EscapeRoutes)
        {
            if (route.ModuleId == "M00")
            {
                Require(route.Id == "R00-gallery-escape" && route.From == new Coordinate(5272, 828, 25) &&
                        route.To == new Coordinate(5273, 828, 0), "Only the exact chapel ledge may be closed.");
                removedEscape++;
                continue;
            }
            expectedEscapes.Add(route);
        }
        Require(removedEscape == 1 && SameDefinition(expectedEscapes.ToArray(), next.EscapeRoutes),
            "Every non-chapel escape must retain its exact V17 geometry and identity.");

        var chapelCount = 0;
        var forgeCount = 0;
        for (var i = 0; i < previous.Modules.Length; i++)
        {
            var expected = previous.Modules[i];
            if (expected.Id == "M00")
            {
                Require(expected.Name == "Chapel of Ash" && expected.Start == new Coordinate(5268, 815, 0) &&
                        expected.Approaches.Length == 7 && expected.Approaches[0].Id == "R00-focus" &&
                        expected.Approaches[1].Id == "R00-upper-focus" &&
                        expected.Approaches[1].Point == new Coordinate(5265, 828, 25),
                    "The chapel predecessor differs from its exact V17 room approaches.");
                var approaches = new List<Approach>(17)
                {
                    expected.Approaches[0],
                    new("R00-upper-focus", new Coordinate(5257, 819, 25)),
                    new("R00-upper-pagan-circle", new Coordinate(5266, 825, 25)),
                    new("R00-upper-pagan-altar", new Coordinate(5267, 824, 25)),
                    new("R00-upper-pagan-west-offerings", new Coordinate(5261, 827, 25)),
                    new("R00-upper-pagan-east-offerings", new Coordinate(5271, 827, 25)),
                    new("R00-upper-pagan-pillars", new Coordinate(5272, 825, 25)),
                    new("R00-upper-pagan-procession", new Coordinate(5262, 824, 25)),
                    new("R00-upper-corner-west", new Coordinate(5259, 828, 25)),
                    new("R00-upper-corner-east", new Coordinate(5277, 828, 25)),
                    new("R00-upper-remains-west", new Coordinate(5258, 828, 25)),
                    new("R00-upper-remains-east", new Coordinate(5278, 828, 25))
                };
                for (var approach = 2; approach < expected.Approaches.Length; approach++)
                {
                    approaches.Add(expected.Approaches[approach]);
                }
                expected = expected with { Approaches = approaches.ToArray() };
                chapelCount++;
            }
            else if (expected.Id == "M05")
            {
                Require(expected.Name == "The Ember Forge" && expected.Start == new Coordinate(5273, 843, 0) &&
                        expected.Approaches.Length == 7 && expected.Approaches[0].Id == "R05-focus",
                    "The forge predecessor differs from its exact V17 room approaches.");
                var approaches = new List<Approach>(12)
                {
                    expected.Approaches[0],
                    new("R05-work-dais", new Coordinate(5273, 842, 0)),
                    new("R05-founder-pad-1", new Coordinate(5258, 836, 0)),
                    new("R05-founder-pad-2", new Coordinate(5274, 836, 0)),
                    new("R05-founder-pad-3", new Coordinate(5258, 852, 0)),
                    new("R05-founder-pad-4", new Coordinate(5274, 849, 0))
                };
                for (var approach = 1; approach < expected.Approaches.Length; approach++)
                {
                    approaches.Add(expected.Approaches[approach]);
                }
                expected = expected with { Approaches = approaches.ToArray() };
                forgeCount++;
            }
            Require(SameDefinition(expected, next.Modules[i]),
                "Only the authorized chapel and forge POI approaches may differ from V17 module definitions.");
        }
        Require(chapelCount == 1 && forgeCount == 1, "The abundant continuation requires both authored rooms.");
    }

    private static void ValidateHash(string hash)
    {
        Require(hash?.Length == 64, "Invalid deployment SHA256.");
        foreach (var character in hash)
        {
            Require(char.IsAsciiHexDigit(character), "Invalid deployment SHA256.");
        }
    }

    private static void ValidatePoint(Manifest manifest, Coordinate coordinate) =>
        Require(coordinate != null && manifest.Bounds.Rectangle.Contains(coordinate.Point) && coordinate.Z is >= -25 and <= 50,
            "A deployment coordinate is outside the authorized footprint or elevation.");

    private static bool CardinalAdjacent(Point3D first, Point3D second) =>
        Math.Abs(first.X - second.X) + Math.Abs(first.Y - second.Y) == 1;

    private static Direction DirectionTo(Point3D first, Point3D second) => (second.X - first.X, second.Y - first.Y) switch
    {
        (0, -1) => Direction.North,
        (1, 0) => Direction.East,
        (0, 1) => Direction.South,
        (-1, 0) => Direction.West,
        _ => throw new InvalidOperationException("A movement check requires a consecutive cardinal step.")
    };

    private enum DeploymentPhase
    {
        NativeAudit,
        DoorPreflight,
        DoorCrossings,
        StairLanes,
        EscapeRoutes,
        Reachability,
        RequestSave,
        AwaitSave,
        Rollback,
        AwaitRollbackSave,
        Finished
    }

    private sealed class Job
    {
        private static readonly Direction[] _directions = [Direction.North, Direction.East, Direction.South, Direction.West];
        private readonly Manifest _manifest;
        private readonly string _manifestHash;
        private readonly bool _auditOnly;
        private readonly bool _sceneryOnly;
        private readonly bool _completedRevision;
        private readonly bool _fullLevelsRevision;
        private readonly bool _ossuaryRevision;
        private readonly bool _abundantRevision;
        private readonly bool _retainedRuntime;
        private readonly IncrementalHash _nativeHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private readonly List<EventDungeonDoor> _doors = new(DoorCount);
        private readonly List<EventDungeonChest> _chests = new();
        private readonly Dictionary<string, EventDungeonDoor> _previousDoors = new(StringComparer.Ordinal);
        private readonly Dictionary<EventDungeonDoor, bool> _previousOpenStates = new();
        private readonly Dictionary<string, EventDungeonChest> _previousChests = new(StringComparer.Ordinal);
        private readonly Dictionary<EventDungeonChest, (Point3D Position, int ItemId)> _previousChestStates = new();
        private RootboundCellarFeature.Features _cellarFeatures;
        private ExtractionEventRuntime.ExtractionDeploymentOwnership _extractionOwnership;
        private bool _extractionOwnershipCaptured;
        private DoorSerial[] _retainedFeatureSerials;
        private DoorSerial[] _retainedCreatureSerials;
        private readonly List<MovementResult> _movementResults = new();
        private readonly HashSet<Point3D> _visited = new();
        private readonly Queue<Point3D> _frontier = new();
        private readonly HashSet<Point3D> _targets = new();
        private TimerExecutionToken _timer;
        private DeploymentPhase _phase = DeploymentPhase.NativeAudit;
        private int _cellCursor;
        private int _staticCount;
        private int _cursor;
        private int _movementChecks;
        private string _nativeDigest;
        private string _failure;
        private DateTime _saveDirectoryTimestamp;
        private long _saveDeadline;
        private bool _snapshotSucceeded;
        private bool _installedResultWritten;

        public Job(Manifest manifest, string manifestHash, bool auditOnly)
        {
            _manifest = manifest;
            _manifestHash = manifestHash;
            _auditOnly = auditOnly;
            _sceneryOnly = manifest.InstallationId is SceneryId or ChapelId;
            _completedRevision = manifest.InstallationId == CompletedId;
            _fullLevelsRevision = manifest.InstallationId == FullLevelsId;
            _ossuaryRevision = manifest.InstallationId == OssuaryId;
            _abundantRevision = manifest.InstallationId == AbundantId;
            _retainedRuntime = _sceneryOnly || _completedRevision || _fullLevelsRevision || _ossuaryRevision || _abundantRevision;
            if (_retainedRuntime)
            {
                LoadRetainedSceneryObjects();
                if (_abundantRevision && ExtractionEventRuntime.Controller != null)
                {
                    CaptureExtractionOwnership();
                }
                return;
            }
            if (manifest.InstallationId is RevisionId or DecoratedId)
            {
                var decorated = manifest.InstallationId == DecoratedId;
                var receipt = ReadPreviousReceipt(decorated);
                Require(EqualHash(receipt.NativeSha256, manifest.OriginalNativeSha256) && receipt.Doors?.Length == DoorCount,
                    "The revision does not match the previous native geometry and door journal.");
                foreach (var serial in receipt.Doors)
                {
                    Require(World.FindItem((Serial)serial.Serial) is EventDungeonDoor, "A recorded previous event door is missing.");
                    var door = (EventDungeonDoor)World.FindItem((Serial)serial.Serial);
                    Require(_previousDoors.TryAdd(serial.Id, door), "A previous door identity is duplicated.");
                    _previousOpenStates.Add(door, door.Open);
                }

                foreach (var definition in manifest.Doors)
                {
                    Require(_previousDoors.TryGetValue(definition.Id, out var door), "A revision doorway is not in the previous installation.");
                    ValidateDoor(door, definition, door.Open);
                }
                if (decorated)
                {
                    var path = Path.Combine(Core.BaseDirectory, "Data", "ModularDungeonV4", "install-manifest.json");
                    Require(File.Exists(path) && new FileInfo(path).Length is > 0 and <= MaximumManifestBytes,
                        "The previous chest placement manifest is required.");
                    var bytes = File.ReadAllBytes(path);
                    Require(EqualHash(Convert.ToHexStringLower(SHA256.HashData(bytes)), receipt.ManifestSha256),
                        "The previous placement manifest differs from its installation receipt.");
                    var previous = JsonSerializer.Deserialize<Manifest>(bytes, _jsonOptions);
                    ValidateManifest(previous);
                    Require(previous.InstallationId == RevisionId && previous.Chests.Length == 99 && receipt.Chests?.Length == 99,
                        "The revision requires exactly 99 recorded previous event chests.");
                    var definitions = new Dictionary<string, ChestDefinition>(StringComparer.Ordinal);
                    foreach (var definition in previous.Chests)
                    {
                        definitions.Add(definition.Id, definition);
                    }
                    foreach (var serial in receipt.Chests)
                    {
                        Require(definitions.TryGetValue(serial.Id, out var definition) &&
                                World.FindItem((Serial)serial.Serial) is EventDungeonChest,
                            "A recorded previous event chest is missing.");
                        var chest = (EventDungeonChest)World.FindItem((Serial)serial.Serial);
                        Require(chest.GetType() == typeof(EventDungeonChest) && chest.Map == Map.Felucca && chest.Parent == null &&
                                !chest.Movable && chest.Location == definition.Position.Point && chest.ItemID == definition.ItemId,
                            "A previous chest differs from its recorded native placement.");
                        Require(_previousChests.TryAdd(serial.Id, chest), "A previous chest identity is duplicated.");
                        _previousChestStates.Add(chest, (chest.Location, chest.ItemID));
                    }
                    foreach (var definition in manifest.Chests)
                    {
                        Require(definition.Type == "RootboundCellarChest" || _previousChests.ContainsKey(definition.Id),
                            "A standard chest has no authorized previous identity.");
                    }
                }
            }
        }

        private void LoadRetainedSceneryObjects()
        {
            var chapel = _manifest.InstallationId == ChapelId;
            var pinnedV6 = chapel || _completedRevision;
            var previousId = _abundantRevision ? OssuaryId : _ossuaryRevision ? FullLevelsId : _fullLevelsRevision ? CompletedId : pinnedV6 ? SceneryId : DecoratedId;
            var previousStaticCount = _abundantRevision ? V17StaticCount : _ossuaryRevision ? V16StaticCount : _fullLevelsRevision ? V15StaticCount : pinnedV6 ? V6StaticCount : V5StaticCount;
            var directory = Path.Combine(Core.BaseDirectory, "Data", _abundantRevision ? "ModularDungeonV17" : _ossuaryRevision ? "ModularDungeonV16" : _fullLevelsRevision ? "ModularDungeonV15" : pinnedV6 ? "ModularDungeonV6" : "ModularDungeonV5");
            var receiptPath = Path.Combine(directory, "installed.json");
            var manifestPath = Path.Combine(directory, "install-manifest.json");
            Require(File.Exists(receiptPath) && new FileInfo(receiptPath).Length is > 0 and <= MaximumManifestBytes &&
                    File.Exists(manifestPath) && new FileInfo(manifestPath).Length is > 0 and <= MaximumManifestBytes,
                "A bounded previous installed receipt and manifest are required for retained-runtime deployment.");
            var receiptBytes = File.ReadAllBytes(receiptPath);
            var receipt = JsonSerializer.Deserialize<Result>(receiptBytes, _jsonOptions);
            Require(receipt is { Success: true, Stage: "installed", DoorCount: DoorCount, Doors.Length: DoorCount, Chests.Length: 100,
                                 Features.Length: 3, Creatures.Length: 1 } &&
                    receipt.InstallationId == previousId && receipt.StaticCount == previousStaticCount &&
                    EqualHash(receipt.NativeSha256, _manifest.OriginalNativeSha256),
                "The previous installation receipt is invalid.");
            var bytes = File.ReadAllBytes(manifestPath);
            Require(EqualHash(Convert.ToHexStringLower(SHA256.HashData(bytes)), receipt.ManifestSha256),
                "The previous manifest differs from its saved installation receipt.");
            if (pinnedV6 || _fullLevelsRevision || _ossuaryRevision || _abundantRevision)
            {
                Require(EqualHash(Convert.ToHexStringLower(SHA256.HashData(receiptBytes)), _manifest.PreviousReceiptSha256) &&
                        EqualHash(receipt.ManifestSha256, _manifest.PreviousManifestSha256),
                    "The continuation does not match its authorized previous receipt and manifest pins.");
            }
            var previous = JsonSerializer.Deserialize<Manifest>(bytes, _jsonOptions);
            ValidateManifest(previous);
            if (_abundantRevision)
            {
                ValidateAbundantDefinitions(previous, _manifest);
            }
            else if (_ossuaryRevision)
            {
                ValidateOssuaryDefinitions(previous, _manifest);
            }
            else if (_fullLevelsRevision)
            {
                ValidateFullLevelDefinitions(previous, _manifest);
            }
            else if (_completedRevision)
            {
                ValidateCompletedDefinitions(previous, _manifest);
            }
            else
            {
                ValidateSceneryDefinitions(previous, _manifest);
            }

            var doors = new Dictionary<string, DoorDefinition>(StringComparer.Ordinal);
            foreach (var definition in _manifest.Doors)
            {
                doors.Add(definition.Id, definition);
            }
            foreach (var serial in receipt.Doors)
            {
                Require(doors.TryGetValue(serial.Id, out var definition) &&
                        World.FindItem((Serial)serial.Serial) is EventDungeonDoor,
                    "A recorded retained event door is missing or has the wrong type.");
                var door = (EventDungeonDoor)World.FindItem((Serial)serial.Serial);
                Require(door.GetType() == typeof(EventDungeonDoor), "A retained event door has the wrong runtime class.");
                Require(_previousDoors.TryAdd(serial.Id, door) && !_previousOpenStates.ContainsKey(door),
                    "A retained doorway serial or identity is duplicated.");
                _previousOpenStates.Add(door, door.Open);
                ValidateDoor(door, definition, door.Open);
            }

            var chests = new Dictionary<string, ChestDefinition>(StringComparer.Ordinal);
            foreach (var definition in previous.Chests)
            {
                chests.Add(definition.Id, definition);
            }
            foreach (var serial in receipt.Chests)
            {
                Require(chests.TryGetValue(serial.Id, out var definition) &&
                        World.FindItem((Serial)serial.Serial) is EventDungeonChest,
                    "A recorded retained event chest is missing or has the wrong type.");
                var chest = (EventDungeonChest)World.FindItem((Serial)serial.Serial);
                Require(_previousChests.TryAdd(serial.Id, chest) && !_previousChestStates.ContainsKey(chest),
                    "A retained chest serial or identity is duplicated.");
                _previousChestStates.Add(chest, (chest.Location, chest.ItemID));
                ValidateRetainedChest(chest, definition);
            }

            _retainedFeatureSerials = receipt.Features;
            _retainedCreatureSerials = receipt.Creatures;
            var featureSerials = new Dictionary<string, uint>(StringComparer.Ordinal);
            var uniqueSerials = new HashSet<uint>();
            foreach (var serial in receipt.Features)
            {
                Require(((Serial)serial.Serial).IsItem && featureSerials.TryAdd(serial.Id, serial.Serial) && uniqueSerials.Add(serial.Serial),
                    "A retained cellar feature serial or identity is duplicated.");
            }
            Require(featureSerials.ContainsKey("R10-hidden-cover") && featureSerials.ContainsKey("R10-hidden-descent") &&
                    featureSerials.ContainsKey("R10-hidden-return") && receipt.Creatures[0].Id == "R10-cellar-lich" &&
                    ((Serial)receipt.Creatures[0].Serial).IsMobile && uniqueSerials.Add(receipt.Creatures[0].Serial),
                "The retained cellar identities are invalid.");
            var cover = World.FindItem((Serial)featureSerials["R10-hidden-cover"]);
            var hatch = World.FindItem((Serial)featureSerials["R10-hidden-descent"]);
            var rune = World.FindItem((Serial)featureSerials["R10-hidden-return"]);
            var lich = World.FindMobile((Serial)receipt.Creatures[0].Serial);
            Require((cover == null || cover.GetType() == typeof(WoodenBox)) &&
                    hatch?.GetType() == typeof(EventDungeonHiddenHatch) && rune?.GetType() == typeof(RootboundCellarReturnRune) &&
                    (lich == null || lich.GetType() == typeof(RootboundCellarLich)),
                "A retained cellar feature or creature has the wrong runtime type.");
            _cellarFeatures = new RootboundCellarFeature.Features
            {
                Cover = (WoodenBox)cover, Hatch = (EventDungeonHiddenHatch)hatch,
                ReturnRune = (RootboundCellarReturnRune)rune, Lich = (RootboundCellarLich)lich
            };
            ValidateRetainedCellar();
        }

        private static void ValidateRetainedChest(EventDungeonChest chest, ChestDefinition definition)
        {
            var expectedType = definition.Type == "RootboundCellarChest" ? typeof(RootboundCellarChest) : typeof(EventDungeonChest);
            Require(chest != null && !chest.Deleted && chest.GetType() == expectedType && chest.Parent == null &&
                    !chest.Movable && !chest.IsDecoContainer && chest.Map == Map.Felucca &&
                    chest.Location == definition.Position.Point && chest.ItemID == definition.ItemId &&
                    (chest is not RootboundCellarChest reward || reward.LootInitialized),
                "A retained event chest differs from its authorized class or native placement.");
        }

        private void ValidateRetainedCellar()
        {
            var cellar = _manifest.HiddenCellar;
            var hatch = _cellarFeatures.Hatch;
            var rune = _cellarFeatures.ReturnRune;
            var cover = _cellarFeatures.Cover;
            Require(hatch is { Deleted: false, Parent: null, Movable: false, Active: true, Creatures: false } &&
                    hatch.ItemID == 0x1BC3 && !hatch.Visible &&
                    hatch.Map == Map.Felucca && hatch.Location == cellar.Hatch.Point && hatch.MapDest == Map.Felucca &&
                    hatch.PointDest == cellar.Entry.Point && hatch.Delay == TimeSpan.Zero &&
                    hatch.CoverOrigin == cellar.Hatch.Point && hatch.CoverMap == Map.Felucca &&
                    (cover is { Deleted: false } ? hatch.CoverBox == cover : hatch.CoverBox == null || hatch.CoverBox.Deleted),
                "The retained hidden hatch or cover identity differs from its authorized transport.");
            Require(rune is { Deleted: false, Parent: null, Movable: false, Active: true, Creatures: false, Visible: true } &&
                    rune.Map == Map.Felucca && rune.Location == cellar.ReturnRune.Point && rune.MapDest == Map.Felucca &&
                    rune.PointDest == cellar.SurfaceReturn.Point && rune.HatchOrigin == cellar.Hatch.Point &&
                    rune.Delay == TimeSpan.Zero && rune.ItemID == 0x1822,
                "The retained return rune differs from its authorized transport.");
        }

        public string Phase => _phase.ToString();
        public int CellCursor => _cellCursor;
        public int MovementChecks => _movementChecks;

        public void Start() => Timer.StartTimer(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50), Tick, out _timer);

        public void Stop()
        {
            _timer.Cancel();
            CloseDoors();
            _doors.Clear();
            _chests.Clear();
            _previousDoors.Clear();
            _previousOpenStates.Clear();
            _previousChests.Clear();
            _previousChestStates.Clear();
            _cellarFeatures = null;
            _extractionOwnership = null;
            _retainedFeatureSerials = null;
            _retainedCreatureSerials = null;
            _nativeHash.Dispose();
        }

        public void OnSuccessfulSnapshot()
        {
            if (_phase is DeploymentPhase.AwaitSave or DeploymentPhase.AwaitRollbackSave)
            {
                _snapshotSucceeded = true;
            }
        }

        private void Tick()
        {
            if (World.Saving || World.WorldState is WorldState.PendingSave or WorldState.WritingSave)
            {
                return;
            }

            try
            {
                switch (_phase)
                {
                    case DeploymentPhase.NativeAudit:
                        AuditNativeSlice();
                        break;
                    case DeploymentPhase.DoorPreflight:
                        PreflightAndCreateDoors();
                        break;
                    case DeploymentPhase.DoorCrossings:
                        CheckDoorCrossings();
                        break;
                    case DeploymentPhase.StairLanes:
                        CheckStairLanes();
                        break;
                    case DeploymentPhase.EscapeRoutes:
                        CheckEscapeRoutes();
                        break;
                    case DeploymentPhase.Reachability:
                        CheckReachabilitySlice();
                        break;
                    case DeploymentPhase.RequestSave when World.WorldState == WorldState.Running:
                        if (!_retainedRuntime)
                        {
                            CreateHiddenCellar();
                        }
                        else
                        {
                            ValidateRetainedCellar();
                        }
                        ValidateOccupancy(_manifest.Bounds.Rectangle, true);
                        ValidateAllDoors();
                        WriteResult("install-state.json", CreateResult(false, "awaiting-save", null));
                        BeginSave(false);
                        break;
                    case DeploymentPhase.AwaitSave:
                        CheckSave(false);
                        break;
                    case DeploymentPhase.Rollback when World.WorldState == WorldState.Running:
                        BeginSave(true);
                        break;
                    case DeploymentPhase.AwaitRollbackSave:
                        CheckSave(true);
                        break;
                }
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
        }

        private void AuditNativeSlice()
        {
            CaptureExtractionOwnership();
            var watch = Stopwatch.StartNew();
            for (var count = 0; count < 128 && _cellCursor < 18496 && watch.ElapsedMilliseconds < 4; count++)
            {
                var x = _manifest.Bounds.X + _cellCursor % 136;
                var y = _manifest.Bounds.Y + _cellCursor / 136;
                ValidateOccupancy(new Rectangle2D(x, y, 1, 1), false);
                foreach (var tile in new Map.StaticTileEnumerable(Map.Felucca, new Point2D(x, y), false, true))
                {
                    throw new InvalidOperationException($"A multi component occupies the audited native tile at {x},{y},{tile.Z}.");
                }

                HashNativeCell(x, y);
                _cellCursor++;
            }

            if (_cellCursor != 18496)
            {
                return;
            }

            _nativeDigest = Convert.ToHexStringLower(_nativeHash.GetHashAndReset());
            var expected = _auditOnly ? _manifest.OriginalNativeSha256 : _manifest.InstalledNativeSha256;
            Require(EqualHash(_nativeDigest, expected), "The native map fingerprint differs from the authorized manifest.");
            Require(_staticCount == (_auditOnly ? _manifest.OriginalStaticCount : _manifest.StaticCount), "The native map contains an unexpected static count.");
            ValidateOccupancy(_manifest.Bounds.Rectangle, false);
            if (_auditOnly)
            {
                WriteResult("audit-result.json", CreateResult(true, "audit", null));
                RetireMarker("audit");
                Finish("Original native geometry and runtime ownership audit passed; the scoped map patch may now be applied while offline.");
            }
            else
            {
                _phase = DeploymentPhase.DoorPreflight;
                _status = _retainedRuntime ? "Patched native geometry verified; surveying 71 retained event doors." :
                    "Patched native geometry verified; preflighting 71 new event doors.";
            }
        }

        private void HashNativeCell(int x, int y)
        {
            var land = Map.Felucca.Tiles.GetLandTile(x, y);
            Span<StaticTile> statics = stackalloc StaticTile[64];
            var count = 0;
            foreach (var tile in Map.Felucca.Tiles.GetStaticTiles(x, y))
            {
                Require(count < statics.Length, "A native tile exceeds the audited static density.");
                var insert = count;
                while (insert > 0 && CompareStatics(tile, statics[insert - 1]) < 0)
                {
                    statics[insert] = statics[insert - 1];
                    insert--;
                }

                statics[insert] = tile;
                count++;
            }

            if (_auditOnly && _manifest.InstallationId == InstallationId)
            {
                Require(count == 0 && land.ID == _manifest.OriginalLandId && land.Z == _manifest.OriginalLandZ,
                    $"Original native map is occupied at {x},{y}; no existing terrain or statics may be removed.");
            }

            Span<byte> bytes = stackalloc byte[20];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, x);
            BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], y);
            BinaryPrimitives.WriteInt32LittleEndian(bytes[8..], land.ID);
            BinaryPrimitives.WriteInt32LittleEndian(bytes[12..], land.Z);
            BinaryPrimitives.WriteInt32LittleEndian(bytes[16..], count);
            _nativeHash.AppendData(bytes);
            for (var i = 0; i < count; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes, statics[i].ID);
                BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], statics[i].Z);
                BinaryPrimitives.WriteInt32LittleEndian(bytes[8..], statics[i].Hue);
                _nativeHash.AppendData(bytes[..12]);
            }

            _staticCount += count;
        }

        private static int CompareStatics(StaticTile left, StaticTile right)
        {
            var id = left.ID.CompareTo(right.ID);
            if (id != 0)
            {
                return id;
            }

            var z = left.Z.CompareTo(right.Z);
            return z != 0 ? z : left.Hue.CompareTo(right.Hue);
        }

        private void CaptureExtractionOwnership()
        {
            if (!_abundantRevision || _extractionOwnershipCaptured)
            {
                return;
            }
            Require(_phase == DeploymentPhase.NativeAudit && _cellCursor == 0,
                "Extraction ownership must be captured before the first native audit cell.");
            // Deferred deserialization publishes the bound controller after content Initialize has created this job.
            if (ExtractionEventRuntime.Controller != null)
            {
                Require(ExtractionEventRuntime.TryGetDeploymentOwnership(out _extractionOwnership, out var error), error);
            }
            _extractionOwnershipCaptured = true;
        }

        private void ValidateOccupancy(Rectangle2D bounds, bool allowOwnDoors)
        {
            if (_abundantRevision)
            {
                CaptureExtractionOwnership();
                Require(_extractionOwnership != null || ExtractionEventRuntime.Controller == null,
                    "An extraction controller appeared after the deployment ownership snapshot.");
                if (_extractionOwnership != null)
                {
                    Require(_extractionOwnership.IsValid(out var error), error);
                }
            }
            foreach (var mobile in Map.Felucca.GetMobilesInBounds<Mobile>(bounds))
            {
                if ((allowOwnDoors || _retainedRuntime) && _cellarFeatures?.Lich == mobile &&
                    (!_abundantRevision || mobile is RootboundCellarLich { Controlled: false, Summoned: false, Spawner: null }) ||
                    _extractionOwnership?.OwnsDeploymentEntity(mobile) == true)
                {
                    continue;
                }
                throw new InvalidOperationException($"Runtime mobile {mobile.Serial} occupies the audited buffer.");
            }

            foreach (var item in Map.Felucca.GetItemsInBounds<Item>(bounds))
            {
                Require(item is EventDungeonDoor door && (_previousOpenStates.ContainsKey(door) || allowOwnDoors && _doors.Contains(door)) ||
                        item is EventDungeonChest previousChest && _previousChestStates.ContainsKey(previousChest) ||
                        allowOwnDoors && item is EventDungeonChest chest && _chests.Contains(chest) ||
                        (allowOwnDoors || _retainedRuntime) &&
                                         _cellarFeatures != null && (item == _cellarFeatures.Cover || item == _cellarFeatures.Hatch ||
                                                                    item == _cellarFeatures.ReturnRune) ||
                        _extractionOwnership?.OwnsDeploymentEntity(item) == true,
                    $"Runtime item {item.Serial} occupies the audited buffer.");
            }

            // Sector multi indexes include components whose owning item's anchor lies outside the buffer.
            for (var x = bounds.X; x < bounds.X + bounds.Width; x += 16)
            {
                for (var y = bounds.Y; y < bounds.Y + bounds.Height; y += 16)
                {
                    ValidateSectorOverlaps(x, y, bounds);
                }
            }

            ValidateSectorOverlaps(bounds.X + bounds.Width - 1, bounds.Y + bounds.Height - 1, bounds);
        }

        private static void ValidateSectorOverlaps(int x, int y, Rectangle2D bounds)
        {
            foreach (var multi in Map.Felucca.GetMultisInSector(x, y))
            {
                Require(!multi.Intersects(bounds), $"Multi {multi.Serial} has components overlapping the audited buffer.");
            }

            foreach (var region in Map.Felucca.GetSector(x, y).Regions)
            {
                if (region is not HouseRegion houseRegion)
                {
                    continue;
                }

                foreach (var area in houseRegion.Area)
                {
                    Require(area.X >= bounds.X + bounds.Width || area.X + area.Width <= bounds.X ||
                            area.Y >= bounds.Y + bounds.Height || area.Y + area.Height <= bounds.Y,
                        $"House {houseRegion.House.Serial} overlaps the audited buffer.");
                }
            }
        }

        private void PreflightAndCreateDoors()
        {
            ValidateOccupancy(_manifest.Bounds.Rectangle, false);
            if (_retainedRuntime)
            {
                Require(!(_completedRevision || _fullLevelsRevision || _ossuaryRevision || _abundantRevision) || !_auditOnly, "The original-map audit cannot preflight or relocate retained containers.");
                ValidateRetainedCellar();
                foreach (var definition in _manifest.Doors)
                {
                    _doors.Add(_previousDoors[definition.Id]);
                }
                if (_ossuaryRevision)
                {
                    RelocateOssuaryChest();
                }
                else if (_completedRevision)
                {
                    RelocateCompletedChests();
                }
                else if (_fullLevelsRevision || _abundantRevision)
                {
                    ValidateRetainedDoorFits();
                    ValidateRetainedChestFits();
                }
                foreach (var definition in _manifest.Chests)
                {
                    var chest = _previousChests[definition.Id];
                    ValidateRetainedChest(chest, definition);
                    _chests.Add(chest);
                }
                WriteResult("install-state.json", CreateResult(false, "survey", null));
                _phase = DeploymentPhase.DoorCrossings;
                _status = "Retained event identities verified; checking native movement without recreating objects.";
                return;
            }
            foreach (var definition in _manifest.Doors)
            {
                _previousDoors.TryGetValue(definition.Id, out var existing);
                var wasOpen = existing?.Open == true;
                try
                {
                    if (existing != null)
                    {
                        existing.Open = true;
                    }

                    Require(Map.Felucca.CanFit(definition.Closed.Point, 16, true, true),
                        $"Door {definition.Id} cannot fit at its closed location.");
                    if (existing != null)
                    {
                        existing.Open = false;
                    }

                    Require(Map.Felucca.CanFit(definition.Opened.Point, 16, true, true, false),
                        $"Door {definition.Id} cannot fit at its native opened hinge location.");
                }
                finally
                {
                    if (existing != null)
                    {
                        existing.Open = wasOpen;
                    }
                }
            }

            foreach (var chest in _previousChests.Values)
            {
                chest.Internalize();
            }
            foreach (var definition in _manifest.Chests ?? [])
            {
                Require(Map.Felucca.CanFit(definition.Position.Point, 16, true, true),
                    $"Chest {definition.Id} has an obstructed placement.");
                Require(Map.Felucca.CanFit(definition.Approach.Point, 16, true, true),
                    $"Chest {definition.Id} has an obstructed opening approach.");
            }
            if (_manifest.HiddenCellar is { } cellar)
            {
                foreach (var point in new[] { cellar.Hatch, cellar.Entry, cellar.ReturnRune, cellar.SurfaceReturn, cellar.CreaturePosition })
                {
                    Require(Map.Felucca.CanFit(point.Point, 16, true, true), "A hidden-cellar endpoint lacks clear native floor support.");
                }
            }

            foreach (var definition in _manifest.Doors)
            {
                var door = _previousDoors.GetValueOrDefault(definition.Id) ?? new EventDungeonDoor(Enum.Parse<DoorFacing>(definition.Facing))
                {
                    Locked = true,
                    KeyValue = 0,
                    Link = null
                };
                _doors.Add(door);
                door.Open = false;
                door.MoveToWorld(definition.Closed.Point, Map.Felucca);
                ValidateDoor(door, definition, false);
            }

            foreach (var definition in _manifest.Chests ?? [])
            {
                var chest = _previousChests.GetValueOrDefault(definition.Id) ??
                    (definition.Type == "RootboundCellarChest" ? new RootboundCellarChest(definition.ItemId) : new EventDungeonChest(definition.ItemId));
                _chests.Add(chest);
                chest.ItemID = definition.ItemId;
                chest.MoveToWorld(definition.Position.Point, Map.Felucca);
            }

            WriteResult("install-state.json", CreateResult(false, "survey", null));
            _phase = DeploymentPhase.DoorCrossings;
            _status = "Event objects ready; checking native door crossings and every staircase lane.";
        }

        private void RelocateCompletedChests()
        {
            ValidateRetainedDoorFits();
            InternalizeCompletedChests();
            ValidateRetainedChestFits();
            PlaceCompletedChests();
        }

        private void RelocateOssuaryChest()
        {
            Require(_ossuaryRevision && !_auditOnly, "Only the installed-map Ossuary continuation may relocate its cellar chest.");
            ValidateRetainedDoorFits();
            ValidateRetainedChestFits();
            var chest = _previousChests["M02-chest-1"];
            Require(_previousChestStates[chest].Position == new Point3D(5312, 818, -25) &&
                    chest.Location == _previousChestStates[chest].Position && chest.Map == Map.Felucca,
                "The retained Ossuary cellar chest must remain at its audited position until installation.");
            // The cellar is sealed by the native patch; preserve its container and contents on a supported surface tile.
            chest.MoveToWorld(new Point3D(5316, 818, 0), Map.Felucca);
        }

        private void RestoreOssuaryChest()
        {
            if (_previousChests.TryGetValue("M02-chest-1", out var chest) && !chest.Deleted &&
                _previousChestStates.TryGetValue(chest, out var state) &&
                (chest.Map != Map.Felucca || chest.Location != state.Position || chest.ItemID != state.ItemId))
            {
                chest.ItemID = state.ItemId;
                chest.MoveToWorld(state.Position, Map.Felucca);
            }
        }

        private void ValidateRetainedDoorFits()
        {
            if (_abundantRevision)
            {
                ValidateOccupancy(_manifest.Bounds.Rectangle, false);
            }
            foreach (var definition in _manifest.Doors)
            {
                var door = _previousDoors[definition.Id];
                try
                {
                    door.Open = true;
                    // Paused, exactly owned event creatures are transient occupancy, not native door geometry.
                    Require(Map.Felucca.CanFit(definition.Closed.Point, 16, true, !_abundantRevision),
                        $"Retained door {definition.Id} lacks clear closed-position support.");
                    door.Open = false;
                    Require(Map.Felucca.CanFit(definition.Opened.Point, 16, true, !_abundantRevision, false),
                        $"Retained door {definition.Id} has an obstructed opened hinge.");
                }
                finally
                {
                    door.Open = _previousOpenStates[door];
                }
            }
        }

        private void ValidateRetainedChestFits()
        {
            foreach (var definition in _manifest.Chests)
            {
                var chest = _previousChests[definition.Id];
                Require(FitsRetainedContainerEndpoint(definition.Position.Point, chest) && FitsSceneryEndpoint(definition.Approach.Point),
                    $"Retained chest {definition.Id} lacks clear native support or an opening approach.");
            }
        }

        private void InternalizeCompletedChests()
        {
            Require(_completedRevision && !_auditOnly, "Only the installed-map continuation may relocate containers.");
            // One new chest position is another retained chest's old position; vacate the four as one group.
            foreach (var definition in _manifest.Chests)
            {
                var chest = _previousChests[definition.Id];
                if (_previousChestStates[chest].Position != definition.Position.Point)
                {
                    chest.Internalize();
                }
            }
        }

        private void PlaceCompletedChests()
        {
            Require(_completedRevision && !_auditOnly, "Only the installed-map continuation may relocate containers.");
            foreach (var definition in _manifest.Chests)
            {
                var chest = _previousChests[definition.Id];
                if (_previousChestStates[chest].Position != definition.Position.Point)
                {
                    chest.MoveToWorld(definition.Position.Point, Map.Felucca);
                }
                ValidateRetainedChest(chest, definition);
            }
        }

        private void RestoreRetainedChests()
        {
            foreach (var entry in _previousChestStates)
            {
                if (!entry.Key.Deleted && (entry.Key.Map != Map.Felucca || entry.Key.Location != entry.Value.Position ||
                                          entry.Key.ItemID != entry.Value.ItemId))
                {
                    entry.Key.ItemID = entry.Value.ItemId;
                    entry.Key.MoveToWorld(entry.Value.Position, Map.Felucca);
                }
            }
        }

        private static Mobile CreateProbe()
        {
            // A virtual serial and Internal map prevent the survey actor from entering persistence or the live map.
            var probe = new Mobile(World.NewVirtual);
            probe.DefaultMobileInit();
            probe.Body = 0x190;
            probe.Player = true;
            probe.AccessLevel = AccessLevel.Player;
            return probe;
        }

        private bool CheckStep(Mobile probe, Point3D from, Point3D to)
        {
            probe.Location = from;
            _movementChecks++;
            return SurveyMovement(probe, from, DirectionTo(from, to), out var z) && z == to.Z;
        }

        private bool CheckBlockedStep(Mobile probe, Point3D from, Point3D to)
        {
            probe.Location = from;
            _movementChecks++;
            return !SurveyMovement(probe, from, DirectionTo(from, to), out _);
        }

        private bool SurveyMovement(Mobile probe, Point3D from, Direction direction, out int z)
        {
            var previousIgnore = Server.Movement.MovementImpl.IgnoreMovableImpassables;
            try
            {
                // Occupancy accepts only the recorded movable cover; surveying its removable obstruction must not move it.
                if (_retainedRuntime)
                {
                    Server.Movement.MovementImpl.IgnoreMovableImpassables = true;
                }
                return CalcMovement.CheckMovement(probe, Map.Felucca, from, direction, out z);
            }
            finally
            {
                Server.Movement.MovementImpl.IgnoreMovableImpassables = previousIgnore;
            }
        }

        private void CheckDoorCrossings()
        {
            var probe = CreateProbe();
            try
            {
                for (var count = 0; count < 4 && _cursor < DoorCount; count++, _cursor++)
                {
                    var door = _doors[_cursor];
                    var definition = _manifest.Doors[_cursor];
                    var first = definition.Approaches[0].Point;
                    var center = definition.Closed.Point;
                    var second = definition.Approaches[1].Point;
                    door.Open = false;
                    Require(CheckBlockedStep(probe, first, center) && CheckBlockedStep(probe, second, center),
                        $"Closed door {definition.Id} does not physically block both approaches.");
                    door.Open = true;
                    ValidateDoor(door, definition, true);
                    Require(CheckStep(probe, first, center) && CheckStep(probe, center, second) &&
                            CheckStep(probe, second, center) && CheckStep(probe, center, first),
                        $"Door {definition.Id} cannot be crossed directly in both directions when open.");
                    door.Open = false;
                    _movementResults.Add(new MovementResult(definition.Id, "door-crossing-both-directions", true));
                }
            }
            finally
            {
                probe.Delete();
                CloseDoors();
            }

            if (_cursor == DoorCount)
            {
                _cursor = 0;
                _phase = DeploymentPhase.StairLanes;
            }
        }

        private void CheckStairLanes()
        {
            var probe = CreateProbe();
            try
            {
                var lane = _manifest.StairLanes[_cursor];
                for (var i = 1; i < lane.Points.Length; i++)
                {
                    Require(CheckStep(probe, lane.Points[i - 1].Point, lane.Points[i].Point),
                        $"Staircase lane {lane.Id} fails ascending at tread {i}.");
                }

                for (var i = lane.Points.Length - 1; i > 0; i--)
                {
                    Require(CheckStep(probe, lane.Points[i].Point, lane.Points[i - 1].Point),
                        $"Staircase lane {lane.Id} fails descending at tread {i}.");
                }

                _movementResults.Add(new MovementResult(lane.Id, "stair-lane-both-directions", true));
                _cursor++;
            }
            finally
            {
                probe.Delete();
            }

            if (_cursor != _manifest.StairLanes.Length)
            {
                return;
            }

            _cursor = 0;
            _phase = DeploymentPhase.EscapeRoutes;
        }

        private void CheckEscapeRoutes()
        {
            if (_manifest.EscapeRoutes?.Length > 0)
            {
                var route = _manifest.EscapeRoutes[_cursor];
                var probe = CreateProbe();
                try
                {
                    Require(CheckStep(probe, route.From.Point, route.To.Point),
                        $"Gallery escape {route.Id} does not land on its clear ground aisle.");
                    _movementResults.Add(new MovementResult(route.Id,
                        route.Id == "R17-collapsed-floor-escape" ? "collapsed-floor-descent" : "gallery-ledge-descent", true));
                    _cursor++;
                }
                finally
                {
                    probe.Delete();
                }

                if (_cursor < _manifest.EscapeRoutes.Length)
                {
                    return;
                }
            }

            foreach (var module in _manifest.Modules)
            {
                _targets.Add(module.Start.Point);
                foreach (var approach in module.Approaches)
                {
                    _targets.Add(approach.Point.Point);
                }
            }

            foreach (var definition in _manifest.Doors)
            {
                _targets.Add(definition.Approaches[0].Point);
                _targets.Add(definition.Approaches[1].Point);
            }

            foreach (var chest in _manifest.Chests ?? [])
            {
                _targets.Add(chest.Approach.Point);
            }

            foreach (var route in _manifest.EscapeRoutes ?? [])
            {
                _targets.Add(route.From.Point);
                _targets.Add(route.To.Point);
            }
            if (_manifest.HiddenCellar is { } cellar)
            {
                foreach (var point in new[] { cellar.Hatch, cellar.Entry, cellar.ReturnRune, cellar.SurfaceReturn, cellar.CreaturePosition })
                {
                    _targets.Add(point.Point);
                }
            }

            _visited.Add(_manifest.Entry.Point);
            _frontier.Enqueue(_manifest.Entry.Point);
            _phase = DeploymentPhase.Reachability;
            _status = "Every staircase lane passes both directions; surveying all module and POI approaches from the north pad.";
        }

        private void CheckReachabilitySlice()
        {
            var probe = CreateProbe();
            try
            {
                foreach (var door in _doors)
                {
                    door.Open = true;
                }

                var watch = Stopwatch.StartNew();
                for (var count = 0; count < 64 && _frontier.Count > 0 && watch.ElapsedMilliseconds < 4; count++)
                {
                    var point = _frontier.Dequeue();
                    _targets.Remove(point);
                    AddTransportDestination(point);
                    foreach (var direction in _directions)
                    {
                        var next = point;
                        CalcMovement.Offset(direction, ref next);
                        if (!_manifest.Bounds.Rectangle.Contains(next))
                        {
                            continue;
                        }

                        probe.Location = point;
                        _movementChecks++;
                        if (!SurveyMovement(probe, point, direction, out var z) || z is < -25 or > 50)
                        {
                            continue;
                        }

                        next = new Point3D(next.X, next.Y, z);
                        if (_visited.Add(next))
                        {
                            Require(_visited.Count <= 100000, "Native reachability survey exceeded its bounded state count.");
                            _frontier.Enqueue(next);
                        }
                    }
                }
            }
            finally
            {
                probe.Delete();
                // Save snapshots run between loop callbacks and must observe retained states, never survey-opened doors.
                CloseDoors();
            }

            if (_targets.Count != 0 && _frontier.Count != 0)
            {
                return;
            }

            foreach (var module in _manifest.Modules)
            {
                Require(_visited.Contains(module.Start.Point), $"Module {module.Id} ({module.Name}) is unreachable from the north pad.");
                _movementResults.Add(new MovementResult(module.Id, "module-approach", true));
                foreach (var approach in module.Approaches)
                {
                    Require(_visited.Contains(approach.Point.Point), $"Module approach {approach.Id} is unreachable from the north pad.");
                    _movementResults.Add(new MovementResult($"{module.Id}/{approach.Id}", "room-or-poi-approach", true));
                }
            }

            Require(_targets.Count == 0, "A door approach is unreachable from the north walk-in pad.");
            foreach (var chest in _manifest.Chests ?? [])
            {
                Require(_visited.Contains(chest.Approach.Point), $"Chest {chest.Id} cannot be approached from the entrance.");
                _movementResults.Add(new MovementResult(chest.Id, "chest-opening-approach", true));
            }
            if (_ossuaryRevision)
            {
                ValidateOssuaryGround();
            }
            if (_manifest.HiddenCellar != null)
            {
                _movementResults.Add(new MovementResult("R10-hidden-descent", "explicit-hatch-transport", true));
                _movementResults.Add(new MovementResult("R10-hidden-return", "explicit-rune-transport", true));
            }
            ValidateAllDoors();
            _phase = DeploymentPhase.RequestSave;
            _status = _retainedRuntime ? "Native movement survey passed; retained door states restored, awaiting a normal world save." :
                "Native movement survey passed; all doors are closed and locked, awaiting a normal world save.";
        }

        private void ValidateOssuaryGround()
        {
            Require(_ossuaryRevision && !_auditOnly, "Only the installed-map Ossuary continuation may verify the restored ground.");
            var chest = _previousChests["M02-chest-1"];
            for (var x = 5316; x <= 5317; x++)
            {
                for (var y = 814; y <= 818; y++)
                {
                    var point = new Point3D(x, y, 0);
                    Require(FitsRetainedContainerEndpoint(point, chest),
                        $"The restored Ossuary ground lacks clear native support at {x},{y}.");
                    // A chest occupies one restored tile; its accessible opening approach proves that surface is usable.
                    var walkingPoint = point == chest.Location ? new Point3D(5316, 817, 0) : point;
                    Require(_visited.Contains(walkingPoint), $"The restored Ossuary ground is unreachable at {x},{y}.");
                    _movementResults.Add(new MovementResult($"M02-ground-{x}-{y}", "restored-ground-support", true));
                }
            }
        }

        private void AddTransportDestination(Point3D from)
        {
            if (_manifest.HiddenCellar is not { } cellar)
            {
                return;
            }
            var destination = from == cellar.Hatch.Point ? cellar.Entry :
                from == cellar.ReturnRune.Point ? cellar.SurfaceReturn : null;
            if (destination == null)
            {
                return;
            }
            Require(_retainedRuntime ? FitsSceneryEndpoint(destination.Point) : Map.Felucca.CanFit(destination.Point, 16, true, true),
                "An explicit cellar transport has an obstructed destination.");
            if (_visited.Add(destination.Point))
            {
                _frontier.Enqueue(destination.Point);
            }
        }

        private bool FitsSceneryEndpoint(Point3D point) => FitsRetainedContainerEndpoint(point, null);

        private bool FitsRetainedContainerEndpoint(Point3D point, Item excluded)
        {
            var map = Map.Felucca;
            if (map.CanFit(point, 16, true, false))
            {
                return true;
            }
            var cover = _cellarFeatures?.Cover;
            var ignoreCover = cover is { Deleted: false, Parent: null } && cover.Map == map && cover.AtWorldPoint(point.X, point.Y);
            var ignoreExcluded = excluded is { Deleted: false, Parent: null } && excluded.Map == map && excluded.AtWorldPoint(point.X, point.Y);
            if (!ignoreCover && !ignoreExcluded)
            {
                return false;
            }

            // Native support must be checked without treating the recorded container itself as an obstruction.
            var land = map.Tiles.GetLandTile(point.X, point.Y);
            map.GetAverageZ(point.X, point.Y, out var lowZ, out var averageZ, out _);
            var landImpassable = (TileData.LandTable[land.ID & TileData.MaxLandValue].Flags & TileFlag.Impassable) != 0;
            if (landImpassable && averageZ > point.Z && point.Z + 16 > lowZ)
            {
                return false;
            }
            var supported = !landImpassable && point.Z == averageZ && !land.Ignored;
            foreach (var tile in map.Tiles.GetStaticAndMultiTiles(point.X, point.Y))
            {
                var data = TileData.ItemTable[tile.ID & TileData.MaxItemValue];
                if ((data.Surface || data.Impassable) && tile.Z + data.CalcHeight > point.Z && point.Z + 16 > tile.Z)
                {
                    return false;
                }
                supported |= data.Surface && !data.Impassable && point.Z == tile.Z + data.CalcHeight;
            }
            foreach (var item in map.GetItemsAt<Item>(point))
            {
                if (ignoreCover && item == cover || ignoreExcluded && item == excluded || item is BaseMulti || item.ItemID > TileData.MaxItemValue)
                {
                    continue;
                }
                var data = item.ItemData;
                if ((data.Surface || data.Impassable || item.BlocksFit) &&
                    item.Z + data.CalcHeight > point.Z && point.Z + 16 > item.Z)
                {
                    return false;
                }
                supported |= data.Surface && !data.Impassable && !item.Movable && point.Z == item.Z + data.CalcHeight;
            }
            return supported;
        }

        private void CreateHiddenCellar()
        {
            Require(!_retainedRuntime, "A retained-runtime deployment cannot construct hidden-cellar objects.");
            if (_manifest.HiddenCellar is not { } cellar || _cellarFeatures != null)
            {
                return;
            }
            EventDungeonChest reward = null;
            for (var i = 0; i < _chests.Count; i++)
            {
                if (_manifest.Chests[i].Id == cellar.ChestId)
                {
                    reward = _chests[i];
                    break;
                }
            }
            Require(reward is RootboundCellarChest, "The secret cellar lacks its dedicated creation-only reward container.");
            _cellarFeatures = RootboundCellarFeature.Create(Map.Felucca, cellar.Hatch.Point, cellar.Entry.Point,
                cellar.ReturnRune.Point, cellar.SurfaceReturn.Point, cellar.CreaturePosition.Point, reward);
        }

        private void CloseDoors()
        {
            if ((_retainedRuntime || _failure != null) && _previousOpenStates.Count > 0)
            {
                foreach (var entry in _previousOpenStates)
                {
                    if (!entry.Key.Deleted)
                    {
                        entry.Key.Open = entry.Value;
                    }
                }

                return;
            }

            foreach (var door in _doors)
            {
                if (door.Deleted)
                {
                    continue;
                }

                door.Open = false;
                door.Locked = true;
                door.Link = null;
            }
        }

        private void ValidateAllDoors()
        {
            Require(_doors.Count == DoorCount, "The installation does not own exactly 71 event doors.");
            for (var i = 0; i < _doors.Count; i++)
            {
                ValidateDoor(_doors[i], _manifest.Doors[i], _retainedRuntime && _previousOpenStates[_doors[i]]);
            }
            if (_retainedRuntime)
            {
                ValidateRetainedCellar();
            }

            Require(_chests.Count == (_manifest.Chests?.Length ?? 0), "The installation chest count is incorrect.");
            for (var i = 0; i < _chests.Count; i++)
            {
                var chest = _chests[i];
                var definition = _manifest.Chests[i];
                Require(!chest.Deleted && chest.Parent == null && !chest.Movable && !chest.IsDecoContainer &&
                        chest.Map == Map.Felucca && chest.ItemID == definition.ItemId && chest.Location == definition.Position.Point,
                    $"Event chest {definition.Id} does not match its authorized placement.");
                if (_retainedRuntime)
                {
                    ValidateRetainedChest(chest, definition);
                }
            }
        }

        private static void ValidateDoor(EventDungeonDoor door, DoorDefinition definition, bool open) =>
            Require(!door.Deleted && door.Map == Map.Felucca && door.Open == open && door.Locked && !door.AutoClose &&
                    !door.Movable && door.Link == null && door.KeyValue == 0 && door.ClosedId == definition.ClosedId &&
                    door.OpenedId == definition.OpenedId && door.ItemID == (open ? definition.OpenedId : definition.ClosedId) &&
                    door.Location == (open ? definition.Opened.Point : definition.Closed.Point),
                $"Event door {definition.Id} does not match its authorized native geometry and event settings.");

        private void BeginSave(bool rollback)
        {
            _saveDirectoryTimestamp = Directory.Exists(World.SavePath) ? Directory.GetLastWriteTimeUtc(World.SavePath) : DateTime.MinValue;
            _snapshotSucceeded = false;
            _saveDeadline = Core.TickCount + 120000;
            _phase = rollback ? DeploymentPhase.AwaitRollbackSave : DeploymentPhase.AwaitSave;
            World.Save();
        }

        private void CheckSave(bool rollback)
        {
            if (World.WorldState != WorldState.Running)
            {
                return;
            }

            var published = _snapshotSucceeded && Directory.Exists(World.SavePath) &&
                            Directory.GetLastWriteTimeUtc(World.SavePath) > _saveDirectoryTimestamp;
            if (!published)
            {
                if (Core.TickCount - _saveDeadline >= 0)
                {
                    if (rollback)
                    {
                        WriteResult("install-result.json", CreateResult(false, "failed", _failure));
                        Finish(_retainedRuntime ? "Retained-runtime verification failed; original object placements restored, but rollback save could not be confirmed. Inspect the journal." :
                            "Installation failed; only its new doors were removed, but rollback save could not be confirmed. Inspect the journal.");
                    }
                    else
                    {
                        throw new InvalidOperationException("The normal world save was not confirmed; the marker remains pending.");
                    }
                }

                return;
            }

            if (rollback)
            {
                WriteResult("install-result.json", CreateResult(false, "failed", _failure, true));
                WriteResult("install-state.json", CreateResult(false, "rolled-back", _failure, true));
                Finish(_retainedRuntime ? "Retained-runtime verification failed; original object placements and door states restored, and rollback saved. Inspect install-result.json." :
                    "Installation failed; its new objects were removed, retained door states restored, and rollback saved. Inspect install-result.json.");
                return;
            }

            ValidateAllDoors();
            var result = CreateResult(true, "installed", null);
            WriteResult("installed.json", result);
            _installedResultWritten = true;
            WriteResult("install-result.json", result);
            WriteResult("install-state.json", result);
            RetireMarker("install");
            Finish(_retainedRuntime ? "Dungeon revision saved; event identities and contents retained, native movement survey passed." :
                "Installation saved; native geometry, doors, stairs, escapes and chest approaches all passed.");
        }

        private void RetireMarker(string phase)
        {
            var marker = JsonSerializer.Deserialize<Marker>(ReadSmallFile(phase + ".pending", 1024), _jsonOptions);
            Require(marker != null && EqualHash(marker.ManifestSha256, _manifestHash), "Deployment marker changed while the phase was running.");
            File.Move(DataPath(phase + ".pending"), DataPath(phase + ".completed"));
        }

        private Result CreateResult(bool success, string stage, string error, bool rollbackSaveConfirmed = false)
        {
            var serials = new DoorSerial[_doors.Count];
            for (var i = 0; i < _doors.Count; i++)
            {
                serials[i] = new DoorSerial(_manifest.Doors[i].Id, (uint)_doors[i].Serial);
            }

            var chestSerials = new DoorSerial[_chests.Count];
            for (var i = 0; i < _chests.Count; i++)
            {
                chestSerials[i] = new DoorSerial(_manifest.Chests[i].Id, (uint)_chests[i].Serial);
            }

            return new Result(1, _manifest.InstallationId, _manifestHash, _nativeDigest, success, stage, error, _staticCount,
                _doors.Count, _movementChecks, _visited.Count, DateTime.UtcNow, _manifest.Bounds, serials,
                _movementResults.ToArray(), rollbackSaveConfirmed, chestSerials,
                _retainedRuntime ? _retainedFeatureSerials : _cellarFeatures == null ? null :
                [new DoorSerial("R10-hidden-cover", (uint)_cellarFeatures.Cover.Serial),
                 new DoorSerial("R10-hidden-descent", (uint)_cellarFeatures.Hatch.Serial),
                 new DoorSerial("R10-hidden-return", (uint)_cellarFeatures.ReturnRune.Serial)],
                _retainedRuntime ? _retainedCreatureSerials :
                _cellarFeatures == null ? null : [new DoorSerial("R10-cellar-lich", (uint)_cellarFeatures.Lich.Serial)]);
        }

        private void Fail(Exception exception)
        {
            _logger.Error(exception, "Modular dungeon deployment failed in {Phase}", _phase);
            _failure ??= exception.Message;
            if (_retainedRuntime)
            {
                if (_completedRevision)
                {
                    RestoreRetainedChests();
                }
                else if (_ossuaryRevision)
                {
                    RestoreOssuaryChest();
                }
                CloseDoors();
                try
                {
                    if (_installedResultWritten)
                    {
                        WriteResult("installed.json", CreateResult(false, "failed", _failure));
                    }
                    WriteResult(_auditOnly ? "audit-result.json" : "install-result.json", CreateResult(false, "failed", _failure));
                    if (_doors.Count > 0)
                    {
                        WriteResult("install-state.json", CreateResult(false, "rollback-awaiting-save", _failure));
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Could not write retained-runtime dungeon failure result");
                }
                if (_doors.Count == 0 || _phase is DeploymentPhase.Rollback or DeploymentPhase.AwaitRollbackSave)
                {
                    Finish("Retained-runtime verification failed; original event object placements restored. Inspect the deployment receipt.");
                }
                else
                {
                    _phase = DeploymentPhase.Rollback;
                    _status = "Retained-runtime verification failed; original object placements and door states restored; a normal rollback save is pending.";
                }
                return;
            }
            if (_phase is DeploymentPhase.Rollback or DeploymentPhase.AwaitRollbackSave)
            {
                Finish("Rollback result could not be written; inspect install-state.json and the pending marker.");
                return;
            }

            foreach (var door in _doors)
            {
                if (!door.Deleted && !_previousOpenStates.ContainsKey(door))
                {
                    door.Delete();
                }
            }

            foreach (var chest in _chests)
            {
                if (!chest.Deleted && !_previousChestStates.ContainsKey(chest))
                {
                    chest.Delete();
                }
            }
            foreach (var entry in _previousChestStates)
            {
                if (!entry.Key.Deleted)
                {
                    entry.Key.ItemID = entry.Value.ItemId;
                    entry.Key.MoveToWorld(entry.Value.Position, Map.Felucca);
                }
            }
            _cellarFeatures?.Delete();
            _cellarFeatures = null;

            CloseDoors();

            try
            {
                WriteResult(_auditOnly ? "audit-result.json" : "install-result.json", CreateResult(false, "failed", _failure));
                if (_installedResultWritten)
                {
                    WriteResult("installed.json", CreateResult(false, "failed", _failure));
                }

                if (_doors.Count > 0)
                {
                    WriteResult("install-state.json", CreateResult(false, "rollback-awaiting-save", _failure));
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not write modular dungeon failure result");
            }

            if (_doors.Count == 0)
            {
                Finish(_failure);
            }
            else
            {
                _phase = DeploymentPhase.Rollback;
                _status = "Installation failed; its new objects were removed and a normal rollback save is pending.";
            }
        }

        private void Finish(string message)
        {
            _phase = DeploymentPhase.Finished;
            _status = message;
            _logger.Information("Modular dungeon deployment: {Status}", message);
            Stop();
            _job = null;
        }
    }
}
