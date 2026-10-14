using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Server;
using Server.Engines.ModularDungeon;
using Server.Engines.TownStruggle;
using Server.Items;
using Server.Mobiles;
using Server.Network;
using Server.Tests;
using Xunit;
using Decoration = Server.Engines.ModularDungeon.ExtractionLobbyDecoration;

namespace UOContent.Tests.Engines.ModularDungeon;

[Collection("Sequential UOContent Tests")]
public class ExtractionLobbyDecorationTests : IDisposable
{
    private readonly List<Item> _items = [];
    private readonly List<Mobile> _mobiles = [];
    private ExtractionEventController _controller;
    private ExtractionEventGate _gate;
    private FakeMap _map;
    private Decoration.Receipt _receipt;
    private readonly TownStruggleController _previousTown = TownStruggleRuntime.Controller;
    private TownStruggleController _townController;

    public void Dispose()
    {
        foreach (var mobile in _mobiles)
        {
            mobile.Delete();
        }
        if (_receipt?.Banker != null)
        {
            World.FindMobile((Serial)_receipt.Banker.Serial)?.Delete();
        }
        if (_controller != null)
        {
            foreach (var item in _controller.ManagedItems.ToArray())
            {
                item.Delete();
            }
        }
        foreach (var item in _items)
        {
            item.Delete();
        }
        _controller?.Delete();
        _townController?.Delete();
        SetTownController(_previousTown);
    }

