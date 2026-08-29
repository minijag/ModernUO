using System;
using System.Runtime.CompilerServices;
using Server.Engines.ConPVP;
using Server.Factions;
using Server.Gumps;
using Server.Menus.Questions;
using Server.Mobiles;
using Server.Multis;
using Server.Network;
using Server.Regions;

namespace Server.Engines.Help.Custom;

public class ContainedMenu : QuestionMenu
{
    private static readonly string[] _options = [
        "Leave my old help request like it is.",
        "Remove my help request from the queue."
    ];

    private readonly Mobile _from;

    public ContainedMenu(Mobile from) : base(
        "You already have an open help request. We will have someone assist you as soon as possible.  What would you like to do?",
        _options
    ) => _from = from;

    public override void OnCancel(NetState state)
    {
        _from.SendLocalizedMessage(1005306, "", 0x35); // Help request unchanged.
    }

    public override void OnResponse(NetState state, int index)
    {
        if (index == 0)
        {
            _from.SendLocalizedMessage(1005306, "", 0x35); // Help request unchanged.
        }
        else if (index == 1)
        {
            var entry = PageQueue.GetEntry(_from);

            if (entry != null && entry.Handler == null)
            {
                _from.SendLocalizedMessage(1005307, "", 0x35); // Removed help request.
                // entry.AddResponse(entry.Sender, "[Canceled]");
                PageQueue.Remove(entry);
            }
            else
            {
                _from.SendLocalizedMessage(1005306, "", 0x35); // Help request unchanged.
            }
        }
    }
}

public sealed class HelpGumpCustom : DynamicGump
{

    public enum GumpFontColor
    {
        White = 0xFFFFFF,
        Yellow = 0xFFFF00,
        Red = 0xFF0000,
        Green = 0x00FF00,
        Blue = 0x0000FF,
        Purple = 0x800080,
        Orange = 0xFFA500,
        Brown = 0xA52A2A,
        Gray = 0x808080,
        LightGray = 0xD3D3D3,
        DarkGray = 0xA9A9A9,
        LightBlue = 0xADD8E6,
        LightGreen = 0x90EE90,
        LightRed = 0xFFB6C1,
        LightPurple = 0xEE82EE,
        LightOrange = 0xFFD700,
        LightBrown = 0xD2B48C,
        LightYellow = 0xFFFFE0,
        LightPink = 0xFFB6C1,
        LightSteelBlue = 0xB0C4DE,
    }

    private readonly Mobile _from;

    public override bool Singleton => true;

    public HelpGumpCustom(Mobile from) : base(0, 0) => _from = from;

    protected override void BuildLayout(ref DynamicGumpBuilder builder)
    {
        if (_from == null)
        {
            return;
        }

        const int x = 50;
        const int y = 25;
        const int width = 540;
        const int height = 430;

        const string titleColor = "#F4D27A";
        const string actionColor = "#FFE8A6";
        const string bodyColor = "#F2F0E8";
        builder.AddPage(0);
        CustomGumpStyle.AddWalnutSteelPanel(ref builder, x, y, width, height);

        builder.AddHtml(x + 22, y + 19, width - 44, 22, "HELP MENU", titleColor, 5, align: TextAlignment.Center);
        builder.AddHtml(
            x + 22, y + 40, width - 44, 14, "Choose the kind of help you need.", bodyColor, 1, align: TextAlignment.Center
        );

        AddHelpOption(
            ref builder, x + 28, y + 82, width - 56, 2, "I'M STUCK",
            "Get help if your character cannot move or escape.", actionColor, bodyColor
        );
        AddHelpOption(
            ref builder, x + 28, y + 170, width - 56, 7, "REPORT HARASSMENT",
            "Report a player who is breaking the rules.", actionColor, bodyColor
        );
        AddHelpOption(
            ref builder, x + 28, y + 258, width - 56, 3, "REPORT A BUG",
            "Send a bug report to help improve Zodiac.", actionColor, bodyColor
        );

        builder.AddButton(x + width - 130, y + height - 50, 4005, 4007, 0, GumpButtonType.Reply, 0);
        builder.AddHtml(x + width - 100, y + height - 50, 70, 20, "CLOSE", actionColor, 2);
    }

