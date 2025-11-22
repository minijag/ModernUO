using Server.Mobiles;
using Server.Engines.Harvest;
using Server.Items;

namespace Server.Engines.Harvest.Custom
{
    public class MiningTracker
    {
        public static void OnOreMined(Mobile from, Item ore, int amount)
        {
            if (from is PlayerMobile pm && ore is not BaseGranite)
            {
                pm.IncrementOreMined(amount);
            }
        }
    }
}
