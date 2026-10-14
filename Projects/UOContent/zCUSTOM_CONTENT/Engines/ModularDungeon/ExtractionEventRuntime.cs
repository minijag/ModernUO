using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Server.Collections;
using Server.Engines.TownStruggle;
using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.Engines.ModularDungeon;

public static class ExtractionEventRuntime
{
    private const string ManifestHash = "7966d9845810420764daaa4a68b0947e0cc87b7bb236127477fecf0b1a31ab14";
    private const string AbundantManifestHash = "7a76c6c244f6a8a2a33aafa34a0371a08c0c87e551a0e2aa1a9d127a35a1095c";
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(ExtractionEventRuntime));
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Point3D[] _exits = [new(5316, 807, 0), new(5255, 820, 0), new(5378, 868, 0)];
    private static Adapter _adapter;
    private static LobbyBuild _build;
    private static ExtractionLobbyRegion _recoveryLobby;
    private static ExtractionArenaRegion _recoveryArena;
    private static string _status = "Not installed.";
    private static bool _deploymentSuspended;
    private static ExtractionEventShrine _prototypeShrine;
    private static ExtractionEventShrine _cairnShrine;

    public static ExtractionEventController Controller { get; private set; }
    public static IReadOnlyList<Point3D> ExitPoints { get; } = Array.AsReadOnly(_exits);

    internal static bool CanOpenEventDoor(EventDungeonDoor door, out string error)
    {
        var managed = _adapter?.OwnsDoor(door) == true;
        if (_adapter == null && Controller != null)
        {
            foreach (var item in Controller.ManagedItems)
            {
                if (item is not ExtractionEventInstallation installation)
                {
                    continue;
                }
                foreach (var recordedDoor in installation.Doors)
                {
                    if (recordedDoor == door)
                    {
                        managed = true;
                        break;
                    }
                }
                if (managed)
                {
                    break;
                }
            }
        }
        if (!managed)
        {
            error = null;
            return true;
        }
        if (!HasBoundController(Controller) || Controller.Phase != ExtractionEventPhase.Active ||
            Core.Now >= Controller.MatchDeadline)
        {
            error = "Event doors can only be opened during an active match.";
            return false;
        }
        if (_adapter.IsExitDoor(door) && !Controller.ExtractionUnlocked)
        {
            error = "Exit doors remain sealed until 7 minutes into the match.";
            return false;
        }
        error = null;
        return true;
    }

    internal static bool CanUseShrine(ExtractionEventShrine shrine, Mobile player, out string error)
    {
        if (!OwnsShrine(shrine))
        {
            error = "This extraction shrine is unavailable or has been moved.";
            return false;
        }

        if (Controller.Phase != ExtractionEventPhase.Active ||
            player is not { Deleted: false, Player: true, Alive: true, NetState: not null } ||
            !Controller.IsParticipant(player) || !_adapter.IsInArena(player))
        {
            error = "The shrine can only extract living participants during an active match.";
            return false;
        }

        if (!Controller.ExtractionUnlocked)
        {
            if (Core.Now >= Controller.MatchDeadline)
            {
                Controller.Advance();
            }
            error = "Extraction flames become available 7 minutes into the match.";
            return false;
        }

        if (player.Map != shrine.Map || player.Z != shrine.Z || !player.InRange(shrine.Location, 2) || !player.InLOS(shrine.Location))
        {
            error = "Stand beside the shrine where you can see it.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool OwnsShrine(ExtractionEventShrine shrine)
    {
        if (_adapter?.PolicyReady != true || Controller is not { Deleted: false } ||
            shrine == null || !IsCachedShrine(shrine) || shrine.Deleted || shrine.Controller != Controller ||
            shrine.Parent != null || shrine.Map != Map.Felucca ||
            !shrine.ExactAppearance)
        {
            return false;
        }
        foreach (var item in Controller.ManagedItems)
        {
            if (item == shrine)
            {
                return true;
            }
        }
        return false;
    }

    internal static bool HasBoundController(ExtractionEventController controller) =>
        Controller == controller && controller is { Deleted: false, IsBound: true } && _adapter?.PolicyReady == true;

    private static bool IsCachedShrine(ExtractionEventShrine shrine) =>
        shrine == _prototypeShrine && shrine.Location == ExtractionShrinePrototype.Point ||
        shrine == _cairnShrine && shrine.Location == ExtractionShrinePrototype.CairnPoint;

    internal static bool TrySummonShrinePortal(ExtractionEventShrine shrine, Mobile player, out string error)
    {
        if (!CanUseShrine(shrine, player, out error))
        {
            return false;
        }
        if (Core.Now >= Controller.MatchDeadline)
        {
            Controller.Advance();
            error = "The match has ended; no moongate can be summoned.";
            return false;
        }
        try
        {
            return shrine.TryCreatePortal(out error);
        }
        catch (Exception exception)
        {
            Controller.PauseForRecovery("The extraction moongate could not be summoned safely.");
            _logger.Error(exception, "Extraction shrine moongate creation failed");
            error = "The event needs staff recovery.";
            return false;
        }
    }

    internal static bool IsOwnedActiveShrinePortal(ExtractionShrinePortal portal) =>
        portal is { Deleted: false } && OwnsShrine(portal.Shrine) &&
        portal.Shrine.Portal == portal && portal.Shrine.HasSummonedForRound(portal.RoundNumber) &&
        portal.RoundNumber == Controller.RoundNumber && portal.Parent == null && !portal.Movable &&
        MoongateAppearance.IsBlueBase(portal.ItemID) && portal.Hue == ExtractionEventShrine.RedPortalHue && portal.Light == LightType.Circle300 &&
        portal.Map == Map.Felucca && portal.Location == portal.Shrine.Location &&
        Controller.Phase == ExtractionEventPhase.Active && Core.Now < Controller.MatchDeadline;

    internal static bool IsValidShrinePortal(ExtractionShrinePortal portal) =>
        IsOwnedActiveShrinePortal(portal) && portal.Visible && Controller.ExtractionUnlocked;

    internal static bool CanUseShrinePortal(ExtractionShrinePortal portal, Mobile player, out string error)
    {
        if (!IsValidShrinePortal(portal))
        {
            if (Controller is { Phase: ExtractionEventPhase.Active } && HasBoundController(Controller) &&
                Core.Now >= Controller.MatchDeadline)
            {
                Controller.Advance();
            }
            error = "This extraction moongate is unavailable or its match has ended.";
            return false;
        }
        if (!CanUseShrine(portal.Shrine, player, out error))
        {
            return false;
        }
        if (player.Spell != null)
        {
            error = "Finish casting before using the extraction moongate.";
            return false;
        }
        if (!player.InRange(portal.Location, 1))
        {
            error = "Stand beside the red extraction moongate.";
            return false;
        }
        return true;
    }

    internal static bool TryUseShrinePortal(ExtractionShrinePortal portal, Mobile player, out string error) =>
        CanUseShrinePortal(portal, player, out error) && Controller.TryExtract(player, out error);

    internal static bool TryUpgradeEchoShrine(Serial expectedShrine, Serial expectedController,
        out ExtractionEventShrine shrine, out bool updated, out string error)
    {
        shrine = null;
        updated = false;
        if (_adapter?.PolicyReady != true || Controller is not { Deleted: false, IsBound: true } controller ||
            controller.Serial != expectedController || CurrentManifestHash != AbundantManifestHash ||
            controller.Phase != ExtractionEventPhase.Waiting || controller.Participants.Count != 0 ||
            controller.QueuedPlayers.Count != 0)
        {
            error = "The red shrine upgrade requires the pinned controller waiting without participants or a queue.";
            return false;
        }
        foreach (var item in controller.ManagedItems)
        {
            if (item is not ExtractionEventShrine candidate || candidate == _cairnShrine)
            {
                continue;
            }
            if (shrine != null || candidate.Deleted || candidate.Serial != expectedShrine ||
                candidate.Controller != controller || candidate.Parent != null || candidate.Map != Map.Felucca ||
                candidate.Location != ExtractionShrinePrototype.Point ||
                !candidate.ExactAppearance && !candidate.ExactLegacyAppearance && !candidate.ExactPreviousRedAppearance &&
                !candidate.ExactPreviousRuneAppearance)
            {
                error = "The pinned shrine identity or exact appearance does not match; no objects were changed.";
                shrine = null;
                return false;
            }
            shrine = candidate;
        }
        if (shrine == null)
        {
            error = "The pinned shrine is missing; no replacement will be created.";
            return false;
        }
        if (!shrine.ExactAppearance)
        {
            if (!shrine.UpgradeLegacyAppearance())
            {
                error = "The exact blue shrine could not be upgraded safely.";
                return false;
            }
            updated = true;
        }
        _prototypeShrine = shrine;
        error = null;
        return true;
    }

    internal static void NotifyShrineMovement(Mobile player, Point3D oldLocation)
    {
        _prototypeShrine?.NotifyPlayerMoved(player, oldLocation);
        _cairnShrine?.NotifyPlayerMoved(player, oldLocation);
    }

    internal static bool TryInstallEchoShrine(out ExtractionEventShrine shrine, out bool created, out string error) =>
        TryInstallShrine(ExtractionShrinePrototype.Point, false, out shrine, out created, out error);

    internal static bool TryInstallCairnShrine(out ExtractionEventShrine shrine, out bool created, out string error) =>
        TryInstallShrine(ExtractionShrinePrototype.CairnPoint, true, out shrine, out created, out error);

    private static bool TryInstallShrine(Point3D point, bool allowActive,
        out ExtractionEventShrine shrine, out bool created, out string error)
    {
        shrine = null;
        created = false;
        if (_adapter?.PolicyReady != true || Controller is not { Deleted: false, IsBound: true } controller ||
            CurrentManifestHash != AbundantManifestHash ||
            controller.Phase != ExtractionEventPhase.Waiting && !(allowActive && controller.Phase == ExtractionEventPhase.Active) ||
            controller.Participants.Count != 0 || controller.QueuedPlayers.Count != 0)
        {
            error = $"Flame installation requires the installed V35 event in a safe phase with no participants or queue " +
                    $"(ready={_adapter?.PolicyReady == true}, bound={Controller?.IsBound == true}, " +
                    $"phase={Controller?.Phase}, participants={Controller?.Participants.Count}, queue={Controller?.QueuedPlayers.Count}).";
            return false;
        }

        foreach (var item in controller.ManagedItems)
        {
            if (item is not ExtractionEventShrine candidate ||
                candidate.Location != point && IsCachedShrine(candidate))
            {
                continue;
            }

            if (shrine != null || candidate.Deleted || candidate.Controller != controller ||
                candidate.Parent != null || candidate.Map != Map.Felucca ||
                candidate.Location != point || !candidate.ExactAppearance)
            {
                error = "The existing shrine ownership or appearance needs staff recovery; it will not be replaced.";
                shrine = null;
                return false;
            }
            shrine = candidate;
        }

        if (shrine != null)
        {
            CacheShrine(shrine);
            error = null;
            return true;
        }

        if (!ExtractionShrinePrototype.CanPlace(point, out error))
        {
            return false;
        }

        var pending = new ExtractionEventShrine(controller);
        try
        {
            pending.MoveToWorld(point, Map.Felucca);
            if (!pending.ExactAppearance || !ExtractionShrinePrototype.CheckStandingTiles(point))
            {
                error = "The shrine would obstruct its standing tiles.";
                return false;
            }
            controller.AddManagedItem(pending);
            shrine = pending;
            CacheShrine(shrine);
            created = true;
            error = null;
            return true;
        }
        finally
        {
            if (!created)
            {
                pending.Delete();
            }
        }
    }

    private static void CacheShrine(ExtractionEventShrine shrine)
    {
        if (shrine.Location == ExtractionShrinePrototype.Point)
        {
            _prototypeShrine = shrine;
        }
        else
        {
            _cairnShrine = shrine;
        }
        shrine.SyncFlameVisibility();
    }

    public static bool TryGetDeploymentOwnership(out ExtractionDeploymentOwnership ownership, out string error)
    {
        ownership = null;
        if (!_deploymentSuspended || _adapter == null || Controller is not { Deleted: false } controller ||
            controller.Phase is not (ExtractionEventPhase.Waiting or ExtractionEventPhase.Active) ||
            controller.Participants.Count != 0 || controller.QueuedPlayers.Count != 0)
        {
            error = "Dungeon deployment requires a suspended, bound event with no participants, queue or preparation in progress.";
            return false;
        }

        try
        {
            ownership = new ExtractionDeploymentOwnership(controller, _adapter, _adapter.DeploymentInstallation);
            return ownership.IsValid(out error);
        }
        catch (Exception exception)
        {
            error = exception.Message;
            ownership = null;
            return false;
        }
    }

    public sealed class ExtractionDeploymentOwnership
    {
        private readonly ExtractionEventController _controller;
        private readonly object _adapterAtCapture;
        private readonly ExtractionEventInstallation _installation;
        private readonly HashSet<BaseCreature> _creatureReferences;
        private readonly HashSet<Item> _managedReferences;
        private readonly HashSet<Item> _lootReferences;
        private readonly RootboundCellarLich _cellarLich;

        internal ExtractionDeploymentOwnership(ExtractionEventController controller, object adapter, ExtractionEventInstallation installation)
        {
            _controller = controller;
            _adapterAtCapture = adapter;
            _installation = installation;
            Require(controller.OwnedCreatures.Count <= 128 && controller.ManagedItems.Count <= 1536 && controller.OwnedLoot.Count <= 16384,
                "The extraction ownership snapshot exceeds its safe deployment bound.");
            _creatureReferences = new HashSet<BaseCreature>(controller.OwnedCreatures);
            _managedReferences = new HashSet<Item>(controller.ManagedItems);
            _lootReferences = new HashSet<Item>(controller.OwnedLoot);
            _cellarLich = installation.Features.Lich;
            Require(SameReferences(controller.OwnedCreatures, _creatureReferences) &&
                    SameReferences(controller.ManagedItems, _managedReferences) && SameReferences(controller.OwnedLoot, _lootReferences),
                "The extraction ownership record contains duplicate references.");
        }

        public bool IsValid(out string error)
        {
            if (!_deploymentSuspended || Controller != _controller || !ReferenceEquals(_adapter, _adapterAtCapture) ||
                _controller is { Deleted: true } || _installation is not { Deleted: false, LobbyComplete: true } ||
                _installation.Controller != _controller ||
                _controller.Phase is not (ExtractionEventPhase.Waiting or ExtractionEventPhase.Active) ||
                _controller.Participants.Count != 0 || _controller.QueuedPlayers.Count != 0 ||
                !SameReferences(_controller.OwnedCreatures, _creatureReferences) ||
                !SameReferences(_controller.ManagedItems, _managedReferences) || !SameReferences(_controller.OwnedLoot, _lootReferences) ||
                _installation.Features.Lich != _cellarLich)
            {
                error = "The suspended extraction ownership or idle deployment state has changed.";
                return false;
            }

            foreach (var creature in _creatureReferences)
            {
                if (creature is { Deleted: false } && !SafeCreature(creature))
                {
                    error = "A tracked extraction creature is controlled, summoned or attached to a spawner.";
                    return false;
                }
            }

            error = null;
            return true;
        }

        public bool OwnsDeploymentEntity(IEntity entity) => entity switch
        {
            BaseCreature creature => (_creatureReferences.Contains(creature) || creature == _cellarLich) && SafeCreature(creature),
            ExtractionEventGate gate => _managedReferences.Contains(gate) && gate.Kind == ExtractionEventGateKind.Extraction &&
                                        gate.Controller == _controller && _adapter?.OwnsGate(gate) == true,
            Item { Deleted: false, Parent: null } item => _lootReferences.Contains(item),
            _ => false
        };

        private static bool SafeCreature(BaseCreature creature) => creature is
            { Deleted: false, Controlled: false, Summoned: false, Spawner: null };

        private static bool SameReferences<T>(IReadOnlyList<T> current, HashSet<T> captured) where T : class
        {
            if (current.Count != captured.Count)
            {
                return false;
            }
            foreach (var reference in current)
            {
                if (!captured.Contains(reference))
                {
                    return false;
                }
            }
            return true;
        }
    }

    public static void Configure()
    {
        var deploymentDirectory = Path.Combine(Core.BaseDirectory, "Data", "ModularDungeonV35");
        _deploymentSuspended = File.Exists(Path.Combine(deploymentDirectory, "audit.pending")) ||
                               File.Exists(Path.Combine(deploymentDirectory, "install.pending"));
        ExtractionEventController.Loaded += Restore;
        ExtractionEventController.Removed += Remove;
        ExtractionDungeonReset.ManagedChestLookup = chest => _adapter?.OwnsChest(chest) == true;
        ExtractionEventDeath.ControllerLookup = () => Controller;
        CommandSystem.Register("ExtractionInstall", AccessLevel.Administrator, Install);
        CommandSystem.Register("ExtractionStart", AccessLevel.Administrator, Start);
        CommandSystem.Register("ExtractionStop", AccessLevel.Administrator, Stop);
        CommandSystem.Register("ExtractionStatus", AccessLevel.Player, Status);
        EventSink.Shutdown += Shutdown;
    }

    public static bool TryUseGate(ExtractionEventGate gate, Mobile player, out string error)
    {
        if (_deploymentSuspended || ExtractionLobbyDecoration.IsBusy || _adapter == null || gate is not { Deleted: false } || gate.Controller != Controller || !_adapter.OwnsGate(gate))
        {
            error = "This event gate is unavailable or has been moved.";
            return false;
        }

        if (!gate.CanUseGate(player, out error))
        {
            return false;
        }

        switch (gate.Kind)
        {
            case ExtractionEventGateKind.Entry:
            {
                return Controller.TryJoinLobby(player, out error);
            }
            case ExtractionEventGateKind.LobbyReturn when _adapter.IsInLobby(player) && !Controller.IsParticipant(player):
            {
                return _adapter.MoveToTown(player, out error);
            }
            case ExtractionEventGateKind.Extraction:
            {
                return Controller.TryExtract(player, out error);
            }
            default:
            {
                error = "You cannot use this gate from here.";
                return false;
            }
        }
    }

    private static void Install(CommandEventArgs args)
    {
        if (args.Length != 0 || Controller != null || _build != null)
        {
            args.Mobile.SendMessage("The event is already installed or being prepared. Use [ExtractionStatus.");
            return;
        }

        try
        {
            BeginInstall();
            args.Mobile.SendMessage("Preparing the lobby and Galven gate in bounded steps. Use [ExtractionStatus.");
        }
        catch (Exception exception)
        {
            _status = exception.Message;
            _logger.Error(exception, "Extraction lobby installation was refused");
            args.Mobile.SendMessage($"Extraction installation refused: {exception.Message}");
        }
    }

    public static void Initialize()
    {
        var marker = Path.Combine(Core.BaseDirectory, "Data", "ExtractionEvent", "install.pending");
        if (!File.Exists(marker) || Controller != null)
        {
            return;
        }

        try
        {
            Require(new FileInfo(marker).Length is > 0 and <= 1024, "The extraction installation marker is invalid.");
            var authorization = JsonSerializer.Deserialize<ModularDungeonDeployment.Marker>(File.ReadAllBytes(marker), _jsonOptions);
            Require(authorization?.ManifestSha256 == CurrentManifestHash, "The extraction marker does not authorize this dungeon.");
            BeginInstall();
        }
        catch (Exception exception)
        {
            _status = exception.Message;
            _logger.Error(exception, "Extraction startup installation was refused");
        }
    }

    private static void BeginInstall()
    {
        Require(!_deploymentSuspended, "The dungeon deployment must complete before the extraction lobby can be installed.");
        Require(Controller == null && _build == null, "The event already has an installation or build in progress.");
        var manifest = ReadManifest();
        var receipt = ReadJson<ModularDungeonDeployment.Result>("installed.json");
        Require(receipt is { Success: true, Stage: "installed", Doors.Length: 71, Chests.Length: 100 } &&
                receipt.InstallationId == manifest.InstallationId && receipt.ManifestSha256 == CurrentManifestHash,
            "A completed pinned dungeon receipt is required.");
        var doors = new EventDungeonDoor[71];
        var chests = new EventDungeonChest[100];
        for (var index = 0; index < doors.Length; index++)
        {
            Require(receipt.Doors[index].Id == manifest.Doors[index].Id &&
                    World.FindItem((Serial)receipt.Doors[index].Serial) is EventDungeonDoor { Deleted: false },
                "A recorded dungeon door is missing or out of order.");
            doors[index] = (EventDungeonDoor)World.FindItem((Serial)receipt.Doors[index].Serial);
        }

        for (var index = 0; index < chests.Length; index++)
        {
            Require(receipt.Chests[index].Id == manifest.Chests[index].Id &&
                    World.FindItem((Serial)receipt.Chests[index].Serial) is EventDungeonChest { Deleted: false },
                "A recorded dungeon chest is missing or out of order.");
            chests[index] = (EventDungeonChest)World.FindItem((Serial)receipt.Chests[index].Serial);
        }

        var features = new RootboundCellarFeature.Features
        {
            Cover = FindRecordedItem<WoodenBox>(receipt.Features, "R10-hidden-cover", true),
            Hatch = FindRecordedItem<EventDungeonHiddenHatch>(receipt.Features, "R10-hidden-descent"),
            ReturnRune = FindRecordedItem<RootboundCellarReturnRune>(receipt.Features, "R10-hidden-return"),
            Lich = FindRecordedMobile(receipt.Creatures, "R10-cellar-lich")
        };
        _build = new LobbyBuild(manifest, doors, chests, features);
        _build.Begin();
    }

    private static T FindRecordedItem<T>(ModularDungeonDeployment.DoorSerial[] records, string id, bool optional = false) where T : Item
    {
        if (records != null)
        {
            foreach (var record in records)
            {
                if (record.Id == id)
                {
                    var item = World.FindItem((Serial)record.Serial);
                    Require(item is T || optional && item == null, "A recorded cellar feature has the wrong type.");
                    return item as T;
                }
            }
        }

        Require(optional, "A recorded cellar transport is missing.");
        return null;
    }

    private static RootboundCellarLich FindRecordedMobile(ModularDungeonDeployment.DoorSerial[] records, string id)
    {
        if (records != null)
        {
            foreach (var record in records)
            {
                if (record.Id == id)
                {
                    var mobile = World.FindMobile((Serial)record.Serial);
                    Require(mobile == null || mobile is RootboundCellarLich, "The recorded cellar creature has the wrong type.");
                    return mobile as RootboundCellarLich;
                }
            }
        }

        return null;
    }

    private static ModularDungeonDeployment.Manifest ReadManifest()
    {
        var bytes = ReadBoundedFile("install-manifest.json");
        return ValidateInstalledManifest(bytes, ReadJson<ModularDungeonDeployment.Result>("installed.json"), AbundantInstalled);
    }

    internal static ModularDungeonDeployment.Manifest ValidateInstalledManifest(byte[] bytes, ModularDungeonDeployment.Result receipt, bool abundant)
    {
        Require(bytes is { Length: > 0 and <= 256 * 1024 }, "A bounded installed dungeon manifest is required.");
        var expectedHash = abundant ? AbundantManifestHash : ManifestHash;
        Require(Convert.ToHexStringLower(SHA256.HashData(bytes)) == expectedHash, "The installed dungeon manifest has changed.");
        var manifest = JsonSerializer.Deserialize<ModularDungeonDeployment.Manifest>(bytes, _jsonOptions);
        Require(ExtractionDungeonLayout.IsSupportedManifest(manifest), "The installed dungeon has no pinned extraction layout.");
        Require(receipt is { Success: true, Stage: "installed", Doors.Length: 71, Chests.Length: 100 } &&
                receipt.InstallationId == manifest.InstallationId && receipt.ManifestSha256 == expectedHash &&
                receipt.NativeSha256 == manifest.InstalledNativeSha256 && receipt.StaticCount == manifest.StaticCount,
            "The extraction layout requires its successful matching dungeon receipt.");
        return manifest;
    }

    private static T ReadJson<T>(string name) => JsonSerializer.Deserialize<T>(ReadBoundedFile(name), _jsonOptions);

    private static bool AbundantInstalled => File.Exists(Path.Combine(Core.BaseDirectory, "Data", "ModularDungeonV35", "installed.json"));
    private static string CurrentManifestHash => AbundantInstalled ? AbundantManifestHash : ManifestHash;

    private static byte[] ReadBoundedFile(string name)
    {
        var path = Path.Combine(Core.BaseDirectory, "Data", AbundantInstalled ? "ModularDungeonV35" : "ModularDungeonV17", name);
        Require(new FileInfo(path).Length is > 0 and <= 256 * 1024, "A bounded installed dungeon file is required.");
        var bytes = File.ReadAllBytes(path);
        Require(bytes.Length <= 256 * 1024, "The installed dungeon file grew beyond its bound.");
        return bytes;
    }

    private static void Restore(ExtractionEventController controller)
    {
        try
        {
            Require(Controller == null || Controller == controller, "Multiple extraction controllers need staff recovery.");
            Controller = controller;
            ExtractionEventInstallation installation = null;
            foreach (var item in controller.ManagedItems)
            {
                if (item is ExtractionEventInstallation candidate)
                {
                    Require(installation == null, "The event has duplicate ownership records.");
                    installation = candidate;
                }
            }

            Require(installation is { Deleted: false, LobbyComplete: true } && installation.Controller == controller,
                "The event lobby installation is incomplete; it will not be replayed automatically.");
            Bind(controller, installation, ReadManifest());
        }
        catch (Exception exception)
        {
            _status = exception.Message;
            controller.PauseForRecovery("The installed event needs staff recovery before it can run.");
            _recoveryLobby ??= new ExtractionLobbyRegion();
            _recoveryArena ??= new ExtractionArenaRegion(controller);
            _recoveryLobby.Register();
            _recoveryArena.Register();
            _logger.Error(exception, "Extraction event recovery was refused");
        }
    }

    private static void Bind(ExtractionEventController controller, ExtractionEventInstallation installation, ModularDungeonDeployment.Manifest manifest)
    {
        _adapter?.Unregister();
        _recoveryLobby?.Unregister();
        _recoveryArena?.Unregister();
        _recoveryLobby = null;
        _recoveryArena = null;
        _adapter = new Adapter(controller, installation, manifest);
        Controller = controller;
        _prototypeShrine = null;
        _cairnShrine = null;
        foreach (var item in controller.ManagedItems)
        {
            if (item is ExtractionEventShrine shrine && !shrine.Deleted && shrine.Controller == controller &&
                shrine.Map == Map.Felucca && shrine.Parent == null &&
                (shrine.Location == ExtractionShrinePrototype.Point || shrine.Location == ExtractionShrinePrototype.CairnPoint) &&
                (shrine.ExactAppearance || shrine.ExactLegacyAppearance || shrine.ExactPreviousRedAppearance ||
                    shrine.ExactPreviousRuneAppearance))
            {
                if (shrine.Location == ExtractionShrinePrototype.Point ? _prototypeShrine != null : _cairnShrine != null)
                {
                    throw new InvalidOperationException("The event has duplicate extraction shrine prototypes.");
                }
                CacheShrine(shrine);
            }
        }
        // Exact native assemblies can be upgraded without replacing any existing component identity.
        if (_prototypeShrine != null && !_prototypeShrine.ExactAppearance)
        {
            Require(_prototypeShrine.UpgradeLegacyAppearance(), "The recognized extraction shrine could not be upgraded safely.");
        }
        if (_cairnShrine != null && !_cairnShrine.ExactAppearance)
        {
            Require(_cairnShrine.UpgradeLegacyAppearance(), "The recognized Cairn shrine could not be upgraded safely.");
        }
        _adapter.Register();
        controller.BindIntegration(_adapter);
        RestoreShrine(_prototypeShrine, "Echo Court", controller);
        RestoreShrine(_cairnShrine, "Cairn Mines", controller);
        _status = _deploymentSuspended
            ? "Dungeon installation in progress; event operations are suspended until the next ordinary startup."
            : "Installed. Fifteen-minute rounds; extract to the lobby or die and return as a ghost.";
    }

    internal static void RestoreShrine(ExtractionEventShrine shrine, string room, ExtractionEventController controller)
    {
        if (shrine != null)
        {
            shrine.OnEventPhaseChanged(controller);
            _logger.Information("Extraction flame restored: {Room}, {Shrine}, {Components} components, controller {Controller}, X {X}, Y {Y}, flame hue {Hue}, flame height {Height}, channel milliseconds {Milliseconds}",
                room, shrine.Serial, shrine.Components.Count, controller.Serial, shrine.X, shrine.Y,
                shrine.Components[9].Hue, shrine.Components[9].Offset.Z, ExtractionEventShrine.ChannelMilliseconds);
        }
    }

    private static void Remove(ExtractionEventController controller)
    {
        if (Controller == controller)
        {
            _prototypeShrine?.OwnerRemoved();
            _cairnShrine?.OwnerRemoved();
            Shutdown();
            Controller = null;
        }
    }

    private static void Shutdown()
    {
        _prototypeShrine?.Suspend();
        _cairnShrine?.Suspend();
        _prototypeShrine = null;
        _cairnShrine = null;
        _build?.Cancel();
        _build = null;
        _adapter?.Unregister();
        _adapter = null;
        _recoveryLobby?.Unregister();
        _recoveryArena?.Unregister();
        _recoveryLobby = null;
        _recoveryArena = null;
    }

    private static void Start(CommandEventArgs args)
    {
        if (Controller == null)
        {
            args.Mobile.SendMessage("The extraction event is not installed.");
        }
        else if (!Controller.TryStart(out var error))
        {
            args.Mobile.SendMessage(error);
        }
        else
        {
            if (!PvpEventRotationRuntime.IsEnabled)
            {
                Controller.ConfigureWaiting(TimeSpan.FromMinutes(15), true);
            }
        }
    }

    private static void Stop(CommandEventArgs args)
    {
        if (Controller == null)
        {
            args.Mobile.SendMessage("The extraction event is not installed.");
        }
        else if (!Controller.TryStop(out var error))
        {
            args.Mobile.SendMessage(error);
        }
    }

    private static void Status(CommandEventArgs args)
    {
        args.Mobile.SendMessage($"Extraction event: {_status}");
        if (Controller is { Deleted: false } controller)
        {
            args.Mobile.SendMessage($"Round {controller.RoundNumber}; {controller.Phase}; {controller.Participants.Count} participants.");
            if (controller.Phase == ExtractionEventPhase.Active)
            {
                var remaining = controller.MatchDeadline - Core.Now;
                args.Mobile.SendMessage($"Time remaining: {Math.Max(0, (int)remaining.TotalSeconds)} seconds.");
            }
            else if (controller.Phase == ExtractionEventPhase.Waiting && controller.AutomaticRounds)
            {
                args.Mobile.SendMessage($"Next match in {Math.Max(0, (int)(controller.WaitingDeadline - Core.Now).TotalSeconds)} seconds.");
            }
            else if (controller.Phase == ExtractionEventPhase.Faulted)
            {
                args.Mobile.SendMessage("The event is paused for staff recovery.");
            }
        }
    }

    private static void Require(bool condition, string error)
    {
        if (!condition)
        {
            throw new InvalidOperationException(error);
        }
    }

    private sealed class Adapter : IExtractionEventIntegration
    {
        private readonly ExtractionEventController _controller;
        private readonly ExtractionEventInstallation _installation;
        private readonly ModularDungeonDeployment.Manifest _manifest;
        private readonly IReadOnlyList<ExtractionRoomSpawn> _rooms;
        private readonly IReadOnlyList<ExtractionModuleSpawn> _moduleSpawns;
        private readonly HashSet<EventDungeonChest> _chests;
        private readonly HashSet<EventDungeonDoor> _doors;
        private readonly HashSet<EventDungeonDoor> _exitDoors = [];
        private readonly ExtractionLobbyRegion _lobby = new();
        private readonly ExtractionArenaRegion _arena;
        private bool _preparedCreaturesFrozen;
        private int _activationCursor;

        public Adapter(ExtractionEventController controller, ExtractionEventInstallation installation, ModularDungeonDeployment.Manifest manifest)
        {
            _controller = controller;
            _installation = installation;
            _manifest = manifest;
            _rooms = ExtractionDungeonLayout.RoomsFor(manifest);
            _moduleSpawns = manifest.BlueprintSha256 == ExtractionDungeonLayout.AbundantBlueprintSha256
                ? ExtractionDungeonLayout.AbundantModuleSpawns
                : ExtractionDungeonLayout.ModuleSpawns;
            Require(installation.Doors.Count == 71 && manifest.Doors.Length == installation.Doors.Count &&
                    installation.Chests.Count == 100, "The event ownership record is incomplete.");
            _chests = new HashSet<EventDungeonChest>(installation.Chests);
            _doors = new HashSet<EventDungeonDoor>(installation.Doors);
            // The saved door order matches the pinned manifest; an open leaf's position does not identify its role.
            for (var index = 0; index < manifest.Doors.Length; index++)
            {
                if (manifest.Doors[index].Id is "D000" or "D036" or "D058")
                {
                    _exitDoors.Add(installation.Doors[index]);
                }
            }
            Require(_exitDoors.Count == 3, "The event must have exactly three recorded exit doors.");
            _arena = new ExtractionArenaRegion(controller);
        }

        public DateTime UtcNow => Core.Now;
        public ExtractionEventInstallation DeploymentInstallation => _installation;
        public bool PolicyReady => !_deploymentSuspended && !ExtractionLobbyDecoration.IsBusy && !_installation.Deleted && _installation.LobbyComplete && _controller.RecoveryStorage is { Deleted: false };
        public IReadOnlyList<ExtractionModuleSpawn> ModuleSpawns => _moduleSpawns;
        public bool IsInLobby(Mobile player) => player.Map == Map.Felucca && player.Z == 0 && ExtractionDungeonLayout.LobbyBounds.Contains(player.Location);
        public bool IsInArena(Mobile player) => player.Map == Map.Felucca && ExtractionDungeonLayout.DungeonArea.Contains(player.Location);
        public bool OwnsChest(EventDungeonChest chest) => _chests.Contains(chest);
        public bool OwnsDoor(EventDungeonDoor door) => _doors.Contains(door);
        public bool IsExitDoor(EventDungeonDoor door) => _exitDoors.Contains(door);
        public void Register()
        {
            _lobby.Register();
            _arena.Register();
        }

        public void Unregister()
        {
            _lobby.Unregister();
            _arena.Unregister();
        }

        public bool OwnsGate(ExtractionEventGate gate)
        {
            if (gate.Map != Map.Felucca || gate.Parent != null)
            {
                return false;
            }

            var owned = false;
            foreach (var item in _controller.ManagedItems)
            {
                if (item == gate)
                {
                    owned = true;
                    break;
                }
            }
            if (!owned)
            {
                return false;
            }

            return gate.Kind switch
            {
                ExtractionEventGateKind.Entry => gate.Location == ExtractionDungeonLayout.GatePoint,
                ExtractionEventGateKind.LobbyReturn => gate.Location == ExtractionDungeonLayout.LobbyReturnPoint ||
                                                       gate.Location == ExtractionLobbyDecoration.OriginalReturnPoint,
                ExtractionEventGateKind.Extraction => Array.IndexOf(_exits, gate.Location) >= 0,
                _ => false
            };
        }

        public Mobile[] SnapshotLobbyPlayers()
        {
            using var players = PooledRefList<Mobile>.Create();
            foreach (var player in Map.Felucca.GetMobilesInBounds<PlayerMobile>(ExtractionDungeonLayout.LobbyBounds))
            {
                if (IsInLobby(player))
                {
                    players.Add(player);
                }
            }

            var snapshot = new Mobile[players.Count];
            for (var index = 0; index < snapshot.Length; index++)
            {
                snapshot[index] = players[index];
            }

            return snapshot;
        }

        public bool TryMoveToLobby(Mobile player, out string error)
        {
            if (!MoveNear(player, ExtractionDungeonLayout.LobbyEntry, ExtractionDungeonLayout.LobbyBounds, out error))
            {
                return false;
            }

            player.SendMessage("You are waiting for the next extraction match. All carried and equipped items are at risk inside.");
            player.SendMessage("The blue moongate returns to Galven. Use [ExtractionStatus to check the countdown.");
            return true;
        }

        public bool MoveToTown(Mobile player, out string error) =>
            MoveNear(player, ExtractionDungeonLayout.TownReturn, new Rectangle2D(1313, 1376, 9, 9), out error);

        public bool TryAdmit(ExtractionEventController controller, Mobile player, ExtractionModuleSpawn spawn, int moduleCycle, out string error)
        {
            var row = spawn.ModuleId / 5;
            var column = spawn.ModuleId % 5;
            var bounds = new Rectangle2D(5257 + column * 24, 809 + row * 24, 23, 23);
            if (!MoveNear(player, spawn.Location, bounds, out error))
            {
                return false;
            }

            player.SendMessage($"Round {controller.RoundNumber}: {_rooms[spawn.ModuleId].Name}. Wait for the match to begin.");
            return true;
        }

        public bool TryFinish(ExtractionEventController controller, Mobile player, ExtractionFinishReason reason, out string error)
        {
            if (reason == ExtractionFinishReason.TimeExpired && player.Alive)
            {
                player.SendMessage("Time expired. You did not extract.");
                player.Kill();
                if (player.Alive)
                {
                    error = "An expired participant could not be killed; staff recovery is required.";
                    return false;
                }
            }

            if (reason == ExtractionFinishReason.Death && player.Alive)
            {
                error = "Death recovery cannot relocate a living participant.";
                return false;
            }

            if (!MoveNear(player, ExtractionDungeonLayout.LobbyEntry, ExtractionDungeonLayout.LobbyBounds, out error))
            {
                return false;
            }

            player.SendMessage(reason == ExtractionFinishReason.Extracted
                ? "You extracted successfully. Your carried loot is safe in the lobby."
                : "You have returned to the lobby. Use the blue moongate to return to Galven.");
            return true;
        }

        private static bool MoveNear(Mobile player, Point3D anchor, Rectangle2D bounds, out string error)
        {
            for (var radius = 0; radius <= 11; radius++)
            {
                for (var dy = -radius; dy <= radius; dy++)
                {
                    for (var dx = -radius; dx <= radius; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius)
                        {
                            continue;
                        }

                        var point = new Point3D(anchor.X + dx, anchor.Y + dy, anchor.Z);
                        if (!bounds.Contains(point) || !Map.Felucca.CanFit(point, 16, true, true) ||
                            !Map.Felucca.LineOfSight(new Point3D(anchor.X, anchor.Y, anchor.Z + 14), new Point3D(point.X, point.Y, point.Z + 14)))
                        {
                            continue;
                        }

                        BaseCreature.TeleportPets(player, point, Map.Felucca);
                        player.Combatant = null;
                        if (player.NetState == null)
                        {
                            player.LogoutLocation = point;
                            player.LogoutMap = Map.Felucca;
                            player.Internalize();
                        }
                        else
                        {
                            player.MoveToWorld(point, Map.Felucca);
                        }
                        error = null;
                        return true;
                    }
                }
            }

            error = "There is no unobstructed arrival tile; no items have been removed.";
            return false;
        }

        public IExtractionEventResetSession CreateResetSession(ExtractionEventController controller)
        {
            var spawns = new ExtractionDungeonReset.MonsterSpawn[ExtractionDungeonLayout.CreatureCount];
            var starts = new Point3D[25];
            var index = 0;
            foreach (var room in _rooms)
            {
                starts[room.ModuleId] = room.PlayerStart;
                foreach (var creature in room.Creatures)
                {
                    spawns[index++] = new ExtractionDungeonReset.MonsterSpawn($"M{room.ModuleId:D2}", creature.Kind.ToString(), creature.Position, creature.Position, creature.RangeHome);
                }
            }

            var features = _installation.Features;
            var session = ExtractionDungeonReset.CreateSession(new ExtractionDungeonReset.Context(
                _manifest, Map.Felucca, _installation.Doors.ToArray(), _installation.Chests.ToArray(), features,
                controller.OwnedCreatures, controller.OwnedLoot, controller.RecoveryStorage, spawns, starts,
                controller.AddOwnedCreature, controller.RemoveOwnedCreature, controller.AddOwnedLoot, controller.RemoveOwnedLoot,
                controller.AddManagedItem, _installation.SyncFeatures
            ));
            _preparedCreaturesFrozen = true;
            _activationCursor = 0;
            return new PersistedReset(session, _installation, features);
        }

        public bool ActivatePreparedCreatures(ExtractionEventController controller, int operationBudget)
        {
            if (!_preparedCreaturesFrozen)
            {
                return true;
            }
            var budget = Math.Clamp(operationBudget, 0, ExtractionEventController.OperationsPerTick);
            while (_activationCursor < controller.OwnedCreatures.Count && budget-- > 0)
            {
                var creature = controller.OwnedCreatures[_activationCursor++];
                if (creature is { Deleted: false })
                {
                    creature.Frozen = false;
                }
            }
            if (_activationCursor < controller.OwnedCreatures.Count)
            {
                return false;
            }
            _preparedCreaturesFrozen = false;
            return true;
        }

        public void NotifyPhase(ExtractionEventController controller)
        {
            _prototypeShrine?.OnEventPhaseChanged(controller);
            _cairnShrine?.OnEventPhaseChanged(controller);
            foreach (var player in Map.Felucca.GetMobilesInBounds<PlayerMobile>(ExtractionDungeonLayout.LobbyBounds))
            {
                player.SendMessage($"Extraction event: {controller.Phase}.");
            }

            foreach (var player in controller.Participants)
            {
                if (player is { Deleted: false, NetState: not null } && controller.Phase == ExtractionEventPhase.Active)
                {
                    if (controller.ExtractionUnlocked)
                    {
                        player.SendMessage("Extraction is now available. The red flames have appeared and exit doors can be opened. Extract before the match ends.");
                    }
                    else
                    {
                        player.SendMessage("The 15-minute match has begun. Module doors can be opened now. Exit doors and extraction flames unlock after 7 minutes. Remaining players die when time expires.");
                    }
                }
            }
        }
    }

    private sealed class PersistedReset(IExtractionEventResetSession session, ExtractionEventInstallation installation, RootboundCellarFeature.Features features)
        : IExtractionEventResetSession
    {
        public bool Complete => session.Complete;
        public void Process(int operationBudget)
        {
            try
            {
                session.Process(operationBudget);
            }
            finally
            {
                installation.SyncFeatures(features);
            }
        }
    }

    private sealed class LobbyBuild(
        ModularDungeonDeployment.Manifest manifest,
        EventDungeonDoor[] doors,
        EventDungeonChest[] chests,
        RootboundCellarFeature.Features features
    )
    {
        private TimerExecutionToken _timer;
        private ExtractionEventController _controller;
        private ExtractionEventInstallation _installation;
        private int _cursor;
        private bool _creating;

        public void Begin()
        {
            _status = "Surveying the lobby, town gate and three exits.";
            Timer.StartTimer(TimeSpan.Zero, TimeSpan.FromMilliseconds(50), Tick, out _timer);
        }

        public void Cancel() => _timer.Cancel();

        private void Tick()
        {
            try
            {
                for (var operation = 0; operation < 8; operation++)
                {
                    if (!_creating)
                    {
                        if (_cursor < 625)
                        {
                            var point = LobbyPoint(_cursor++);
                            foreach (var tile in Map.Felucca.Tiles.GetStaticTiles(point.X, point.Y))
                            {
                                throw new InvalidOperationException("The lobby overlaps native statics.");
                            }
                            Require(Map.Felucca.Tiles.GetLandTile(point.X, point.Y).ID == 0x244, "The lobby overlaps existing terrain.");
                            foreach (var item in Map.Felucca.GetItemsInRange<Item>(point, 0))
                            {
                                throw new InvalidOperationException("The lobby overlaps an existing world item.");
                            }
                            foreach (var mobile in Map.Felucca.GetMobilesInRange<Mobile>(point, 0))
                            {
                                throw new InvalidOperationException("The lobby overlaps an existing mobile.");
                            }
                            continue;
                        }

                        Require(Map.Felucca.CanFit(ExtractionDungeonLayout.GatePoint, 16, true, true), "Galven's gate location is obstructed.");
                        foreach (var exit in _exits)
                        {
                            Require(Map.Felucca.CanFit(exit, 16, true, true), "An exterior extraction tile is obstructed.");
                        }

                        _controller = new ExtractionEventController();
                        _installation = new ExtractionEventInstallation();
                        _installation.Initialize(_controller, doors, chests, features);
                        _controller.AddManagedItem(_installation);
                        _installation.MoveToWorld(Point3D.Zero, Map.Internal);
                        var storage = new WoodenBox { Visible = false, Movable = false, MaxItems = int.MaxValue };
                        storage.MoveToWorld(Point3D.Zero, Map.Internal);
                        _controller.SetRecoveryStorage(storage);
                        _controller.MoveToWorld(Point3D.Zero, Map.Internal);
                        Controller = _controller;
                        _creating = true;
                        _cursor = 0;
                        _status = "Building the lobby with server-side statics; client map files are unchanged.";
                    }

                    if (_cursor < 625)
                    {
                        var point = LobbyPoint(_cursor++);
                        Place(new Static(0x519), point);
                        var x = point.X - 5384;
                        var y = point.Y - 808;
                        if (x is 0 or 24 || y is 0 or 24)
                        {
                            Place(new Static(x is 0 or 24 && y is 0 or 24 ? 0x59 : x is 0 or 24 ? 0x57 : 0x58), point);
                        }
                        continue;
                    }

                    Place(new ExtractionEventGate(_controller, ExtractionEventGateKind.Entry), ExtractionDungeonLayout.GatePoint);
                    Place(new ExtractionEventGate(_controller, ExtractionEventGateKind.LobbyReturn), ExtractionDungeonLayout.LobbyReturnPoint);
                    foreach (var exit in _exits)
                    {
                        Place(new ExtractionEventGate(_controller, ExtractionEventGateKind.Extraction), exit);
                    }
                    Require(Map.Felucca.CanFit(ExtractionDungeonLayout.LobbyEntry, 16, true, true), "The new lobby has no walkable arrival tile.");
                    _installation.MarkLobbyComplete();
                    Bind(_controller, _installation, manifest);
                    _controller.ConfigureWaiting(TimeSpan.FromMinutes(15), true);
                    _logger.Information("Extraction event installed: controller {Controller}, lobby {Lobby}, 110 room monsters plus cellar lich per match", _controller.Serial, ExtractionDungeonLayout.LobbyEntry);
                    Cancel();
                    _build = null;
                    return;
                }
            }
            catch (Exception exception)
            {
                Cancel();
                _status = exception.Message;
                _logger.Error(exception, "Extraction lobby installation failed; only newly created lobby objects will be rolled back");
                if (_controller != null)
                {
                    var created = new Item[_controller.ManagedItems.Count];
                    for (var index = 0; index < created.Length; index++)
                    {
                        created[index] = _controller.ManagedItems[index];
                    }
                    foreach (var item in created)
                    {
                        item.Delete();
                    }
                    _controller.RecoveryStorage?.Delete();
                    _controller.Delete();
                }
                _build = null;
            }
        }

        private static Point3D LobbyPoint(int index) => new(5384 + index % 25, 808 + index / 25, 0);
        private void Place(Item item, Point3D point)
        {
            _controller.AddManagedItem(item);
            item.MoveToWorld(point, Map.Felucca);
        }
    }
}
