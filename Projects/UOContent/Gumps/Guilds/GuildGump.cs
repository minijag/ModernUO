using System;
using Server.Guilds;
using Server.Network;

namespace Server.Gumps
{
    public class GuildGump : DynamicGump
    {
        private readonly Guild _guild;
        private readonly Mobile _mobile;

        public override bool Singleton => true;

        private GuildGump(Mobile beholder, Guild guild) : base(40, 50)
        {
            _mobile = beholder;
            _guild = guild;
        }

        public static void DisplayTo(Mobile from, Guild guild)
        {
            if (from?.NetState == null || guild == null)
            {
                return;
            }

            EnsureClosed(from);
            from.SendGump(new GuildGump(from, guild));
        }

        protected override void BuildLayout(ref DynamicGumpBuilder builder)
        {
            builder.AddPage();
            CustomGumpStyle.AddContentPanel(ref builder, 0, 0, 550, 430, "GUILD");

            builder.AddHtml(20, 15, 200, 35, _guild.Name, CustomGumpStyle.WhiteTextColor);

            var leader = _guild.Leader;

            if (leader != null)
            {
                var leadTitle = leader.GuildTitle != null ? leader.GuildTitle.AsSpan().Trim() : ReadOnlySpan<char>.Empty;
                var leadName = leader.Name;
                var leadNameSpan = leadName != null ? leadName.AsSpan().Trim() : ReadOnlySpan<char>.Empty;
                var text = leadTitle.Length > 0 ? $"{leadTitle}: {leadNameSpan}" : leadNameSpan;

                builder.AddHtml(220, 15, 250, 35, text, CustomGumpStyle.WhiteTextColor);
            }

            builder.AddButton(20, 50, 4005, 4007, 1);
            builder.AddHtmlLocalized(55, 50, 100, 20, 1013022, CustomGumpStyle.LocalizedTextColor); // Loyal to

            var fealty = _mobile.GuildFealty;

            if (fealty == null || !_guild.IsMember(fealty))
            {
                fealty = leader;
            }

            fealty ??= _mobile;

            if (_mobile == fealty)
            {
                builder.AddHtmlLocalized(55, 70, 470, 20, 1018002, CustomGumpStyle.LocalizedTextColor); // yourself
            }
            else
            {
                var fealtyName = fealty.Name;
                builder.AddHtml(55, 70, 470, 20, fealtyName != null ? fealtyName.AsSpan().Trim() : "(empty)", CustomGumpStyle.WhiteTextColor);
            }

            builder.AddButton(215, 50, 4005, 4007, 2);
            builder.AddHtmlLocalized(250, 50, 170, 20, 1013023, CustomGumpStyle.LocalizedTextColor);                                       // Display guild abbreviation
            builder.AddHtmlLocalized(250, 70, 50, 20, _mobile.DisplayGuildTitle ? 1011262 : 1011263, CustomGumpStyle.LocalizedTextColor); // on/off

            builder.AddButton(20, 100, 4005, 4007, 3);
            builder.AddHtmlLocalized(55, 100, 470, 30, 1011086, CustomGumpStyle.LocalizedTextColor); // View the current roster.

            builder.AddButton(20, 130, 4005, 4007, 4);
            builder.AddHtmlLocalized(55, 130, 470, 30, 1011085, CustomGumpStyle.LocalizedTextColor); // Recruit someone into the guild.

            if (_guild.Candidates.Count > 0)
            {
                builder.AddButton(20, 160, 4005, 4007, 5);
                builder.AddHtmlLocalized(55, 160, 470, 30, 1011093, CustomGumpStyle.LocalizedTextColor); // View list of candidates who have been sponsored to the guild.
            }
            else
            {
                builder.AddImage(20, 160, 4020);
                builder.AddHtmlLocalized(55, 160, 470, 30, 1013031, CustomGumpStyle.LocalizedTextColor); // There are currently no candidates for membership.
            }

            builder.AddButton(20, 220, 4005, 4007, 6);
            builder.AddHtmlLocalized(55, 220, 470, 30, 1011087, CustomGumpStyle.LocalizedTextColor); // View the guild's charter.

            builder.AddButton(20, 250, 4005, 4007, 7);
            builder.AddHtmlLocalized(55, 250, 470, 30, 1011092, CustomGumpStyle.LocalizedTextColor); // Resign from the guild.

            builder.AddButton(20, 280, 4005, 4007, 8);
            builder.AddHtmlLocalized(55, 280, 470, 30, 1011095, CustomGumpStyle.LocalizedTextColor); // View list of guilds you are at war with.

            if (_mobile.AccessLevel >= AccessLevel.GameMaster || _mobile == leader)
            {
                builder.AddButton(20, 310, 4005, 4007, 9);
                builder.AddHtmlLocalized(55, 310, 470, 30, 1011094, CustomGumpStyle.LocalizedTextColor); // Access guildmaster functions.
            }
            else
            {
                builder.AddImage(20, 310, 4020);
                builder.AddHtmlLocalized(55, 310, 470, 30, 1018013, CustomGumpStyle.LocalizedTextColor); // Reserved for guildmaster
            }

            builder.AddButton(20, 345, 4005, 4007, 10);
            builder.AddHtml(55, 345, 470, 30, "Guild progress", CustomGumpStyle.WhiteTextColor, CustomGumpStyle.StandardFontSize);

            builder.AddButton(20, 390, 4005, 4007, 0);
            builder.AddHtmlLocalized(55, 390, 470, 30, 1011441, CustomGumpStyle.LocalizedTextColor); // EXIT
        }

