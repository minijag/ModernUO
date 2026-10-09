using System.Collections.Generic;
using Server.Guilds;
using Server.Multis;
using Server.Network;
using Server.Prompts;

namespace Server.Gumps
{
    public class HouseListGump : DynamicGump
    {
        private readonly BaseHouse _house;
        private readonly bool _accountOf;
        private readonly List<Mobile> _list;
        private readonly int _number;

        public HouseListGump(int number, List<Mobile> list, BaseHouse house, bool accountOf) : base(40, 50)
        {
            _accountOf = accountOf;
            _list = list;
            _number = number;
            _house = house;
        }

        protected override void BuildLayout(ref DynamicGumpBuilder builder)
        {
            builder.AddPage();

            CustomGumpStyle.AddContentPanel(ref builder, 0, 0, 420, 430, "HOUSE ACCESS");

            builder.AddButton(20, 388, 4005, 4007, 0);
            builder.AddHtmlLocalized(55, 388, 300, 20, 1011104, CustomGumpStyle.LocalizedTextColor); // Return to previous menu

            builder.AddHtmlLocalized(20, 20, 350, 20, _number, CustomGumpStyle.LocalizedTextColor);

            if (_list == null)
            {
                return;
            }

            for (var i = 0; i < _list.Count; ++i)
            {
                if (i % 16 == 0)
                {
                    if (i != 0)
                    {
                        builder.AddButton(370, 20, 4005, 4007, 0, GumpButtonType.Page, i / 16 + 1);
                    }

                    builder.AddPage(i / 16 + 1);

                    if (i != 0)
                    {
                        builder.AddButton(340, 20, 4014, 4016, 0, GumpButtonType.Page, i / 16);
                    }
                }

                var m = _list[i];

                string name;

                if (m == null || (name = m.Name) == null || (name = name.Trim()).Length <= 0)
                {
                    continue;
                }

                builder.AddLabel(
                    55,
                    55 + i % 16 * 20,
                    CustomGumpStyle.TextHue,
                    _accountOf && m.Player && m.Account != null
                        ? $"Account of {name}"
                        : name
                );
            }
        }

        public override void OnResponse(NetState state, in RelayInfo info)
        {
            if (_house.Deleted)
            {
                return;
            }

            var from = state.Mobile;

            HouseGump.DisplayTo(from, _house);
        }
    }

    public class HouseRemoveGump : DynamicGump
    {
        private readonly bool _accountOf;
        private readonly List<Mobile> _copy;
        private readonly BaseHouse _house;
        private readonly List<Mobile> _list;
        private readonly int _number;

        public HouseRemoveGump(int number, List<Mobile> list, BaseHouse house, bool accountOf) : base(40, 50)
        {
            if (house.Deleted)
            {
                return;
            }

            _house = house;
            _list = list;
            _number = number;
            _accountOf = accountOf;

            if (list != null)
            {
                _copy = new List<Mobile>(list);
            }
        }

        protected override void BuildLayout(ref DynamicGumpBuilder builder)
        {
            builder.AddPage();

            CustomGumpStyle.AddContentPanel(ref builder, 0, 0, 420, 430, "HOUSE ACCESS");

            builder.AddButton(20, 388, 4005, 4007, 0);
            builder.AddHtmlLocalized(55, 388, 300, 20, 1011104, CustomGumpStyle.LocalizedTextColor); // Return to previous menu

            builder.AddButton(20, 365, 4005, 4007, 1);
            builder.AddHtmlLocalized(55, 365, 300, 20, 1011270, CustomGumpStyle.LocalizedTextColor); // Remove now!

            builder.AddHtmlLocalized(20, 20, 350, 20, _number, CustomGumpStyle.LocalizedTextColor);

            if (_list == null)
            {
                return;
            }

            for (var i = 0; i < _list.Count; ++i)
            {
                if (i % 15 == 0)
                {
                    if (i != 0)
                    {
                        builder.AddButton(370, 20, 4005, 4007, 0, GumpButtonType.Page, i / 15 + 1);
                    }

                    builder.AddPage(i / 15 + 1);

                    if (i != 0)
                    {
                        builder.AddButton(340, 20, 4014, 4016, 0, GumpButtonType.Page, i / 15);
                    }
                }

                var m = _list[i];

                string name;

                if (m == null || (name = m.Name) == null || (name = name.Trim()).Length <= 0)
                {
                    continue;
                }

                builder.AddCheckbox(34, 52 + i % 15 * 20, 0xD2, 0xD3, false, i);
                builder.AddLabel(
                    55,
                    52 + i % 15 * 20,
                    CustomGumpStyle.TextHue,
                    _accountOf && m.Player && m.Account != null
                        ? $"Account of {name}"
                        : name
                );
            }
        }

