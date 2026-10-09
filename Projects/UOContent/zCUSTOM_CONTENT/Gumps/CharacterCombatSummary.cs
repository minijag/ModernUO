using System;
using System.Collections.Generic;
using Server.Items;
using Server.Mobiles;

namespace Server.Gumps;

// Read-only T2A breakdown: never roll damage or perform skill checks for the UI.
internal sealed class CharacterCombatSummary
{
    internal readonly record struct Modifier(string Effect, string Amount, string Source);
    public List<Modifier> Modifiers { get; } = new();
    public int DamageMin { get; private set; }
    public int DamageMax { get; private set; }
    public double DamageImprovement { get; private set; }
    public double SwingSeconds { get; private set; }
    public double SpeedImprovement { get; private set; }
    public string WeaponName { get; private set; } = "Unarmed";
    public bool Supported { get; private set; }

    public static CharacterCombatSummary Capture(PlayerMobile player)
    {
        var result = new CharacterCombatSummary();
        // Do not apply a T2A breakdown if the shard switches combat eras.
        if (!Core.T2A || Core.UOR || Core.UOTD || Core.AOS || player.Weapon is not BaseWeapon weapon)
        {
            return result;
        }

        result.Supported = true;
        result.WeaponName = weapon.Name ?? weapon.GetType().Name;
        weapon.GetBaseDamageRange(player, out var baseMin, out var baseMax);
        var flatBonus = GetT2ADamageLevelBonus((int)weapon.DamageLevel);
        result.DamageMin = (int)weapon.ScaleDamageOld(player, baseMin + flatBonus, false);
        result.DamageMax = (int)weapon.ScaleDamageOld(player, baseMax + flatBonus, false);
        result.DamageImprovement = GetImprovement(baseMin + (double)baseMax, result.DamageMin + (double)result.DamageMax);
        result.SwingSeconds = weapon.GetDelay(player).TotalSeconds;
        var zeroStaminaDelay = 15000.0 / Math.Max(1, 100 * (int)weapon.Speed);
        result.SpeedImprovement = weapon.Speed > 0 ? GetImprovement(result.SwingSeconds, zeroStaminaDelay) : 0;
        result.Percent("Damage multiplier", player.Skills[SkillName.Tactics].Value - 50, "[Skill] Effective Tactics (neutral at 50)");
        result.Percent("Damage bonus", player.Str / 5.0, "[Attribute] Effective strength");
        result.Percent("Damage bonus", player.Skills[SkillName.Anatomy].Value / 5.0, "[Skill] Effective Anatomy");
        result.Percent("Damage bonus", ((int)weapon.Quality - 1) * 20, $"[Weapon] {weapon.Quality} quality");
        result.Percent("Damage bonus", weapon.VirtualDamageBonus, "[Weapon] Special damage property");
        if (flatBonus != 0)
        {
            result.Modifiers.Add(new("Base damage", $"+{flatBonus} points", $"[Weapon] {weapon.DamageLevel} enchantment"));
        }

        var durability = weapon.MaxHitPoints > 0 && weapon.HitPoints < weapon.MaxHitPoints
            ? 50 + 50 * weapon.HitPoints / weapon.MaxHitPoints : 100;
        result.Percent("Damage multiplier", durability - 100, "[Weapon] Current durability");
        result.Percent("Swing rate", result.SpeedImprovement, "[Resource] Stamina versus zero");
        result.Armor(player.NeckArmor);
        result.Armor(player.HandArmor);
        result.Armor(player.HeadArmor);
        result.Armor(player.ArmsArmor);
        result.Armor(player.LegsArmor);
        result.Armor(player.ChestArmor);
        result.Armor(player.ShieldArmor);
        result.Points(player.VirtualArmor, "[Innate] Base virtual armor");
        result.Points(player.VirtualArmorMod, "[Effect] Virtual armor modifier");
        return result;
    }

    internal static int GetT2ADamageLevelBonus(int level) => level == 0 ? 0 : 2 * level - 1;

    internal static double GetImprovement(double baseline, double effective) =>
        baseline > 0 && double.IsFinite(baseline) && double.IsFinite(effective)
            ? (effective / baseline - 1) * 100 : 0;

    private void Percent(string effect, double amount, string source)
    {
        if (Math.Abs(amount) >= 0.005)
        {
            Modifiers.Add(new(effect, $"{amount:+0.00;-0.00;0.00}%", source));
        }
    }

    private void Armor(Item item)
    {
        if (item is BaseArmor armor)
        {
            Points(armor.ArmorRatingScaled, $"[Equipment] {armor.Name ?? armor.GetType().Name}");
        }
    }

    private void Points(double amount, string source)
    {
        if (Math.Abs(amount) >= 0.005)
        {
            Modifiers.Add(new("Armor rating", $"{amount:+0.00;-0.00;0.00} AR", source));
        }
    }
}
