using System;

namespace Server.Engines.TownStruggle;

public enum TownStruggleTeam
{
    Neutral,
    Order,
    Chaos
}

public enum TownStrugglePhase
{
    Dormant,
    Signup,
    Active,
    Results,
    Faulted
}

public readonly record struct TownStruggleCaptureState(int Progress, TownStruggleTeam Owner);

public readonly record struct TownStruggleZoneAward(
    int OrderPoints,
    int ChaosPoints,
    decimal OrderIndividualPoints,
    decimal ChaosIndividualPoints
);

public static class TownStruggleRules
{
    public const int CaptureZoneCount = 3;
    public const int RespawnZoneCount = 3;
    public const int CaptureLimit = 100;
    public const int CaptureRatePerSecond = 5;
    public const int ControlledZonePoints = 10;
    public const int ContestedZonePoints = 5;
    public const int KillTeamPoints = 10;
    public const int KillIndividualPoints = 10;
    public const int MaximumRewardPoints = 5;

    public static readonly TimeSpan DefaultSignupDuration = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan DefaultMatchDuration = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan ControlCheckInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan OutsideGrace = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan IdleBanDuration = TimeSpan.FromHours(48);
    public static readonly TimeSpan ResultsDuration = TimeSpan.FromMinutes(15);

    public static TownStruggleTeam Opponent(TownStruggleTeam team) => team switch
    {
        TownStruggleTeam.Neutral => TownStruggleTeam.Neutral,
        TownStruggleTeam.Order => TownStruggleTeam.Chaos,
        TownStruggleTeam.Chaos => TownStruggleTeam.Order,
        _ => throw new ArgumentOutOfRangeException(nameof(team))
    };

    public static TownStruggleCaptureState StepCapture(
        TownStruggleCaptureState state,
        int orderCount,
        int chaosCount,
        int elapsedSeconds = 1
    )
    {
        ValidateCapture(state);
        ValidateCounts(orderCount, chaosCount);
        ArgumentOutOfRangeException.ThrowIfNegative(elapsedSeconds);

        if (elapsedSeconds == 0 || orderCount > 0 && chaosCount > 0)
        {
            return state;
        }

        var change = (long)CaptureRatePerSecond * elapsedSeconds;
        var progress = (orderCount > 0, chaosCount > 0) switch
        {
            (true, false) => (int)Math.Min(CaptureLimit, state.Progress + change),
            (false, true) => (int)Math.Max(-CaptureLimit, state.Progress - change),
            _ when state.Progress > 0 => (int)Math.Max(0, state.Progress - change),
            _ => (int)Math.Min(0, state.Progress + change)
        };

        // Ownership survives partial capture until the controlling team's progress reaches neutral.
        var owner = state.Owner switch
        {
            TownStruggleTeam.Order when progress <= 0 => TownStruggleTeam.Neutral,
            TownStruggleTeam.Chaos when progress >= 0 => TownStruggleTeam.Neutral,
            _ => state.Owner
        };
        owner = progress switch
        {
            CaptureLimit => TownStruggleTeam.Order,
            -CaptureLimit => TownStruggleTeam.Chaos,
            _ => owner
        };

        return new TownStruggleCaptureState(progress, owner);
    }

    public static TownStruggleZoneAward ScoreZone(
        TownStruggleCaptureState state,
        int orderCount,
        int chaosCount
    )
    {
        ValidateCapture(state);
        ValidateCounts(orderCount, chaosCount);

        if (orderCount > 0 && chaosCount > 0)
        {
            return new TownStruggleZoneAward(
                ContestedZonePoints,
                ContestedZonePoints,
                (decimal)ContestedZonePoints / orderCount,
                (decimal)ContestedZonePoints / chaosCount
            );
        }

        // A controlled, empty zone still earns team points until its ownership decays to neutral.
        return state.Owner switch
        {
            TownStruggleTeam.Order when chaosCount == 0 => new TownStruggleZoneAward(
                ControlledZonePoints,
                0,
                orderCount == 0 ? 0 : (decimal)ControlledZonePoints / orderCount,
                0
            ),
            TownStruggleTeam.Chaos when orderCount == 0 => new TownStruggleZoneAward(
                0,
                ControlledZonePoints,
                0,
                chaosCount == 0 ? 0 : (decimal)ControlledZonePoints / chaosCount
            ),
            _ => default
        };
    }

    public static TimeSpan RespawnDelay(int ownScore, int enemyScore)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ownScore);
        ArgumentOutOfRangeException.ThrowIfNegative(enemyScore);
        var deficit = Math.Max(0L, (long)enemyScore - ownScore);
        return TimeSpan.FromSeconds(Math.Max(30L, 90 - deficit / 100 * 5));
    }

    public static TownStruggleTeam Winner(int orderScore, int chaosScore)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(orderScore);
        ArgumentOutOfRangeException.ThrowIfNegative(chaosScore);
        return orderScore.CompareTo(chaosScore) switch
        {
            > 0 => TownStruggleTeam.Order,
            < 0 => TownStruggleTeam.Chaos,
            _ => TownStruggleTeam.Neutral
        };
    }

    /// <summary>
    /// Uses inclusive descending rank: first is 100%, last is 100 / total, and ties share their rank.
    /// </summary>
    public static decimal RankPercent(int rank, int total)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(total);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rank);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rank, total);
        return (decimal)(total - rank + 1) * 100 / total;
    }

    public static int RewardPoints(
        int rank,
        int total,
        bool winningTeam,
        bool isMvp,
        int healerRank,
        int teamSize,
        bool qualified = true
    )
    {
        if (!qualified)
        {
            return 0;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(healerRank);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(teamSize);
        var points = RankPercent(rank, total) switch
        {
            >= 75 => 3,
            >= 50 => 2,
            >= 25 => 1,
            _ => 0
        };

        if (winningTeam && points > 0)
        {
            points++;
        }

        if (isMvp)
        {
            points += 2;
        }

        points += healerRank switch
        {
            1 => 2,
            2 when teamSize >= 6 => 1,
            _ => 0
        };
        return Math.Min(MaximumRewardPoints, points);
    }

    private static void ValidateCapture(TownStruggleCaptureState state)
    {
        if (state.Progress is < -CaptureLimit or > CaptureLimit ||
            state.Owner is not (TownStruggleTeam.Neutral or TownStruggleTeam.Order or TownStruggleTeam.Chaos) ||
            state.Owner == TownStruggleTeam.Order && state.Progress <= 0 ||
            state.Owner == TownStruggleTeam.Chaos && state.Progress >= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }
    }

    private static void ValidateCounts(int orderCount, int chaosCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(orderCount);
        ArgumentOutOfRangeException.ThrowIfNegative(chaosCount);
    }
}
