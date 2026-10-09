using Server.Gumps;
using Xunit;

namespace Server.Tests.Gumps;

public class CharacterStatsGumpTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(3, 5)]
    [InlineData(4, 7)]
    [InlineData(5, 9)]
    public void T2AEnchantmentAddsFlatDamageBeforeScaling(int level, int expected)
    {
        Assert.Equal(expected, CharacterCombatSummary.GetT2ADamageLevelBonus(level));
    }

    [Theory]
    [InlineData(20, 40, 100)]
    [InlineData(20, 10, -50)]
    [InlineData(20, 20, 0)]
    [InlineData(20, 42, 110)]
    [InlineData(2, 3, 50)]
    [InlineData(0, 40, 0)]
    [InlineData(double.NaN, 40, 0)]
    [InlineData(20, double.PositiveInfinity, 0)]
    public void TotalImprovementUsesEffectiveResultRatherThanAddingBonuses(double baseline, double effective, double expected)
    {
        Assert.Equal(expected, CharacterCombatSummary.GetImprovement(baseline, effective), 6);
    }

    [Theory]
    [InlineData(2.0, 30.0)]
    [InlineData(1.5, 40.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(-1.0, 0.0)]
    [InlineData(double.NaN, 0.0)]
    [InlineData(double.PositiveInfinity, 0.0)]
    public void SwingRateHandlesValidAndUnavailableDelays(double seconds, double expected)
    {
        Assert.Equal(expected, CharacterStatsGump.GetSwingsPerMinute(seconds));
    }
}
