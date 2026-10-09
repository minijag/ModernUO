using Server.Engines.Custom;
using Server.Engines.Help.Custom;
using Server.Mobiles;
using Server.Network;

namespace Server.Gumps;

public sealed class HelpMenuGump : StaticGump<HelpMenuGump>
{
    private readonly Mobile _from;
    public override bool Singleton => true;
    protected override bool Cached => true;

    private HelpMenuGump(Mobile from) : base(50, 50) => _from = from;

    public static void DisplayTo(Mobile from)
    {
        if (from is { Deleted: false, NetState: not null })
        {
            from.SendGump(new HelpMenuGump(from));
        }
    }

    protected override void BuildLayout(ref StaticGumpBuilder builder) => BuildMenu(ref builder);

    internal static void BuildMenu(ref StaticGumpBuilder builder)
    {
        builder.AddPage();
        CustomGumpStyle.AddPanel(ref builder, 0, 0, 640, 340, "HELP MENU");
        CustomGumpStyle.AddMenuButton(ref builder, 98, 56, 1, "Website");
        CustomGumpStyle.AddMenuButton(ref builder, 246, 56, 2, "Wiki");
        CustomGumpStyle.AddMenuButton(ref builder, 394, 56, 3, "Discord");
        CustomGumpStyle.AddMenuButton(ref builder, 542, 56, 4, "In-game Help");
        CustomGumpStyle.AddMenuButton(ref builder, 148, 168, 5, "Stats");
        CustomGumpStyle.AddMenuButton(ref builder, 320, 168, 6, "Rankings");
        CustomGumpStyle.AddMenuButton(ref builder, 492, 168, 7, "Enchantments");
        builder.AddButton(524, 298, 4005, 4007, 0);
        builder.AddHtml(558, 298, 58, 24, "CLOSE", CustomGumpStyle.WhiteTextColor, CustomGumpStyle.StandardFontSize);
    }

    internal static string GetBrowserUrl(int buttonId) => buttonId switch
    {
        1 => "https://www.google.com/",
        2 => "https://www.bing.com/",
        3 => "https://www.discord.com/",
        _ => null
    };

    public override void OnResponse(NetState sender, in RelayInfo info)
    {
        if (sender.Mobile != _from || _from.Deleted)
        {
            return;
        }

        var url = GetBrowserUrl(info.ButtonID);
        if (url != null)
        {
            _from.LaunchBrowser(url);
            DisplayTo(_from);
            return;
        }

        switch (info.ButtonID)
        {
            case 4:
                HelpGumpCustom.RequestInGameHelp(_from);
                break;
            case 5:
                CharacterStatsGump.DisplayTo(_from);
                break;
            case 6 when _from is PlayerMobile player:
                LeaderboardGump.DisplayTo(player);
                break;
            case 7:
                EnchantmentsGump.DisplayTo(_from);
                break;
        }
    }
}
