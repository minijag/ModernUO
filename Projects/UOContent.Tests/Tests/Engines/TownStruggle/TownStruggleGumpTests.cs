using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Server.Engines.TownStruggle;
using Server.Gumps;
using Xunit;

namespace UOContent.Tests.Engines.TownStruggle;

[Collection("Sequential UOContent Tests")]
public class TownStruggleGumpTests
{
    [Theory]
    [InlineData(TownStrugglePhase.Dormant, false)]
    [InlineData(TownStrugglePhase.Signup, false)]
    [InlineData(TownStrugglePhase.Active, false)]
    [InlineData(TownStrugglePhase.Results, false)]
    [InlineData(TownStrugglePhase.Faulted, false)]
    [InlineData(TownStrugglePhase.Active, true)]
    [InlineData(TownStrugglePhase.Results, true)]
    public void PanelsUseSharedThemeAndAllControlsStayInsideContentPadding(TownStrugglePhase phase, bool roster)
    {
        var board = Render(Snapshot(phase, roster));
        Assert.Contains("gumppic 700 580 60009", board.Layout);
        Assert.Contains(" 60002 }", board.Layout);
        Assert.Contains(" 2624 }", board.Layout);
        Assert.Contains(" 64040 }", board.Layout);

        foreach (Match button in Regex.Matches(board.Layout, @"\{ button (\d+) (\d+) (\d+) (\d+) (\d+) (\d+) (\d+) \}"))
        {
            AssertInside(Number(button, 1), Number(button, 2), CustomGumpStyle.OptionButtonSize, CustomGumpStyle.OptionButtonSize);
            Assert.Contains(Number(button, 3), new[] { 9721, 2154 });
            Assert.Equal(2151, Number(button, 4));
        }

        foreach (Match text in Regex.Matches(board.Layout, @"\{ htmlgump (\d+) (-?\d+) (\d+) (\d+)"))
        {
            if (Number(text, 2) < CustomGumpStyle.ContentPadding)
            {
                continue;
            }
            AssertInside(Number(text, 1), Number(text, 2), Number(text, 3), Number(text, 4));
        }
    }

    [Fact]
    public void ActiveMatchCannotLeakOpposingTeamScoreOrUnassignedTeamScore()
    {
        var view = Snapshot(TownStrugglePhase.Active, false);
        var board = Render(view);
        Assert.Contains(board.Strings, value => value.Contains("Your team: Order - 77 points"));
        Assert.DoesNotContain(board.Strings, value => value.Contains("998877") || value.Contains("887766"));

        board = Render(view with { Team = TownStruggleTeam.Neutral, Registered = false });
        Assert.DoesNotContain(board.Strings, value => value.Contains("77 points") || value.Contains("998877") || value.Contains("887766"));

        board = Render(view with { Phase = TownStrugglePhase.Results });
        Assert.Contains(board.Strings, value => value.Contains("Order 998877 / Chaos 887766"));
    }

    [Fact]
    public void DeploymentOnlyExposesOwnedCaptureAndAllRespawnZones()
    {
        var board = Render(Snapshot(TownStrugglePhase.Active, false));
        Assert.True(HasButton(board.Layout, 10));
        Assert.False(HasButton(board.Layout, 11));
        Assert.False(HasButton(board.Layout, 12));
        Assert.True(HasButton(board.Layout, 13));
        Assert.True(HasButton(board.Layout, 14));
        Assert.True(HasButton(board.Layout, 15));
        board = Render(Snapshot(TownStrugglePhase.Active, false) with { Disqualified = true });
        for (var id = 10; id <= 15; id++)
        {
            Assert.False(HasButton(board.Layout, id));
        }
    }

    [Fact]
    public void SignupJoinButtonRequiresNoExistingRegistrationOrDisqualification()
    {
        var view = Snapshot(TownStrugglePhase.Signup, false) with { Registered = false };
        Assert.True(HasButton(Render(view).Layout, 2));
        Assert.False(HasButton(Render(view with { Registered = true }).Layout, 2));
        Assert.False(HasButton(Render(view with { Disqualified = true }).Layout, 2));
        Assert.False(HasButton(Render(view with { Phase = TownStrugglePhase.Active }).Layout, 2));
    }

