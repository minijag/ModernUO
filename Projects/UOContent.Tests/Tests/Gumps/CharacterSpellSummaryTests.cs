using Server.Gumps;
using Xunit;

namespace Server.Tests.Gumps;

public class CharacterSpellSummaryTests
{
    [Theory]
    [InlineData(100, 100, 0, true, 2.4)]
    [InlineData(100, 100, 0, false, 1.2)]
    [InlineData(0, 0, 0, true, 1.5)]
    [InlineData(100, 100, 100, true, 2.0)]
    [InlineData(50, 100, 100, false, 0.75)]
    [InlineData(100, 120, 0, true, 2.5)]
    public void T2AScalarIncludesSkillsResistanceAndMonsterMultiplier(
        double evaluation, double magery, double resistance, bool monster, double expected)
    {
        Assert.Equal(expected, CharacterSpellSummary.GetDamageScalar(evaluation, magery, resistance, monster), 6);
    }

    [Theory]
    [InlineData(4, 2.4, 9)]
    [InlineData(7, 2.4, 16)]
    [InlineData(27, 2.4, 64)]
    [InlineData(48, 2.4, 115)]
    public void SpellDamageTruncatesLikeSpellHelper(int baseDamage, double scalar, int expected)
    {
        Assert.Equal(expected, CharacterSpellSummary.ScaleDamage(baseDamage, scalar));
    }
}
