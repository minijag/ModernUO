using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Server.Engines.TownStruggle;
using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.Engines.ModularDungeon;

public static class ExtractionLobbyDecoration
{
    internal const string InstallationId = "pvp-lobby-ritual-court-v1";
    internal const uint ControllerSerial = 1073765306;
    internal const uint TownStoneSerial = 1073774065;
    internal const int BatchSize = 12;
    internal static Point3D OriginalReturnPoint => new(5396, 823, 0);
    internal static Point3D TownStonePoint => new(5397, 820, 0);
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(ExtractionLobbyDecoration));
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    private static TimerExecutionToken _timer;
    private static Session _session;
    public static bool IsBusy { get; private set; }

    internal sealed record Entry(string Id, int ItemId, int Hue, int X, int Y, int Z, string Role);
    internal sealed record BankerPin(int X, int Y, int Z);
    internal sealed record Manifest(string InstallationId, uint ControllerSerial, Entry[] Items, BankerPin Banker = null);
    internal sealed record Marker(string InstallationId, string ManifestSha256);
    internal sealed record Snapshot(uint Serial, int ItemId, int Hue, int X, int Y, int Z, string Name, int Light);
    internal sealed record Placed(string Id, uint Serial);
    internal sealed record BankerSnapshot(uint Serial, int X, int Y, int Z, string Name, int Body, int Hue);
    internal sealed record Receipt(string InstallationId, string ManifestSha256, uint ControllerSerial,
        string Stage, Snapshot[] BaseItems, Snapshot GateBefore, Placed[] Items, BankerSnapshot Banker = null);

    public static void Initialize()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data", "ExtractionLobbyDecoration");
        // Cosmetic receipts do not impose a permanent startup gate on normal lobby use.
        if (!File.Exists(Path.Combine(directory, "install.pending")))
        {
            return;
        }
        IsBusy = true;
        EventSink.Shutdown += Shutdown;
        Timer.StartTimer(TimeSpan.FromSeconds(1), () => Begin(directory, 0), out _timer);
    }

    private static void Begin(string directory, int attempt)
    {
        try
        {
            if (World.Saving || World.WorldState is WorldState.PendingSave or WorldState.WritingSave)
            {
                Timer.StartTimer(TimeSpan.FromSeconds(1), () => Begin(directory, attempt), out _timer);
                return;
            }
            var controller = ExtractionEventRuntime.Controller;
            if ((controller == null || !controller.IsBound) && attempt < 10)
            {
                Timer.StartTimer(TimeSpan.FromSeconds(1), () => Begin(directory, attempt + 1), out _timer);
                return;
            }
            Require(controller is { Deleted: false, IsBound: true } && controller.Serial.Value == ControllerSerial,
                "The pinned, bound extraction controller is required.");
            var bytes = ReadBounded(Path.Combine(directory, "manifest.json"), 256 * 1024);
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var manifest = ParseManifest(bytes);
            var receiptPath = Path.Combine(directory, "receipt.json");
            var receipt = File.Exists(receiptPath)
                ? JsonSerializer.Deserialize<Receipt>(ReadBounded(receiptPath, 1024 * 1024), JsonOptions)
                : null;
            var markerPath = Path.Combine(directory, "install.pending");
            var marker = JsonSerializer.Deserialize<Marker>(ReadBounded(markerPath, 1024), JsonOptions);
            Require(marker is { InstallationId: InstallationId } && marker.ManifestSha256 == hash,
                "The marker does not authorize this exact lobby decoration manifest.");
            _session = new Session(controller, manifest, hash, receipt, new LobbyMap(),
                value => WriteJson(receiptPath, value));
            Timer.StartTimer(TimeSpan.Zero, TimeSpan.FromMilliseconds(50), () => Tick(directory), out _timer);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Lobby decoration startup refused; event admission remains suspended");
            try
            {
                WriteJson(Path.Combine(directory, "result.json"), new { Success = false, Error = exception.ToString() });
            }
            catch (Exception writeException)
            {
                _logger.Error(writeException, "Could not persist the lobby decoration startup refusal");
            }
        }
    }

    private static void Tick(string directory)
    {
        if (World.Saving || World.WorldState is WorldState.PendingSave or WorldState.WritingSave)
        {
            return;
        }
        _session.Process(BatchSize);
        if (!_session.Complete)
        {
            return;
        }
        _timer.Cancel();
        IsBusy = !_session.Success;
        try
        {
            WriteJson(Path.Combine(directory, "result.json"), new
            {
                _session.Success, InstallationId, _session.Error,
                Added = _session.CreatedCount, Verified = _session.VerifiedCount,
                AddedBankers = _session.CreatedBankerCount,
                AwaitingWorldSave = _session.Success && _session.CreatedCount != 0,
                ReturnGate = ExtractionDungeonLayout.LobbyReturnPoint
            });
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not write the lobby decoration result");
        }
        _logger.Information("Lobby decoration finished: success {Success}, created {Created}, verified {Verified}, error {Error}",
            _session.Success, _session.CreatedCount, _session.VerifiedCount, _session.Error);
        _session = null;
    }

    private static void Shutdown() => _timer.Cancel();

    internal static Manifest ParseManifest(byte[] bytes)
    {
        Require(bytes is { Length: > 0 and <= 256 * 1024 }, "Invalid lobby manifest size.");
        var manifest = JsonSerializer.Deserialize<Manifest>(bytes, JsonOptions);
        ValidateManifest(manifest);
        return manifest;
    }

    internal static void ValidateManifest(Manifest manifest)
    {
        Require(manifest is { InstallationId: InstallationId, ControllerSerial: ControllerSerial, Items.Length: > 0 and <= 1200 },
            "Invalid lobby decoration identity, controller or item count.");
        Require(manifest.Banker == null || manifest.Banker is { X: 3, Y: 4, Z: 0 },
            "The lobby banker must occupy the approved northwest station.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var placements = new HashSet<(int, int, int, int, int)>();
        foreach (var entry in manifest.Items)
        {
            Require(entry != null && !string.IsNullOrWhiteSpace(entry.Id) && entry.Id.Length <= 64 && ids.Add(entry.Id) &&
                    !string.IsNullOrWhiteSpace(entry.Role) && entry.Role.Length <= 100 &&
                    entry.ItemId is > 0 and <= 65535 && entry.Hue is >= 0 and <= 3000 &&
                    entry.X is >= 0 and <= 24 && entry.Y is >= 0 and <= 24 && entry.Z is >= 0 and <= 35,
                "Invalid or duplicate lobby decoration entry.");
            Require(placements.Add((entry.ItemId, entry.X, entry.Y, entry.Z, entry.Hue)), "Duplicate lobby decoration placement.");
            Require(manifest.Banker == null || entry.X != 3 || entry.Y != 4,
                "The banker standing tile must remain clear of decoration.");
            Require(!(entry.X is >= 20 and <= 22 && entry.Y is >= 20 and <= 22) && !(entry.X == 12 && entry.Y == 15),
                "The return gate or its reserved approach is occupied.");
            Require(!(entry.X is >= 10 and <= 14 && entry.Y is >= 10 and <= 14) ||
                    entry is { Z: 0, Hue: 0 } && IsArrivalArtwork(entry.ItemId),
                "Only the approved low blood and bone artwork may occupy the arrival area.");
        }
    }

    internal static bool IsArrivalArtwork(int itemId) => itemId is >= 0x1CF1 and <= 0x1D12 or 0x1B09 or 0x1B0D or 0x1B0E or 0xECA;

    internal interface ILobbyMap
    {
        void RequireEmptyOfMobiles(Mobile allowedBanker);
        void SurveyNativeTile(Point3D point);
        IEnumerable<Item> ItemsAt(Point3D point);
        bool CanFit(Point3D point);
    }

    private sealed class LobbyMap : ILobbyMap
    {
        public void RequireEmptyOfMobiles(Mobile allowedBanker)
        {
            foreach (var mobile in Map.Felucca.GetMobilesInBounds<Mobile>(ExtractionDungeonLayout.LobbyBounds))
            {
                if (mobile == allowedBanker)
                {
                    continue;
                }
                throw new InvalidOperationException("All mobiles, including staff, must leave the lobby before decoration.");
            }
        }
        public void SurveyNativeTile(Point3D point)
        {
            Require(Map.Felucca.Tiles.GetLandTile(point.X, point.Y).ID == 0x244, "The lobby native terrain has changed.");
            foreach (var tile in Map.Felucca.Tiles.GetStaticTiles(point.X, point.Y))
            {
                throw new InvalidOperationException("The lobby contains unexpected native statics.");
            }
        }
        public IEnumerable<Item> ItemsAt(Point3D point)
        {
            var items = new List<Item>();
            foreach (var item in Map.Felucca.GetItemsInRange<Item>(point, 0))
            {
                Require(items.Count < 64, "A lobby tile exceeds the bounded survey density.");
                items.Add(item);
            }
            return items;
        }
        public bool CanFit(Point3D point) => Map.Felucca.CanFit(point, 16, true, false);
    }

    internal sealed class Session
    {
        private readonly ExtractionEventController _controller;
        private readonly Manifest _manifest;
        private readonly string _hash;
        private readonly ILobbyMap _map;
        private readonly Action<Receipt> _persist;
        private readonly HashSet<Item> _owned;
        private readonly List<Snapshot> _base = [];
        private readonly List<Static> _created = [];
        private readonly Dictionary<uint, Entry> _recorded = [];
        private readonly HashSet<Snapshot> _baseIdentities = [];
        private readonly HashSet<uint> _receiptBaseSerials = [];
        private readonly Receipt _existing;
        private ExtractionEventGate _gate;
        private ExtractionEventGate _originalGate;
        private Snapshot _gateBefore;
        private int _cursor;
        private int _stage;
        private int _persistedCount;
        private bool _gateMoved;
        private Banker _banker;
        private BankerSnapshot _bankerSnapshot;
        private bool _createdBanker;
        private bool _bankerPersisted;
        private TownStruggleStone _townStone;
        public bool Complete { get; private set; }
        public bool Success { get; private set; }
        public string Error { get; private set; }
        public int CreatedCount => _created.Count;
        public int VerifiedCount { get; private set; }
        public int CreatedBankerCount => _createdBanker ? 1 : 0;

        internal Session(ExtractionEventController controller, Manifest manifest, string hash, Receipt existing,
            ILobbyMap map, Action<Receipt> persist)
        {
            ValidateManifest(manifest);
            Require(controller is { Deleted: false } && controller.Serial.Value == manifest.ControllerSerial,
                "Wrong lobby controller.");
            Require(controller.ManagedItems.Count <= 4096, "Unbounded lobby ownership.");
            _controller = controller;
            _manifest = manifest;
            _hash = hash;
            _map = map;
            _persist = persist;
            _existing = existing;
            _owned = new HashSet<Item>(controller.ManagedItems);
            Require(_owned.Count == controller.ManagedItems.Count, "Duplicate controller ownership references.");
            if (existing != null)
            {
                Require(existing is { InstallationId: InstallationId, ControllerSerial: ControllerSerial, Stage: "installed", BaseItems.Length: 721, GateBefore: not null, Items: not null } &&
                        existing.ManifestSha256 == hash && existing.Items.Length == manifest.Items.Length,
                    "Incomplete or mismatched lobby receipt requires staff recovery; it will not be replayed.");
                Require((manifest.Banker == null) == (existing.Banker == null),
                    "The banker receipt does not match the approved manifest.");
                if (existing.Banker != null)
                {
                    _banker = World.FindMobile((Serial)existing.Banker.Serial) as Banker;
                    _bankerSnapshot = existing.Banker;
                    ValidateBanker(_banker, manifest.Banker, existing.Banker);
                }
                foreach (var item in existing.BaseItems)
                {
                    Require(item != null && _receiptBaseSerials.Add(item.Serial), "Duplicate or invalid base receipt identities.");
                }
                Require(!_receiptBaseSerials.Contains(existing.GateBefore.Serial), "The receipt reuses its return gate identity.");
                for (var i = 0; i < manifest.Items.Length; i++)
                {
                    Require(existing.Items[i] != null && existing.Items[i].Id == manifest.Items[i].Id &&
                            existing.Items[i].Serial != existing.GateBefore.Serial && !_receiptBaseSerials.Contains(existing.Items[i].Serial) &&
                            _recorded.TryAdd(existing.Items[i].Serial, manifest.Items[i]),
                        "The lobby receipt contains duplicate or reordered decoration identities.");
                }
            }
            RequireSafe();
        }

        internal void Process(int budget)
        {
            if (Complete)
            {
                return;
            }
            Require(budget is > 0 and <= 16, "Lobby processing must remain bounded.");
            try
            {
                if (_stage == 5)
                {
                    Rollback(budget);
                    return;
                }
                RequireSafe();
                for (var operation = 0; operation < budget && !Complete; operation++)
                {
                    switch (_stage)
                    {
                        case 0:
                            SurveyTile(_cursor++, false);
                            if (_cursor == 625)
                            {
                                Require(_base.Count == 721 && _gate != null, "The exact lobby base or return gate is missing.");
                                _originalGate = _gate;
                                _baseIdentities.UnionWith(_base);
                                if (_existing != null)
                                {
                                    Require(_gate.Location == ExtractionDungeonLayout.LobbyReturnPoint, "The installed return gate has moved.");
                                    Require(Describe(_gate) == _existing.GateBefore with
                                    {
                                        X = ExtractionDungeonLayout.LobbyReturnPoint.X,
                                        Y = ExtractionDungeonLayout.LobbyReturnPoint.Y,
                                        Z = ExtractionDungeonLayout.LobbyReturnPoint.Z
                                    }, "The return gate properties differ from the receipt.");
                                    var expected = new HashSet<Snapshot>(_existing.BaseItems);
                                    Require(expected.Count == 721 && expected.SetEquals(_base), "The original lobby base identities have changed.");
                                    _stage = 2;
                                }
                                else
                                {
                                    _gateBefore = Describe(_gate);
                                    Persist("installing");
                                    _gate.MoveToWorld(ExtractionDungeonLayout.LobbyReturnPoint, Map.Felucca);
                                    _gateMoved = true;
                                    _stage = _manifest.Banker == null ? 1 : 6;
                                }
                                _cursor = 0;
                            }
                            break;
                        case 1:
                            Create(_manifest.Items[_cursor++]);
                            if (_cursor == _manifest.Items.Length)
                            {
                                _stage = 2;
                                _cursor = 0;
                            }
                            break;
                        case 2:
                            var entry = _manifest.Items[_cursor];
                            var item = _existing == null ? _created[_cursor] : World.FindItem((Serial)_existing.Items[_cursor].Serial);
                            ValidateDecoration(item, entry, _controller);
                            VerifiedCount++;
                            if (++_cursor == _manifest.Items.Length)
                            {
                                _stage = 3;
                                _cursor = 0;
                            }
                            break;
                        case 3:
                            var point = _cursor < 25
                                ? new Point3D(5394 + _cursor % 5, 818 + _cursor / 5, 0)
                                : new Point3D(5404 + (_cursor - 25) % 3, 828 + (_cursor - 25) / 3, 0);
                            // The owned signup stone occupies one tile beside the unchanged arrival anchor.
                            Require(_townStone != null && point == TownStonePoint || _map.CanFit(point),
                                "A reserved lobby arrival or gate approach is obstructed.");
                            if (++_cursor == 34)
                            {
                                _stage = 4;
                                _cursor = 0;
                                _gate = null;
                            }
                            break;
                        case 4:
                            SurveyTile(_cursor++, true);
                            if (_cursor == 625)
                            {
                                var before = _existing?.GateBefore ?? _gateBefore;
                                Require(_gate != null && Describe(_gate) == before with
                                {
                                    X = ExtractionDungeonLayout.LobbyReturnPoint.X,
                                    Y = ExtractionDungeonLayout.LobbyReturnPoint.Y,
                                    Z = ExtractionDungeonLayout.LobbyReturnPoint.Z
                                }, "The relocated gate differs from its preserved identity or properties.");
                                if (_manifest.Banker != null)
                                {
                                    ValidateBanker(_banker, _manifest.Banker, _bankerSnapshot);
                                    Require(_map.CanFit(BankerPosition(_manifest.Banker)), "The banker station became obstructed.");
                                }
                                if (_existing == null)
                                {
                                    Persist("installed");
                                }
                                Success = true;
                                Complete = true;
                            }
                            break;
                        case 6:
                            CreateBanker();
                            _stage = 1;
                            break;
                    }
                }
                if (_existing == null && (_created.Count > _persistedCount || _createdBanker && !_bankerPersisted) && !Complete)
                {
                    Persist("installing");
                }
            }
            catch (Exception exception)
            {
                Error = exception.Message;
                _stage = 5;
                _cursor = _created.Count - 1;
            }
        }

        private void RequireSafe()
        {
            Require(!_controller.Deleted, "The lobby controller has been deleted.");
            Require(_townStone == null || IsApprovedTownStone(_townStone),
                "The preserved Town Struggle signup stone changed during decoration.");
            if (_existing != null)
            {
                return;
            }
            Require(_controller.Phase == ExtractionEventPhase.Waiting &&
                    _controller.Participants.Count == 0 && _controller.QueuedPlayers.Count == 0,
                "Lobby decoration requires a waiting event with no participants or queue.");
            if (_createdBanker)
            {
                ValidateBanker(_banker, _manifest.Banker, _bankerSnapshot);
            }
            _map.RequireEmptyOfMobiles(_createdBanker ? _banker : null);
        }

        private void SurveyTile(int index, bool final)
        {
            var x = index % 25;
            var y = index / 25;
            var point = new Point3D(5384 + x, 808 + y, 0);
            _map.SurveyNativeTile(point);
            var floors = 0;
            var walls = 0;
            foreach (var item in _map.ItemsAt(point))
            {
                if (item.Serial.Value == TownStoneSerial)
                {
                    Require(IsApprovedTownStone(item) && (_townStone == item || !final && _townStone == null),
                        "The lobby contains an unowned or altered Town Struggle signup stone.");
                    _townStone = (TownStruggleStone)item;
                    continue;
                }
                if (_existing != null && !_recorded.ContainsKey(item.Serial.Value) &&
                    !_receiptBaseSerials.Contains(item.Serial.Value) && item.Serial.Value != _existing.GateBefore.Serial)
                {
                    continue;
                }
                Require(item is { Deleted: false, Parent: null, Movable: false, Visible: true } && _owned.Contains(item),
                    "The lobby contains an unowned, moved or altered item.");
                if (item is ExtractionEventGate gate)
                {
                    Require(_gate == null && gate.Controller == _controller && gate.Kind == ExtractionEventGateKind.LobbyReturn &&
                            (gate.Location == OriginalReturnPoint || gate.Location == ExtractionDungeonLayout.LobbyReturnPoint),
                        "Unexpected or duplicate lobby return gate.");
                    _gate = gate;
                    continue;
                }
                if (_recorded.TryGetValue(item.Serial.Value, out var entry))
                {
                    ValidateDecoration(item, entry, _controller);
                    continue;
                }
                Require(item.GetType() == typeof(Static) && item.Location == point && item.Hue == 0 && item.Name == null,
                    "A lobby base tile differs from its original assembly.");
                if (item.ItemID == 0x519)
                {
                    floors++;
                }
                else
                {
                    Require(x is 0 or 24 || y is 0 or 24, "Unexpected interior base wall.");
                    var expected = x is 0 or 24 && y is 0 or 24 ? 0x59 : x is 0 or 24 ? 0x57 : 0x58;
                    Require(item.ItemID == expected, "A lobby wall has the wrong native assembly.");
                    walls++;
                }
                if (final)
                {
                    Require(_baseIdentities.Contains(Describe(item)), "An original lobby floor or wall changed during decoration.");
                }
                else
                {
                    _base.Add(Describe(item));
                }
            }
            Require(floors == 1 && walls == (x is 0 or 24 || y is 0 or 24 ? 1 : 0), "Missing or duplicate original lobby floor or wall.");
        }

        private void Create(Entry entry)
        {
            Require(entry.ItemId < TileData.ItemTable.Length, "Decoration artwork is outside native tile data.");
            if (entry.X is >= 10 and <= 14 && entry.Y is >= 10 and <= 14)
            {
                var data = TileData.ItemTable[entry.ItemId];
                Require(data.Height <= 1 && (data.Flags & (TileFlag.Impassable | TileFlag.Surface)) == 0,
                    "Arrival artwork is not a low nonblocking decal.");
            }
            var item = new Static(entry.ItemId) { Hue = entry.Hue, Movable = false };
            _created.Add(item);
            _controller.AddManagedItem(item);
            _owned.Add(item);
            _recorded.Add(item.Serial.Value, entry);
            item.MoveToWorld(Position(entry), Map.Felucca);
        }

        private void CreateBanker()
        {
            var point = BankerPosition(_manifest.Banker);
            Require(_map.CanFit(point), "The banker station is obstructed.");
            _banker = new Banker();
            _createdBanker = true;
            _banker.Blessed = true;
            _banker.CantWalk = true;
            _banker.Frozen = false;
            _banker.Home = point;
            _banker.HomeMap = Map.Felucca;
            _banker.RangeHome = 0;
            // The scene's gold is static artwork, not a reward in the vendor's backpack.
            if (_banker.Backpack != null)
            {
                var seedGold = new List<Gold>();
                foreach (var gold in _banker.Backpack.FindItemsByType<Gold>())
                {
                    seedGold.Add(gold);
                }
                foreach (var gold in seedGold)
                {
                    gold.Delete();
                }
            }
            _banker.MoveToWorld(point, Map.Felucca);
            _bankerSnapshot = DescribeBanker(_banker);
            ValidateBanker(_banker, _manifest.Banker, _bankerSnapshot);
        }

        private void Persist(string stage)
        {
            var items = new Placed[_created.Count];
            for (var i = 0; i < items.Length; i++)
            {
                items[i] = new Placed(_manifest.Items[i].Id, _created[i].Serial.Value);
            }
            _persist(new Receipt(InstallationId, _hash, ControllerSerial, stage, _base.ToArray(), _gateBefore, items, _bankerSnapshot));
            _persistedCount = items.Length;
            _bankerPersisted = _bankerSnapshot != null;
        }

        private void Rollback(int budget)
        {
            for (var operation = 0; operation < budget && _cursor >= 0; operation++, _cursor--)
            {
                var item = _created[_cursor];
                _controller.RemoveManagedItem(item);
                if (!item.Deleted)
                {
                    item.Delete();
                }
            }
            if (_cursor >= 0)
            {
                return;
            }
            if (_createdBanker && _banker is { Deleted: false })
            {
                _banker.Delete();
            }
            if (_gateMoved && _originalGate is { Deleted: false } && _originalGate.Controller == _controller &&
                _originalGate.Location == ExtractionDungeonLayout.LobbyReturnPoint && _originalGate.Map == Map.Felucca)
            {
                _originalGate.MoveToWorld(new Point3D(_gateBefore.X, _gateBefore.Y, _gateBefore.Z), Map.Felucca);
            }
            Complete = true;
            if (_existing == null && _gateBefore != null)
            {
                try
                {
                    Persist("rolled-back");
                }
                catch (Exception exception)
                {
                    _logger.Error(exception, "Lobby rollback completed but its receipt could not be updated");
                }
            }
        }
    }

    internal static bool IsApprovedTownStone(Item item) =>
        item != null && item.GetType() == typeof(TownStruggleStone) && item.Serial.Value == TownStoneSerial &&
        item is { Deleted: false, Parent: null, Movable: false, Visible: true, ItemID: 0xED4, Hue: 0x489 } &&
        item.Name == "Town Struggle" && item.Map == Map.Felucca && item.Location == TownStonePoint &&
        TownStruggleRuntime.Controller is { Deleted: false } town && town.AuxiliaryItems.Contains(item);

    internal static void ValidateDecoration(Item item, Entry entry, ExtractionEventController controller)
    {
        Require(item != null && item.GetType() == typeof(Static) && !item.Deleted && item.Parent == null &&
                item.Map == Map.Felucca && item.Location == Position(entry) && item.ItemID == entry.ItemId &&
                item.Hue == entry.Hue && !item.Movable && item.Visible && item.Name == null &&
                controller.ManagedItems.Contains(item), "A recorded lobby decoration no longer matches its exact owned placement.");
    }

    internal static Point3D Position(Entry entry) => new(5384 + entry.X, 808 + entry.Y, entry.Z);
    internal static Point3D BankerPosition(BankerPin banker) => new(5384 + banker.X, 808 + banker.Y, banker.Z);
    internal static BankerSnapshot DescribeBanker(Banker banker) =>
        new(banker.Serial.Value, banker.X, banker.Y, banker.Z, banker.Name, banker.Body, banker.Hue);
    internal static void ValidateBanker(Banker banker, BankerPin definition, BankerSnapshot receipt)
    {
        Require(banker != null && banker.GetType() == typeof(Banker) && banker is
                { Deleted: false, Alive: true, Player: false, Blessed: true, CantWalk: true, Frozen: false,
                    Controlled: false, Summoned: false, Spawner: null, RangeHome: 0 } &&
                definition != null && receipt != null && banker.Map == Map.Felucca && banker.HomeMap == Map.Felucca &&
                banker.Home == BankerPosition(definition) && banker.Location == banker.Home &&
                ReferenceEquals(World.FindMobile(banker.Serial), banker) && DescribeBanker(banker) == receipt,
            "The recorded banker is missing, altered or no longer at the approved station; it will not be recreated.");
    }
    private static Snapshot Describe(Item item) => new(item.Serial.Value, item.ItemID, item.Hue, item.X, item.Y, item.Z, item.Name, (int)item.Light);
    private static byte[] ReadBounded(string path, int maximum)
    {
        Require(new FileInfo(path).Length is > 0 && new FileInfo(path).Length <= maximum, "Invalid lobby deployment file size.");
        var bytes = File.ReadAllBytes(path);
        Require(bytes.Length <= maximum, "A lobby deployment file exceeded its bound.");
        return bytes;
    }
    private static void WriteJson<T>(string path, T value)
    {
        var temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, value, JsonOptions);
            stream.Flush(true);
        }
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
