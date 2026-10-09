using System;
using System.Collections.Generic;
using System.Net;
using Server.Mobiles;
using Server.Network;

namespace Server.Gumps;

public sealed class CharacterStatsGump : DynamicGump
{
    private readonly PlayerMobile _player;
    private readonly bool _showSpells;
    public override bool Singleton => true;

    private CharacterStatsGump(PlayerMobile player, bool showSpells) : base(50, 50)
    {
        _player = player;
        _showSpells = showSpells;
    }

    public static void DisplayTo(Mobile from, bool showSpells = false)
    {
        if (from is PlayerMobile { Deleted: false, NetState: not null } player)
        {
            player.SendGump(new CharacterStatsGump(player, showSpells));
        }
    }

    protected override void BuildLayout(ref DynamicGumpBuilder builder)
    {
        builder.AddPage();
        CustomGumpStyle.AddPanel(ref builder, 0, 0,
            CustomGumpStyle.MaximumPanelWidth, CustomGumpStyle.MaximumPanelHeight, "CHARACTER SHEET");
        Text(ref builder, 28, 36, 664, WebUtility.HtmlEncode(_player.Name), align: TextAlignment.Center);
        List<CharacterCombatSummary.Modifier> modifiers;
        var modifierTitleY = 226;
        if (_showSpells)
        {
            var spells = CharacterSpellSummary.Capture(_player);
            BuildSpells(ref builder, spells);
            modifiers = spells.Modifiers;
            modifierTitleY = 310;
        }
        else
        {
            var combat = CharacterCombatSummary.Capture(_player);
            BuildMelee(ref builder, combat);
            modifiers = combat.Modifiers;
        }

        Text(ref builder, 28, modifierTitleY, 664, "EFFECTS & MODIFIERS", CustomGumpStyle.TitleColor);
        Text(ref builder, 28, modifierTitleY + 30, 180, "Effect");
        Text(ref builder, 212, modifierTitleY + 30, 110, "Amount");
        Text(ref builder, 336, modifierTitleY + 30, 356, "Source");
        BuildTabs(ref builder);
        builder.AddButton(28, 568, 4005, 4007, 1);
        Text(ref builder, 63, 568, 125, "REFRESH");
        builder.AddButton(582, 568, 4005, 4007, 0);
        Text(ref builder, 617, 568, 75, "CLOSE");

        var rowsPerPage = _showSpells ? 5 : 8;
        var pages = Math.Max(1, (modifiers.Count + rowsPerPage - 1) / rowsPerPage);
        for (var page = 0; page < pages; page++)
        {
            builder.AddPage(page + 1);
            for (var row = 0; row < rowsPerPage && page * rowsPerPage + row < modifiers.Count; row++)
            {
                var modifier = modifiers[page * rowsPerPage + row];
                var y = modifierTitleY + 58 + row * 25;
                Text(ref builder, 28, y, 180, modifier.Effect);
                Text(ref builder, 212, y, 118, modifier.Amount);
                Text(ref builder, 336, y, 356, WebUtility.HtmlEncode(modifier.Source));
            }

            if (modifiers.Count == 0)
            {
                Text(ref builder, 28, modifierTitleY + 58, 664, "No active modifiers to display.");
            }

            if (page > 0)
            {
                builder.AddButton(245, 568, 4014, 4016, 0, GumpButtonType.Page, page);
            }

            Text(ref builder, 282, 568, 130, $"{page + 1} / {pages}", align: TextAlignment.Center);
            if (page + 1 < pages)
            {
                builder.AddButton(430, 568, 4005, 4007, 0, GumpButtonType.Page, page + 2);
            }
        }
    }

