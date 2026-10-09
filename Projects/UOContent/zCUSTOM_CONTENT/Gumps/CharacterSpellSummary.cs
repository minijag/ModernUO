using System.Collections.Generic;
using Server.Mobiles;
using Server.Spells;
using Server.Spells.First;
using Server.Spells.Third;
using Server.Spells.Fourth;
using Server.Spells.Sixth;
using Server.Spells.Seventh;

namespace Server.Gumps;

internal sealed class CharacterSpellSummary
{
    internal readonly record struct SpellDamage(string Name, int Minimum, int Maximum, double CastSeconds);

    public List<SpellDamage> Spells { get; } = new();
    public List<CharacterCombatSummary.Modifier> Modifiers { get; } = new();
    public double DamageScalar { get; private set; }
    public double DamageImprovement => (DamageScalar - 1) * 100;
    public bool Supported { get; private set; }

    public static CharacterSpellSummary Capture(PlayerMobile player)
    {
        var result = new CharacterSpellSummary();
        if (!Core.T2A || Core.UOR || Core.UOTD || Core.AOS)
        {
            return result;
        }

        result.Supported = true;
        var evaluation = player.Skills.EvalInt.Value;
        var magery = player.Skills.Magery.Value;
        result.DamageScalar = GetDamageScalar(evaluation, magery, 0, true);
        result.Modifiers.Add(new("Damage bonus", $"{evaluation / 5:+0.00;-0.00;0.00}%", "[Skill] Effective Evaluating Intelligence"));
        result.Modifiers.Add(new("Damage bonus", $"{(magery - 100) / 4:+0.00;-0.00;0.00}%", "[Skill] Effective Magery (neutral at 100)"));
        result.Modifiers.Add(new("Damage multiplier", "+100.00%", "[Target] Non-human monster"));

        // Base ranges match the pre-AOS Random(start, count) calls; the maximum is start + count - 1.
        result.AddSpell(new MagicArrowSpell(player), 4, 7);
        result.AddSpell(new FireballSpell(player), 10, 16);
        result.AddSpell(new LightningSpell(player), 12, 20);
        result.AddSpell(new EnergyBoltSpell(player), 24, 41);
        result.AddSpell(new ExplosionSpell(player), 23, 44);
        result.AddSpell(new FlameStrikeSpell(player), 27, 48);
        return result;
    }

    // Mirrors Spell.GetDamageScalar without target hooks, evasion, random rolls, or skill checks.
    internal static double GetDamageScalar(double evaluation, double magery, double resistance, bool nonHumanMonster)
    {
        var difference = evaluation - resistance;
        var scalar = 1 + difference / (difference > 0 ? 500 : 200) + (magery - 100) / 400;
        return nonHumanMonster ? scalar * 2 : scalar;
    }

    internal static int ScaleDamage(int baseDamage, double scalar) => (int)(baseDamage * scalar);

    private void AddSpell(MagerySpell spell, int minimum, int maximum) =>
        Spells.Add(new(spell.Name, ScaleDamage(minimum, DamageScalar), ScaleDamage(maximum, DamageScalar),
            spell.GetCastDelay().TotalSeconds));
}
