using System;
using System.Collections.Generic;
using Server;
using Server.Engines.TownStruggle;
using Server.Items;
using Server.Mobiles;
using Xunit;

namespace UOContent.Tests.Engines.TownStruggle;

[Collection("Sequential UOContent Tests")]
public partial class TownStruggleLootTests : IDisposable
{
    private readonly List<IEntity> _created = [];

    public TownStruggleLootTests() => Timer.Init(Core.TickCount);

    public void Dispose()
    {
        for (var i = _created.Count - 1; i >= 0; i--) { _created[i].Delete(); }
        Timer.Init(Core.TickCount);
    }

    [Theory]
    [InlineData("damage")]
    [InlineData("accuracy")]
    [InlineData("durability")]
    [InlineData("slayer")]
    [InlineData("aos")]
    [InlineData("colored")]
    public void MagicalAndExceptionalColoredWeaponsAreEligible(string type)
    {
        var weapon = Track(new Longsword());
        switch (type)
        {
            case "damage": weapon.DamageLevel = WeaponDamageLevel.Ruin; break;
            case "accuracy": weapon.AccuracyLevel = WeaponAccuracyLevel.Accurate; break;
            case "durability": weapon.DurabilityLevel = WeaponDurabilityLevel.Durable; break;
            case "slayer": weapon.Slayer = SlayerName.Silver; break;
            case "aos": weapon.Attributes.BonusStr = 5; break;
            case "colored": weapon.Quality = WeaponQuality.Exceptional; weapon.Resource = CraftResource.Valorite; break;
        }
        Assert.True(TownStruggleLoot.IsEligibleEquipment(weapon));
    }

    [Theory]
    [InlineData("protection")]
    [InlineData("durability")]
    [InlineData("aos")]
    [InlineData("colored")]
    public void MagicalAndExceptionalColoredArmorAreEligible(string type)
    {
        var armor = Track(new PlateChest());
        switch (type)
        {
            case "protection": armor.ProtectionLevel = ArmorProtectionLevel.Guarding; break;
            case "durability": armor.Durability = ArmorDurabilityLevel.Durable; break;
            case "aos": armor.Attributes.BonusDex = 3; break;
            case "colored": armor.Quality = ArmorQuality.Exceptional; armor.Resource = CraftResource.Agapite; break;
        }
        Assert.True(TownStruggleLoot.IsEligibleEquipment(armor));
    }

    [Fact]
    public void OrdinaryExceptionalCraftBonusesAndRegularColoredGearAreRetained()
    {
        var ordinary = Track(new Longsword { Quality = WeaponQuality.Exceptional, PlayerConstructed = true });
        ordinary.Attributes.WeaponDamage = 50;
        Assert.False(TownStruggleLoot.IsEligibleEquipment(ordinary));
        var armor = Track(new PlateChest { Quality = ArmorQuality.Exceptional, PlayerConstructed = true, PhysicalBonus = 10, FireBonus = 10 });
        Assert.False(TownStruggleLoot.IsEligibleEquipment(armor));
        var colored = Track(new Longsword { Resource = CraftResource.Valorite });
        Assert.False(TownStruggleLoot.IsEligibleEquipment(colored));
        ordinary.Attributes.BonusStr = 1;
        Assert.True(TownStruggleLoot.IsEligibleEquipment(ordinary));
    }