        public override void OnResponse(NetState state, in RelayInfo info)
        {
            if (_house.Deleted)
            {
                return;
            }

            var from = state.Mobile;

            if (_list != null && info.ButtonID == 1) // Remove now
            {
                var switches = info.Switches;

                if (switches.Length > 0)
                {
                    for (var i = 0; i < switches.Length; ++i)
                    {
                        var index = switches[i];

                        if (index >= 0 && index < _copy.Count)
                        {
                            _list.Remove(_copy[index]);
                        }
                    }

                    if (_list.Count > 0)
                    {
                        var gumps = from.GetGumps();

                        gumps.Close<HouseGump>();
                        gumps.Close<HouseListGump>();
                        gumps.Close<HouseRemoveGump>();
                        gumps.Send(new HouseRemoveGump(_number, _list, _house, _accountOf));
                        return;
                    }
                }
            }

            HouseGump.DisplayTo(from, _house);
        }
    }

    public class HouseGump : DynamicGump
    {
        private readonly Mobile _from;
        private readonly BaseHouse _house;

        public override bool Singleton => true;

        private HouseGump(Mobile from, BaseHouse house) : base(40, 50)
        {
            _from = from;
            _house = house;
        }

        public static void DisplayTo(Mobile from, BaseHouse house)
        {
            if (from?.NetState == null || house == null || house.Deleted)
            {
                return;
            }

            var gumps = from.GetGumps();

            gumps.Close<HouseListGump>();
            gumps.Close<HouseRemoveGump>();

            from.SendGump(new HouseGump(from, house));
        }

