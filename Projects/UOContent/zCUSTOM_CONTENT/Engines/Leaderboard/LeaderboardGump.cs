using System.Collections.Generic;
using System.Net;
using Server.Gumps;
using Server.Mobiles;
using Server.Network;

namespace Server.Engines.Custom;

public sealed class LeaderboardGump : DynamicGump
{
    internal const int RowsPerPage = 10;
    internal const int PreviousPageButton = 100;
    internal const int NextPageButton = 101;
    internal const int ShowMyRankingButton = 102;

    private static readonly LeaderboardStat[] _visibleStats =
    [
        LeaderboardStat.OreMined,
        LeaderboardStat.LumberHarvested,
        LeaderboardStat.LeatherGathered,
        LeaderboardStat.TreasureMapsCompleted,
        LeaderboardStat.GoldFarmed,
        LeaderboardStat.MonstersKilled
    ];

    private readonly PlayerMobile _from;
    private readonly LeaderboardStat _stat;
    private readonly LeaderboardPage _page;

    public override bool Singleton => true;

    private LeaderboardGump(PlayerMobile from, LeaderboardStat stat, in LeaderboardPage page) : base(50, 50)
    {
        _from = from;
        _stat = stat;
        _page = page;
    }

    public static void DisplayTo(
        Mobile from, LeaderboardStat stat = LeaderboardStat.OreMined, int pageIndex = 0, bool showMyRanking = false
    )
    {
        if (from is PlayerMobile { Deleted: false, NetState: not null } player && GetName(stat) != null)
        {
            var page = LeaderboardService.GetPage(stat, pageIndex, RowsPerPage, player, showMyRanking);
            if (showMyRanking && page.OwnRank == null)
            {
                player.SendMessage("You have not earned a ranking in this category yet.");
            }

            player.SendGump(new LeaderboardGump(player, stat, page));
        }
    }

    protected override void BuildLayout(ref DynamicGumpBuilder builder) =>
        BuildBoard(ref builder, _stat, _page);

    internal static void BuildBoard(ref DynamicGumpBuilder builder, LeaderboardStat stat, in LeaderboardPage page)
    {
        const string gold = CustomGumpStyle.TitleColor;
        const string white = CustomGumpStyle.WhiteTextColor;
        const int font = CustomGumpStyle.StandardFontSize;

        builder.AddPage();
        CustomGumpStyle.AddPanel(ref builder, 0, 0, 700, 580, "LEADERBOARDS");
        builder.AddHtml(28, 54, 220, 24, "CATEGORIES", gold, font);
        for (var i = 0; i < _visibleStats.Length; i++)
        {
            var category = _visibleStats[i];
            var y = 102 + i * 50;
            CustomGumpStyle.AddOptionButton(ref builder, 28, y + 1, i + 1, category == stat);
            AddCategoryIcon(ref builder, category, 84, y + 20);
            var labelY = category == LeaderboardStat.TreasureMapsCompleted ? y : y + 8;
            builder.AddHtml(114, labelY, 142, 40, GetName(category), category == stat ? gold : white, font);
        }

        AddCategoryIcon(ref builder, stat, 308, 76);
        builder.AddHtml(344, 66, 328, 24, GetName(stat), gold, font, align: TextAlignment.Center);
        builder.AddHtml(278, 112, 56, 24, "RANK", gold, font);
        builder.AddHtml(338, 112, 193, 24, "PLAYER NAME", gold, font);
        builder.AddHtml(542, 112, 130, 24, "TOTAL", gold, font, align: TextAlignment.Right);

        for (var i = 0; i < RowsPerPage && i < page.Entries.Count; i++)
        {
            var row = page.Entries[i];
            var rank = page.PageIndex * RowsPerPage + i + 1;
            var color = rank == page.OwnRank ? gold : white;
            var y = 148 + i * 29;
            builder.AddHtml(278, y, 56, 24, $"{rank:N0}", color, font);
            builder.AddHtml(338, y, 193, 24, WebUtility.HtmlEncode(row.PlayerName), color, font);
            builder.AddHtml(542, y, 130, 24, $"{row.Total:N0}", color, font, align: TextAlignment.Right);
        }

        if (page.Entries.Count == 0)
        {
            builder.AddHtml(278, 164, 394, 48, "No totals recorded yet.", white, font, align: TextAlignment.Center);
        }

        if (page.PageIndex > 0)
        {
            CustomGumpStyle.AddOptionButton(ref builder, 278, 446, PreviousPageButton);
            builder.AddHtml(315, 449, 90, 24, "PREVIOUS", white, font);
        }

        builder.AddHtml(406, 449, 158, 24, $"PAGE {page.PageIndex + 1:N0} / {page.PageCount:N0}", gold, font,
            align: TextAlignment.Center);
        if (page.PageIndex + 1 < page.PageCount)
        {
            CustomGumpStyle.AddOptionButton(ref builder, 576, 446, NextPageButton);
            builder.AddHtml(612, 449, 60, 24, "NEXT", white, font);
        }

        builder.AddHtml(278, 490, 250, 24, $"YOUR TOTAL: {page.OwnTotal:N0}", white, font);
        if (page.OwnRank is { } ownRank)
        {
            builder.AddHtml(532, 490, 140, 24, $"YOUR RANK: {ownRank:N0}", gold, font, align: TextAlignment.Right);
        }
        else
        {
            builder.AddHtml(532, 490, 140, 24, "UNRANKED", white, font, align: TextAlignment.Right);
        }

        CustomGumpStyle.AddOptionButton(ref builder, 278, 526, ShowMyRankingButton);
        builder.AddHtml(317, 529, 210, 24, "SHOW MY RANKING", CustomGumpStyle.ActionColor, font);
        CustomGumpStyle.AddOptionButton(ref builder, 560, 526, 0);
        builder.AddHtml(599, 529, 73, 24, "CLOSE", white, font);
    }

