using Server.Accounting;
using Server.Commands;
using Server.Commands.Generic;
using Server.Mobiles;
using Server.Network;
using Server.Targets;

namespace Server.Gumps
{
    public class ClientGump : Gump
    {
        private const int LabelColor32 = 0xFFFFFF;
        private readonly NetState m_State;

        public ClientGump(Mobile from, NetState state, string initialText = "") : base(30, 20)
        {
            if (state == null)
            {
                return;
            }

            m_State = state;

            AddPage(0);

            AddBackground(0, 0, 420, 314, 5054);

            AddImageTiled(10, 10, 400, 19, 0xA40);
            AddAlphaRegion(10, 10, 400, 19);

            AddImageTiled(10, 32, 400, 272, 0xA40);
            AddAlphaRegion(10, 32, 400, 272);

            AddHtml(10, 10, 380, 20, "User Information".Center(LabelColor32));

            var line = 0;

            AddHtml(14, 36 + line * 20, 200, 20, "Address:".Color(LabelColor32));
            AddHtml(70, 36 + line++ * 20, 200, 20, state.ToString().Color(LabelColor32));

            AddHtml(14, 36 + line * 20, 200, 20, "Client:".Color(LabelColor32));
            AddHtml(
                72,
                36 + line++ * 20,
                200,
                20,
                (state.Version?.ToString().DefaultIfNullOrEmpty("(null)")).Color(LabelColor32)
            );

            AddHtml(14, 36 + line * 20, 200, 20, "Assistant:".Color(LabelColor32));
            AddHtml(
                72,
                36 + line++ * 20,
                200,
                20,
                (state.Assistant ?? "(-none-)").Color(LabelColor32)
            );

            AddHtml(14, 36 + line * 20, 200, 20, "Version:".Color(LabelColor32));

            var info = state.ExpansionInfo;
            var expansionName = info.Name;

            AddHtml(72, 36 + line++ * 20, 200, 20, expansionName.Color(LabelColor32));

            var a = state.Account as Account;
            var m = state.Mobile;

            if (from.AccessLevel >= AccessLevel.GameMaster && a != null)
            {
                AddHtml(14, 36 + line * 20, 200, 20, "Account:".Color(LabelColor32));
                AddHtml(72, 36 + line++ * 20, 200, 20, a.Username.Color(LabelColor32));
            }

            if (m != null)
            {
                AddHtml(14, 36 + line * 20, 200, 20, "Mobile:".Color(LabelColor32));
                AddHtml(72, 36 + line++ * 20, 200, 20, $"{m.Name} ({m.Serial})".Color(LabelColor32));

                AddHtml(14, 36 + line * 20, 200, 20, "Location:".Color(LabelColor32));
                AddHtml(72, 36 + line++ * 20, 200, 20, $"{m.Location} [{m.Map}]".Color(LabelColor32));

                AddButton(13, 197, 0xFAB, 0xFAD, 1);
                AddHtml(48, 198, 200, 20, "Send Message".Color(LabelColor32));

                AddImageTiled(12, 222, 376, 80, 0xA40);
                AddImageTiled(13, 223, 374, 78, 0xBBC);
                AddTextEntry(15, 223, 372, 78, 0x480, 0, initialText);

                AddImageTiled(265, 35, 142, 144, 5058);

                AddImageTiled(266, 36, 140, 142, 0xA40);
                AddAlphaRegion(266, 36, 140, 142);

                line = 0;

                if (BaseCommand.IsAccessible(from, m))
                {
                    AddButton(266, 36 + line * 20, 0xFA5, 0xFA7, 4);
                    AddHtml(300, 38 + line++ * 20, 100, 20, "Properties".Color(LabelColor32));
                }

                if (from != m)
                {
                    AddButton(266, 36 + line * 20, 0xFA5, 0xFA7, 5);
                    AddHtml(300, 38 + line++ * 20, 100, 20, "Go to them".Color(LabelColor32));

                    AddButton(266, 36 + line * 20, 0xFA5, 0xFA7, 6);
                    AddHtml(300, 38 + line++ * 20, 100, 20, "Bring them here".Color(LabelColor32));
                }

                AddButton(266, 36 + line * 20, 0xFA5, 0xFA7, 7);
                AddHtml(300, 38 + line++ * 20, 100, 20, "Move to target".Color(LabelColor32));

                if (from.AccessLevel >= AccessLevel.GameMaster && from.AccessLevel > m.AccessLevel)
                {
                    AddButton(266, 36 + line * 20, 0xFA5, 0xFA7, 8);
                    AddHtml(300, 38 + line++ * 20, 100, 20, "Disconnect".Color(LabelColor32));

                    if (m.Alive)
                    {
                        AddButton(266, 36 + line * 20, 0xFA5, 0xFA7, 9);
                        AddHtml(300, 38 + line++ * 20, 100, 20, "Kill".Color(LabelColor32));
                    }
                    else
                    {
                        AddButton(266, 36 + line * 20, 0xFA5, 0xFA7, 10);
                        AddHtml(300, 38 + line++ * 20, 100, 20, "Resurrect".Color(LabelColor32));
                    }
                }

                if (from.AccessLevel >= AccessLevel.Counselor && from.AccessLevel > m.AccessLevel)
                {
                    AddButton(266, 36 + line * 20, 0xFA5, 0xFA7, 11);
                    AddHtml(300, 38 + line++ * 20, 100, 20, "Skills browser".Color(LabelColor32));
                }
            }
        }

