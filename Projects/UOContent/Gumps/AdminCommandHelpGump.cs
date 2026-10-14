using System.Net;
using Server.Network;

namespace Server.Gumps;

internal sealed class AdminCommandHelpGump : DynamicGump
{
    private readonly Mobile _from;
    private readonly AdminCommandHelp.Entry _entry;
    private readonly AdminGumpPage _returnPage;
    private readonly string _broadcastDraft;
    private readonly string _commandHtml;

    public override bool Singleton => true;
    internal int CommandIndex => _entry.Index;
    internal AdminGumpPage ReturnPage => _returnPage;

    private AdminCommandHelpGump(Mobile from, AdminCommandHelp.Entry entry, AdminGumpPage returnPage, string broadcastDraft) : base(50, 40)
    {
        _from = from;
        _entry = entry;
        _returnPage = returnPage;
        _broadcastDraft = broadcastDraft;
        _commandHtml = WebUtility.HtmlEncode(entry.Command);
    }

    internal static void DisplayTo(Mobile from, int index, AdminGumpPage returnPage, string broadcastDraft = null)
    {
        if (from?.NetState == null || !AdminCommandHelp.TryGet(index, out var entry) || !AdminCommandHelp.CanRead(from, returnPage, entry))
        {
            return;
        }
        from.SendGump(new AdminCommandHelpGump(from, entry, returnPage, broadcastDraft));
    }

    protected override void BuildLayout(ref DynamicGumpBuilder builder)
    {
        builder.AddPage();
        CustomGumpStyle.AddPanel(ref builder, 0, 0, 640, 540, "COMMAND INFO");
        builder.AddHtml(32, 42, 576, 28, _entry.Title, CustomGumpStyle.TitleColor, CustomGumpStyle.StandardFontSize);
        builder.AddHtml(32, 76, 576, 28, "Read-only help. This window cannot run the command.",
            CustomGumpStyle.TextColor, CustomGumpStyle.StandardFontSize);
        builder.AddHtml(32, 116, 576, 344,
            $"<B>Command / control</B><BR>{_commandHtml}<BR><BR><B>What it does</B><BR>{_entry.Behavior}<BR><BR><B>Target and scope</B><BR>{_entry.Scope}<BR><BR><B>Access to use this menu control</B><BR>{_entry.CommandAccess}<BR><BR><B>Before you use it</B><BR>{_entry.Caution}",
            CustomGumpStyle.TextColor, CustomGumpStyle.StandardFontSize, scrollbar: true);
        builder.AddButton(32, 488, 4023, 4025, 1);
        builder.AddHtml(68, 486, 330, 28, "Back to Administer", CustomGumpStyle.ActionColor, CustomGumpStyle.StandardFontSize);
        builder.AddButton(486, 488, 4017, 4019, 0);
        builder.AddHtml(522, 486, 82, 28, "Close", CustomGumpStyle.ActionColor, CustomGumpStyle.StandardFontSize);
    }

    public override void OnResponse(NetState sender, in RelayInfo info)
    {
        if (info.ButtonID == 1 && sender?.Mobile == _from && AdminCommandHelp.CanRead(_from, _returnPage, _entry))
        {
            _from.SendGump(new AdminGump(_from, _returnPage, broadcastDraft: _broadcastDraft));
        }
    }
}
