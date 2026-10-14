using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Server.Gumps;
using Xunit;

namespace Server.Tests.Gumps;

[Collection("Sequential UOContent Tests")]
public class CharacterStatsGumpTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(3, 5)]
    [InlineData(4, 7)]
    [InlineData(5, 9)]
    public void T2AEnchantmentAddsFlatDamageBeforeScaling(int level, int expected)
    {
        Assert.Equal(expected, CharacterCombatSummary.GetT2ADamageLevelBonus(level));
    }

    [Theory]
    [InlineData(20, 40, 100)]
    [InlineData(20, 10, -50)]
    [InlineData(20, 20, 0)]
    [InlineData(20, 42, 110)]
    [InlineData(2, 3, 50)]
    [InlineData(0, 40, 0)]
    [InlineData(double.NaN, 40, 0)]
    [InlineData(20, double.PositiveInfinity, 0)]
    public void TotalImprovementUsesEffectiveResultRatherThanAddingBonuses(double baseline, double effective, double expected)
    {
        Assert.Equal(expected, CharacterCombatSummary.GetImprovement(baseline, effective), 6);
    }

    [Theory]
    [InlineData(2.0, 30.0)]
    [InlineData(1.5, 40.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(-1.0, 0.0)]
    [InlineData(double.NaN, 0.0)]
    [InlineData(double.PositiveInfinity, 0.0)]
    public void SwingRateHandlesValidAndUnavailableDelays(double seconds, double expected)
    {
        Assert.Equal(expected, CharacterStatsGump.GetSwingsPerMinute(seconds));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FooterKeepsAllFourReplyControlsOnOneRowWithTheSelectedTabHighlighted(bool showSpells)
    {
        var footer = RenderFooter(showSpells);
        var buttons = ReadButtons(footer.Layout);
        Assert.Equal(4, buttons.Count);
        var ids = new HashSet<int>();
        foreach (var button in buttons)
        {
            Assert.True(ids.Add(button.Id));
            Assert.Equal(1, button.Type);
            Assert.Equal(0, button.Target);
            Assert.Equal(4005, button.NormalArt);
            Assert.Equal(4007, button.PressedArt);
            Assert.Equal(buttons[0].Bounds.Y, button.Bounds.Y);
        }
        Assert.Equal(536, buttons[0].Bounds.Y);
        Assert.True(ids.SetEquals(new[] { 0, 1, 2, 3 }));
        Assert.Contains("{ button 28 536 4005 4007 1 0 1 }", footer.Layout);
        Assert.Contains("{ button 190 536 4005 4007 1 0 2 }", footer.Layout);
        Assert.Contains("{ button 490 536 4005 4007 1 0 3 }", footer.Layout);
        Assert.Contains("{ button 603 536 4005 4007 1 0 0 }", footer.Layout);

        var labels = ReadLabels(footer);
        Assert.Equal(4, labels.Count);
        var refresh = Assert.Single(labels, label => label.Text.Contains(">REFRESH<", StringComparison.Ordinal));
        var close = Assert.Single(labels, label => label.Text.Contains(">CLOSE<", StringComparison.Ordinal));
        var melee = Assert.Single(labels, label => label.Text.Contains(">MELEE<", StringComparison.Ordinal));
        var spells = Assert.Single(labels, label => label.Text.Contains(">SPELLS<", StringComparison.Ordinal));
        Assert.Contains(CustomGumpStyle.WhiteTextColor, refresh.Text);
        Assert.Contains(CustomGumpStyle.WhiteTextColor, close.Text);
        Assert.Contains(showSpells ? CustomGumpStyle.WhiteTextColor : CustomGumpStyle.TitleColor, melee.Text);
        Assert.Contains(showSpells ? CustomGumpStyle.TitleColor : CustomGumpStyle.WhiteTextColor, spells.Text);
        foreach (var label in labels)
        {
            Assert.Equal(refresh.Bounds.Y, label.Bounds.Y);
            Assert.Contains("SIZE=4", label.Text);
        }
        Assert.Equal(535, refresh.Bounds.Y);

        Assert.Single(Regex.Matches(footer.Layout, @"\{ tilepic \d+ \d+ 5049 \}"));
        Assert.Single(Regex.Matches(footer.Layout, @"\{ tilepic \d+ \d+ 3834 \}"));
        Assert.Contains("{ tilepic 142 534 5049 }", footer.Layout);
        Assert.Contains("{ tilepic 450 535 3834 }", footer.Layout);
    }

    [Theory]
    [InlineData(false, 0, 1, 0, 0)]
    [InlineData(false, 0, 3, 0, 2)]
    [InlineData(false, 1, 3, 1, 3)]
    [InlineData(false, 2, 3, 2, 0)]
    [InlineData(true, 0, 1, 0, 0)]
    [InlineData(true, 0, 3, 0, 2)]
    [InlineData(true, 1, 3, 1, 3)]
    [InlineData(true, 2, 3, 2, 0)]
    public void ModifierPaginationStaysOnTheFooterRowAndTargetsOnlyExistingClientPages(
        bool showSpells, int page, int pages, int previousTarget, int nextTarget)
    {
        var footer = RenderFooter(showSpells, page, pages);
        var buttons = ReadButtons(footer.Layout);
        var navigationCount = 0;
        var previousCount = 0;
        var nextCount = 0;
        foreach (var button in buttons)
        {
            Assert.Equal(buttons[0].Bounds.Y, button.Bounds.Y);
            if (button.Type == 0)
            {
                navigationCount++;
                Assert.Equal(0, button.Id);
                Assert.InRange(button.Target, 1, pages);
                Assert.NotEqual(page + 1, button.Target);
                if (button.NormalArt == 4014)
                {
                    previousCount++;
                    Assert.NotEqual(0, previousTarget);
                    Assert.Equal(previousTarget, button.Target);
                    Assert.Equal(4016, button.PressedArt);
                    Assert.Equal(308, button.Bounds.X);
                }
                else
                {
                    nextCount++;
                    Assert.NotEqual(0, nextTarget);
                    Assert.Equal(nextTarget, button.Target);
                    Assert.Equal(4005, button.NormalArt);
                    Assert.Equal(4007, button.PressedArt);
                    Assert.Equal(416, button.Bounds.X);
                }
            }
        }
        Assert.Equal((previousTarget == 0 ? 0 : 1) + (nextTarget == 0 ? 0 : 1), navigationCount);
        Assert.Equal(previousTarget == 0 ? 0 : 1, previousCount);
        Assert.Equal(nextTarget == 0 ? 0 : 1, nextCount);
        Assert.Equal(4 + navigationCount, buttons.Count);
        var labels = ReadLabels(footer);
        var counter = Assert.Single(labels, label => label.Text.Contains($">{page + 1} / {pages}<", StringComparison.Ordinal));
        Assert.Equal(labels[0].Bounds.Y, counter.Bounds.Y);
        Assert.Equal(new Bounds(342, 535, 70, 24), counter.Bounds);
        Assert.Contains("<CENTER>", counter.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FooterArtworkAndLabelsFitInsideTheFrameWithoutOverlapping(bool showSpells)
    {
        var footer = RenderFooter(showSpells, 1, 3);
        var visibleBounds = new List<Bounds>();
        foreach (var button in ReadButtons(footer.Layout))
        {
            AssertInsideFrame(button.Bounds);
            visibleBounds.Add(button.Bounds);
        }
        foreach (var label in ReadLabels(footer))
        {
            AssertInsideFrame(label.Bounds);
            visibleBounds.Add(label.Bounds);
        }
        foreach (Match icon in Regex.Matches(footer.Layout, @"\{ tilepic (\d+) (\d+) (\d+) \}"))
        {
            var x = Number(icon, 1);
            var y = Number(icon, 2);
            var art = Number(icon, 3);
            Assert.Contains(art, new[] { 0x13B9, 0xEFA });
            AssertInsideFrame(new Bounds(x, y, 44, art == 0x13B9 ? 39 : 36));
            visibleBounds.Add(art == 0x13B9 ? new Bounds(x + 17, y + 1, 25, 24) : new Bounds(x + 11, y + 1, 19, 21));
        }
        for (var i = 0; i < visibleBounds.Count; i++)
        {
            for (var j = i + 1; j < visibleBounds.Count; j++)
            {
                Assert.False(visibleBounds[i].Overlaps(visibleBounds[j]),
                    $"Footer content overlaps: {visibleBounds[i]} and {visibleBounds[j]}.");
            }
        }
    }

    private static void AssertInsideFrame(Bounds bounds)
    {
        Assert.InRange(bounds.X, CustomGumpStyle.ContentPadding,
            CustomGumpStyle.MaximumPanelWidth - CustomGumpStyle.ContentPadding - bounds.Width);
        Assert.InRange(bounds.Y, CustomGumpStyle.ContentPadding,
            CustomGumpStyle.MaximumPanelHeight - CustomGumpStyle.ContentPadding - bounds.Height);
    }

    private static Footer RenderFooter(bool showSpells, int page = -1, int pages = 1)
    {
        var builder = new DynamicGumpBuilder();
        try
        {
            CharacterStatsGump.BuildFooter(ref builder, showSpells);
            if (page >= 0)
            {
                CharacterStatsGump.BuildModifierNavigation(ref builder, page, pages);
            }
            var strings = new List<string>();
            var data = builder.StringsData;
            while (!data.IsEmpty)
            {
                var length = BinaryPrimitives.ReadUInt16BigEndian(data) * 2;
                strings.Add(Encoding.BigEndianUnicode.GetString(data.Slice(2, length)));
                data = data[(length + 2)..];
            }
            return new Footer(Encoding.ASCII.GetString(builder.LayoutData), strings);
        }
        finally
        {
            builder.Dispose();
        }
    }

    private static List<Button> ReadButtons(string layout)
    {
        var buttons = new List<Button>();
        foreach (Match match in Regex.Matches(layout, @"\{ button (\d+) (\d+) (\d+) (\d+) (\d+) (\d+) (\d+) \}"))
        {
            buttons.Add(new Button(new Bounds(Number(match, 1), Number(match, 2), 30, 22),
                Number(match, 3), Number(match, 4), Number(match, 5), Number(match, 6), Number(match, 7)));
        }
        return buttons;
    }

    private static List<Label> ReadLabels(Footer footer)
    {
        var labels = new List<Label>();
        foreach (Match match in Regex.Matches(footer.Layout, @"\{ htmlgump (\d+) (\d+) (\d+) (\d+) (\d+) 0 0 \}"))
        {
            labels.Add(new Label(new Bounds(Number(match, 1), Number(match, 2), Number(match, 3), Number(match, 4)),
                footer.Strings[Number(match, 5)]));
        }
        return labels;
    }

    private static int Number(Match match, int group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

    private readonly record struct Footer(string Layout, List<string> Strings);
    private readonly record struct Button(Bounds Bounds, int NormalArt, int PressedArt, int Type, int Target, int Id);
    private readonly record struct Label(Bounds Bounds, string Text);
    private readonly record struct Bounds(int X, int Y, int Width, int Height)
    {
        public bool Overlaps(Bounds other) =>
            X < other.X + other.Width && X + Width > other.X && Y < other.Y + other.Height && Y + Height > other.Y;
    }
}
