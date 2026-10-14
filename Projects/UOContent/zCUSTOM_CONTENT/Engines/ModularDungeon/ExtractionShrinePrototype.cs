using System;
using System.IO;
using System.Text.Json;
using Server.Logging;

namespace Server.Engines.ModularDungeon;

public static class ExtractionShrinePrototype
{
    public const string PrototypeId = "echo-court-extraction-shrine-v1";
    public const string RedPrototypeId = "echo-court-red-moongate-v2";
    public const string CairnPrototypeId = "cairn-mines-extraction-flame-v1";
    public const string ManifestSha256 = "7a76c6c244f6a8a2a33aafa34a0371a08c0c87e551a0e2aa1a9d127a35a1095c";
    public static Point3D Point { get; } = new(5322, 910, 0);
    public static Point3D CairnPoint { get; } = new(5288, 867, 0);
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(ExtractionShrinePrototype));
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static TimerExecutionToken _startupTimer;

    public static void Configure()
    {
        CommandSystem.Register("ExtractionShrineInstall", AccessLevel.Administrator, InstallCommand);
        CommandSystem.Register("ExtractionShrineGo", AccessLevel.GameMaster, GoCommand);
        EventSink.ServerStarted += SchedulePending;
        EventSink.Shutdown += CancelPending;
    }

    // Deferred controller restoration is published on the first game-loop ticks, after ServerStarted.
    private static void SchedulePending() => Timer.StartTimer(TimeSpan.FromSeconds(1), InstallPending, out _startupTimer);
    private static void CancelPending() => _startupTimer.Cancel();

    private static void InstallCommand(CommandEventArgs args)
    {
        if (args.Length != 0)
        {
            args.Mobile.SendMessage("Use [ExtractionShrineInstall with no arguments.");
            return;
        }
        if (ExtractionEventRuntime.TryInstallEchoShrine(out _, out var created, out var error))
        {
            args.Mobile.SendMessage(created ? "The Echo Court extraction shrine is installed." : "The Echo Court shrine is already installed.");
        }
        else
        {
            args.Mobile.SendMessage(error);
        }
    }

    private static void GoCommand(CommandEventArgs args)
    {
        var destination = new Point3D(Point.X, Point.Y + 2, Point.Z);
        if (args.Length == 0 && Map.Felucca.CanFit(destination, 16, true, true))
        {
            args.Mobile.MoveToWorld(destination, Map.Felucca);
        }
        else
        {
            args.Mobile.SendMessage("The shrine viewing tile is occupied or the command has unexpected arguments.");
        }
    }

    internal static bool CanPlace(out string error) => CanPlace(Point, out error);

    internal static bool CanPlace(Point3D point, out string error)
    {
        foreach (var component in ExtractionEventShrine.ComponentLayout)
        {
            var ground = new Point3D(point.X + component.X, point.Y + component.Y, point.Z);
            if (!Map.Felucca.CanFit(ground, 16, true, true))
            {
                error = "The shrine footprint is obstructed; no existing objects will be removed.";
                return false;
            }
            foreach (var tile in Map.Felucca.Tiles.GetStaticTiles(ground.X, ground.Y))
            {
                var data = TileData.ItemTable[tile.ID & TileData.MaxItemValue];
                if (tile.Z >= point.Z && tile.Z < point.Z + 16 &&
                    (data.Height != 0 || !data.Surface || data.Impassable))
                {
                    error = "The shrine would overlap an existing native prop.";
                    return false;
                }
            }
            foreach (var item in Map.Felucca.GetItemsInRange<Item>(ground, 0))
            {
                if (!item.Deleted && item.Parent == null && Math.Abs(item.Z - point.Z) < 16)
                {
                    error = "The shrine would overlap an existing world item.";
                    return false;
                }
            }
        }
        if (!CheckStandingTiles(point))
        {
            error = "The shrine needs clear standing tiles on all four sides.";
            return false;
        }
        error = null;
        return true;
    }

    internal static bool CheckStandingTiles() => CheckStandingTiles(Point);

    internal static bool CheckStandingTiles(Point3D point) =>
        Map.Felucca.CanFit(new Point3D(point.X - 2, point.Y, point.Z), 16, true, false) &&
        Map.Felucca.CanFit(new Point3D(point.X + 2, point.Y, point.Z), 16, true, false) &&
        Map.Felucca.CanFit(new Point3D(point.X, point.Y - 2, point.Z), 16, true, false) &&
        Map.Felucca.CanFit(new Point3D(point.X, point.Y + 2, point.Z), 16, true, false);

    private static void InstallPending()
    {
        InstallOriginalPending();
        InstallRedPending();
        InstallCairnPending();
    }

    private static void InstallOriginalPending()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data", "ExtractionEvent");
        var path = Path.Combine(directory, "echo-shrine.pending");
        if (!File.Exists(path))
        {
            return;
        }
        try
        {
            if (new FileInfo(path).Length is <= 0 or > 1024)
            {
                throw new InvalidOperationException("The shrine installation marker is invalid.");
            }
            var marker = JsonSerializer.Deserialize<Marker>(File.ReadAllBytes(path), _jsonOptions);
            if (marker is not { PrototypeId: PrototypeId, ManifestSha256: ManifestSha256, DelaySeconds: 12 })
            {
                throw new InvalidOperationException("The marker does not authorize this Echo Court prototype.");
            }
            if (!ExtractionEventRuntime.TryInstallEchoShrine(out var shrine, out var created, out var error))
            {
                throw new InvalidOperationException(error);
            }
            var result = new
            {
                Success = true,
                PrototypeId,
                ShrineSerial = shrine.Serial.Value,
                ControllerSerial = shrine.Controller.Serial.Value,
                Created = created,
                Point.X,
                Point.Y,
                Point.Z,
                ComponentCount = shrine.Components.Count,
                DelaySeconds = 12,
                DamageInterrupts = false,
                ExistingExteriorExits = ExtractionEventRuntime.ExitPoints.Count
            };
            var resultPath = Path.Combine(directory, "echo-shrine-result.json");
            File.WriteAllBytes(resultPath + ".tmp", JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions));
            File.Move(resultPath + ".tmp", resultPath, true);
            // The operator retires authorization only after confirming a normal save's disk publication.
            _logger.Information("Echo Court extraction shrine ready: {Shrine}, {Components} components, created {Created}", shrine.Serial, shrine.Components.Count, created);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Echo Court extraction shrine installation refused; authorization remains pending");
        }
    }

    public sealed record Marker(string PrototypeId, string ManifestSha256, int DelaySeconds);

    private static void InstallRedPending()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data", "ExtractionEvent");
        var path = Path.Combine(directory, "echo-shrine-red.pending");
        if (!File.Exists(path))
        {
            return;
        }
        try
        {
            if (new FileInfo(path).Length is <= 0 or > 1024)
            {
                throw new InvalidOperationException("The red shrine update marker is invalid.");
            }
            var marker = JsonSerializer.Deserialize<RedMarker>(File.ReadAllBytes(path), _jsonOptions);
            if (marker is not { PrototypeId: RedPrototypeId, ManifestSha256: ManifestSha256, DelaySeconds: 12 } ||
                marker.ShrineSerial <= 0 || marker.ControllerSerial <= 0)
            {
                throw new InvalidOperationException("The marker does not authorize this red shrine update.");
            }
            if (!ExtractionEventRuntime.TryUpgradeEchoShrine(
                    (Serial)marker.ShrineSerial, (Serial)marker.ControllerSerial,
                    out var shrine, out var updated, out var error))
            {
                throw new InvalidOperationException(error);
            }
            var result = new
            {
                Success = true,
                PrototypeId = RedPrototypeId,
                ShrineSerial = shrine.Serial.Value,
                ControllerSerial = shrine.Controller.Serial.Value,
                Updated = updated,
                Point.X,
                Point.Y,
                Point.Z,
                ComponentCount = shrine.Components.Count,
                FlameHue = ExtractionEventShrine.RedFlameHue,
                PortalItemId = 0xF6C,
                PortalHue = ExtractionEventShrine.RedPortalHue,
                DelaySeconds = 12,
                DamageInterrupts = false,
                SharedGate = true,
                ImmediateExtraction = false,
                GateLifetime = "UntilMatchEnds",
                ExistingExteriorExits = ExtractionEventRuntime.ExitPoints.Count
            };
            var resultPath = Path.Combine(directory, "echo-shrine-red-result.json");
            File.WriteAllBytes(resultPath + ".tmp", JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions));
            File.Move(resultPath + ".tmp", resultPath, true);
            _logger.Information("Echo Court red moongate shrine ready: {Shrine}, controller {Controller}, flame hue {Hue}, updated {Updated}",
                shrine.Serial, shrine.Controller.Serial, ExtractionEventShrine.RedFlameHue, updated);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Echo Court red shrine update refused; authorization remains pending");
        }
    }

    public sealed record RedMarker(string PrototypeId, string ManifestSha256, int DelaySeconds, uint ShrineSerial, uint ControllerSerial);

    private static void InstallCairnPending()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data", "ExtractionEvent");
        var path = Path.Combine(directory, "cairn-flame.pending");
        if (!File.Exists(path))
        {
            return;
        }
        try
        {
            if (new FileInfo(path).Length is <= 0 or > 1024)
            {
                throw new InvalidOperationException("The Cairn flame installation marker is invalid.");
            }
            var marker = JsonSerializer.Deserialize<CairnMarker>(File.ReadAllBytes(path), _jsonOptions);
            if (marker is not { PrototypeId: CairnPrototypeId, ManifestSha256: ManifestSha256 } ||
                marker.ControllerSerial == 0 || ExtractionEventRuntime.Controller?.Serial.Value != marker.ControllerSerial)
            {
                throw new InvalidOperationException("The marker does not authorize this Cairn flame installation.");
            }
            if (!ExtractionEventRuntime.TryInstallCairnShrine(out var shrine, out var created, out var error))
            {
                throw new InvalidOperationException(error);
            }
            var result = new
            {
                Success = true,
                PrototypeId = CairnPrototypeId,
                ShrineSerial = shrine.Serial.Value,
                ControllerSerial = shrine.Controller.Serial.Value,
                Created = created,
                shrine.X,
                shrine.Y,
                shrine.Z,
                ComponentCount = shrine.Components.Count,
                FlameHue = ExtractionEventShrine.RedFlameHue,
                FlameHeight = ExtractionEventShrine.FlameHeight,
                DelaySeconds = ExtractionEventShrine.ChannelMilliseconds / 1000,
                DamageInterrupts = false,
                SharedGate = true,
                PortalHue = ExtractionEventShrine.RedPortalHue
            };
            var resultPath = Path.Combine(directory, "cairn-flame-result.json");
            File.WriteAllBytes(resultPath + ".tmp", JsonSerializer.SerializeToUtf8Bytes(result, _jsonOptions));
            File.Move(resultPath + ".tmp", resultPath, true);
            ExtractionEventRuntime.RestoreShrine(shrine, "Cairn Mines", shrine.Controller);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Cairn extraction flame installation refused; authorization remains pending");
        }
    }

    public sealed record CairnMarker(string PrototypeId, string ManifestSha256, uint ControllerSerial);
}
