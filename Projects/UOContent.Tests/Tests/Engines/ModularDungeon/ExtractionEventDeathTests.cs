using System;
using System.Collections.Generic;
using System.Reflection;
using Server;
using Server.Engines.ModularDungeon;
using Server.Items;
using Server.Mobiles;
using Xunit;

namespace UOContent.Tests.Engines.ModularDungeon;

[Collection("Sequential UOContent Tests")]
public class ExtractionEventDeathTests : IDisposable
{
    private readonly List<IEntity> _created = [];
    private readonly Expansion _expansion = Core.Expansion;
    private readonly bool _insurance = ServerFeatureFlags.InsuranceEnabled;
    private readonly CreateCorpseHandler _corpseHandler = Mobile.CreateCorpseHandler;
    private readonly long _ticks = Core.TickCount;
    private readonly DateTime _now = Core.Now;

    public ExtractionEventDeathTests()
    {
        Core.Expansion = Expansion.T2A;
        Timer.Init(Core.TickCount);
    }

    public void Dispose()
    {
        ExtractionEventDeath.ControllerLookup = null;
        for (var i = _created.Count - 1; i >= 0; i--)
        {
            _created[i].Delete();
        }

        Mobile.CreateCorpseHandler = _corpseHandler;
        ServerFeatureFlags.InsuranceEnabled = _insurance;
        Core.Expansion = _expansion;
        Core._tickCount = _ticks;
        Core._now = _now;
        Timer.Init(Core.TickCount);
    }

    [Theory]
    [InlineData("Blessed", false)]
    [InlineData("Blessed", true)]
    [InlineData("Newbied", false)]
    [InlineData("Newbied", true)]
    [InlineData("Insured", false)]
    [InlineData("Insured", true)]
    [InlineData("BlessedFor", false)]
    [InlineData("BlessedFor", true)]
    [InlineData("Immovable", false)]
    [InlineData("Immovable", true)]
    public void ParticipantPossessions_DropBeforeProtectionRules_WithoutChangingTheirFlags(string protection, bool equipped)
    {
        var player = Player();
        player.Young = true;
        Enroll(player, ExtractionEventPhase.Active);
        var item = Track(new Item(0xF5E) { Layer = Layer.OneHanded });
        switch (protection)
        {
            case "Blessed": item.LootType = LootType.Blessed; break;
            case "Newbied": item.LootType = LootType.Newbied; break;
            case "Insured": ServerFeatureFlags.InsuranceEnabled = true; item.Insured = true; break;
            case "BlessedFor": item.BlessedFor = player; break;
            case "Immovable": item.Movable = false; break;
        }

        if (equipped)
        {
            player.AddItem(item);
        }
        else
        {
            player.Backpack.DropItem(item);
        }

        var lootType = item.LootType;
        var insured = item.Insured;
        var blessedFor = item.BlessedFor;
        var movable = item.Movable;

        Assert.True(ExtractionEventDeath.MustDropOnDeath(player, item));
        Assert.Equal(DeathMoveResult.MoveToCorpse, equipped ? player.GetParentMoveResultFor(item) : player.GetInventoryMoveResultFor(item));
        Assert.Equal(lootType, item.LootType);
        Assert.Equal(insured, item.Insured);
        Assert.Same(blessedFor, item.BlessedFor);
        Assert.Equal(movable, item.Movable);
        Assert.Null(player.FindBankNoCreate());
    }

    [Theory]
    [InlineData(ExtractionEventPhase.Active, true)]
    [InlineData(ExtractionEventPhase.Finishing, true)]
    [InlineData(ExtractionEventPhase.Faulted, true)]
    [InlineData(ExtractionEventPhase.Waiting, false)]
    [InlineData(ExtractionEventPhase.Starting, false)]
    public void ForcedDrop_IsScopedToARegisteredMatchOrItsRecovery(ExtractionEventPhase phase, bool expected)
    {
        var player = Player();
        Enroll(player, phase);
        var item = Track(new Gold(1) { LootType = LootType.Blessed });
        player.Backpack.DropItem(item);

        Assert.Equal(expected, ExtractionEventDeath.MustDropOnDeath(player, item));
        Assert.Equal(expected ? DeathMoveResult.MoveToCorpse : DeathMoveResult.MoveToBackpack,
            player.GetInventoryMoveResultFor(item));
    }

