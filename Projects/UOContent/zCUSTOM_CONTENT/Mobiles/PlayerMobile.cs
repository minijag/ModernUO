namespace Server.Mobiles
{
    public partial class PlayerMobile
    {
        private int _OreMinedTotal = 0;
        public int OreMinedTotal { get { return _OreMinedTotal; } set { _OreMinedTotal = value; } }

        [CommandProperty(AccessLevel.GameMaster)]
        public int TotalOreMined
        {
            get => OreMinedTotal;
            set => OreMinedTotal = value;
        }

        public void IncrementOreMined(int amount)
        {
            OreMinedTotal += amount;
        }

    }
}