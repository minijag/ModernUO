using System;
using Server.Engines.TownStruggle;
using Xunit;

namespace UOContent.Tests.Engines.TownStruggle;

public class TownStruggleRulesTests
{
    [Fact]
    public void DefaultsMatchEventRules()
    {
        Assert.Equal(3, TownStruggleRules.CaptureZoneCount);
        Assert.Equal(3, TownStruggleRules.RespawnZoneCount);
        Assert.Equal(TimeSpan.FromMinutes(15), TownStruggleRules.DefaultSignupDuration);
        Assert.Equal(TimeSpan.FromMinutes(15), TownStruggleRules.DefaultMatchDuration);
        Assert.Equal(TimeSpan.FromSeconds(30), TownStruggleRules.ControlCheckInterval);
        Assert.Equal(TimeSpan.FromMinutes(10), TownStruggleRules.IdleLimit);
        Assert.Equal(TimeSpan.FromHours(48), TownStruggleRules.IdleBanDuration);
        Assert.Equal(10, TownStruggleRules.KillTeamPoints);
        Assert.Equal(10, TownStruggleRules.KillIndividualPoints);
    }

    [Theory]
    [InlineData(1, 0, 5)]
    [InlineData(99, 0, 5)]
    [InlineData(0, 1, -5)]
    [InlineData(0, 99, -5)]
    [InlineData(1, 1, 0)]
    [InlineData(0, 0, 0)]
    public void OnlySoleOccupantAdvancesCaptureWithoutPopulationMultiplier(int order, int chaos, int progress)
    {
        Assert.Equal(new TownStruggleCaptureState(progress, TownStruggleTeam.Neutral),
            TownStruggleRules.StepCapture(default, order, chaos));
    }

    [Theory]
    [InlineData(1, 0, 100, TownStruggleTeam.Order)]
    [InlineData(0, 1, -100, TownStruggleTeam.Chaos)]
    public void TwentySecondsCapturesNeutralZone(int order, int chaos, int progress, TownStruggleTeam owner)
    {
        var result = TownStruggleRules.StepCapture(default, order, chaos, 19);
        Assert.Equal(TownStruggleTeam.Neutral, result.Owner);
        result = TownStruggleRules.StepCapture(result, order, chaos);
        Assert.Equal(new TownStruggleCaptureState(progress, owner), result);
        Assert.Equal(result, TownStruggleRules.StepCapture(result, order, chaos, int.MaxValue));
    }

    [Theory]
    [InlineData(100, TownStruggleTeam.Order, 1, 1)]
    [InlineData(-100, TownStruggleTeam.Chaos, 1, 30)]
    [InlineData(45, TownStruggleTeam.Neutral, 20, 1)]
    public void ContestedZoneFreezesProgressAndOwnership(int progress, TownStruggleTeam owner, int order, int chaos)
    {
        var state = new TownStruggleCaptureState(progress, owner);
        Assert.Equal(state, TownStruggleRules.StepCapture(state, order, chaos, 500));
    }

    [Fact]
    public void OpponentMustNeutralizeOwnerBeforeTakingControl()
    {
        var state = new TownStruggleCaptureState(100, TownStruggleTeam.Order);
        state = TownStruggleRules.StepCapture(state, 0, 1, 19);
        Assert.Equal(new TownStruggleCaptureState(5, TownStruggleTeam.Order), state);
        state = TownStruggleRules.StepCapture(state, 0, 1);
        Assert.Equal(default, state);
        state = TownStruggleRules.StepCapture(state, 0, 1, 19);
        Assert.Equal(new TownStruggleCaptureState(-95, TownStruggleTeam.Neutral), state);
        state = TownStruggleRules.StepCapture(state, 0, 1);
        Assert.Equal(new TownStruggleCaptureState(-100, TownStruggleTeam.Chaos), state);
    }

    [Theory]
    [InlineData(100, TownStruggleTeam.Order, 95)]
    [InlineData(-100, TownStruggleTeam.Chaos, -95)]
    [InlineData(3, TownStruggleTeam.Neutral, 0)]
    [InlineData(-3, TownStruggleTeam.Neutral, 0)]
    public void EmptyZoneDecaysToNeutralWithoutCrossingIt(int progress, TownStruggleTeam owner, int expected)
    {
        var state = new TownStruggleCaptureState(progress, owner);
        var result = TownStruggleRules.StepCapture(state, 0, 0);
        Assert.Equal(expected, result.Progress);
        Assert.Equal(expected == 0 ? TownStruggleTeam.Neutral : owner, result.Owner);
        Assert.Equal(default, TownStruggleRules.StepCapture(state, 0, 0, int.MaxValue));
    }

    [Fact]
    public void BatchedCaptureMatchesPerSecondSteps()
    {
        var start = new TownStruggleCaptureState(-100, TownStruggleTeam.Chaos);
        var sequential = start;
        for (var i = 0; i < 27; i++)
        {
            sequential = TownStruggleRules.StepCapture(sequential, 1, 0);
        }
        Assert.Equal(sequential, TownStruggleRules.StepCapture(start, 1, 0, 27));
        Assert.Equal(new TownStruggleCaptureState(35, TownStruggleTeam.Neutral), sequential);
        Assert.Equal(start, TownStruggleRules.StepCapture(start, 1, 0, 0));
    }