    private static void AddHelpOption(
        ref DynamicGumpBuilder builder, int x, int y, int width, int buttonId, string title, string description,
        string titleColor, string bodyColor
    )
    {
        builder.AddButton(x + 18, y + 24, 4005, 4007, buttonId, GumpButtonType.Reply, 0);
        builder.AddHtml(x + 54, y + 11, width - 70, 22, title, titleColor, 3);
        builder.AddHtml(x + 54, y + 36, width - 70, 22, description, bodyColor, 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddOption(
        ref DynamicGumpBuilder builder, int y, int buttonId, int localizedName, GumpButtonType type = GumpButtonType.Reply,
        int page = 0
    ) => AddOption(ref builder, y, 74, buttonId, localizedName, type, page);

    private static void AddOption(
        ref DynamicGumpBuilder builder, int y, int height, int buttonId, int localizedName,
        GumpButtonType type = GumpButtonType.Reply, int page = 0
    )
    {
        builder.AddButton(80, y, 9721, 9722, buttonId, type, page);
        builder.AddHtmlLocalized(
            110,
            y,
            450,
            height,
            localizedName,
            true,
            true
        );
    }

    public static void HelpRequest(Mobile m)
    {
        if (m.HasGump<HelpGumpCustom>())
        {
            return;
        }

        if (!PageQueue.CheckAllowedToPage(m))
        {
            return;
        }

        if (PageQueue.Contains(m))
        {
            m.SendMenu(new ContainedMenu(m));
        }
        else
        {
            m.SendGump(new HelpGumpCustom(m));
        }
    }

    private static bool IsYoung(Mobile m) => m is PlayerMobile mobile && mobile.Young;

    public static bool CheckCombat(Mobile m)
    {
        for (var i = 0; i < m.Aggressed.Count; ++i)
        {
            var info = m.Aggressed[i];

            if (Core.Now - info.LastCombatTime < TimeSpan.FromSeconds(30.0))
            {
                return true;
            }
        }

        return false;
    }

    public override void OnResponse(NetState state, in RelayInfo info)
    {
        var from = state.Mobile;

        var type = (PageType)(-1);

        switch (info.ButtonID)
        {
            case 0: // Close/Cancel
                {
                    from.SendLocalizedMessage(501235, "", 0x35); // Help request aborted.

                    break;
                }
            case 1: // General question
                {
                    type = PageType.Question;
                    break;
                }
            case 2: // Stuck
                {
                    var house = BaseHouse.FindHouseAt(from);

                    if (house?.IsAosRules == true && !from.Region.IsPartOf<SafeZone>()) // Dueling
                    {
                        from.Location = house.BanLocation;
                    }
                    else if (from.Region.IsPartOf<JailRegion>())
                    {
                        from.SendLocalizedMessage(1114345, "", 0x35); // You'll need a better jailbreak plan than that!
                    }
                    else if (Sigil.ExistsOn(from))
                    {
                        from.SendLocalizedMessage(1061632); // You can't do that while carrying the sigil.
                    }
                    else if (from is PlayerMobile mobile && mobile.CanUseStuckMenu())
                    {
                        var menu = new StuckMenu(mobile, mobile, true);

                        menu.BeginClose();

                        mobile.SendGump(menu);
                    }
                    else
                    {
                        from.SendMessage("You cant use the stuck menu anymore wait up to 24 hours"); 
                    }

                    break;
                }
            case 3: // Report bug
                {
                    type = PageType.Bug;
                    break;
                }
            case 4: // Game suggestion
                {
                    type = PageType.Suggestion;
                    break;
                }
            case 5: // Account management
                {
                    type = PageType.Account;
                    break;
                }
            case 6: // Other
                {
                    type = PageType.Other;
                    break;
                }
            case 7: // Harassment: verbal/exploit
                {
                    type = PageType.VerbalHarassment;
                    break;
                }
            case 8: // Harassment: physical
                {
                    type = PageType.PhysicalHarassment;
                    break;
                }
            case 9: // Young player transport
                {
                    if (IsYoung(from))
                    {
                        if (from.Region.IsPartOf<JailRegion>())
                        {
                            // You'll need a better jailbreak plan than that!
                            from.SendLocalizedMessage(1114345, "", 0x35);
                        }
                        else if (from.Region.IsPartOf("Haven Island"))
                        {
                            from.SendLocalizedMessage(1041529); // You're already in Haven
                        }
                        else
                        {
                            from.MoveToWorld(new Point3D(3503, 2574, 14), Map.Trammel);
                        }
                    }

                    break;
                }
        }

        if (type != (PageType)(-1) && PageQueue.CheckAllowedToPage(from))
        {
            from.SendGump(new PagePromptGump(from, type));
        }
    }
}
