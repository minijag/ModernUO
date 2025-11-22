using Server.Mobiles;

namespace Server.Commands
{
    public static class OreMiningCommand
    {
        public static void Configure()
        {
            CommandSystem.Register("OreMined", AccessLevel.Player, HowMuchOreMined_OnCommand);
        }

        [Usage("OreMined")]
        [Description("Displays how much ore you have mined in your lifetime.")]
        private static void HowMuchOreMined_OnCommand(CommandEventArgs e)
        {
            if (e.Mobile is PlayerMobile pm)
            {
                pm.SendMessage($"You have mined {pm.TotalOreMined} ore in your lifetime.");
            }
            else
            {
                e.Mobile.SendMessage("This command is only available to players.");
            }
        }
    }
}
