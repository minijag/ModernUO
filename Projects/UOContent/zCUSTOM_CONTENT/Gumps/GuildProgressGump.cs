using System.Net;
using Server.Engines.Custom;
using Server.Guilds;
using Server.Mobiles;
using Server.Network;

namespace Server.Gumps;

public sealed class GuildProgressGump : DynamicGump
{
    private readonly PlayerMobile _owner;
    private readonly Guild _guild;

    public override bool Singleton => true;

    private GuildProgressGump(PlayerMobile owner, Guild guild) : base(50, 50)
    {
        _owner = owner;
        _guild = guild;
    }

    public static void DisplayTo(Mobile from, Guild guild)
    {
        if (from is not PlayerMobile { Deleted: false, NetState: not null } player || guild is not { Disbanded: false } ||
            !guild.IsMember(player) || player.Guild != guild)
        {
            return;
        }

        player.SendGump(new GuildProgressGump(player, guild));
    }

    protected override void BuildLayout(ref DynamicGumpBuilder builder)
    {
        builder.AddPage();
        CustomGumpStyle.AddPanel(ref builder, 0, 0, 560, 360, "GUILD PROGRESS");
        var progress = GuildProgressService.GetProgress(_guild);
        builder.AddHtml(30, 45, 500, 26, WebUtility.HtmlEncode(_guild.Name), CustomGumpStyle.WhiteTextColor,
            CustomGumpStyle.StandardFontSize, align: TextAlignment.Center);

        AddRow(ref builder, 94, "Guild level", progress.Level);
        AddRow(ref builder, 128, "Standing points", progress.StandingPoints);
        AddRow(ref builder, 162, "Bosses killed", progress.BossesKilled);
        AddRow(ref builder, 196, "Gold farmed", progress.GoldFarmed);
        builder.AddHtml(30, 244, 500, 44, $"{progress.PointsToNextLevel:N0} standing points to the next level. Boss kills are the only source of standing.",
            CustomGumpStyle.WhiteTextColor, CustomGumpStyle.StandardFontSize);

        builder.AddButton(30, 310, 4023, 4025, 1);
        builder.AddHtml(62, 308, 130, 24, "BACK TO GUILD", CustomGumpStyle.WhiteTextColor, CustomGumpStyle.StandardFontSize);
        builder.AddButton(425, 310, 4005, 4007, 0);
        builder.AddHtml(457, 308, 75, 24, "CLOSE", CustomGumpStyle.WhiteTextColor, CustomGumpStyle.StandardFontSize);
    }

    private static void AddRow(ref DynamicGumpBuilder builder, int y, string label, long value)
    {
        builder.AddHtml(40, y, 300, 26, label, CustomGumpStyle.WhiteTextColor, CustomGumpStyle.StandardFontSize);
        builder.AddHtml(350, y, 170, 26, $"{value:N0}", CustomGumpStyle.WhiteTextColor,
            CustomGumpStyle.StandardFontSize, align: TextAlignment.Right);
    }

    public override void OnResponse(NetState sender, in RelayInfo info)
    {
        if (sender?.Mobile != _owner || _owner.NetState != sender || _owner.Deleted ||
            _guild.Disbanded || _owner.Guild != _guild || !_guild.IsMember(_owner))
        {
            return;
        }

        if (info.ButtonID == 1)
        {
            GuildGump.DisplayTo(_owner, _guild);
        }
    }
}
