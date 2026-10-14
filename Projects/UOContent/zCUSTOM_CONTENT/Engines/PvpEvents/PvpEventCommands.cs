namespace Server.Engines.PvpEvents;

public static class PvpEventCommands
{
    public static void Configure()
    {
        CommandSystem.Register("PvpEvents", AccessLevel.Player, args => PvpEventsGump.DisplayTo(args.Mobile));
    }
}