    private static void AddCategoryIcon(ref DynamicGumpBuilder builder, LeaderboardStat stat, int centerX, int centerY)
    {
        var (itemId, offsetX, offsetY) = GetIcon(stat);
        builder.AddItem(centerX - offsetX, centerY - offsetY, itemId);
    }

    internal static int GetIconItemId(LeaderboardStat stat) => GetIcon(stat).ItemId;

    // Native item art includes transparent padding; center its visible pixels in the icon slot.
    private static (int ItemId, int CenterOffsetX, int CenterOffsetY) GetIcon(LeaderboardStat stat) => stat switch
    {
        LeaderboardStat.OreMined => (0x19B8, 20, 12),
        LeaderboardStat.LumberHarvested => (0x1BDD, 26, 11),
        LeaderboardStat.LeatherGathered => (0x1081, 25, 18),
        LeaderboardStat.TreasureMapsCompleted => (0x14EB, 21, 15),
        LeaderboardStat.GoldFarmed => (0xEEF, 23, 13),
        LeaderboardStat.MonstersKilled => (0x2203, 11, 13),
        _ => default
    };

    internal static LeaderboardStat? GetStatFromButton(int buttonId) =>
        buttonId > 0 && buttonId <= _visibleStats.Length ? _visibleStats[buttonId - 1] : null;

    internal static string GetName(LeaderboardStat stat) => stat switch
    {
        LeaderboardStat.OreMined => "Ore Mined",
        LeaderboardStat.LumberHarvested => "Lumber Chopped",
        LeaderboardStat.LeatherGathered => "Leather Skinned",
        LeaderboardStat.TreasureMapsCompleted => "Treasure Maps Completed",
        LeaderboardStat.GoldFarmed => "Gold Farmed",
        LeaderboardStat.MonstersKilled => "Monsters Killed",
        _ => null
    };

    public override void OnResponse(NetState state, in RelayInfo info)
    {
        if (state == null || state != _from.NetState || state.Mobile != _from || _from.Deleted)
        {
            return;
        }

        if (GetStatFromButton(info.ButtonID) is { } stat)
        {
            DisplayTo(_from, stat);
            return;
        }

        switch (info.ButtonID)
        {
            case PreviousPageButton when _page.PageIndex > 0:
                DisplayTo(_from, _stat, _page.PageIndex - 1);
                break;
            case NextPageButton when _page.PageIndex + 1 < _page.PageCount:
                DisplayTo(_from, _stat, _page.PageIndex + 1);
                break;
            case ShowMyRankingButton:
                DisplayTo(_from, _stat, _page.PageIndex, showMyRanking: true);
                break;
        }
    }
}