    [Fact]
    public void Manifest_RoundTripsApprovedCamelCaseSchemaAndFlatArrivalBlood()
    {
        var manifest = Manifest(new Decoration.Entry("blood", 0x1CF1, 0, 12, 12, 0, "ritual circle"));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, Decoration.JsonOptions);
        var restored = Decoration.ParseManifest(bytes);
        Assert.Equal(manifest.InstallationId, restored.InstallationId);
        Assert.Equal(manifest.Items, restored.Items);
        Assert.Contains("\"controllerSerial\"", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void CanonicalReturnAnchor_MovesToSoutheastWithoutChangingArrivalOrGalvenDestination()
    {
        Assert.Equal(new Point3D(5405, 829, 0), ExtractionDungeonLayout.LobbyReturnPoint);
        Assert.Equal(new Point3D(5396, 820, 0), ExtractionDungeonLayout.LobbyEntry);
        Assert.Equal(new Point3D(1317, 1380, 0), ExtractionDungeonLayout.TownReturn);
        Assert.Equal(new Point3D(1317, 1379, 0), ExtractionDungeonLayout.GatePoint);
    }

    [Theory]
    [InlineData(4, 4, 0)]
    [InlineData(3, 3, 0)]
    [InlineData(3, 4, 1)]
    public void BankerManifest_RejectsAnyUnapprovedStation(int x, int y, int z)
    {
        var manifest = Manifest(DefaultEntry()) with { Banker = new Decoration.BankerPin(x, y, z) };
        Assert.Throws<InvalidOperationException>(() => Decoration.ValidateManifest(manifest));
    }

    [Fact]
    public void BankerManifest_RejectsDecorOnTheBankerStandingTile()
    {
        var manifest = BankerManifest() with { Items = [DefaultEntry() with { X = 3, Y = 4 }] };
        Assert.Throws<InvalidOperationException>(() => Decoration.ValidateManifest(manifest));
    }

    [Fact]
    public void Banker_IsANormalStationaryBlessedVendorOutsideEventOwnershipWithNoSeedCurrency()
    {
        Setup();
        var banker = InstallBanker();
        Assert.IsType<Banker>(banker);
        Assert.Equal("the banker", banker.Title);
        Assert.True(banker.Blessed);
        Assert.True(banker.CantWalk);
        Assert.False(banker.Frozen);
        Assert.Equal(new Point3D(5387, 812, 0), banker.Location);
        Assert.Equal(banker.Location, banker.Home);
        Assert.Same(Map.Felucca, banker.HomeMap);
        Assert.Equal(0, banker.RangeHome);
        Assert.Empty(_controller.OwnedCreatures);
        Assert.Empty(_controller.OwnedLoot);
        Assert.Null(banker.Spawner);
        Assert.False(banker.Controlled);
        Assert.False(banker.Summoned);
        Assert.NotNull(banker.FindItemOnLayer(Layer.Shoes));
        Assert.Empty(CarriedGold(banker.Backpack));
        Assert.Equal(Decoration.DescribeBanker(banker), _receipt.Banker);
        Assert.Same(banker, _map.LastAllowedBanker);
    }

    [Fact]
    public void Banker_StandardSpeechOpensThePlayersBankAndWithdrawsOnlyTheirExistingFunds()
    {
        Setup();
        var banker = InstallBanker();
        var customer = new Mobile { Player = true, Body = 0x190 };
        _mobiles.Add(customer);
        customer.AddItem(new Backpack());
        customer.MoveToWorld(new Point3D(5387, 813, 0), Map.Felucca);
        var speech = new SpeechEventArgs(customer, "bank", MessageType.Regular, 0x3B2, [0x0002]);
        Assert.True(banker.HandlesOnSpeech(customer));
        banker.OnSpeech(speech);
        Assert.True(speech.Handled);
        Assert.True(customer.BankBox.Opened);
        customer.BankBox.DropItem(new Gold(100));
        banker.OnSpeech(new SpeechEventArgs(customer, "withdraw 40", MessageType.Regular, 0x3B2, [0x0000]));
        Assert.Equal(60, Banker.GetBalance(customer));
        Assert.Equal(40, Assert.Single(CarriedGold(customer.Backpack)).Amount);
        customer.BankBox.Close();
        customer.Criminal = true;
        banker.OnSpeech(new SpeechEventArgs(customer, "bank", MessageType.Regular, 0x3B2, [0x0002]));
        Assert.False(customer.BankBox.Opened);
        Assert.Empty(CarriedGold(banker.Backpack));
    }

    [Fact]
    public void Banker_ExistingReceiptVerifiesTheSameNpcWithoutCreatingOrWritingAnything()
    {
        Setup();
        var banker = InstallBanker();
        var session = new Decoration.Session(_controller, BankerManifest(), "hash", _receipt, _map,
            _ => throw new InvalidOperationException("Verification must be read-only."));
        Finish(session);
        Assert.True(session.Success, session.Error);
        Assert.Equal(0, session.CreatedBankerCount);
        Assert.Equal(0, session.CreatedCount);
        Assert.Same(banker, World.FindMobile((Serial)_receipt.Banker.Serial));
        Assert.Empty(_controller.OwnedCreatures);
    }

    [Theory]
    [InlineData("Moved")]
    [InlineData("CanWalk")]
    [InlineData("Deleted")]
    [InlineData("Name")]
    [InlineData("WrongSerial")]
    [InlineData("MissingReceipt")]
    public void Banker_InvalidRecordedNpcIsNeverRecreatedOrRepaired(string invalid)
    {
        Setup();
        var banker = InstallBanker();
        var receipt = _receipt;
        switch (invalid)
        {
            case "Moved":
                banker.MoveToWorld(new Point3D(5388, 812, 0), Map.Felucca);
                break;
            case "CanWalk":
                banker.CantWalk = false;
                break;
            case "Deleted":
                banker.Delete();
                break;
            case "Name":
                banker.Name = "changed by staff";
                break;
            case "WrongSerial":
                receipt = receipt with { Banker = receipt.Banker with { Serial = 0 } };
                break;
            case "MissingReceipt":
                receipt = receipt with { Banker = null };
                break;
        }
        Assert.Throws<InvalidOperationException>(() => new Decoration.Session(_controller, BankerManifest(), "hash", receipt, _map, _ => { }));
        Assert.Equal(invalid == "Deleted", banker.Deleted);
        Assert.Empty(_controller.OwnedCreatures);
    }

    [Fact]
    public void Banker_RollbackDeletesOnlyTheNewNpcAndDecorWhenAnotherMobileEnters()
    {
        Setup();
        var session = Session(BankerManifest());
        AdvanceUntilBanker(session);
        var banker = (Banker)World.FindMobile((Serial)_receipt.Banker.Serial);
        _mobiles.Add(banker);
        var other = new Mobile();
        _mobiles.Add(other);
        other.MoveToWorld(new Point3D(5388, 812, 0), Map.Felucca);
        Finish(session);
        Assert.False(session.Success);
        Assert.True(banker.Deleted);
        Assert.False(other.Deleted);
        Assert.Equal(722, _controller.ManagedItems.Count);
        Assert.Equal(Decoration.OriginalReturnPoint, _gate.Location);
        Assert.Equal("rolled-back", _receipt.Stage);
    }

    [Fact]
    public void Banker_PersistenceFailureDeletesTheNewNpcEvenIfItsReceiptCannotBeWritten()
    {
        Setup();
        Banker created = null;
        var session = new Decoration.Session(_controller, BankerManifest(), "hash", null, _map, receipt =>
        {
            if (receipt.Banker != null)
            {
                created ??= (Banker)World.FindMobile((Serial)receipt.Banker.Serial);
                throw new InvalidOperationException("Simulated banker receipt disk failure.");
            }
        });
        Finish(session);
        Assert.False(session.Success);
        Assert.NotNull(created);
        Assert.True(created.Deleted);
        Assert.Equal(722, _controller.ManagedItems.Count);
        Assert.Equal(Decoration.OriginalReturnPoint, _gate.Location);
    }

    [Fact]
    public void Banker_UsesNativeSerializationForStationarityHomeAndBlessedState()
    {
        Setup();
        var banker = InstallBanker();
        var writer = new BufferWriter(true);
        banker.Serialize(writer);
        var bytes = writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();
        var restored = new Banker(World.NewMobile);
        _mobiles.Add(restored);
        var reader = new BufferReader(bytes);
        restored.Deserialize(reader);
        Assert.Equal(bytes.Length, reader.Position);
        Assert.True(restored.CantWalk);
        Assert.True(restored.Blessed);
        Assert.False(restored.Frozen);
        Assert.Equal(banker.Home, restored.Home);
        Assert.Equal(banker.HomeMap, restored.HomeMap);
        Assert.Equal(0, restored.RangeHome);
        Assert.Equal(banker.Name, restored.Name);
        Assert.Equal(banker.Title, restored.Title);
    }

    [SkippableFact]
    public void ApprovedManifest_ParsesAndKeepsArrivalArtworkNonblocking()
    {
        var path = Environment.GetEnvironmentVariable("PVP_LOBBY_MANIFEST");
        Skip.If(string.IsNullOrWhiteSpace(path), "Set PVP_LOBBY_MANIFEST to validate the reviewed deployment artifact.");
        var bytes = File.ReadAllBytes(path!);
        Assert.Equal("ee79d90903e08abce8be3801f992f35a6305b32d94cd394da8df203ec165b949", Convert.ToHexStringLower(SHA256.HashData(bytes)));
        var manifest = Decoration.ParseManifest(bytes);
        Assert.Equal(306, manifest.Items.Length);
        Assert.Equal(new Decoration.BankerPin(3, 4, 0), manifest.Banker);
        foreach (var entry in manifest.Items)
        {
            Assert.InRange(entry.ItemId, 1, TileData.ItemTable.Length - 1);
            if (entry.X is >= 10 and <= 14 && entry.Y is >= 10 and <= 14)
            {
                Assert.True(Decoration.IsArrivalArtwork(entry.ItemId));
                var data = TileData.ItemTable[entry.ItemId];
                Assert.InRange(data.Height, 0, 1);
                Assert.Equal((TileFlag)0, data.Flags & (TileFlag.Impassable | TileFlag.Surface));
            }
        }
    }

    [Theory]
    [InlineData("WrongId")]
    [InlineData("WrongController")]
    [InlineData("Empty")]
    [InlineData("TooMany")]
    [InlineData("DuplicateId")]
    [InlineData("DuplicatePlacement")]
    [InlineData("NullEntry")]
    [InlineData("OutsideX")]
    [InlineData("OutsideY")]
    [InlineData("HighZ")]
    [InlineData("NegativeZ")]
    [InlineData("ArtOutOfRange")]
    [InlineData("MissingRole")]
    [InlineData("OldGate")]
    [InlineData("NewGate")]
    [InlineData("ArrivalProp")]
    [InlineData("RaisedBlood")]
    [InlineData("HuedBlood")]
    public void Manifest_RejectsUnapprovedOrUnsafeEntries(string invalid)
    {
        var entry = DefaultEntry();
        var manifest = invalid switch
        {
            "WrongId" => Manifest(entry) with { InstallationId = "other" },
            "WrongController" => Manifest(entry) with { ControllerSerial = 1 },
            "Empty" => Manifest(),
            "TooMany" => Manifest(new Decoration.Entry[1201]),
            "DuplicateId" => Manifest(entry, entry with { X = 3 }),
            "DuplicatePlacement" => Manifest(entry, entry with { Id = "two" }),
            "NullEntry" => Manifest((Decoration.Entry)null),
            "OutsideX" => Manifest(entry with { X = 25 }),
            "OutsideY" => Manifest(entry with { Y = -1 }),
            "HighZ" => Manifest(entry with { Z = 36 }),
            "NegativeZ" => Manifest(entry with { Z = -1 }),
            "ArtOutOfRange" => Manifest(entry with { ItemId = 65536 }),
            "MissingRole" => Manifest(entry with { Role = "" }),
            "OldGate" => Manifest(entry with { X = 12, Y = 15 }),
            "NewGate" => Manifest(entry with { X = 20, Y = 22 }),
            "ArrivalProp" => Manifest(entry with { X = 12, Y = 12, ItemId = 0x122A }),
            "RaisedBlood" => Manifest(entry with { X = 12, Y = 12, ItemId = 0x1CF1, Z = 1 }),
            "HuedBlood" => Manifest(entry with { X = 12, Y = 12, ItemId = 0x1CF1, Hue = 1 }),
            _ => throw new ArgumentOutOfRangeException(nameof(invalid))
        };
        Assert.Throws<InvalidOperationException>(() => Decoration.ValidateManifest(manifest));
    }

    [Fact]
    public void Install_PreservesAllBaseSerialsAndGatePropertiesAndOwnsOnlyNewStatics()
    {
        Setup();
        var original = _controller.ManagedItems.ToArray();
        var gateSerial = _gate.Serial;
        var session = Session(Manifest(DefaultEntry()));
        Finish(session);
        Assert.True(session.Success, session.Error);
        Assert.Equal(1, session.CreatedCount);
        Assert.Equal(1, session.VerifiedCount);
        Assert.Equal("installed", _receipt.Stage);
        Assert.Equal(721, _receipt.BaseItems.Length);
        Assert.Equal(723, _controller.ManagedItems.Count);
        foreach (var item in original)
        {
            Assert.False(item.Deleted);
            Assert.Contains(item, _controller.ManagedItems);
        }
        Assert.Equal(gateSerial, _gate.Serial);
        Assert.Equal(ExtractionDungeonLayout.LobbyReturnPoint, _gate.Location);
        Assert.Equal(ExtractionEventGateKind.LobbyReturn, _gate.Kind);
        Assert.Same(_controller, _gate.Controller);
        Assert.Equal(0x482, _gate.Hue);
        Assert.Equal(Decoration.OriginalReturnPoint.X, _receipt.GateBefore.X);
        Assert.Equal(Decoration.OriginalReturnPoint.Y, _receipt.GateBefore.Y);
        var decor = World.FindItem((Serial)Assert.Single(_receipt.Items).Serial);
        Assert.IsType<Static>(decor);
        Assert.False(decor.Movable);
        Assert.Null(decor.Name);
    }

    [Fact]
    public void TownStone_InstallAndReplayPreserveItsExactIdentityOwnershipAndOccupiedTile()
    {
        Setup();
        var stone = AddTownStone();
        _map.BlockedPoints.Add(stone.Location);
        var session = Session(BankerManifest());
        Finish(session);
        Assert.True(session.Success, session.Error);
        Assert.Equal(1, session.CreatedCount);
        Assert.Equal(1, session.CreatedBankerCount);
        Assert.True(Decoration.IsApprovedTownStone(stone));
        Assert.DoesNotContain(stone, _controller.ManagedItems);
        Assert.Contains(stone, _townController.AuxiliaryItems);
        Assert.DoesNotContain(_receipt.Items, entry => entry.Serial == stone.Serial.Value);
        Assert.DoesNotContain(_receipt.BaseItems, entry => entry.Serial == stone.Serial.Value);
        Assert.DoesNotContain(stone.Location, _map.FitChecks);
        Assert.Contains(ExtractionDungeonLayout.LobbyEntry, _map.FitChecks);
        Assert.Equal(33, _map.FitChecks.Count(point => point != Decoration.BankerPosition(BankerManifest().Banker)));

        var replay = new Decoration.Session(_controller, BankerManifest(), "hash", _receipt, _map,
            _ => throw new InvalidOperationException("Verification must not write."));
        Finish(replay);
        Assert.True(replay.Success, replay.Error);
        Assert.Equal(0, replay.CreatedCount);
        Assert.Equal(0, replay.CreatedBankerCount);
        Assert.False(stone.Deleted);
        Assert.Equal(Decoration.TownStonePoint, stone.Location);
        Assert.Same(stone, World.FindItem((Serial)Decoration.TownStoneSerial));
    }

    [Theory]
    [InlineData("Unowned")]
    [InlineData("WrongSerial")]
    [InlineData("RuntimeMissing")]
    [InlineData("Moved")]
    [InlineData("Art")]
    [InlineData("Hue")]
    [InlineData("Movable")]
    [InlineData("Hidden")]
    [InlineData("Name")]
    public void TownStone_RejectsUnownedLookalikeOrAlteredStoneBeforeCreatingAnything(string invalid)
    {
        Setup();
        var stone = AddTownStone(invalid != "Unowned", invalid == "WrongSerial" ? Decoration.TownStoneSerial + 1 : Decoration.TownStoneSerial);
        switch (invalid)
        {
            case "RuntimeMissing":
                SetTownController(null);
                break;
            case "Moved":
                stone.MoveToWorld(new Point3D(5398, 820, 0), Map.Felucca);
                break;
            case "Art":
                stone.ItemID = 0xED5;
                break;
            case "Hue":
                stone.Hue = 0;
                break;
            case "Movable":
                stone.Movable = true;
                break;
            case "Hidden":
                stone.Visible = false;
                break;
            case "Name":
                stone.Name = "another stone";
                break;
        }
        var session = Session(Manifest(DefaultEntry()));
        Finish(session);
        Assert.False(session.Success);
        Assert.Equal(0, session.CreatedCount);
        Assert.Null(_receipt);
        Assert.False(stone.Deleted);
    }

    [Fact]
    public void TownStone_DoesNotPermitOtherUnownedItemsOnItsTile()
    {
        Setup();
        var stone = AddTownStone();
        var extra = new Static(0xECA);
        _items.Add(extra);
        extra.MoveToWorld(stone.Location, stone.Map);
        _map.ExtraItems.Add(extra);
        var session = Session(Manifest(DefaultEntry()));
        Finish(session);
        Assert.False(session.Success);
        Assert.Equal(0, session.CreatedCount);
        Assert.False(extra.Deleted);
        Assert.False(stone.Deleted);
    }

    [Fact]
    public void TownStone_NeighboringArrivalObstructionStillRollsBackOnlyNewDecoration()
    {
        Setup();
        var stone = AddTownStone();
        _map.BlockedPoints.Add(stone.Location);
        _map.BlockedPoints.Add(ExtractionDungeonLayout.LobbyEntry);
        var session = Session(Manifest(DefaultEntry()));
        Finish(session);
        Assert.False(session.Success);
        Assert.Equal(1, session.CreatedCount);
        Assert.Equal(722, _controller.ManagedItems.Count);
        Assert.Equal(Decoration.OriginalReturnPoint, _gate.Location);
        Assert.True(Decoration.IsApprovedTownStone(stone));
        Assert.Contains(stone, _townController.AuxiliaryItems);
        Assert.Equal("rolled-back", _receipt.Stage);
    }

    [Fact]
    public void TownStone_ConcurrentMovementRollsBackWithoutMovingOrDeletingTheStone()
    {
        Setup();
        var stone = AddTownStone();
        var session = Session(ManyEntries());
        AdvanceUntilCreated(session);
        var moved = new Point3D(5398, 820, 0);
        stone.MoveToWorld(moved, Map.Felucca);
        Finish(session);
        Assert.False(session.Success);
        Assert.Equal(722, _controller.ManagedItems.Count);
        Assert.Equal(Decoration.OriginalReturnPoint, _gate.Location);
        Assert.False(stone.Deleted);
        Assert.Equal(moved, stone.Location);
        Assert.Contains(stone, _townController.AuxiliaryItems);
    }

    [Fact]
    public void CompletedReceipt_VerifiesSameSerialsWithoutRecreatingOrWritingEvenWithAnActiveOccupiedLobby()
    {
        Setup();
        var manifest = Manifest(DefaultEntry());
        var installed = Session(manifest);
        Finish(installed);
        Assert.True(installed.Success, installed.Error);
        var serials = _controller.ManagedItems.Select(item => item.Serial).ToArray();
        _map.HasMobiles = true;
        typeof(ExtractionEventController).GetProperty(nameof(ExtractionEventController.Phase))!
            .SetValue(_controller, ExtractionEventPhase.Active);
        var replay = new Decoration.Session(_controller, manifest, "hash", _receipt, _map,
            _ => throw new InvalidOperationException("Read-only verification wrote a receipt."));
        Finish(replay);
        Assert.True(replay.Success, replay.Error);
        Assert.Equal(0, replay.CreatedCount);
        Assert.Equal(serials, _controller.ManagedItems.Select(item => item.Serial));
    }

    [Fact]
    public void ExplicitReceiptVerification_IgnoresLaterUnrelatedPlayerItemsWithoutAdoptingOrChangingThem()
    {
        Setup();
        var manifest = Manifest(DefaultEntry());
        Finish(Session(manifest));
        var bag = new Bag();
        _items.Add(bag);
        bag.MoveToWorld(new Point3D(5388, 812, 0), Map.Felucca);
        _map.ExtraItems.Add(bag);
        var verify = new Decoration.Session(_controller, manifest, "hash", _receipt, _map, _ =>
            throw new InvalidOperationException("Read-only verification must not write."));
        Finish(verify);
        Assert.True(verify.Success, verify.Error);
        Assert.False(bag.Deleted);
        Assert.True(bag.Movable);
        Assert.DoesNotContain(bag, _controller.ManagedItems);
        Assert.Equal(723, _controller.ManagedItems.Count);
    }

    [Theory]
    [InlineData("Incomplete")]
    [InlineData("WrongHash")]
    [InlineData("WrongSerial")]
    [InlineData("MissingBase")]
    public void Receipt_RejectsIncompleteOrMismatchedReceiptsWithoutReplay(string invalid)
    {
        Setup();
        var manifest = Manifest(DefaultEntry());
        Finish(Session(manifest));
        var before = _controller.ManagedItems.Count;
        var receipt = invalid switch
        {
            "Incomplete" => _receipt with { Stage = "installing" },
            "WrongHash" => _receipt with { ManifestSha256 = "other" },
            "WrongSerial" => _receipt with { ControllerSerial = 1 },
            "MissingBase" => _receipt with { BaseItems = [] },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid))
        };
        Assert.Throws<InvalidOperationException>(() => new Decoration.Session(_controller, manifest, "hash", receipt, _map, _ => { }));
        Assert.Equal(before, _controller.ManagedItems.Count);
    }

    [Theory]
    [InlineData("MissingFloor")]
    [InlineData("DuplicateFloor")]
    [InlineData("WrongCorner")]
    [InlineData("UnownedItem")]
    [InlineData("NativeStatics")]
    [InlineData("MovedGate")]
    public void Survey_RefusesAnyUnexpectedBaseBeforeCreatingAnything(string change)
    {
        Setup();
        var floor = _controller.ManagedItems.First(item => item.ItemID == 0x519);
        switch (change)
        {
            case "MissingFloor":
                floor.Delete();
                break;
            case "DuplicateFloor":
                Add(new Static(0x519), floor.Location);
                break;
            case "WrongCorner":
                _controller.ManagedItems.First(item => item.ItemID == 0x59).ItemID = 0x57;
                break;
            case "UnownedItem":
                var unowned = new Static(0xECA);
                _items.Add(unowned);
                unowned.MoveToWorld(new Point3D(5385, 809, 0), Map.Felucca);
                _map.ExtraItems.Add(unowned);
                break;
            case "NativeStatics":
                _map.NativeInvalid = true;
                break;
            case "MovedGate":
                _gate.MoveToWorld(new Point3D(5396, 824, 0), Map.Felucca);
                break;
        }
        var session = Session(Manifest(DefaultEntry()));
        Finish(session);
        Assert.False(session.Success);
        Assert.Equal(0, session.CreatedCount);
        Assert.Null(_receipt);
    }

    [Fact]
    public void CreationIsBatchedAndFailureRollsBackOnlyNewDecorAndRestoresGate()
    {
        Setup();
        var manifest = ManyEntries();
        var original = _controller.ManagedItems.ToArray();
        var session = Session(manifest);
        AdvanceUntilCreated(session);
        Assert.InRange(session.CreatedCount, 1, Decoration.BatchSize);
        _map.HasMobiles = true;
        Finish(session);
        Assert.False(session.Success);
        Assert.Equal(original, _controller.ManagedItems);
        Assert.All(original, item => Assert.False(item.Deleted));
        Assert.Equal(Decoration.OriginalReturnPoint, _gate.Location);
        Assert.Equal("rolled-back", _receipt.Stage);
    }

    [Fact]
    public void PersistenceFailureRollsBackNewStaticsEvenWhenTheReceiptCannotBeUpdated()
    {
        Setup();
        var writes = 0;
        var session = new Decoration.Session(_controller, ManyEntries(), "hash", null, _map, _ =>
        {
            if (++writes > 1)
            {
                throw new InvalidOperationException("Simulated disk failure.");
            }
        });
        Finish(session);
        Assert.False(session.Success);
        Assert.Equal(722, _controller.ManagedItems.Count);
        Assert.Equal(Decoration.OriginalReturnPoint, _gate.Location);
    }

    [Fact]
    public void FinalSurveyDetectsConcurrentBaseChangeWithoutUndoingThatExternalChange()
    {
        Setup();
        var session = Session(ManyEntries());
        AdvanceUntilCreated(session);
        var floor = _controller.ManagedItems.First(item => item.ItemID == 0x519);
        floor.Hue = 7;
        Finish(session);
        Assert.False(session.Success);
        Assert.Equal(7, floor.Hue);
        Assert.False(floor.Deleted);
        Assert.Equal(722, _controller.ManagedItems.Count);
        Assert.Equal(Decoration.OriginalReturnPoint, _gate.Location);
    }

    [Fact]
    public void BlockedArrivalRollsBackWithoutDeletingAnyBaseObjects()
    {
        Setup();
        _map.Fit = false;
        var session = Session(Manifest(DefaultEntry()));
        Finish(session);
        Assert.False(session.Success);
        Assert.Equal(722, _controller.ManagedItems.Count);
        Assert.Equal(Decoration.OriginalReturnPoint, _gate.Location);
    }

    [Fact]
    public void AlteredRecordedDecorationFailsReadOnlyVerificationWithoutDeletingOrRepairingIt()
    {
        Setup();
        var manifest = Manifest(DefaultEntry());
        Finish(Session(manifest));
        var decor = World.FindItem((Serial)_receipt.Items[0].Serial);
        decor.Hue = 99;
        var verify = new Decoration.Session(_controller, manifest, "hash", _receipt, _map, _ => { });
        Finish(verify);
        Assert.False(verify.Success);
        Assert.False(decor.Deleted);
        Assert.Equal(99, decor.Hue);
        Assert.Equal(723, _controller.ManagedItems.Count);
    }

    [Fact]
    public void InstallRefusesAnOccupiedOrNonWaitingLobby()
    {
        Setup();
        _map.HasMobiles = true;
        Assert.Throws<InvalidOperationException>(() => Session(Manifest(DefaultEntry())));
        _map.HasMobiles = false;
        typeof(ExtractionEventController).GetProperty(nameof(ExtractionEventController.Phase))!
            .SetValue(_controller, ExtractionEventPhase.Active);
        Assert.Throws<InvalidOperationException>(() => Session(Manifest(DefaultEntry())));
        Assert.Equal(722, _controller.ManagedItems.Count);
    }

    private void Setup()
    {
        _controller = new ExtractionEventController((Serial)Decoration.ControllerSerial);
        _map = new FakeMap(_controller);
        for (var y = 0; y < 25; y++)
        {
            for (var x = 0; x < 25; x++)
            {
                var point = new Point3D(5384 + x, 808 + y, 0);
                Add(new Static(0x519), point);
                if (x is 0 or 24 || y is 0 or 24)
                {
                    Add(new Static(x is 0 or 24 && y is 0 or 24 ? 0x59 : x is 0 or 24 ? 0x57 : 0x58), point);
                }
            }
        }
        _gate = new ExtractionEventGate(_controller, ExtractionEventGateKind.LobbyReturn);
        Add(_gate, Decoration.OriginalReturnPoint);
    }

    private void Add(Item item, Point3D point)
    {
        _items.Add(item);
        _controller.AddManagedItem(item);
        item.MoveToWorld(point, Map.Felucca);
    }

    private TownStruggleStone AddTownStone(bool owned = true, uint serial = Decoration.TownStoneSerial)
    {
        _townController = new TownStruggleController();
        SetTownController(_townController);
        var stone = new TownStruggleStone((Serial)serial)
        {
            ItemID = 0xED4, Hue = 0x489, Amount = 1, Movable = false, Visible = true
        };
        World.AddEntity(stone);
        _items.Add(stone);
        _map.ExtraItems.Add(stone);
        if (owned)
        {
            _townController.AddAuxiliaryItem(stone);
        }
        stone.MoveToWorld(Decoration.TownStonePoint, Map.Felucca);
        return stone;
    }

    private static void SetTownController(TownStruggleController controller) =>
        typeof(TownStruggleRuntime).GetProperty(nameof(TownStruggleRuntime.Controller))!.SetValue(null, controller);

    private Decoration.Session Session(Decoration.Manifest manifest) =>
        new(_controller, manifest, "hash", null, _map, receipt => _receipt = receipt);

    private Banker InstallBanker()
    {
        var session = Session(BankerManifest());
        Finish(session);
        Assert.True(session.Success, session.Error);
        Assert.Equal(1, session.CreatedBankerCount);
        var banker = Assert.IsType<Banker>(World.FindMobile((Serial)_receipt.Banker.Serial));
        _mobiles.Add(banker);
        return banker;
    }

    private void AdvanceUntilBanker(Decoration.Session session)
    {
        for (var i = 0; i < 100 && _receipt?.Banker == null && !session.Complete; i++)
        {
            session.Process(Decoration.BatchSize);
        }
        Assert.NotNull(_receipt?.Banker);
        Assert.False(session.Complete);
    }

    private static Decoration.Entry DefaultEntry() => new("pvp-0001", 0xECA, 0, 2, 2, 0, "bones");
    private static Gold[] CarriedGold(Container container)
    {
        var gold = new List<Gold>();
        foreach (var pile in container.FindItemsByType<Gold>())
        {
            gold.Add(pile);
        }
        return gold.ToArray();
    }
    private static Decoration.Manifest Manifest(params Decoration.Entry[] entries) =>
        new(Decoration.InstallationId, Decoration.ControllerSerial, entries);
    private static Decoration.Manifest BankerManifest() => Manifest(DefaultEntry()) with { Banker = new Decoration.BankerPin(3, 4, 0) };
    private static Decoration.Manifest ManyEntries() => Manifest(Enumerable.Range(0, 40)
        .Select(index => new Decoration.Entry($"pvp-{index}", 0xECA, 0, 1 + index % 8, 1 + index / 8, 0, "bones")).ToArray());
    private static void AdvanceUntilCreated(Decoration.Session session)
    {
        for (var i = 0; i < 100 && session.CreatedCount == 0 && !session.Complete; i++)
        {
            session.Process(Decoration.BatchSize);
        }
        Assert.True(session.CreatedCount > 0, session.Error);
    }
    private static void Finish(Decoration.Session session)
    {
        for (var i = 0; i < 500 && !session.Complete; i++)
        {
            session.Process(Decoration.BatchSize);
        }
        Assert.True(session.Complete, "The bounded session did not complete.");
    }

    private sealed class FakeMap(ExtractionEventController controller) : Decoration.ILobbyMap
    {
        public bool HasMobiles { get; set; }
        public bool NativeInvalid { get; set; }
        public bool Fit { get; set; } = true;
        public List<Item> ExtraItems { get; } = [];
        public HashSet<Point3D> BlockedPoints { get; } = [];
        public List<Point3D> FitChecks { get; } = [];
        public Mobile LastAllowedBanker { get; private set; }
        public void RequireEmptyOfMobiles(Mobile allowedBanker)
        {
            LastAllowedBanker = allowedBanker;
            if (HasMobiles)
            {
                throw new InvalidOperationException("The lobby is occupied.");
            }
            foreach (var mobile in Map.Felucca.GetMobilesInBounds<Mobile>(ExtractionDungeonLayout.LobbyBounds))
            {
                if (mobile != allowedBanker)
                {
                    throw new InvalidOperationException("An unrelated mobile occupies the lobby.");
                }
            }
        }
        public void SurveyNativeTile(Point3D point)
        {
            if (NativeInvalid)
            {
                throw new InvalidOperationException("Unexpected native statics.");
            }
        }
        public IEnumerable<Item> ItemsAt(Point3D point) => controller.ManagedItems.Concat(ExtraItems)
            .Where(item => !item.Deleted && item.X == point.X && item.Y == point.Y);
        public bool CanFit(Point3D point)
        {
            FitChecks.Add(point);
            return Fit && !BlockedPoints.Contains(point);
        }
    }
}