        private void Resend(Mobile to, RelayInfo info)
        {
            to.SendGump(new ClientGump(to, m_State, info.GetTextEntry(0) ?? ""));
        }

        public override void OnResponse(NetState state, in RelayInfo info)
        {
            if (m_State == null)
            {
                return;
            }

            var focus = m_State.Mobile;
            var from = state.Mobile;

            if (focus == null)
            {
                from.SendMessage("That character is no longer online.");
                return;
            }

            if (focus.Deleted)
            {
                from.SendMessage("That character no longer exists.");
                return;
            }

            if (from != focus && focus.Hidden && from.AccessLevel < focus.AccessLevel &&
                (focus as PlayerMobile)?.VisibilityList.Contains(from) != true)
            {
                from.SendMessage("That character is no longer visible.");
                return;
            }

            switch (info.ButtonID)
            {
                case 1: // Tell
                    {
                        var text = info.GetTextEntry(0)?.Trim();

                        if (text?.Length > 0)
                        {
                            focus.SendMessage(0x482, $"{from.Name} tells you:");
                            focus.SendMessage(0x482, text);

                            CommandLogging.WriteLine(
                                from,
                                $"{from.AccessLevel} {CommandLogging.Format(from)} telling {CommandLogging.Format(focus)} \"{text}\""
                            );
                        }

                        from.SendGump(new ClientGump(from, m_State));

                        break;
                    }
                case 4: // Props
                    {
                        Resend(from, info);

                        if (!BaseCommand.IsAccessible(from, focus))
                        {
                            from.SendLocalizedMessage(500447); // That is not accessible.
                        }
                        else
                        {
                            from.SendGump(new PropertiesGump(from, focus));
                            CommandLogging.WriteLine(
                                from,
                                $"{from.AccessLevel} {CommandLogging.Format(from)} opening properties gump of {CommandLogging.Format(focus)}"
                            );
                        }

                        break;
                    }
                case 5: // Go to
                    {
                        if (focus.Map == null || focus.Map == Map.Internal)
                        {
                            from.SendMessage("That character is not in the world.");
                        }
                        else
                        {
                            from.MoveToWorld(focus.Location, focus.Map);
                            Resend(from, info);

                            CommandLogging.WriteLine(
                                from,
                                $"{from.AccessLevel} {CommandLogging.Format(from)} going to {CommandLogging.Format(focus)}, {focus.Location} ({focus.Map})"
                            );
                        }

                        break;
                    }
                case 6: // Get
                    {
                        if (from.Map == null || from.Map == Map.Internal)
                        {
                            from.SendMessage("You cannot bring that person here.");
                        }
                        else
                        {
                            focus.MoveToWorld(from.Location, from.Map);
                            Resend(from, info);

                            CommandLogging.WriteLine(
                                from,
                                $"{from.AccessLevel} {CommandLogging.Format(from)} bringing {CommandLogging.Format(focus)} to {focus.Location} ({focus.Map})"
                            );
                        }

                        break;
                    }
                case 7: // Move
                    {
                        from.Target = new MoveTarget(focus);
                        Resend(from, info);

                        break;
                    }
                case 8: // Kick
                    {
                        if (from.AccessLevel >= AccessLevel.GameMaster && from.AccessLevel > focus.AccessLevel)
                        {
                            focus.Say("I've been kicked!");

                            m_State.Disconnect($"Kicked by {from}.");

                            CommandLogging.WriteLine(
                                from,
                                $"{from.AccessLevel} {CommandLogging.Format(from)} kicking {CommandLogging.Format(focus)}"
                            );
                        }

                        break;
                    }
                case 9: // Kill
                    {
                        if (from.AccessLevel >= AccessLevel.GameMaster && from.AccessLevel > focus.AccessLevel)
                        {
                            focus.Kill();
                            CommandLogging.WriteLine(
                                from,
                                $"{from.AccessLevel} {CommandLogging.Format(from)} killing {CommandLogging.Format(focus)}"
                            );
                        }

                        Resend(from, info);

                        break;
                    }
                case 10: // Res
                    {
                        if (from.AccessLevel >= AccessLevel.GameMaster && from.AccessLevel > focus.AccessLevel)
                        {
                            focus.PlaySound(0x214);
                            focus.FixedEffect(0x376A, 10, 16);

                            focus.Resurrect();

                            CommandLogging.WriteLine(
                                from,
                                $"{from.AccessLevel} {CommandLogging.Format(from)} resurrecting {CommandLogging.Format(focus)}"
                            );
                        }

                        Resend(from, info);

                        break;
                    }
                case 11: // Skills
                    {
                        Resend(from, info);

                        if (from.AccessLevel > focus.AccessLevel)
                        {
                            from.SendGump(new SkillsGump(from, focus));
                            CommandLogging.WriteLine(
                                from,
                                $"{from.AccessLevel} {CommandLogging.Format(from)} opening Skills gump of {CommandLogging.Format(focus)}"
                            );
                        }

                        break;
                    }
            }
        }
    }
}