    [Theory]
    [InlineData("Unenrolled")]
    [InlineData("Lobby")]
    [InlineData("Outside")]
    [InlineData("WrongFacet")]
    [InlineData("WrongHeight")]
    [InlineData("OfflineLobby")]
    [InlineData("OfflineWrongFacet")]
    [InlineData("DeletedController")]
    [InlineData("NoController")]
    public void OtherDeaths_RetainNormalProtection(string scope)
    {
        var player = Player();
        var controller = Enroll(player, ExtractionEventPhase.Active);
        var item = Track(new Gold(1) { LootType = LootType.Blessed });
        player.Backpack.DropItem(item);
        switch (scope)
        {
            case "Unenrolled": ParticipantSet(controller).Remove(player); break;
            case "Lobby": player.MoveToWorld(ExtractionDungeonLayout.LobbyEntry, Map.Felucca); break;
            case "Outside": player.MoveToWorld(new Point3D(100, 100, 0), Map.Felucca); break;
            case "WrongFacet": player.MoveToWorld(player.Location, Map.Trammel); break;
            case "WrongHeight": player.MoveToWorld(new Point3D(player.X, player.Y, 100), Map.Felucca); break;
            case "OfflineLobby":
                player.LogoutMap = Map.Felucca;
                player.LogoutLocation = ExtractionDungeonLayout.LobbyEntry;
                player.MoveToWorld(Point3D.Zero, Map.Internal);
                break;
            case "OfflineWrongFacet":
                player.LogoutMap = Map.Trammel;
                player.LogoutLocation = player.Location;
                player.MoveToWorld(Point3D.Zero, Map.Internal);
                break;
            case "DeletedController": controller.Delete(); break;
            case "NoController": ExtractionEventDeath.ControllerLookup = null; break;
        }

        Assert.False(ExtractionEventDeath.MustDropOnDeath(player, item));
        Assert.Equal(DeathMoveResult.MoveToBackpack, player.GetInventoryMoveResultFor(item));
        Assert.Equal(LootType.Blessed, item.LootType);
    }

    [Fact]
    public void BankAndStructuralItems_AreNeverForcedOntoTheCorpse()
    {
        var player = Player();
        Enroll(player, ExtractionEventPhase.Active);
        var bank = player.BankBox;
        var bankGold = Track(new Gold(700));
        var nested = Track(new Bag());
        bank.DropItem(nested);
        nested.DropItem(bankGold);
        Assert.False(ExtractionEventDeath.MustDropOnDeath(player, bank));
        Assert.False(ExtractionEventDeath.MustDropOnDeath(player, nested));
        Assert.False(ExtractionEventDeath.MustDropOnDeath(player, bankGold));
        Assert.False(ExtractionEventDeath.MustDropOnDeath(player, player.Backpack));
        foreach (var layer in new[] { Layer.Hair, Layer.FacialHair, Layer.Mount })
        {
            var structure = Track(new Item(1) { Layer = layer, Movable = false });
            player.AddItem(structure);
            Assert.False(ExtractionEventDeath.MustDropOnDeath(player, structure));
            Assert.Equal(DeathMoveResult.RemainEquipped, player.GetParentMoveResultFor(structure));
        }

        var carriedExtraBackpack = Track(new Backpack());
        player.Backpack.DropItem(carriedExtraBackpack);
        Assert.True(ExtractionEventDeath.MustDropOnDeath(player, carriedExtraBackpack));
        Assert.Same(nested, bankGold.Parent);
    }

