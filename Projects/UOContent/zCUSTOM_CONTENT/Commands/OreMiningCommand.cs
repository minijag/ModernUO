using Server.Mobiles;
using Server.Engines.Custom;

namespace Server.Commands
{
    public static class OreMiningCommand
    {
        public static void Configure()
        {
            CommandSystem.Register("OreMined", AccessLevel.Player, HowMuchOreMined_OnCommand);
            CommandSystem.Register("OreLeaderboard", AccessLevel.Player, OreLeaderboard_OnCommand);
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

        [Usage("OreLeaderboard")]
        [Description("Displays the top 10 players who have mined the most ore.")]
        private static void OreLeaderboard_OnCommand(CommandEventArgs e)
        {
            var leaderboard = OreLeaderboard.GetLeaderboard(10);

            if (leaderboard.Count == 0)
            {
                e.Mobile.SendMessage("No players have mined ore yet.");
                return;
            }

            e.Mobile.SendMessage("=== Top 10 Ore Miners ===");
            
            for (int i = 0; i < leaderboard.Count; i++)
            {
                var entry = leaderboard[i];
                var rank = i + 1;
                e.Mobile.SendMessage($"{rank}. {entry.PlayerName}: {entry.TotalOreMined:N0} ore");
            }
        }
    }
}