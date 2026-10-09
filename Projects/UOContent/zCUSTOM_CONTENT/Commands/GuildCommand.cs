using Server.Guilds;

namespace Server.Commands;

public static class GuildCommand
{
    public static void Configure() => CommandSystem.Register("Guild", AccessLevel.Player, OnGuild);

    [Usage("Guild")]
    [Description("Opens your guild menu without requiring a visit to the guildstone.")]
    private static void OnGuild(CommandEventArgs e) => Guild.GuildGumpRequest(e.Mobile);
}
