using System;

namespace Server.Engines.ModularDungeon;

internal static class EventDungeonOpening
{
    public const double DoorBaseSeconds = 4;
    public const double ChestBaseSeconds = 6;
    public const double LockpickingMaximumReduction = 2;
    public const double RemoveTrapMaximumReduction = 1;
    public const int ValidationIntervalMilliseconds = 40;

    public static TimeSpan GetDelay(Mobile from, double baseSeconds)
    {
        var lockpicking = Math.Clamp(from.Skills[SkillName.Lockpicking].Value, 0, 100);
        var removeTrap = Math.Clamp(from.Skills[SkillName.RemoveTrap].Value, 0, 100);
        return TimeSpan.FromSeconds(baseSeconds
            - lockpicking / 100 * LockpickingMaximumReduction
            - removeTrap / 100 * RemoveTrapMaximumReduction);
    }

    // Standard sound packets cannot interrupt the current clip; cancellation suppresses future replays.
    public static bool CanReplayWorkingSound(long nextSoundTick, long deadlineTick) =>
        Core.TickCount - nextSoundTick >= 0 && Core.TickCount - deadlineTick < 0;
}