    [Fact]
    public void OnlyOriginalEligibleSerialsDropIncludingNestedAndEquippedGear()
    {
        var player = Player();
        var magical = Track(new Longsword { DamageLevel = WeaponDamageLevel.Ruin, Layer = Layer.OneHanded });
        player.AddItem(magical);
        var bag = Track(new Bag());
        player.Backpack.DropItem(bag);
        var nested = Track(new PlateChest { ProtectionLevel = ArmorProtectionLevel.Guarding });
        bag.DropItem(nested);
        var plain = Track(new Longsword());
        bag.DropItem(plain);
        var gold = Track(new Gold(500));
        bag.DropItem(gold);
        var bankMagic = Track(new Longsword { DamageLevel = WeaponDamageLevel.Vanq });
        player.BankBox.DropItem(bankMagic);
        var previousCorpse = player.Corpse;
        var controller = ActiveLootController(player);
        Assert.True(TownStruggleLoot.TryDropEligible(controller, player, out var error), error);
        var corpse = Track(Assert.IsType<TownStruggleCorpse>(magical.Parent));
        Assert.Same(corpse, nested.Parent);
        Assert.Equal(2, corpse.Items.Count);
        Assert.Same(player, corpse.Owner);
        Assert.True(corpse.Criminal);
        var looter = Player();
        Assert.False(corpse.IsCriminalAction(looter));
        Assert.False(corpse.CanEventLoot(looter));
        Assert.False(corpse.CheckItemUse(looter, magical));
        var rejection = Server.Network.LRReason.Inspecific;
        Assert.False(corpse.CheckLift(looter, magical, ref rejection));
        Assert.Same(controller, corpse.Controller);
        Assert.Equal(controller.RoundNumber, corpse.RoundNumber);
        Assert.Equal(player.Location, corpse.Location);
        Assert.Equal(player.Map, corpse.Map);
        Assert.Same(previousCorpse, player.Corpse);
        Assert.True(player.Alive);
        Assert.Same(bag, plain.Parent);
        Assert.Same(bag, gold.Parent);
        Assert.Same(player.BankBox, bankMagic.Parent);
        Assert.Equal(500, gold.Amount);
    }

    [Fact]
    public void BlessedInsuredNewbiedAndProtectedContainerContentsRemain()
    {
        var player = Player();
        var blessed = Track(new Longsword { DamageLevel = WeaponDamageLevel.Ruin, LootType = LootType.Blessed });
        var insured = Track(new Longsword { DamageLevel = WeaponDamageLevel.Ruin, Insured = true });
        var newbied = Track(new Longsword { DamageLevel = WeaponDamageLevel.Ruin, LootType = LootType.Newbied });
        var bag = Track(new Bag { LootType = LootType.Blessed });
        var nested = Track(new Longsword { DamageLevel = WeaponDamageLevel.Ruin });
        player.Backpack.DropItem(blessed);
        player.Backpack.DropItem(insured);
        player.Backpack.DropItem(newbied);
        player.Backpack.DropItem(bag);
        bag.DropItem(nested);
        var placed = false;
        Assert.True(TownStruggleLoot.TryDropEligible(player, (_, _, _) => placed = true, out _));
        Assert.False(placed);
        Assert.Same(player.Backpack, blessed.Parent);
        Assert.Same(player.Backpack, insured.Parent);
        Assert.Same(player.Backpack, newbied.Parent);
        Assert.Same(bag, nested.Parent);
    }

    [Fact]
    public void FailedPlacementRestoresExactParentsLocationsAndSerials()
    {
        var player = Player();
        var equipped = Track(new Longsword { DamageLevel = WeaponDamageLevel.Ruin, Layer = Layer.OneHanded });
        player.AddItem(equipped);
        var bag = Track(new Bag());
        player.Backpack.DropItem(bag);
        var nested = Track(new PlateChest { ProtectionLevel = ArmorProtectionLevel.Guarding });
        bag.DropItem(nested);
        nested.Location = new Point3D(47, 53, 0);
        var originalLocation = nested.Location;
        var serial = nested.Serial;
        Corpse attempted = null;
        Assert.False(TownStruggleLoot.TryDropEligible(player, (corpse, _, _) =>
        {
            attempted = corpse;
            throw new InvalidOperationException("Test placement refused.");
        }, out _));
        Assert.NotNull(attempted);
        Assert.True(attempted.Deleted);
        Assert.False(equipped.Deleted);
        Assert.Same(player, equipped.Parent);
        Assert.Same(bag, nested.Parent);
        Assert.Equal(originalLocation, nested.Location);
        Assert.Equal(serial, nested.Serial);
        Assert.True(player.Alive);
    }