    [Fact]
    public void RealDeath_CreatesALootableCorpseWithEquipmentAndNestedBlessedPossessions_BankRemainsIntact()
    {
        var player = Player();
        Enroll(player, ExtractionEventPhase.Active);
        var sword = Track(new Item(0xF5E) { Layer = Layer.OneHanded, LootType = LootType.Blessed });
        player.AddItem(sword);
        var robe = Track(new Item(0x1F03) { Layer = Layer.OuterTorso, LootType = LootType.Newbied });
        player.AddItem(robe);
        var bag = Track(new Bag { LootType = LootType.Blessed });
        player.Backpack.DropItem(bag);
        var treasure = Track(new Gold(321) { LootType = LootType.Blessed });
        bag.DropItem(treasure);
        var bankGold = Track(new Gold(765));
        player.BankBox.DropItem(bankGold);
        Mobile.CreateCorpseHandler = Corpse.Mobile_CreateCorpseHandler;

        player.Kill();

        Assert.False(player.Alive);
        var corpse = Track(Assert.IsType<Corpse>(player.Corpse));
        Assert.Same(corpse, sword.Parent);
        Assert.Same(corpse, robe.Parent);
        Assert.Same(corpse, bag.Parent);
        Assert.Same(bag, treasure.Parent);
        Assert.True(treasure.IsChildOf(corpse));
        Assert.Same(player.BankBox, bankGold.Parent);
        Assert.Equal(765, bankGold.Amount);
        Assert.Equal(LootType.Blessed, sword.LootType);
        Assert.Equal(LootType.Newbied, robe.LootType);
        Assert.Equal(LootType.Blessed, treasure.LootType);
        var looter = Player();
        Assert.True(corpse.Criminal);
        Assert.False(corpse.IsCriminalAction(looter));
        Assert.True(corpse.CanLoot(looter, sword));
        Assert.True(corpse.CanLoot(looter, bag));
    }

    [Fact]
    public void OfflineTimeoutDeath_DropsProtectedPossessionsAtLogoutPosition_WithoutTouchingTheBankOrResurrecting()
    {
        var player = Player();
        Enroll(player, ExtractionEventPhase.Finishing);
        var sword = Track(new Item(0xF5E) { Layer = Layer.OneHanded, LootType = LootType.Blessed });
        player.AddItem(sword);
        var treasure = Track(new Gold(321) { LootType = LootType.Newbied });
        player.Backpack.DropItem(treasure);
        var bankGold = Track(new Gold(765));
        player.BankBox.DropItem(bankGold);
        player.LogoutMap = player.Map;
        player.LogoutLocation = player.Location;
        player.MoveToWorld(Point3D.Zero, Map.Internal);
        Mobile.CreateCorpseHandler = Corpse.Mobile_CreateCorpseHandler;

        player.Kill();

        Assert.False(player.Alive);
        Assert.Same(Map.Internal, player.Map);
        var corpse = Track(Assert.IsType<Corpse>(player.Corpse));
        Assert.Same(Map.Felucca, corpse.Map);
        Assert.Equal(player.LogoutLocation, corpse.Location);
        Assert.Same(corpse, sword.Parent);
        Assert.Same(corpse, treasure.Parent);
        Assert.Same(player.BankBox, bankGold.Parent);
        Assert.Equal(765, bankGold.Amount);
        Assert.Equal(LootType.Blessed, sword.LootType);
        Assert.Equal(LootType.Newbied, treasure.LootType);
        Assert.True(corpse.Criminal);
        Assert.False(corpse.IsCriminalAction(Player()));
    }

    [Fact]
    public void OrdinaryDeath_DoesNotMarkTheCorpseCriminalOrBypassProtection()
    {
        var player = Player();
        var controller = Enroll(player, ExtractionEventPhase.Active);
        ParticipantSet(controller).Remove(player);
        var treasure = Track(new Gold(321) { LootType = LootType.Blessed });
        player.Backpack.DropItem(treasure);
        Mobile.CreateCorpseHandler = Corpse.Mobile_CreateCorpseHandler;

        player.Kill();

        var corpse = Track(Assert.IsType<Corpse>(player.Corpse));
        Assert.False(corpse.Criminal);
        Assert.Same(player.Backpack, treasure.Parent);
        Assert.Equal(LootType.Blessed, treasure.LootType);
        Assert.True(corpse.IsCriminalAction(Player()));
    }

    private PlayerMobile Player()
    {
        var player = Track(new PlayerMobile { Player = true, Body = 0x190, Young = false });
        var backpack = Track(new Backpack());
        player.AddItem(backpack);
        player.MoveToWorld(ExtractionDungeonLayout.Rooms[0].PlayerStart, Map.Felucca);
        return player;
    }

    private ExtractionEventController Enroll(PlayerMobile player, ExtractionEventPhase phase)
    {
        var controller = Track(new ExtractionEventController());
        typeof(ExtractionEventController).GetField("_phase", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, phase);
        ParticipantSet(controller).Add(player);
        ExtractionEventDeath.ControllerLookup = () => controller;
        return controller;
    }

    private static HashSet<Mobile> ParticipantSet(ExtractionEventController controller) =>
        (HashSet<Mobile>)typeof(ExtractionEventController).GetField("_participantSet", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }
}
