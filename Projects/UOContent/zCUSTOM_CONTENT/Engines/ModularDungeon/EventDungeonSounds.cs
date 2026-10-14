namespace Server.Engines.ModularDungeon;

internal static class EventDungeonSounds
{
    public const int Working = 0x241;

    // Reserved entry in the shard's client sound archive.
    public const int Unlocked = 0xF01;

    // The full stock clip lasts about 2.665 seconds; align replays with the 40 ms validation cadence.
    public const int WorkingCadenceMilliseconds = 2680;
}
