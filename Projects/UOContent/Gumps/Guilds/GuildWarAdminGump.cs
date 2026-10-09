using Server.Guilds;
using Server.Network;

namespace Server.Gumps
{
    public class GuildWarAdminGump : DynamicGump
    {
        private readonly Guild _guild;

        public override bool Singleton => true;

        private GuildWarAdminGump(Guild guild) : base(40, 50) => _guild = guild;

        public static void DisplayTo(Mobile from, Guild guild)
        {
            if (from?.NetState == null || guild == null)
            {
                return;
            }

            GuildGump.EnsureClosed(from);
            from.SendGump(new GuildWarAdminGump(guild));
        }

        protected override void BuildLayout(ref DynamicGumpBuilder builder)
        {
            builder.AddPage();
            CustomGumpStyle.AddContentPanel(ref builder, 0, 0, 550, 440, "WAR MANAGEMENT");

            builder.AddHtmlLocalized(20, 10, 510, 35, 1011105, CustomGumpStyle.LocalizedTextColor); // <center>WAR FUNCTIONS</center>

            builder.AddButton(20, 40, 4005, 4007, 1);
            builder.AddHtmlLocalized(55, 40, 400, 30, 1011099, CustomGumpStyle.LocalizedTextColor); // Declare war through guild name search.

            if (_guild.Enemies.Count > 0)
            {
                builder.AddButton(20, 160 + 30, 4005, 4007, 2);
                builder.AddHtmlLocalized(55, 160, 400, 30, 1011103, CustomGumpStyle.LocalizedTextColor); // Declare peace.
            }
            else
            {
                builder.AddHtmlLocalized(20, 160, 400, 30, 1013033, CustomGumpStyle.LocalizedTextColor); // No current wars
            }

            if (_guild.WarInvitations.Count > 0)
            {
                builder.AddButton(20, 190, 4005, 4007, 3);
                builder.AddHtmlLocalized(55, 190, 400, 30, 1011100, CustomGumpStyle.LocalizedTextColor); // Accept war invitations.

                builder.AddButton(20, 190, 4005, 4007, 4);
                builder.AddHtmlLocalized(55, 190, 400, 30, 1011101, CustomGumpStyle.LocalizedTextColor); // Reject war invitations.
            }
            else
            {
                builder.AddHtmlLocalized(20, 190, 400, 30, 1018012, CustomGumpStyle.LocalizedTextColor); // No current invitations received for war.
            }

            if (_guild.WarDeclarations.Count > 0)
            {
                builder.AddButton(20, 220, 4005, 4007, 5);
                builder.AddHtmlLocalized(55, 220, 400, 30, 1011102, CustomGumpStyle.LocalizedTextColor); // Rescind your war declarations.
            }
            else
            {
                builder.AddHtmlLocalized(20, 220, 400, 30, 1013055, CustomGumpStyle.LocalizedTextColor); // No current war declarations
            }

            builder.AddButton(20, 400, 4005, 4007, 6);
            builder.AddHtmlLocalized(55, 400, 400, 35, 1011104, CustomGumpStyle.LocalizedTextColor); // Return to the previous menu.
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
                case 1: // Declare war
                    {
                        from.SendLocalizedMessage(1018001); // Declare war through search - Enter Guild Name:
                        from.Prompt = new GuildDeclareWarPrompt(_guild);
                        break;
                    }
                case 2: // Declare peace
                    {
                        GuildDeclarePeaceGump.DisplayTo(from, _guild);
                        break;
                    }
                case 3: // Accept war
                    {
                        GuildAcceptWarGump.DisplayTo(from, _guild);
                        break;
                    }
                case 4: // Reject war
                    {
                        GuildRejectWarGump.DisplayTo(from, _guild);
                        break;
                    }
                case 5: // Rescind declarations
                    {
                        GuildRescindDeclarationGump.DisplayTo(from, _guild);
                        break;
                    }
                case 6: // Return
                    {
                        GuildmasterGump.DisplayTo(from, _guild);
                        break;
                    }
            }
        }
    }
}