        public static void EnsureClosed(Mobile m)
        {
            var gumps = m.GetGumps();

            gumps.Close<DeclareFealtyGump>();
            gumps.Close<GrantGuildTitleGump>();
            gumps.Close<GuildAdminCandidatesGump>();
            gumps.Close<GuildCandidatesGump>();
            gumps.Close<GuildChangeTypeGump>();
            gumps.Close<GuildCharterGump>();
            gumps.Close<GuildDismissGump>();
            gumps.Close<GuildGump>();
            gumps.Close<GuildProgressGump>();
            gumps.Close<GuildmasterGump>();
            gumps.Close<GuildRosterGump>();
            gumps.Close<GuildWarGump>();
        }

        public static bool BadLeader(Mobile m, Guild g)
        {
            if (m.Deleted || g.Disbanded || m.AccessLevel < AccessLevel.GameMaster && g.Leader != m)
            {
                return true;
            }

            var stone = g.Guildstone;

            return stone?.Deleted != false;
        }

        public static bool BadMember(Mobile m, Guild g)
        {
            if (m.Deleted || g.Disbanded || m.AccessLevel < AccessLevel.GameMaster && !g.IsMember(m))
            {
                return true;
            }

            var stone = g.Guildstone;

            return stone?.Deleted != false;
        }

        public override void OnResponse(NetState sender, in RelayInfo info)
        {
            if (sender?.Mobile != _mobile || _mobile.NetState != sender || BadMember(_mobile, _guild))
            {
                return;
            }

            switch (info.ButtonID)
            {
                case 1: // Loyalty
                    {
                        DeclareFealtyGump.DisplayTo(_mobile, _guild);
                        break;
                    }
                case 2: // Toggle display abbreviation
                    {
                        _mobile.DisplayGuildTitle = !_mobile.DisplayGuildTitle;
                        DisplayTo(_mobile, _guild);
                        break;
                    }
                case 3: // View the current roster
                    {
                        GuildRosterGump.DisplayTo(_mobile, _guild);
                        break;
                    }
                case 4: // Recruit
                    {
                        _mobile.Target = new GuildRecruitTarget(_mobile, _guild);
                        break;
                    }
                case 5: // Membership candidates
                    {
                        GuildCandidatesGump.DisplayTo(_mobile, _guild);
                        break;
                    }
                case 6: // View charter
                    {
                        GuildCharterGump.DisplayTo(_mobile, _guild);
                        break;
                    }
                case 7: // Resign
                    {
                        _guild.RemoveMember(_mobile);
                        break;
                    }
                case 8: // View wars
                    {
                        GuildWarGump.DisplayTo(_mobile, _guild);
                        break;
                    }
                case 9: // Guildmaster functions
                    {
                        if (_mobile.AccessLevel >= AccessLevel.GameMaster || _guild.Leader == _mobile)
                        {
                            GuildmasterGump.DisplayTo(_mobile, _guild);
                        }

                        break;
                    }
                case 10:
                    {
                        GuildProgressGump.DisplayTo(_mobile, _guild);
                        break;
                    }
            }
        }
    }
}
