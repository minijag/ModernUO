using System;
using System.Collections.Generic;
using Server.Items;
using Server.Logging;

namespace Server.Engines.TownStruggle;

public static class TownStruggleLoot
{
    private const int MaximumInspectedItems = 2048;
    private const int MaximumDroppedItems = 256;
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(TownStruggleLoot));

    public static bool TryDropEligible(Mobile player, out string error) =>
        TryDropEligible(TownStruggleRuntime.Controller, player, out error);

    public static bool TryDropEligible(TownStruggleController controller, Mobile player, out string error) =>
        TryDropEligible(controller, player, static (corpse, point, map) => corpse.MoveToWorld(point, map), out error);

    internal static bool TryDropEligible(Mobile player, Action<Corpse, Point3D, Map> place, out string error) =>
        TryDropEligible(TownStruggleRuntime.Controller, player, place, out error);

    internal static bool TryDropEligible(TownStruggleController controller, Mobile player,
        Action<Corpse, Point3D, Map> place, out string error)
    {
        if (player is not { Deleted: false, Player: true, Alive: true } || player.Map == null || player.Map == Map.Internal ||
            player.X < 0 || player.Y < 0 || player.X >= player.Map.Width || player.Y >= player.Map.Height)
        {
            error = "Selective event loot requires a living player at a valid battlefield location.";
            return false;
        }

        var selected = new List<Original>();
        Corpse corpse = null;
        try
        {
            var pending = new Stack<Item>();
            var seen = new HashSet<Item>();
            var held = player.Holding;
            if (held != null)
            {
                if (held.Deleted || held.Parent != null || held.HeldBy != player || held.Map != Map.Internal)
                {
                    throw new InvalidOperationException("The held item is not in a valid cursor state.");
                }
                // The event intercepts death before Mobile.DropHolding; the cursor is still carried inventory.
                pending.Push(held);
            }
            foreach (var root in player.Items)
            {
                if (root is BankBox || root.Layer is Layer.Bank or Layer.Hair or Layer.FacialHair or Layer.Mount)
                {
                    continue;
                }
                pending.Push(root);
            }
            while (pending.Count > 0)
            {
                var item = pending.Pop();
                if (item.Deleted || !seen.Add(item))
                {
                    throw new InvalidOperationException("The carried inventory contains a stale or duplicate reference.");
                }
                if (seen.Count > MaximumInspectedItems)
                {
                    throw new InvalidOperationException("The carried inventory exceeds the selective-loot inspection bound.");
                }
                if (item != player.Backpack && IsProtected(item, player))
                {
                    continue;
                }
                if (IsEligibleEquipment(item))
                {
                    if (selected.Count >= MaximumDroppedItems)
                    {
                        throw new InvalidOperationException("The carried equipment exceeds the selective-loot drop bound.");
                    }
                    selected.Add(new Original(item, item.Parent, item.Location, item == held));
                }
                if (item is Container container)
                {
                    if (pending.Count + container.Items.Count > MaximumInspectedItems)
                    {
                        throw new InvalidOperationException("The carried inventory exceeds the selective-loot inspection bound.");
                    }
                    foreach (var child in container.Items) { pending.Push(child); }
                }
            }
            if (selected.Count == 0)
            {
                error = null;
                return true;
            }

            corpse = new TownStruggleCorpse(controller, player);
            foreach (var original in selected)
            {
                if (original.Item.Deleted || original.Item.Parent != original.Parent)
                {
                    throw new InvalidOperationException("A selected item changed ownership before it could be dropped.");
                }
                if (original.WasHeld)
                {
                    if (player.Holding != original.Item || original.Item.HeldBy != player)
                    {
                        throw new InvalidOperationException("A selected cursor item changed ownership before it could be dropped.");
                    }
                    player.Holding = null;
                }
                corpse.DropItem(original.Item);
            }
            place(corpse, player.Location, player.Map);
            if (corpse.Deleted || corpse.Map != player.Map || corpse.Location != player.Location || corpse.Parent != null)
            {
                throw new InvalidOperationException("The selective-loot corpse could not be placed at the defeat location.");
            }
            foreach (var original in selected)
            {
                if (original.WasHeld) { original.Item.ClearBounce(); }
            }
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            var restored = true;
            foreach (var original in selected)
            {
                try
                {
                    if (original.Item.Deleted) { restored = false; continue; }
                    if (original.WasHeld)
                    {
                        if (player.Holding != null && player.Holding != original.Item)
                        {
                            restored = false;
                            continue;
                        }
                        original.Item.Internalize();
                        original.Item.Location = original.Location;
                        player.Holding = original.Item;
                        continue;
                    }
                    if (original.Item.Parent != original.Parent)
                    {
                        switch (original.Parent)
                        {
                            case Container { Deleted: false } container:
                                container.AddItem(original.Item);
                                break;
                            case Mobile { Deleted: false } mobile:
                                mobile.AddItem(original.Item);
                                break;
                            default:
                                restored = false;
                                continue;
                        }
                    }
                    original.Item.Location = original.Location;
                }
                catch (Exception rollbackFailure)
                {
                    restored = false;
                    _logger.Error(rollbackFailure, "Town Struggle selective-loot rollback needs recovery for item {Item}", original.Item.Serial);
                }
            }
            if (corpse is { Deleted: false })
            {
                if (restored && corpse.Items.Count == 0) { corpse.Delete(); }
                else
                {
                    // Do not delete unrecovered possessions with their container; retain staff recovery time.
                    corpse.BeginDecay(TimeSpan.FromDays(7));
                    _logger.Error("Town Struggle selective-loot recovery container {Corpse} requires staff attention", corpse.Serial);
                }
            }
            _logger.Error(exception, "Town Struggle selective loot was refused for {Player}; restored {Restored}", player.Serial, restored);
            error = restored
                ? "Selective loot could not be placed; all equipment was retained. Staff must recover the match."
                : "Selective loot needs staff recovery. No recovery container was deleted.";
            return false;
        }
    }

    private static bool IsProtected(Item item, Mobile player) =>
        !item.Movable || item.Insured || item.QuestItem || item.Nontransferable || item.BlessedFor == player ||
        item.LootType is LootType.Blessed or LootType.Newbied;

    internal static bool IsEligibleEquipment(Item item) => item switch
    {
        BaseWeapon weapon => IsMagical(weapon) || weapon.Quality == WeaponQuality.Exceptional && IsColored(weapon.Resource),
        BaseArmor armor => IsMagical(armor) || armor.Quality == ArmorQuality.Exceptional && IsColored(armor.Resource),
        _ => false
    };

    private static bool IsColored(CraftResource resource) => (int)resource is
        >= (int)CraftResource.DullCopper and <= (int)CraftResource.Valorite or
        >= (int)CraftResource.SpinedLeather and <= (int)CraftResource.BarbedLeather or
        >= (int)CraftResource.RedScales and <= (int)CraftResource.BlueScales or
        >= (int)CraftResource.OakWood and <= (int)CraftResource.Frostwood;

    private static bool IsMagical(BaseWeapon weapon)
    {
        if (weapon.DamageLevel != WeaponDamageLevel.Regular || weapon.AccuracyLevel != WeaponAccuracyLevel.Regular ||
            weapon.DurabilityLevel != WeaponDurabilityLevel.Regular || weapon.Slayer != SlayerName.None || weapon.Slayer2 != SlayerName.None ||
            !weapon.WeaponAttributes.IsEmpty || !weapon.SkillBonuses.IsEmpty || !weapon.AosElementDamages.IsEmpty)
        {
            return true;
        }
        if (weapon.Attributes.IsEmpty) { return false; }
        // Ordinary exceptional crafting grants damage increase without creating magical equipment.
        return !(weapon.PlayerConstructed && weapon.Quality == WeaponQuality.Exceptional &&
                 weapon.Attributes.Names == (uint)AosAttribute.WeaponDamage &&
                 weapon.Attributes.Values.Length == 1 && weapon.Attributes.Values[0] is >= 0 and <= 50);
    }

    private static bool IsMagical(BaseArmor armor)
    {
        if (armor.ProtectionLevel != ArmorProtectionLevel.Regular || armor.Durability != ArmorDurabilityLevel.Regular ||
            !armor.Attributes.IsEmpty || !armor.ArmorAttributes.IsEmpty || !armor.SkillBonuses.IsEmpty)
        {
            return true;
        }
        var resistBonus = (long)armor.PhysicalBonus + armor.FireBonus + armor.ColdBonus + armor.PoisonBonus + armor.EnergyBonus;
        if (armor.PhysicalBonus == 0 && armor.FireBonus == 0 && armor.ColdBonus == 0 && armor.PoisonBonus == 0 && armor.EnergyBonus == 0)
        {
            return false;
        }
        // Exceptional crafting and Arms Lore can distribute up to twenty ordinary resist points.
        return !(armor.PlayerConstructed && armor.Quality == ArmorQuality.Exceptional && resistBonus <= 20 &&
                 armor.PhysicalBonus >= 0 && armor.FireBonus >= 0 && armor.ColdBonus >= 0 && armor.PoisonBonus >= 0 && armor.EnergyBonus >= 0);
    }

    private readonly record struct Original(Item Item, object Parent, Point3D Location, bool WasHeld);
}
