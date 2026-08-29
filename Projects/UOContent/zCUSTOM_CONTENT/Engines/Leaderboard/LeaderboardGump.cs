using Server.Gumps;
using Server.Mobiles;
using Server.Network;
using Server.Text;

namespace Server.Engines.Custom;

public sealed class LeaderboardGump : DynamicGump
{
    private readonly PlayerMobile _from;
    private readonly LeaderboardStat _stat;
    public override bool Singleton => true;

    public LeaderboardGump(PlayerMobile from, LeaderboardStat stat = LeaderboardStat.OreMined) : base(0, 0)
    {
        _from = from;
        _stat = stat;
    }

    protected override void BuildLayout(ref DynamicGumpBuilder builder)
    {
        const int x = 50, y = 25, width = 540, height = 430;
        const string gold = "#F4D27A", cream = "#F2F0E8";
        builder.AddPage(0);
        CustomGumpStyle.AddWalnutSteelPanel(ref builder, x, y, width, height);
        builder.AddHtml(x + 20, y + 18, width - 40, 24, "LEADERBOARDS", gold, 5, align: TextAlignment.Center);

        AddTab(ref builder, x + 25, y + 52, "GATHERING", 1, _stat is LeaderboardStat.OreMined or LeaderboardStat.LumberHarvested or LeaderboardStat.LeatherGathered);
        AddTab(ref builder, x + 135, y + 52, "COMBAT", 2, _stat is LeaderboardStat.MonstersKilled);
        AddTab(ref builder, x + 225, y + 52, "WEALTH", 3, _stat is LeaderboardStat.MonsterGoldGenerated);
        AddTab(ref builder, x + 315, y + 52, "CRAFTING", 4, _stat is LeaderboardStat.BoardsUsed or LeaderboardStat.LeatherUsed or LeaderboardStat.IngotsUsed);
        AddTab(ref builder, x + 415, y + 52, "MAPS", 5, _stat is LeaderboardStat.TreasureMapsCompleted);
        builder.AddHtml(x + 20, y + 88, width - 40, 18, GetName(_stat), gold, 3, align: TextAlignment.Center);
        builder.AddHtml(x + 34, y + 112, 80, 16, "RANKING", gold, 1);
        builder.AddHtml(x + 140, y + 112, 220, 16, "PLAYER NAME", gold, 1);
        builder.AddHtml(x + 385, y + 112, 115, 16, "TOTAL", gold, 1, align: TextAlignment.Right);

        var rows = LeaderboardService.GetTop(_stat, 10);
        for (var i = 0; i < 10; i++)
        {
            var rowY = y + 134 + i * 22;
            if (i < rows.Count)
            {
                builder.AddHtml(x + 40, rowY, 60, 16, (i + 1).ToString(), gold, 1);
                builder.AddHtml(x + 140, rowY, 220, 16, rows[i].PlayerName, cream, 1);
                builder.AddHtml(x + 385, rowY, 115, 16, rows[i].Total.ToString("N0"), cream, 1, align: TextAlignment.Right);
            }
        }
        builder.AddHtml(x + 34, y + 366, 310, 18, $"YOUR RANKING: {_from.GetLeaderboardStat(_stat):N0}", gold, 1);
        builder.AddButton(x + width - 120, y + height - 48, 4005, 4007, 0, GumpButtonType.Reply, 0);
        builder.AddHtml(x + width - 90, y + height - 48, 65, 18, "CLOSE", gold, 1);
    }

    private static void AddTab(ref DynamicGumpBuilder builder, int x, int y, string label, int id, bool selected)
    {
        builder.AddButton(x, y, selected ? 4005 : 4007, selected ? 4007 : 4005, id, GumpButtonType.Reply, 0);
        builder.AddHtml(x + 20, y, 72, 18, label, selected ? "#F4D27A" : "#F2F0E8", 1, align: TextAlignment.Center);
    }

    private static string GetName(LeaderboardStat stat) => stat switch
    {
        LeaderboardStat.OreMined => "ORE MINED", LeaderboardStat.LumberHarvested => "LUMBER HARVESTED",
        LeaderboardStat.MonsterGoldGenerated => "MONSTER GOLD", LeaderboardStat.MonstersKilled => "MONSTERS KILLED",
        LeaderboardStat.TreasureMapsCompleted => "TREASURE MAPS", LeaderboardStat.LeatherGathered => "LEATHER GATHERED",
        LeaderboardStat.BoardsUsed => "BOARDS USED", LeaderboardStat.LeatherUsed => "LEATHER USED", _ => "INGOTS USED"
    };

    public override void OnResponse(NetState state, in RelayInfo info)
    {
        var stat = info.ButtonID switch
        {
            1 => LeaderboardStat.OreMined, 2 => LeaderboardStat.MonstersKilled, 3 => LeaderboardStat.MonsterGoldGenerated,
            4 => LeaderboardStat.IngotsUsed, 5 => LeaderboardStat.TreasureMapsCompleted, _ => _stat
        };
        if (info.ButtonID != 0) state.Mobile.SendGump(new LeaderboardGump(_from, stat));
    }
}
