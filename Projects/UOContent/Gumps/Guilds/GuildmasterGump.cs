using Server.Guilds;
using Server.Items;
using Server.Network;

namespace Server.Gumps
{
    public class GuildmasterGump : DynamicGump
    {
        private readonly Guild _guild;

        public override bool Singleton => true;

        private GuildmasterGump(Guild guild) : base(40, 50) => _guild = guild;

        public static void DisplayTo(Mobile from, Guild guild)
        {
            if (from?.NetState == null || guild == null)
            {
                return;
            }

            GuildGump.EnsureClosed(from);
            from.SendGump(new GuildmasterGump(guild));
        }

        protected override void BuildLayout(ref DynamicGumpBuilder builder)
        {
            builder.AddPage();
            CustomGumpStyle.AddContentPanel(ref builder, 0, 0, 550, 400, "GUILD MANAGEMENT");

            builder.AddHtmlLocalized(20, 15, 510, 35, 1011121, CustomGumpStyle.LocalizedTextColor); // <center>GUILDMASTER FUNCTIONS</center>

            builder.AddButton(20, 40, 4005, 4007, 2);
            builder.AddHtmlLocalized(55, 40, 470, 30, 1011107, CustomGumpStyle.LocalizedTextColor); // Set the guild name.

            builder.AddButton(20, 70, 4005, 4007, 3);
            builder.AddHtmlLocalized(55, 70, 470, 30, 1011109, CustomGumpStyle.LocalizedTextColor); // Set the guild's abbreviation.

            if (Guild.OrderChaos)
            {
                builder.AddButton(20, 100, 4005, 4007, 4);

                switch (_guild.Type)
                {
                    case GuildType.Regular:
                        {
                            builder.AddHtmlLocalized(55, 100, 470, 30, 1013059, CustomGumpStyle.LocalizedTextColor); // Change guild type: Currently Standard
                            break;
                        }
                    case GuildType.Order:
                        {
                            builder.AddHtmlLocalized(55, 100, 470, 30, 1013057, CustomGumpStyle.LocalizedTextColor); // Change guild type: Currently Order
                            break;
                        }
                    case GuildType.Chaos:
                        {
                            builder.AddHtmlLocalized(55, 100, 470, 30, 1013058, CustomGumpStyle.LocalizedTextColor); // Change guild type: Currently Chaos
                            break;
                        }
                }
            }

            builder.AddButton(20, 130, 4005, 4007, 5);
            builder.AddHtmlLocalized(55, 130, 470, 30, 1011112, CustomGumpStyle.LocalizedTextColor); // Set the guild's charter.

            builder.AddButton(20, 160, 4005, 4007, 6);
            builder.AddHtmlLocalized(55, 160, 470, 30, 1011113, CustomGumpStyle.LocalizedTextColor); // Dismiss a member.

            builder.AddButton(20, 190, 4005, 4007, 7);
            builder.AddHtmlLocalized(55, 190, 470, 30, 1011114, CustomGumpStyle.LocalizedTextColor); // Go to the WAR menu.

            if (_guild.Candidates.Count > 0)
            {
                builder.AddButton(20, 220, 4005, 4007, 8);
                builder.AddHtmlLocalized(55, 220, 470, 30, 1013056, CustomGumpStyle.LocalizedTextColor); // Administer the list of candidates
            }
            else
            {
                builder.AddImage(20, 220, 4020);
                builder.AddHtmlLocalized(55, 220, 470, 30, 1013031, CustomGumpStyle.LocalizedTextColor); // There are currently no candidates for membership.
            }

            builder.AddButton(20, 250, 4005, 4007, 9);
            builder.AddHtmlLocalized(55, 250, 470, 30, 1011117, CustomGumpStyle.LocalizedTextColor); // Set the guildmaster's title.

            builder.AddButton(20, 280, 4005, 4007, 10);
            builder.AddHtmlLocalized(55, 280, 470, 30, 1011118, CustomGumpStyle.LocalizedTextColor); // Grant a title to another member.

            builder.AddButton(20, 310, 4005, 4007, 11);
            builder.AddHtmlLocalized(55, 310, 470, 30, 1011119, CustomGumpStyle.LocalizedTextColor); // Move this guildstone.

            builder.AddButton(20, 360, 4005, 4007, 1);
            builder.AddHtmlLocalized(55, 360, 245, 30, 1011120, CustomGumpStyle.LocalizedTextColor); // Return to the main menu.

            builder.AddButton(300, 360, 4005, 4007, 0);
            builder.AddHtmlLocalized(335, 360, 100, 30, 1011441, CustomGumpStyle.LocalizedTextColor); // EXIT
        }

        public override void OnResponse(NetState state, in RelayInfo info)
        {
            var from = state.Mobile;
            if (GuildGump.BadLeader(from, _guild))
            {
                return;
            }

            switch (info.ButtonID)
            {
                case 1: // Main menu
                    {
                        GuildGump.DisplayTo(from, _guild);
                        break;
                    }
                case 2: // Set guild name
                    {
                        from.SendLocalizedMessage(1013060); // Enter new guild name (40 characters max):
                        from.Prompt = new GuildNamePrompt(from, _guild);
                        break;
                    }
                case 3: // Set guild abbreviation
                    {
                        from.SendLocalizedMessage(1013061); // Enter new guild abbreviation (3 characters max):
                        from.Prompt = new GuildAbbrvPrompt(_guild);
                        break;
                    }
                case 4: // Change guild type
                    {
                        if (!Guild.OrderChaos)
                        {
                            return;
                        }

                        GuildChangeTypeGump.DisplayTo(from, _guild);
                        break;
                    }
                case 5: // Set charter
                    {
                        from.SendLocalizedMessage(1013071); // Enter the new guild charter (50 characters max):
                        from.Prompt = new GuildCharterPrompt(_guild);
                        break;
                    }
                case 6: // Dismiss member
                    {
                        GuildDismissGump.DisplayTo(from, _guild);
                        break;
                    }
                case 7: // War menu
                    {
                        GuildWarAdminGump.DisplayTo(from, _guild);
                        break;
                    }
                case 8: // Administer candidates
                    {
                        GuildAdminCandidatesGump.DisplayTo(from, _guild);
                        break;
                    }
                case 9: // Set guildmaster's title
                    {
                        from.SendLocalizedMessage(1013073); // Enter new guildmaster title (20 characters max):
                        from.Prompt = new GuildTitlePrompt(from, _guild);
                        break;
                    }
                case 10: // Grant title
                    {
                        GrantGuildTitleGump.DisplayTo(from, _guild);
                        break;
                    }
                case 11: // Move guildstone
                    {
                        if (_guild.Guildstone != null)
                        {
                            var item = new GuildTeleporter(_guild.Guildstone);

                            _guild.Teleporter?.Delete();

                            // Use the teleporting object placed in your backpack to move this guildstone.
                            from.SendLocalizedMessage(501133);

                            from.AddToBackpack(item);
                            _guild.Teleporter = item;
                        }

                        DisplayTo(from, _guild);
                        break;
                    }
            }
        }
    }
}
