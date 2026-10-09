using Server.Gumps;

namespace Server.Commands;

public static class CharacterStatsCommand
{
    public static void Configure() => CommandSystem.Register("Stats", AccessLevel.Player, OnStats);

    [Usage("Stats")]
    [Description("Opens your character sheet with current attributes, equipment and combat stats.")]
    private static void OnStats(CommandEventArgs e) => CharacterStatsGump.DisplayTo(e.Mobile);
}
