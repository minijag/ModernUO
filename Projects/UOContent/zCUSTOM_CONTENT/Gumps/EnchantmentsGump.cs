using Server.Network;

namespace Server.Gumps;

public sealed class EnchantmentsGump : StaticGump<EnchantmentsGump>
{
    private readonly Mobile _from;
    public override bool Singleton => true;
    protected override bool Cached => true;

    private EnchantmentsGump(Mobile from) : base(50, 50) => _from = from;

    public static void DisplayTo(Mobile from)
    {
        if (from is { Deleted: false, NetState: not null })
        {
            from.SendGump(new EnchantmentsGump(from));
        }
    }

    protected override void BuildLayout(ref StaticGumpBuilder builder) => BuildPlaceholder(ref builder);

    internal static void BuildPlaceholder(ref StaticGumpBuilder builder)
    {
        // An unfinished shop still needs visible controls so it can be dismissed safely.
        builder.AddPage();
        CustomGumpStyle.AddPanel(ref builder, 0, 0, 540, 330, "ENCHANTMENTS");
        builder.AddButton(24, 286, 4023, 4025, 1);
        builder.AddHtml(58, 286, 160, 24, "HELP MENU", CustomGumpStyle.WhiteTextColor, CustomGumpStyle.StandardFontSize);
        builder.AddButton(424, 286, 4005, 4007, 0);
        builder.AddHtml(458, 286, 58, 24, "CLOSE", CustomGumpStyle.WhiteTextColor, CustomGumpStyle.StandardFontSize);
    }

    public override void OnResponse(NetState sender, in RelayInfo info)
    {
        if (info.ButtonID == 1 && sender.Mobile == _from && !_from.Deleted)
        {
            HelpMenuGump.DisplayTo(_from);
        }
    }
}
