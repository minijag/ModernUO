using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Server.Engines.ModularDungeon;
using Server.Logging;

namespace Server.Engines.TownStruggle;

/// <summary>A hash-pinned, fail-closed, one-shot migration; absent approval is entirely inert.</summary>
public static class TownStruggleInstallation
{
    internal const string InstallationId = "town-struggle-v1";
    internal const string ApprovedSha256 = "7fa9e4f06a154eedd9c1e728fd5bf7d8d00894efcebc64aa6a3c70d2fab35543";
    internal const uint ApprovedExtractionSerial = 1073765306;
    internal const int MaximumPlayers = 128;
    internal const int MaximumPlayerItems = 2048;
    internal const int MaximumTotalItems = 8192;
    internal static readonly Point3D LobbyStonePoint = new(5397, 820, 0);
    private static readonly TimeSpan _interval = TimeSpan.FromMinutes(15);
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(TownStruggleInstallation));
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    private static TimerExecutionToken _timer;
    private static Session _session;

    internal sealed record Marker(string InstallationId, string ManifestSha256, uint ExtractionSerial, bool AllowRecovery);
    internal sealed record Possession(uint Serial, uint ParentSerial, int ItemId, int Hue, int Amount, int Layer);
    internal sealed record Position(int Map, int X, int Y, int Z);
    internal sealed record PlayerSnapshot(uint Serial, bool Participant, bool Alive, uint Corpse, uint Backpack,
        Position Position, Position LogoutPosition, Possession[] Possessions);
    internal sealed record Archive(string InstallationId, string ManifestSha256, uint ExtractionSerial,
        string Phase, DateTime CreatedAt, bool AutomaticRounds, TimeSpan WaitingInterval, PlayerSnapshot[] Players);
    internal sealed record Receipt(string InstallationId, string ManifestSha256, string Stage, uint ExtractionSerial,
        bool Recovered, uint TownSerial, uint RotationSerial, uint StoneSerial, uint[] MarkerSerials,
        bool Enabled, string NextEvent, DateTime NextDeadline);
    internal sealed record Result(bool Success, string InstallationId, string Stage, bool AwaitingWorldSave, string Error);

    public static void Initialize()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data", "TownStruggle");
        if (!HasPending(directory))
        {
            return;
        }
        EventSink.Shutdown += Shutdown;
        Timer.StartTimer(TimeSpan.FromSeconds(2), () => Begin(directory), out _timer);
    }

    private static void Shutdown()
    {
        _timer.Cancel();
        _session = null;
    }

    internal static bool HasPending(string directory) => File.Exists(Path.Combine(directory, "install.pending"));

    private static bool Saving => World.Saving || World.WorldState is WorldState.PendingSave or WorldState.WritingSave;

    private static void Begin(string directory)
    {
        if (Saving)
        {
            Timer.StartTimer(TimeSpan.FromSeconds(1), () => Begin(directory), out _timer);
            return;
        }
        try
        {
            if (!HasPending(directory))
            {
                return;
            }
            var marker = JsonSerializer.Deserialize<Marker>(ReadBounded(Path.Combine(directory, "install.pending"), 2048), _options);
            var bytes = ReadBounded(Path.Combine(directory, "venues.json"), 32768);
            ValidateApproval(marker, bytes);
            var json = Encoding.UTF8.GetString(bytes);
            var extraction = ExtractionEventRuntime.Controller;
            Require(extraction is { Deleted: false } && extraction.Serial.Value == marker.ExtractionSerial &&
                    ReferenceEquals(World.FindItem((Serial)marker.ExtractionSerial), extraction) && extraction.IsBound &&
                    extraction.ReadyForScheduledStart, "The exact approved Extraction controller must be installed and bound.");
            if (File.Exists(Path.Combine(directory, "receipt.json")))
            {
                VerifyReplay(directory, extraction);
                WriteJson(Path.Combine(directory, "result.json"), new Result(true, InstallationId, "verified", false, null));
                return;
            }
            Require(!File.Exists(Path.Combine(directory, "before.json")),
                "An incomplete before archive exists; automatic recovery or reinstallation is refused.");
            ValidatePreflight(extraction.Serial.Value, extraction.Phase, extraction.Participants.Count,
                extraction.QueuedPlayers.Count, marker.AllowRecovery,
                TownStruggleRuntime.Controller is { Deleted: false }, PvpEventRotationRuntime.Current is { Deleted: false });
            var venues = TownStruggleVenue.ParseCatalog(json);
            ValidateCatalog(venues);
            foreach (var venue in venues)
            {
                venue.ValidateTerrain();
            }
            ValidateStonePoint();
            _session = new Session(directory, json, extraction, marker.AllowRecovery);
            Tick();
        }
        catch (Exception exception)
        {
            Fail(directory, exception);
        }
    }

    internal static void ValidateApproval(Marker marker, byte[] bytes)
    {
        Require(marker is { InstallationId: InstallationId, ExtractionSerial: ApprovedExtractionSerial } &&
                marker.ManifestSha256 == ApprovedSha256 && bytes is { Length: > 0 and <= 32768 } &&
                Convert.ToHexStringLower(SHA256.HashData(bytes)) == ApprovedSha256,
            "The marker must authorize the exact approved venue bytes, installation ID and Extraction serial.");
    }

    internal static void ValidatePreflight(uint serial, ExtractionEventPhase phase, int participants, int queued,
        bool allowRecovery, bool townExists, bool rotationExists)
    {
        Require(serial == ApprovedExtractionSerial && !townExists && !rotationExists,
            "Only the pinned Extraction event and a new Town Struggle/rotation installation are authorized.");
        Require(participants is >= 0 and <= MaximumPlayers && queued is >= 0 and <= MaximumPlayers,
            "The event exceeds bounded recovery capacity.");
        Require(phase == ExtractionEventPhase.Faulted && allowRecovery ||
                phase == ExtractionEventPhase.Waiting && participants == 0 && queued == 0,
            "Only an explicitly authorized Faulted event or an empty Waiting event can be installed over.");
    }

    internal static void ValidateCatalog(TownStruggleVenue[] venues)
    {
        Require(venues is { Length: 3 }, "Exactly the three approved town venues are required.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var venue in venues)
        {
            Require(venue != null && venue.Map == Map.Felucca && venue.NeutralBase == ExtractionDungeonLayout.LobbyEntry &&
                    names.Add(venue.Name), "Venue map, lobby, or identity differs from the approved catalog.");
        }
        Require(names.SetEquals(["Galven", "Albus", "Bowan"]), "The approved towns are Galven, Albus and Bowan.");
    }

    private static void ValidateStonePoint()
    {
        Require(Map.Felucca.CanFit(LobbyStonePoint, 16, false, false, true), "The lobby registration stone needs a walkable tile.");
        foreach (var item in Map.Felucca.GetItemsInRange<Item>(LobbyStonePoint, 0))
        {
            Require(item is not TownStruggleStone, "An unowned Town Struggle lobby stone already exists.");
        }
    }

    private static void Tick()
    {
        var session = _session;
        if (session == null)
        {
            return;
        }
        try
        {
            if (!Saving && session.Step())
            {
                _session = null;
                return;
            }
            Timer.StartTimer(TimeSpan.FromMilliseconds(250), Tick, out _timer);
        }
        catch (Exception exception)
        {
            Fail(session.Directory, exception);
        }
    }

    private sealed class Session
    {
        internal readonly string Directory;
        private readonly string _json;
        private readonly ExtractionEventController _extraction;
        private readonly bool _allowRecovery;
        private readonly List<(Mobile Player, bool Participant)> _players = [];
        private readonly List<PlayerSnapshot> _snapshots = [];
        private readonly ExtractionEventPhase _phase;
        private int _totalItems;
        private bool _recoveryStarted;
        private DateTime _recoveryDeadline;
        private PvpEventRotationController _createdRotation;

        internal Session(string directory, string json, ExtractionEventController extraction, bool allowRecovery)
        {
            Directory = directory;
            _json = json;
            _extraction = extraction;
            _allowRecovery = allowRecovery;
            _phase = extraction.Phase;
            var seen = new HashSet<Serial>();
            foreach (var player in extraction.Participants)
            {
                Require(player is { Deleted: false } && seen.Add(player.Serial), "The participant roster contains missing or duplicate identities.");
                _players.Add((player, true));
            }
            foreach (var player in extraction.QueuedPlayers)
            {
                Require(player is { Deleted: false }, "A queued player reference is missing.");
                if (seen.Add(player.Serial))
                {
                    _players.Add((player, false));
                }
            }
            Require(_players.Count <= MaximumPlayers, "The combined roster exceeds bounded recovery capacity.");
        }

        internal bool Step()
        {
            Require(ReferenceEquals(ExtractionEventRuntime.Controller, _extraction) && !_extraction.Deleted &&
                    _extraction.IsBound && _extraction.ReadyForScheduledStart,
                "Extraction ownership or recovery integration changed during installation.");
            if (!_recoveryStarted)
            {
                ValidatePreflight(_extraction.Serial.Value, _extraction.Phase, _extraction.Participants.Count,
                    _extraction.QueuedPlayers.Count, _allowRecovery,
                    TownStruggleRuntime.Controller is { Deleted: false }, PvpEventRotationRuntime.Current is { Deleted: false });
                Require(_phase == _extraction.Phase, "The Extraction phase changed before recovery.");
                ValidateRoster();
                if (_snapshots.Count < _players.Count)
                {
                    var (player, participant) = _players[_snapshots.Count];
                    var snapshot = Capture(player, participant);
                    _totalItems += snapshot.Possessions.Length;
                    Require(_totalItems <= MaximumTotalItems, "The inventory archive exceeds bounded recovery capacity.");
                    _snapshots.Add(snapshot);
                    return false;
                }
                foreach (var snapshot in _snapshots)
                {
                    ValidatePreserved(snapshot, false);
                }
                WriteNewJson(Path.Combine(Directory, "before.json"), new Archive(InstallationId, ApprovedSha256,
                    _extraction.Serial.Value, _phase.ToString(), Core.Now, _extraction.AutomaticRounds,
                    _extraction.WaitingInterval, _snapshots.ToArray()));
                // Stopped returns participants without the TimeExpired death path. No fallback may kill or replace them.
                if (_phase == ExtractionEventPhase.Faulted)
                {
                    Require(_allowRecovery, "Safe Extraction stop was not authorized.");
                    Require(_extraction.TryStop(out var stopError), stopError);
                }
                _recoveryStarted = true;
                _recoveryDeadline = Core.Now + TimeSpan.FromSeconds(120);
                return false;
            }
            Require(Core.Now <= _recoveryDeadline, "Safe Extraction recovery exceeded 120 seconds; manual recovery is required.");
            if (_extraction.Phase != ExtractionEventPhase.Waiting || _extraction.Participants.Count != 0 || _extraction.QueuedPlayers.Count != 0)
            {
                Require(_extraction.Phase == ExtractionEventPhase.Finishing,
                    "Extraction did not reach or remain on its safe Stopped recovery path.");
                return false;
            }
            foreach (var snapshot in _snapshots)
            {
                ValidatePreserved(snapshot, true);
            }
            Require(TownStruggleRuntime.Controller is not { Deleted: false } &&
                    PvpEventRotationRuntime.Current is not { Deleted: false }, "Event ownership changed during recovery.");
            ValidateStonePoint();
            Require(TownStruggleRuntime.TryInstall(_json, out var town, out var installError), installError);
            var stone = new TownStruggleStone();
            town.AddAuxiliaryItem(stone);
            stone.MoveToWorld(LobbyStonePoint, Map.Felucca);
            Require(PvpEventRotationRuntime.TryEnableRotation(_extraction, town, _interval, out var rotation, out var rotationError), rotationError);
            _createdRotation = rotation;
            var markerSerials = new uint[town.Markers.Count];
            for (var i = 0; i < markerSerials.Length; i++)
            {
                markerSerials[i] = town.Markers[i].Serial.Value;
            }
            Require(markerSerials.Length == 15 && rotation.Enabled && !rotation.Paused &&
                    rotation.NextEvent == PvpRotatingEvent.Extraction && rotation.NextDeadline == Core.Now + _interval,
                "Installed controller, markers or first Extraction deadline failed verification.");
            WriteJson(Path.Combine(Directory, "receipt.json"), new Receipt(InstallationId, ApprovedSha256, "installed",
                _extraction.Serial.Value, _phase == ExtractionEventPhase.Faulted, town.Serial.Value, rotation.Serial.Value,
                stone.Serial.Value, markerSerials, rotation.Enabled, rotation.NextEvent.ToString(), rotation.NextDeadline));
            WriteJson(Path.Combine(Directory, "result.json"), new Result(true, InstallationId, "installed", true, null));
            _logger.Information("Town Struggle installed with controller {TownSerial}; rotation {RotationSerial}; Extraction recovered {Recovered}",
                town.Serial, rotation.Serial, _phase == ExtractionEventPhase.Faulted);
            return true;
        }

        internal void StopOnFailure()
        {
            PauseIncompleteRotation(_createdRotation);
            _createdRotation = null;
        }

        private void ValidateRoster()
        {
            var participants = 0;
            foreach (var (player, participant) in _players)
            {
                Require(player.NetState == null && ReferenceEquals(World.FindMobile(player.Serial), player),
                    "Recovery requires the same logged-out players throughout the migration.");
                if (participant)
                {
                    participants++;
                    Require(_extraction.IsParticipant(player), "The archived participant roster changed.");
                }
                else
                {
                    var found = false;
                    foreach (var queued in _extraction.QueuedPlayers)
                    {
                        found |= ReferenceEquals(queued, player);
                    }
                    Require(found, "The archived queue changed.");
                }
            }
            Require(participants == _extraction.Participants.Count &&
                    _players.Count - participants == _extraction.QueuedPlayers.Count,
                "The Extraction roster changed during archival.");
        }
    }

    internal static PlayerSnapshot Capture(Mobile player, bool participant)
    {
        Require(player is { Deleted: false, Holding: null } && player.NetState == null &&
                ReferenceEquals(World.FindMobile(player.Serial), player), "A player is missing, connected or holding an unarchived item.");
        var entries = new List<Possession>();
        var pending = new Stack<Item>();
        var seen = new HashSet<Serial>();
        Require(player.Items.Count <= MaximumPlayerItems, "A player's equipment exceeds bounded recovery capacity.");
        foreach (var item in player.Items)
        {
            if (item.Layer != Layer.Bank)
            {
                pending.Push(item);
            }
        }
        while (pending.TryPop(out var item))
        {
            Require(item is { Deleted: false } && entries.Count < MaximumPlayerItems && seen.Add(item.Serial) &&
                    ReferenceEquals(World.FindItem(item.Serial), item) && item.Parent is IEntity,
                "Invalid, duplicate or excessive player inventory identity.");
            entries.Add(new Possession(item.Serial.Value, ((IEntity)item.Parent).Serial.Value,
                item.ItemID, item.Hue, item.Amount, (int)item.Layer));
            Require(pending.Count + item.Items.Count <= MaximumPlayerItems, "An inventory container exceeds bounded recovery capacity.");
            foreach (var child in item.Items)
            {
                pending.Push(child);
            }
        }
        entries.Sort((left, right) => left.Serial.CompareTo(right.Serial));
        return new PlayerSnapshot(player.Serial.Value, participant, player.Alive, player.Corpse?.Serial.Value ?? 0,
            player.Backpack?.Serial.Value ?? 0, At(player.Map, player.Location), At(player.LogoutMap, player.LogoutLocation), entries.ToArray());
    }

    internal static void ValidatePreserved(PlayerSnapshot original, bool recovered)
    {
        Require(original != null, "A player archive is missing.");
        var player = World.FindMobile((Serial)original.Serial);
        var current = Capture(player, original.Participant);
        Require(current.Alive == original.Alive && current.Corpse == original.Corpse && current.Backpack == original.Backpack &&
                original.Possessions != null && current.Possessions.AsSpan().SequenceEqual(original.Possessions),
            "A player's life, corpse, backpack, item identity, amount or ownership changed during recovery.");
        if (recovered && original.Participant)
        {
            var map = player.Map == Map.Internal ? player.LogoutMap : player.Map;
            var point = player.Map == Map.Internal ? player.LogoutLocation : player.Location;
            Require(map == Map.Felucca && ExtractionDungeonLayout.LobbyBounds.Contains(point),
                "A recovered player did not safely return to the lobby.");
        }
        else
        {
            Require(current.Position == original.Position && current.LogoutPosition == original.LogoutPosition,
                "An unrecovered player's saved position changed.");
        }
    }

    private static Position At(Map map, Point3D p) => new(map?.MapIndex ?? -1, p.X, p.Y, p.Z);

    private static void VerifyReplay(string directory, ExtractionEventController extraction)
    {
        var archive = JsonSerializer.Deserialize<Archive>(ReadBounded(Path.Combine(directory, "before.json"), 4194304), _options);
        var receipt = JsonSerializer.Deserialize<Receipt>(ReadBounded(Path.Combine(directory, "receipt.json"), 32768), _options);
        Require(archive is { InstallationId: InstallationId, ExtractionSerial: ApprovedExtractionSerial, Players.Length: <= MaximumPlayers } &&
                archive.ManifestSha256 == ApprovedSha256 && receipt is
                { InstallationId: InstallationId, ExtractionSerial: ApprovedExtractionSerial, Stage: "installed", Enabled: true, MarkerSerials.Length: 15 } &&
                receipt.ManifestSha256 == ApprovedSha256, "The receipt or before archive is incomplete or differently pinned.");
        var town = World.FindItem((Serial)receipt.TownSerial) as TownStruggleController;
        var rotation = World.FindItem((Serial)receipt.RotationSerial) as PvpEventRotationController;
        var stone = World.FindItem((Serial)receipt.StoneSerial) as TownStruggleStone;
        Require(town is { Deleted: false } && ReferenceEquals(town, TownStruggleRuntime.Controller) &&
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(town.VenueJson))) == ApprovedSha256 &&
                rotation is { Deleted: false, Enabled: true, Paused: false } && ReferenceEquals(rotation, PvpEventRotationRuntime.Current) &&
                rotation.Town == town && rotation.Extraction == extraction && rotation.Interval == _interval &&
                stone is { Deleted: false } && stone.Map == Map.Felucca && stone.Location == LobbyStonePoint &&
                town.AuxiliaryItems.Count == 1 && town.AuxiliaryItems[0] == stone && town.Markers.Count == 15,
            "Saved installation ownership or enabled rotation differs from the completed receipt.");
        var seen = new HashSet<uint>();
        for (var i = 0; i < receipt.MarkerSerials.Length; i++)
        {
            var serial = receipt.MarkerSerials[i];
            Require(seen.Add(serial) && town.Markers[i] is { Deleted: false } marker && marker.Serial.Value == serial &&
                    marker.Controller == town && marker.Zone == i / 5 && marker.Corner == i % 5 &&
                    ReferenceEquals(World.FindItem((Serial)serial), marker),
                "A saved control marker identity is missing or changed.");
        }
        // Deadlines and the next event advance normally; replay verifies ownership without resetting either.
    }

    private static void Fail(string directory, Exception exception)
    {
        _timer.Cancel();
        _session?.StopOnFailure();
        _session = null;
        _logger.Error(exception, "Town Struggle installation refused; no automatic retry");
        try
        {
            WriteJson(Path.Combine(directory, "result.json"), new Result(false, InstallationId, "refused", false, exception.Message));
        }
        catch (Exception writeException)
        {
            _logger.Error(writeException, "Could not write Town Struggle installation refusal");
        }
    }

    internal static void PauseIncompleteRotation(PvpEventRotationController owned)
    {
        if (owned is { Deleted: false, Enabled: true } && ReferenceEquals(PvpEventRotationRuntime.Current, owned))
        {
            owned.Pause("Installation did not finish durably; staff must verify its archive and receipt before resuming.");
        }
    }

    private static byte[] ReadBounded(string path, int maximum)
    {
        Require(new FileInfo(path).Length is > 0 && new FileInfo(path).Length <= maximum, "Invalid installation file size.");
        var bytes = File.ReadAllBytes(path);
        Require(bytes.Length <= maximum, "The installation file grew while reading.");
        return bytes;
    }

    private static void WriteNewJson<T>(string path, T value)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, value, _options);
        stream.Flush(true);
    }

    private static void WriteJson<T>(string path, T value)
    {
        var temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, value, _options);
            stream.Flush(true);
        }
        File.Move(temporary, path, true);
    }

    private static void Require(bool condition, string error)
    {
        if (!condition)
        {
            throw new InvalidOperationException(error ?? "Town Struggle installation verification failed.");
        }
    }
}