    private void BuildMelee(ref DynamicGumpBuilder builder, CharacterCombatSummary combat)
    {
        Text(ref builder, 28, 66, 664, WebUtility.HtmlEncode(combat.WeaponName), CustomGumpStyle.TitleColor);

        if (combat.Supported)
        {
            Row(ref builder, 28, 132, "Damage range", $"{combat.DamageMin} - {combat.DamageMax}");
            Row(ref builder, 28, 158, "Total damage gain", $"{combat.DamageImprovement:+0.00;-0.00;0.00}%");
            Row(ref builder, 376, 132, "Swing interval", $"{combat.SwingSeconds:0.00} sec");
            Row(ref builder, 376, 158, "Total swing-rate gain", $"{combat.SpeedImprovement:+0.00;-0.00;0.00}%");
            Row(ref builder, 376, 184, "Swings / minute", $"{GetSwingsPerMinute(combat.SwingSeconds):0.0}");
        }
        else
        {
            Text(ref builder, 28, 132, 664, "Modifier breakdown unavailable for this combat ruleset.");
        }

        Row(ref builder, 28, 184, "Effective armor", $"{_player.ArmorRating:0.00} AR");
    }

    private static void BuildSpells(ref DynamicGumpBuilder builder, CharacterSpellSummary spells)
    {
        Text(ref builder, 28, 66, 664, "Vs non-human monsters, 0 Resist, unresisted", CustomGumpStyle.TitleColor);
        if (!spells.Supported)
        {
            Text(ref builder, 28, 96, 664, "Spell breakdown unavailable for this combat ruleset.");
            return;
        }

        Row(ref builder, 28, 96, "Total spell damage gain", $"{spells.DamageImprovement:+0.00;-0.00;0.00}%");
        Row(ref builder, 376, 96, "Damage multiplier", $"{spells.DamageScalar:0.00}x");
        Text(ref builder, 28, 126, 220, "Spell");
        Text(ref builder, 260, 126, 180, "Damage range");
        Text(ref builder, 500, 126, 192, "Cast time");
        for (var i = 0; i < spells.Spells.Count; i++)
        {
            var spell = spells.Spells[i];
            var y = 156 + i * 24;
            Text(ref builder, 28, y, 220, spell.Name);
            Text(ref builder, 260, y, 180, $"{spell.Minimum} - {spell.Maximum}");
            Text(ref builder, 500, y, 192, $"{spell.CastSeconds:0.00} sec");
        }
    }

    private void BuildTabs(ref DynamicGumpBuilder builder)
    {
        Text(ref builder, 236, 496, 112, "MELEE", _showSpells ? CustomGumpStyle.WhiteTextColor : CustomGumpStyle.TitleColor,
            TextAlignment.Center);
        Text(ref builder, 396, 496, 112, "SPELLS", _showSpells ? CustomGumpStyle.TitleColor : CustomGumpStyle.WhiteTextColor,
            TextAlignment.Center);
        builder.AddItem(246, 524, 0x13B9);
        builder.AddItem(406, 524, 0xEFA);
        builder.AddButton(294, 524, 4005, 4007, 2);
        builder.AddButton(454, 524, 4005, 4007, 3);
    }

    internal static double GetSwingsPerMinute(double seconds) =>
        double.IsFinite(seconds) && seconds > 0 ? 60.0 / seconds : 0;

    private static void Text(ref DynamicGumpBuilder builder, int x, int y, int width, string text,
        string color = CustomGumpStyle.WhiteTextColor, TextAlignment align = TextAlignment.Left) =>
        builder.AddHtml(x, y, width, 24, text, color, CustomGumpStyle.StandardFontSize, align: align);

    private static void Row(ref DynamicGumpBuilder builder, int x, int y, string label, string value)
    {
        Text(ref builder, x, y, 196, label);
        Text(ref builder, x + 198, y, 118, value, align: TextAlignment.Right);
    }

    public override void OnResponse(NetState sender, in RelayInfo info)
    {
        if (sender.Mobile != _player)
        {
            return;
        }

        switch (info.ButtonID)
        {
            case 1:
                DisplayTo(_player, _showSpells);
                break;
            case 2:
                DisplayTo(_player);
                break;
            case 3:
                DisplayTo(_player, true);
                break;
        }
    }
}