    [Fact]
    public void RosterPaginationAndSelectedVoteUseNativeOptionArtwork()
    {
        var board = Render(Snapshot(TownStrugglePhase.Results, true));
        Assert.True(HasButton(board.Layout, 6));
        Assert.True(HasButton(board.Layout, 7));
        Assert.Contains("button 584 204 2154 2151 1 0 100", board.Layout);
        Assert.Contains("button 584 407 9721 2151 1 0 107", board.Layout);
        Assert.Contains(board.Strings, value => value.Contains("Page 2 / 3"));
    }

    [Fact]
    public void ConfirmationUsesStandardThemeAndHasBothExplicitChoices()
    {
        var builder = new DynamicGumpBuilder();
        try
        {
            BattleCommendation.RedeemGump.BuildConfirmation(ref builder);
            var layout = Encoding.ASCII.GetString(builder.LayoutData);
            var strings = ReadStrings(builder.StringsData);
            Assert.Contains("gumppic 460 230 60009", layout);
            Assert.Contains("button 28 188 9721 2151 1 0 1", layout);
            Assert.Contains("button 304 188 9721 2151 1 0 0", layout);
            Assert.Contains(strings, value => value.Contains("2,500 gold"));
            Assert.Contains(strings, value => value.Contains("Random-item redemption is not available"));
        }
        finally
        {
            builder.Dispose();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SignupClearlyWarnsAboutTheConfiguredLootRule(bool selective)
    {
        var board = Render(Snapshot(TownStrugglePhase.Signup, false) with { SelectiveLootEnabled = selective });
        if (selective)
        {
            Assert.Contains(board.Strings, value => value.Contains("magic gear + exceptional colored gear can drop"));
            Assert.Contains(board.Strings, value => value.Contains("blessed/insured protections apply"));
            Assert.DoesNotContain(board.Strings, value => value.Contains("all equipment is retained"));
        }
        else
        {
            Assert.Contains(board.Strings, value => value.Contains("all equipment is retained"));
            Assert.DoesNotContain(board.Strings, value => value.Contains("can drop"));
        }
    }

    private static TownStruggleGump.View Snapshot(TownStrugglePhase phase, bool roster)
    {
        var rows = new TownStruggleGump.Row[8];
        for (var i = 0; i < rows.Length; i++)
        {
            rows[i] = new TownStruggleGump.Row($"Player {i}", 22.5, 124, phase == TownStrugglePhase.Results, i == 0);
        }
        return new TownStruggleGump.View(phase, "Galven", TimeSpan.FromMinutes(14), TownStruggleTeam.Order, true, false,
            77, 998877, 887766, 22.5, 10, 125, 2, TimeSpan.FromSeconds(60),
            [new(100, TownStruggleTeam.Order), new(-100, TownStruggleTeam.Chaos), default], rows, 1, 3, roster);
    }

    private static (string Layout, List<string> Strings) Render(TownStruggleGump.View view)
    {
        var builder = new DynamicGumpBuilder();
        try
        {
            TownStruggleGump.BuildBoard(ref builder, view);
            return (Encoding.ASCII.GetString(builder.LayoutData), ReadStrings(builder.StringsData));
        }
        finally
        {
            builder.Dispose();
        }
    }

    private static List<string> ReadStrings(ReadOnlySpan<byte> bytes)
    {
        var strings = new List<string>();
        while (!bytes.IsEmpty)
        {
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes) * 2;
            strings.Add(Encoding.BigEndianUnicode.GetString(bytes.Slice(2, length)));
            bytes = bytes[(length + 2)..];
        }
        return strings;
    }

    private static bool HasButton(string layout, int id) => layout.Contains($" 1 0 {id} }}", StringComparison.Ordinal);
    private static int Number(Match match, int group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
    private static void AssertInside(int x, int y, int width, int height)
    {
        Assert.InRange(x, CustomGumpStyle.ContentPadding, TownStruggleGump.PanelWidth - CustomGumpStyle.ContentPadding - width);
        Assert.InRange(y, CustomGumpStyle.ContentPadding, TownStruggleGump.PanelHeight - CustomGumpStyle.ContentPadding - height);
    }
}
