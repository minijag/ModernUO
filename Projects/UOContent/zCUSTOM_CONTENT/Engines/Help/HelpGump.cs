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

        // Colors
        var titleColor = 0x433622; // White
        var mainTextColor = 0x8E7C5C; // Yellow
        var subTextColor = 0x988D7B; // Gray

        //EBCF8A
        titleColor = 0xF4A462; // White
        mainTextColor = 0xFFD080; // Yellow
        subTextColor = 0xA3906F; // Gray

        
        titleColor = 0x739ad1; // White
        mainTextColor = 0xF4A462; // Yellow
        subTextColor = 0xA3906F; // Gray

        builder.AddBackground(50, 25, 540, 430, 5100);
        builder.AddPage(0);
        
        // Close button
        builder.AddButton(425, 415, 2073, 2072, 0, GumpButtonType.Reply, 0);
        
        // Centered title "Help Menu"
        builder.AddHtml(150, 50, 360, 40, titleColor, "<CENTER>Help Menu</CENTER>", false, false);
        
        // Option 1: Character is stuck
        builder.AddButton(75, 125, 2362, 2151, 2, GumpButtonType.Reply, 0);
        builder.AddHtml(110, 120, 450, 20, mainTextColor, "My character is stuck in the game", false, false);
        builder.AddHtml(110, 140, 450, 40, subTextColor, "Covers a case when your character is stuck in a location they cannot move out of", false, false);
        
        // Option 2: Harassment
        builder.AddButton(75, 205, 2362, 2151, 7, GumpButtonType.Reply, 0);
        builder.AddHtml(110, 200, 450, 20, mainTextColor, "Another player is harassing me", false, false);
        builder.AddHtml(110, 220, 450, 40, subTextColor, "I am verbally harassed by another player and want to report it", false, false);
        
        // Option 3: Report a bug
        builder.AddButton(75, 285, 2362, 2151, 3, GumpButtonType.Reply, 0);
        builder.AddHtml(110, 280, 450, 20, mainTextColor, "Report a bug", false, false);
        builder.AddHtml(110, 300, 450, 40, subTextColor, "To report a bug, use the Discord channel. Type /report to describe the issue", false, false);


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