        protected override void BuildLayout(ref DynamicGumpBuilder builder)
        {
            var isCombatRestricted = _house.IsCombatRestricted(_from);

            var isOwner = _house.IsOwner(_from);
            var isCoOwner = isOwner || _house.IsCoOwner(_from);
            var isFriend = isCoOwner || _house.IsFriend(_from);

            if (isCombatRestricted)
            {
                isFriend = isCoOwner = isOwner = false;
            }

            builder.AddPage();

            CustomGumpStyle.AddContentPanel(ref builder, 0, 0, 420, isFriend ? 430 : 120, "HOUSE MANAGEMENT");

            builder.AddImage(130, 0, 100);

            if (_house.Sign != null)
            {
                var lines = _house.Sign.GetName().Wrap(10, 6);

                for (int i = 0, y = (101 - lines.Count * 14) / 2; i < lines.Count; ++i, y += 14)
                {
                    var s = lines[i];

                    builder.AddLabel(130 + (143 - s.Length * 8) / 2, y, 0, s);
                }
            }

            if (!isFriend)
            {
                return;
            }

            builder.AddHtmlLocalized(55, 103, 75, 20, 1011233, CustomGumpStyle.LocalizedTextColor); // INFO
            builder.AddButton(20, 103, 4005, 4007, 0, GumpButtonType.Page, 1);

            builder.AddHtmlLocalized(170, 103, 75, 20, 1011234, CustomGumpStyle.LocalizedTextColor); // FRIENDS
            builder.AddButton(135, 103, 4005, 4007, 0, GumpButtonType.Page, 2);

            builder.AddHtmlLocalized(295, 103, 75, 20, 1011235, CustomGumpStyle.LocalizedTextColor); // OPTIONS
            builder.AddButton(260, 103, 4005, 4007, 0, GumpButtonType.Page, 3);

            builder.AddHtmlLocalized(295, 390, 75, 20, 1011441, CustomGumpStyle.LocalizedTextColor); // EXIT
            builder.AddButton(260, 390, 4005, 4007, 0);

            builder.AddHtmlLocalized(55, 390, 200, 20, 1011236, CustomGumpStyle.LocalizedTextColor); // Change this house's name!
            builder.AddButton(20, 390, 4005, 4007, 1);

            // Info page
            builder.AddPage(1);

            builder.AddHtmlLocalized(20, 135, 100, 20, 1011242, CustomGumpStyle.LocalizedTextColor); // Owned by:
            builder.AddHtml(120, 135, 100, 20, GetOwnerName(), CustomGumpStyle.TextColor);

            builder.AddHtml(20, 170, 240, 20, "Locked down items:", CustomGumpStyle.TextColor);
            builder.AddHtml(270, 170, 130, 20, $"{_house.LockDownCount} / {_house.MaxLockDowns}", CustomGumpStyle.TextColor);

            builder.AddHtml(20, 190, 240, 20, "Secured Containers:", CustomGumpStyle.TextColor);
            builder.AddHtml(270, 190, 130, 20, $"{_house.SecureCount} / {_house.MaxSecures}", CustomGumpStyle.TextColor);

            builder.AddHtmlLocalized(20, 260, 400, 20, 1018032, CustomGumpStyle.LocalizedTextColor); // This house is properly placed.
            builder.AddHtmlLocalized(20, 280, 400, 20, 1018035, CustomGumpStyle.LocalizedTextColor); // This house is of modern design.

            if (_house.Public)
            {
                // TODO: Validate exact placement
                builder.AddHtmlLocalized(20, 305, 275, 20, 1011241, CustomGumpStyle.LocalizedTextColor); // Number of visits this building has had
                builder.AddHtml(320, 305, 50, 20, $"{_house.Visits}", CustomGumpStyle.TextColor);
            }

            // Friends page
            builder.AddPage(2);

            builder.AddHtmlLocalized(45, 130, 150, 20, 1011266, CustomGumpStyle.LocalizedTextColor); // List of co-owners
            builder.AddButton(20, 130, 2714, 2715, 2);

            builder.AddHtmlLocalized(45, 150, 150, 20, 1011267, CustomGumpStyle.LocalizedTextColor); // Add a co-owner
            builder.AddButton(20, 150, 2714, 2715, 3);

            builder.AddHtmlLocalized(45, 170, 150, 20, 1018036, CustomGumpStyle.LocalizedTextColor); // Remove a co-owner
            builder.AddButton(20, 170, 2714, 2715, 4);

            builder.AddHtmlLocalized(45, 190, 150, 20, 1011268, CustomGumpStyle.LocalizedTextColor); // Clear co-owner list
            builder.AddButton(20, 190, 2714, 2715, 5);

            builder.AddHtmlLocalized(225, 130, 155, 20, 1011243, CustomGumpStyle.LocalizedTextColor); // List of Friends
            builder.AddButton(200, 130, 2714, 2715, 6);

            builder.AddHtmlLocalized(225, 150, 155, 20, 1011244, CustomGumpStyle.LocalizedTextColor); // Add a Friend
            builder.AddButton(200, 150, 2714, 2715, 7);

            builder.AddHtmlLocalized(225, 170, 155, 20, 1018037, CustomGumpStyle.LocalizedTextColor); // Remove a Friend
            builder.AddButton(200, 170, 2714, 2715, 8);

            builder.AddHtmlLocalized(225, 190, 155, 20, 1011245, CustomGumpStyle.LocalizedTextColor); // Clear Friends list
            builder.AddButton(200, 190, 2714, 2715, 9);

            builder.AddHtmlLocalized(120, 215, 280, 20, 1011258, CustomGumpStyle.LocalizedTextColor); // Ban someone from the house
            builder.AddButton(95, 215, 2714, 2715, 10);

            builder.AddHtmlLocalized(120, 235, 280, 20, 1011259, CustomGumpStyle.LocalizedTextColor); // Eject someone from the house
            builder.AddButton(95, 235, 2714, 2715, 11);

            builder.AddHtmlLocalized(120, 255, 280, 20, 1011260, CustomGumpStyle.LocalizedTextColor); // View a list of banned people
            builder.AddButton(95, 255, 2714, 2715, 12);

            builder.AddHtmlLocalized(120, 275, 280, 20, 1011261, CustomGumpStyle.LocalizedTextColor); // Lift a ban
            builder.AddButton(95, 275, 2714, 2715, 13);

            // Options page
            builder.AddPage(3);

            builder.AddHtmlLocalized(45, 150, 355, 30, 1011248, CustomGumpStyle.LocalizedTextColor); // Transfer ownership of the house
            builder.AddButton(20, 150, 2714, 2715, 14);

            builder.AddHtmlLocalized(45, 180, 355, 30, 1011249, CustomGumpStyle.LocalizedTextColor); // Demolish house and get deed back
            builder.AddButton(20, 180, 2714, 2715, 15);

            if (!_house.Public)
            {
                builder.AddHtmlLocalized(45, 210, 355, 30, 1011247, CustomGumpStyle.LocalizedTextColor); // Change the house locks
                builder.AddButton(20, 210, 2714, 2715, 16);

                builder.AddHtmlLocalized(
                    45,
                    240,
                    350,
                    90,
                    1011253,
                    CustomGumpStyle.LocalizedTextColor
                ); // Declare this building to be public. This will make your front door unlockable.
                builder.AddButton(20, 240, 2714, 2715, 17);
            }
            else
            {
                // AddHtmlLocalized( 45, 280, 350, 30, 1011250, false, false ); // Change the sign type
                builder.AddHtmlLocalized(45, 210, 350, 30, 1011250, CustomGumpStyle.LocalizedTextColor); // Change the sign type
                builder.AddButton(20, 210, 2714, 2715, 0, GumpButtonType.Page, 4);

                builder.AddHtmlLocalized(45, 240, 350, 30, 1011252, CustomGumpStyle.LocalizedTextColor); // Declare this building to be private.
                builder.AddButton(20, 240, 2714, 2715, 17);

                // Change the sign type
                builder.AddPage(4);

                for (var i = 0; i < 24; ++i)
                {
                    builder.AddRadio(53 + i / 4 * 50, 137 + i % 4 * 35, 210, 211, false, i + 1);
                    builder.AddItem(60 + i / 4 * 50, 130 + i % 4 * 35, 2980 + i * 2);
                }

                builder.AddHtmlLocalized(200, 305, 129, 20, 1011254, CustomGumpStyle.LocalizedTextColor); // Guild sign choices
                builder.AddButton(350, 305, 252, 253, 0, GumpButtonType.Page, 5);

                builder.AddHtmlLocalized(200, 340, 355, 30, 1011277, CustomGumpStyle.LocalizedTextColor); // Okay that is fine.
                builder.AddButton(350, 340, 4005, 4007, 18);

                builder.AddPage(5);

                for (var i = 0; i < 29; ++i)
                {
                    builder.AddRadio(53 + i / 5 * 50, 137 + i % 5 * 35, 210, 211, false, i + 25);
                    builder.AddItem(60 + i / 5 * 50, 130 + i % 5 * 35, 3028 + i * 2);
                }

                builder.AddHtmlLocalized(200, 305, 129, 20, 1011255, CustomGumpStyle.LocalizedTextColor); // Shop sign choices
                builder.AddButton(350, 305, 250, 251, 0, GumpButtonType.Page, 4);

                builder.AddHtmlLocalized(200, 340, 355, 30, 1011277, CustomGumpStyle.LocalizedTextColor); // Okay that is fine.
                builder.AddButton(350, 340, 4005, 4007, 18);
            }
        }

