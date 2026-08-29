namespace Server.Mobiles
{
    public partial class PlayerMobile
    {
        private long _lumberHarvested, _monsterGoldGenerated, _monstersKilled, _treasureMapsCompleted;
        private long _leatherGathered, _boardsUsed, _leatherUsed, _ingotsUsed, _oreMined;
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
            AddLeaderboardStat(Server.Engines.Custom.LeaderboardStat.OreMined, amount);
        }

        public long GetLeaderboardStat(Server.Engines.Custom.LeaderboardStat stat) => stat switch
        {
            Server.Engines.Custom.LeaderboardStat.OreMined => _oreMined,
            Server.Engines.Custom.LeaderboardStat.LumberHarvested => _lumberHarvested,
            Server.Engines.Custom.LeaderboardStat.MonsterGoldGenerated => _monsterGoldGenerated,
            Server.Engines.Custom.LeaderboardStat.MonstersKilled => _monstersKilled,
            Server.Engines.Custom.LeaderboardStat.TreasureMapsCompleted => _treasureMapsCompleted,
            Server.Engines.Custom.LeaderboardStat.LeatherGathered => _leatherGathered,
            Server.Engines.Custom.LeaderboardStat.BoardsUsed => _boardsUsed,
            Server.Engines.Custom.LeaderboardStat.LeatherUsed => _leatherUsed,
            _ => _ingotsUsed
        };

        public void SetLeaderboardStat(Server.Engines.Custom.LeaderboardStat stat, long value)
        {
            switch (stat)
            {
                case Server.Engines.Custom.LeaderboardStat.OreMined: _oreMined = value; break;
                case Server.Engines.Custom.LeaderboardStat.LumberHarvested: _lumberHarvested = value; break;
                case Server.Engines.Custom.LeaderboardStat.MonsterGoldGenerated: _monsterGoldGenerated = value; break;
                case Server.Engines.Custom.LeaderboardStat.MonstersKilled: _monstersKilled = value; break;
                case Server.Engines.Custom.LeaderboardStat.TreasureMapsCompleted: _treasureMapsCompleted = value; break;
                case Server.Engines.Custom.LeaderboardStat.LeatherGathered: _leatherGathered = value; break;
                case Server.Engines.Custom.LeaderboardStat.BoardsUsed: _boardsUsed = value; break;
                case Server.Engines.Custom.LeaderboardStat.LeatherUsed: _leatherUsed = value; break;
                case Server.Engines.Custom.LeaderboardStat.IngotsUsed: _ingotsUsed = value; break;
            }
        }

        public void AddLeaderboardStat(Server.Engines.Custom.LeaderboardStat stat, long amount) =>
            SetLeaderboardStat(stat, GetLeaderboardStat(stat) + amount);

        public void SerializeLeaderboardStats(IGenericWriter writer)
        {
            for (var stat = Server.Engines.Custom.LeaderboardStat.OreMined; stat <= Server.Engines.Custom.LeaderboardStat.IngotsUsed; stat++) writer.Write(GetLeaderboardStat(stat));
        }

        public void DeserializeLeaderboardStats(IGenericReader reader)
        {
            for (var stat = Server.Engines.Custom.LeaderboardStat.OreMined; stat <= Server.Engines.Custom.LeaderboardStat.IngotsUsed; stat++) SetLeaderboardStat(stat, reader.ReadLong());
        }

    }
}