    [Theory]
    [InlineData(100, TownStruggleTeam.Order, 2, 0, 10, 0, 5, 0)]
    [InlineData(-100, TownStruggleTeam.Chaos, 0, 2, 0, 10, 0, 5)]
    [InlineData(50, TownStruggleTeam.Neutral, 2, 0, 0, 0, 0, 0)]
    [InlineData(50, TownStruggleTeam.Order, 0, 1, 0, 0, 0, 0)]
    [InlineData(-50, TownStruggleTeam.Chaos, 1, 0, 0, 0, 0, 0)]
    [InlineData(50, TownStruggleTeam.Order, 0, 0, 10, 0, 0, 0)]
    [InlineData(0, TownStruggleTeam.Neutral, 1, 5, 5, 5, 5, 1)]
    [InlineData(100, TownStruggleTeam.Order, 5, 1, 5, 5, 1, 5)]
    public void ZoneAwardsFollowOwnershipAndDivideOnlyAmongPresentMembers(
        int progress, TownStruggleTeam owner, int order, int chaos,
        int orderPoints, int chaosPoints, int orderShare, int chaosShare
    )
    {
        Assert.Equal(new TownStruggleZoneAward(orderPoints, chaosPoints, orderShare, chaosShare),
            TownStruggleRules.ScoreZone(new TownStruggleCaptureState(progress, owner), order, chaos));
    }

    [Fact]
    public void IndividualSharesAreNotIntegerTruncated()
    {
        var result = TownStruggleRules.ScoreZone(new TownStruggleCaptureState(100, TownStruggleTeam.Order), 3, 0);
        Assert.Equal(10m / 3, result.OrderIndividualPoints);
        result = TownStruggleRules.ScoreZone(default, 3, 4);
        Assert.Equal(5m / 3, result.OrderIndividualPoints);
        Assert.Equal(1.25m, result.ChaosIndividualPoints);
    }

    [Theory]
    [InlineData(0, 0, 90)]
    [InlineData(300, 200, 90)]
    [InlineData(0, 99, 90)]
    [InlineData(0, 100, 85)]
    [InlineData(100, 299, 85)]
    [InlineData(100, 300, 80)]
    [InlineData(0, 1199, 35)]
    [InlineData(0, 1200, 30)]
    [InlineData(0, int.MaxValue, 30)]
    public void UnderdogRespawnUsesWholeHundredsAndThirtySecondFloor(int own, int enemy, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), TownStruggleRules.RespawnDelay(own, enemy));
    }

    [Theory]
    [InlineData(1, 1, 100)]
    [InlineData(1, 10, 100)]
    [InlineData(5, 10, 60)]
    [InlineData(8, 10, 30)]
    [InlineData(10, 10, 10)]
    [InlineData(2, 4, 75)]
    [InlineData(3, 4, 50)]
    [InlineData(4, 4, 25)]
    public void RankPercentIsInclusiveDescending(int rank, int total, int expected)
    {
        Assert.Equal(expected, TownStruggleRules.RankPercent(rank, total));
    }

    [Theory]
    [InlineData(1, 4, 3)]
    [InlineData(2, 4, 3)]
    [InlineData(3, 4, 2)]
    [InlineData(4, 4, 1)]
    [InlineData(10, 10, 0)]
    public void RankRewardsIncludeExactThresholds(int rank, int total, int expected)
    {
        Assert.Equal(expected, TownStruggleRules.RewardPoints(rank, total, false, false, 0, 2));
    }

    [Fact]
    public void WinningBonusRequiresRankReward()
    {
        Assert.Equal(4, TownStruggleRules.RewardPoints(1, 10, true, false, 0, 5));
        Assert.Equal(0, TownStruggleRules.RewardPoints(10, 10, true, false, 0, 5));
        Assert.Equal(2, TownStruggleRules.RewardPoints(10, 10, true, true, 0, 5));
    }

    [Fact]
    public void MvpAndHealerBonusesRespectTeamSizeAndOverallCap()
    {
        Assert.Equal(2, TownStruggleRules.RewardPoints(10, 10, false, true, 0, 5));
        Assert.Equal(2, TownStruggleRules.RewardPoints(10, 10, false, false, 1, 5));
        Assert.Equal(0, TownStruggleRules.RewardPoints(10, 10, false, false, 2, 5));
        Assert.Equal(1, TownStruggleRules.RewardPoints(10, 10, false, false, 2, 6));
        Assert.Equal(0, TownStruggleRules.RewardPoints(10, 10, false, false, 3, 6));
        Assert.Equal(5, TownStruggleRules.RewardPoints(1, 10, true, true, 1, 5));
        Assert.Equal(0, TownStruggleRules.RewardPoints(0, 0, true, true, 1, 0, false));
    }

    [Theory]
    [InlineData(0, 0, TownStruggleTeam.Neutral)]
    [InlineData(1, 0, TownStruggleTeam.Order)]
    [InlineData(0, 1, TownStruggleTeam.Chaos)]
    public void WinnerDoesNotBreakTiesArbitrarily(int order, int chaos, TownStruggleTeam expected)
    {
        Assert.Equal(expected, TownStruggleRules.Winner(order, chaos));
        Assert.Equal(expected, TownStruggleRules.Opponent(TownStruggleRules.Opponent(expected)));
    }

    [Fact]
    public void InvalidInputsFailClosed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TownStruggleRules.StepCapture(default, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TownStruggleRules.StepCapture(default, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TownStruggleRules.StepCapture(default, 0, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TownStruggleRules.StepCapture(new(101, TownStruggleTeam.Order), 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TownStruggleRules.StepCapture(new(0, TownStruggleTeam.Order), 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TownStruggleRules.StepCapture(new(1, TownStruggleTeam.Chaos), 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TownStruggleRules.ScoreZone(default, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TownStruggleRules.RespawnDelay(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TownStruggleRules.RespawnDelay(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TownStruggleRules.RankPercent(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TownStruggleRules.RankPercent(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TownStruggleRules.RankPercent(2, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TownStruggleRules.Opponent((TownStruggleTeam)99));
    }
}