    [Fact]
    public void UnchangedLocationCallbackIsRejectedAndRolledBack()
    {
        var player = Player();
        var weapon = Track(new Longsword { DamageLevel = WeaponDamageLevel.Ruin });
        player.Backpack.DropItem(weapon);
        Assert.False(TownStruggleLoot.TryDropEligible(player, (_, _, _) => { }, out _));
        Assert.Same(player.Backpack, weapon.Parent);
        Assert.False(weapon.Deleted);
    }

    [Fact]
    public void InternalizedPlayerCannotDropEquipment()
    {
        var player = Player();
        var weapon = Track(new Longsword { DamageLevel = WeaponDamageLevel.Ruin });
        player.Backpack.DropItem(weapon);
        player.Internalize();
        Assert.False(TownStruggleLoot.TryDropEligible(player, out _));
        Assert.Same(player.Backpack, weapon.Parent);
    }

    [Fact]
    public void CursorHeldMagicalEquipmentDropsAndClearsHolding()
    {
        var player = Player();
        var weapon = Track(new Longsword { DamageLevel = WeaponDamageLevel.Ruin });
        Hold(player, weapon);
        Assert.True(TownStruggleLoot.TryDropEligible(player, out var error), error);
        var corpse = Track(Assert.IsType<TownStruggleCorpse>(weapon.Parent));
        Assert.Null(player.Holding);
        Assert.Null(weapon.HeldBy);
        Assert.Equal(player.Location, corpse.Location);
        Assert.False(weapon.Deleted);
    }

    [Fact]
    public void CursorHeldBagIsInspectedWithoutDroppingItsRetainedContents()
    {
        var player = Player();
        var bag = Track(new Bag());
        var magical = Track(new Longsword { DamageLevel = WeaponDamageLevel.Ruin });
        var gold = Track(new Gold(400));
        bag.DropItem(magical);
        bag.DropItem(gold);
        Hold(player, bag);
        Assert.True(TownStruggleLoot.TryDropEligible(player, out var error), error);
        Track(Assert.IsType<TownStruggleCorpse>(magical.Parent));
        Assert.Same(bag, player.Holding);
        Assert.Same(player, bag.HeldBy);
        Assert.Same(bag, gold.Parent);
        Assert.Equal(Map.Internal, bag.Map);
    }

    [Fact]
    public void ProtectedCursorEquipmentRemainsHeld()
    {
        var player = Player();
        var weapon = Track(new Longsword { DamageLevel = WeaponDamageLevel.Ruin, LootType = LootType.Blessed });
        Hold(player, weapon);
        Assert.True(TownStruggleLoot.TryDropEligible(player, (_, _, _) => throw new InvalidOperationException(), out var error), error);
        Assert.Same(weapon, player.Holding);
        Assert.Same(player, weapon.HeldBy);
        Assert.Null(weapon.Parent);
        Assert.Equal(Map.Internal, weapon.Map);
    }

    [Fact]
    public void FailedCursorLootPlacementRestoresCursorState()
    {
        var player = Player();
        var weapon = Track(new Longsword { DamageLevel = WeaponDamageLevel.Ruin });
        Hold(player, weapon);
        var location = weapon.Location;
        Corpse attempted = null;
        Assert.False(TownStruggleLoot.TryDropEligible(player, (corpse, _, _) =>
        {
            attempted = corpse;
            throw new InvalidOperationException("Test placement refused.");
        }, out _));
        Assert.True(attempted.Deleted);
        Assert.Same(weapon, player.Holding);
        Assert.Same(player, weapon.HeldBy);
        Assert.Null(weapon.Parent);
        Assert.Equal(Map.Internal, weapon.Map);
        Assert.Equal(location, weapon.Location);
        Assert.False(weapon.Deleted);
    }

    private static void Hold(Mobile player, Item item)
    {
        item.Internalize();
        player.Holding = item;
    }

    private PlayerMobile Player()
    {
        var player = Track(new PlayerMobile { Player = true, Body = 0x190, RawStr = 100 });
        player.AddItem(Track(new Backpack()));
        player.MoveToWorld(new Point3D(50, 50, 0), Map.Felucca);
        return player;
    }

    private T Track<T>(T entity) where T : IEntity { _created.Add(entity); return entity; }
}