        private string GetOwnerName()
        {
            var m = _house.Owner;

            if (m == null)
            {
                return "(unowned)";
            }

            string name;

            if ((name = m.Name) == null || (name = name.Trim()).Length <= 0)
            {
                name = "(no name)";
            }

            return name;
        }

        public override void OnResponse(NetState sender, in RelayInfo info)
        {
            if (_house.Deleted)
            {
                return;
            }

            var from = sender.Mobile;

            var isCombatRestricted = _house.IsCombatRestricted(from);

            var isOwner = _house.IsOwner(from);
            var isCoOwner = isOwner || _house.IsCoOwner(from);
            var isFriend = isCoOwner || _house.IsFriend(from);

            if (isCombatRestricted)
            {
                isFriend = isCoOwner = isOwner = false;
            }

            if (!isFriend || !from.Alive)
            {
                return;
            }

            Item sign = _house.Sign;

            if (sign == null || from.Map != sign.Map || !from.InRange(sign.GetWorldLocation(), 18))
            {
                return;
            }

            var gumps = from.GetGumps();

            switch (info.ButtonID)
            {
                case 1: // Rename sign
                    {
                        from.Prompt = new RenamePrompt(_house);
                        from.SendLocalizedMessage(501302); // What dost thou wish the sign to say?

                        break;
                    }
                case 2: // List of co-owners
                    {
                        gumps.Close<HouseGump>();
                        gumps.Close<HouseRemoveGump>();
                        gumps.Close<HouseListGump>();
                        gumps.Send(new HouseListGump(1011275, _house.CoOwners, _house, false));

                        break;
                    }
                case 3: // Add co-owner
                    {
                        if (isOwner)
                        {
                            from.SendLocalizedMessage(
                                501328
                            ); // Target the person you wish to name a co-owner of your household.
                            from.Target = new CoOwnerTarget(true, _house);
                        }
                        else
                        {
                            from.SendLocalizedMessage(501327); // Only the house owner may add Co-owners.
                        }

                        break;
                    }
                case 4: // Remove co-owner
                    {
                        if (isOwner)
                        {
                            gumps.Close<HouseGump>();
                            gumps.Close<HouseListGump>();
                            gumps.Close<HouseRemoveGump>();
                            gumps.Send(new HouseRemoveGump(1011274, _house.CoOwners, _house, false));
                        }
                        else
                        {
                            from.SendLocalizedMessage(501329); // Only the house owner may remove co-owners.
                        }

                        break;
                    }
                case 5: // Clear co-owners
                    {
                        if (isOwner)
                        {
                            _house.CoOwners?.Clear();

                            from.SendLocalizedMessage(501333); // All co-owners have been removed from this house.
                        }
                        else
                        {
                            from.SendLocalizedMessage(501330); // Only the house owner may remove co-owners.
                        }

                        break;
                    }
                case 6: // List friends
                    {
                        gumps.Close<HouseGump>();
                        gumps.Close<HouseRemoveGump>();
                        gumps.Close<HouseListGump>();
                        gumps.Send(new HouseListGump(1011273, _house.Friends, _house, false));

                        break;
                    }
                case 7: // Add friend
                    {
                        if (isCoOwner)
                        {
                            from.SendLocalizedMessage(
                                501317
                            ); // Target the person you wish to name a friend of your household.
                            from.Target = new HouseFriendTarget(true, _house);
                        }
                        else
                        {
                            from.SendLocalizedMessage(501316); // Only the house owner may add friends.
                        }

                        break;
                    }
                case 8: // Remove friend
                    {
                        if (isCoOwner)
                        {
                            gumps.Close<HouseGump>();
                            gumps.Close<HouseListGump>();
                            gumps.Close<HouseRemoveGump>();
                            gumps.Send(new HouseRemoveGump(1011272, _house.Friends, _house, false));
                        }
                        else
                        {
                            from.SendLocalizedMessage(501318); // Only the house owner may remove friends.
                        }

                        break;
                    }
                case 9: // Clear friends
                    {
                        if (isCoOwner)
                        {
                            _house.Friends?.Clear();

                            from.SendLocalizedMessage(501332); // All friends have been removed from this house.
                        }
                        else
                        {
                            from.SendLocalizedMessage(501319); // Only the house owner may remove friends.
                        }

                        break;
                    }
                case 10: // Ban
                    {
                        from.SendLocalizedMessage(501325); // Target the individual to ban from this house.
                        from.Target = new HouseBanTarget(true, _house);

                        break;
                    }
                case 11: // Eject
                    {
                        from.SendLocalizedMessage(501326); // Target the individual to eject from this house.
                        from.Target = new HouseKickTarget(_house);

                        break;
                    }
                case 12: // List bans
                    {
                        gumps.Close<HouseGump>();
                        gumps.Close<HouseRemoveGump>();
                        gumps.Close<HouseListGump>();
                        gumps.Send(new HouseListGump(1011271, _house.Bans, _house, true));

                        break;
                    }
                case 13: // Remove ban
                    {
                        gumps.Close<HouseGump>();
                        gumps.Close<HouseListGump>();
                        gumps.Close<HouseRemoveGump>();
                        gumps.Send(new HouseRemoveGump(1011269, _house.Bans, _house, true));

                        break;
                    }
                case 14: // Transfer ownership
                    {
                        if (isOwner)
                        {
                            from.SendLocalizedMessage(501309); // Target the person to whom you wish to give this house.
                            from.Target = new HouseOwnerTarget(_house);
                        }
                        else
                        {
                            from.SendLocalizedMessage(501310); // Only the house owner may do this.
                        }

                        break;
                    }
                case 15: // Demolish house
                    {
                        if (isOwner)
                        {
                            if (!Guild.NewGuildSystem && _house.FindGuildstone() != null)
                            {
                                from.SendLocalizedMessage(501389); // You cannot redeed a house with a guildstone inside.
                            }
                            else
                            {
                                gumps.Send(new ConfirmDemolishHouseGump(_house));
                            }
                        }
                        else
                        {
                            from.SendLocalizedMessage(501320); // Only the house owner may do this.
                        }

                        break;
                    }
                case 16: // Change locks
                    {
                        if (_house.Public)
                        {
                            from.SendLocalizedMessage(501669); // Public houses are always unlocked.
                        }
                        else
                        {
                            if (isOwner)
                            {
                                _house.RemoveKeys(from);
                                _house.ChangeLocks(from);

                                from.SendLocalizedMessage(
                                    501306
                                ); // The locks on your front door have been changed, and new master keys have been placed in your bank and your backpack.
                            }
                            else
                            {
                                from.SendLocalizedMessage(501303); // Only the house owner may change the house locks.
                            }
                        }

                        break;
                    }
                case 17: // Declare public/private
                    {
                        if (isOwner)
                        {
                            if (_house.Public && _house.PlayerVendors.Count > 0)
                            {
                                from.SendLocalizedMessage(
                                    501887
                                ); // You have vendors working out of this building. It cannot be declared private until there are no vendors in place.
                                break;
                            }

                            _house.Public = !_house.Public;
                            if (!_house.Public)
                            {
                                _house.ChangeLocks(from);

                                from.SendLocalizedMessage(501888); // This house is now private.
                                from.SendLocalizedMessage(
                                    501306
                                ); // The locks on your front door have been changed, and new master keys have been placed in your bank and your backpack.
                            }
                            else
                            {
                                _house.RemoveKeys(from);
                                _house.RemoveLocks();
                                from.SendLocalizedMessage(
                                    501886
                                ); // This house is now public. Friends of the house my now have vendors working out of this building.
                            }
                        }
                        else
                        {
                            from.SendLocalizedMessage(501307); // Only the house owner may do this.
                        }

                        break;
                    }
                case 18: // Change type
                    {
                        if (isOwner)
                        {
                            if (_house.Public && info.Switches.Length > 0)
                            {
                                var index = info.Switches[0] - 1;

                                if (index >= 0 && index < 53)
                                {
                                    _house.ChangeSignType(2980 + index * 2);
                                }
                            }
                        }
                        else
                        {
                            from.SendLocalizedMessage(501307); // Only the house owner may do this.
                        }

                        break;
                    }
            }
        }
    }
}

namespace Server.Prompts
{
    public class RenamePrompt : Prompt
    {
        private readonly BaseHouse m_House;

        public RenamePrompt(BaseHouse house) => m_House = house;

        public override void OnResponse(Mobile from, string text)
        {
            if (m_House.IsFriend(from))
            {
                if (m_House.Sign != null)
                {
                    m_House.Sign.Name = text;
                }

                from.SendMessage("Sign changed.");
            }
        }
    }
}
