using System;
using System.Collections.Generic;
using Server.Guilds;

namespace Server.Gumps
{
    public abstract class GuildMobileListGump : DynamicGump
    {
        protected Guild _guild;
        protected List<Mobile> _list;
        private readonly bool _radio;

        public override bool Singleton => true;

        protected GuildMobileListGump(Guild guild, bool radio, List<Mobile> list) : base(40, 50)
        {
            _guild = guild;
            _radio = radio;
            _list = new List<Mobile>(list);
        }

        protected override void BuildLayout(ref DynamicGumpBuilder builder)
        {
            builder.AddPage();
            CustomGumpStyle.AddContentPanel(ref builder, 0, 0, 550, 440, "GUILD MEMBERS");

            BuildHeader(ref builder);

            for (var i = 0; i < _list.Count; ++i)
            {
                if (i % 11 == 0)
                {
                    if (i != 0)
                    {
                        builder.AddButton(300, 370, 4005, 4007, 0, GumpButtonType.Page, i / 11 + 1);
                        builder.AddHtmlLocalized(335, 370, 300, 35, 1011066, CustomGumpStyle.LocalizedTextColor); // Next page
                    }

                    builder.AddPage(i / 11 + 1);

                    if (i != 0)
                    {
                        builder.AddButton(20, 370, 4014, 4016, 0, GumpButtonType.Page, i / 11);
                        builder.AddHtmlLocalized(55, 370, 300, 35, 1011067, CustomGumpStyle.LocalizedTextColor); // Previous page
                    }
                }

                if (_radio)
                {
                    builder.AddRadio(20, 35 + i % 11 * 30, 208, 209, false, i);
                }

                var m = _list[i];

                var name = m.Name;
                builder.AddLabel(_radio ? 55 : 20, 35 + i % 11 * 30, CustomGumpStyle.TextHue, name != null ? name.AsSpan().Trim() : "(empty)");
            }
        }

        protected abstract void BuildHeader(ref DynamicGumpBuilder builder);
    }
}
