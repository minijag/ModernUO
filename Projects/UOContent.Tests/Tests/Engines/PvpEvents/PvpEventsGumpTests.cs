using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Server;
using Server.Engines.PvpEvents;
using Server.Gumps;
using Server.Mobiles;
using Server.Network;
using Server.Tests;
using Server.Tests.Network;
using Xunit;

namespace UOContent.Tests.Engines.PvpEvents;

[Collection("Sequential UOContent Tests")]
public class PvpEventsGumpTests : IDisposable
{
    private readonly PvpEventLedger _previousLedger = PvpEventHistory.Ledger;

    public PvpEventsGumpTests() => PvpEventHistory.Ledger = new PvpEventLedger();
    public void Dispose() => PvpEventHistory.Ledger = _previousLedger;

    [Theory]
    [InlineData(EventKind.Extraction, StatsScope.PreviousEvent)]
    [InlineData(EventKind.Extraction, StatsScope.Seasonal)]
    [InlineData(EventKind.Extraction, StatsScope.Lifetime)]
    [InlineData(EventKind.TownStruggle, StatsScope.PreviousEvent)]
    [InlineData(EventKind.TownStruggle, StatsScope.Seasonal)]
    [InlineData(EventKind.TownStruggle, StatsScope.Lifetime)]
    public void EveryViewUsesSharedThemeAndKeepsTextAndControlsInsideTheFrame(EventKind kind, StatsScope scope)
    {
        var view = Snapshot(kind, scope) with { Page = 1 };
        var board = Render(view);
        Assert.Contains("gumppic 700 580 60009", board.Layout);
        Assert.Contains(" 60002 }", board.Layout);
        Assert.Contains(" 2624 }", board.Layout);
        Assert.Contains(" 64040 }", board.Layout);
        var selected = 0;
        foreach (Match button in Regex.Matches(board.Layout, @"\{ button (\d+) (\d+) (\d+) (\d+) (\d+) (\d+) (\d+) \}"))
        {
            AssertInside(Number(button, 1), Number(button, 2), 29, 29);
            Assert.Contains(Number(button, 3), new[] { 9721, 2154 });
            Assert.Equal(2151, Number(button, 4));
            if (Number(button, 3) == 2154) { selected++; }
        }
        Assert.Equal(2, selected);
        foreach (Match text in Regex.Matches(board.Layout, @"\{ (?:htmlgump|croppedtext) (\d+) (-?\d+) (\d+) (\d+)"))
        {
            if (Number(text, 2) >= CustomGumpStyle.ContentPadding)
            {
                AssertInside(Number(text, 1), Number(text, 2), Number(text, 3), Number(text, 4));
            }
        }
        Assert.True(HasButton(board.Layout, 10));
        Assert.True(HasButton(board.Layout, 11));
        Assert.True(HasButton(board.Layout, 20));
        Assert.True(HasButton(board.Layout, 21));
        Assert.True(HasButton(board.Layout, 22));
        Assert.Equal(kind == EventKind.TownStruggle, HasButton(board.Layout, 5));
    }

    [Theory]
    [InlineData(EventKind.Extraction)]
    [InlineData(EventKind.TownStruggle)]
    public void FullPageTextRowsDoNotOverlapTheNoticeOwnStatisticsOrFooter(EventKind kind)
    {
        var board = Render(Snapshot(kind, StatsScope.PreviousEvent) with { Page = 1, ResultsPending = true });
        var regions = new List<(int X, int Y, int Width, int Height)>();
        foreach (Match text in Regex.Matches(board.Layout, @"\{ (?:htmlgump|croppedtext) (\d+) (-?\d+) (\d+) (\d+)"))
        {
            var region = (X: Number(text, 1), Y: Number(text, 2), Width: Number(text, 3), Height: Number(text, 4));
            if (region.Y < CustomGumpStyle.ContentPadding) { continue; }
            foreach (var other in regions)
            {
                Assert.False(region.X < other.X + other.Width && other.X < region.X + region.Width &&
                    region.Y < other.Y + other.Height && other.Y < region.Y + region.Height,
                    $"Text rectangles overlap: {region} and {other}");
            }
            regions.Add(region);
        }
        Assert.Contains(regions, region => region.Y == 434 && region.Height == 24);
        Assert.Contains(regions, region => region.Y == 459 && region.Height == 24);
        Assert.Contains(regions, region => region.Y == 486 && region.Height == 24);
    }

    [Theory]
    [InlineData(EventKind.Extraction, StatsScope.PreviousEvent)]
    [InlineData(EventKind.Extraction, StatsScope.Seasonal)]
    [InlineData(EventKind.Extraction, StatsScope.Lifetime)]
    [InlineData(EventKind.TownStruggle, StatsScope.PreviousEvent)]
    [InlineData(EventKind.TownStruggle, StatsScope.Seasonal)]
    [InlineData(EventKind.TownStruggle, StatsScope.Lifetime)]
    public void NativeFontAdvancesFitAllSingleLineCaptionsHeadersAndOwnStatistics(EventKind kind, StatsScope scope)
    {
        Assert.Equal(4, CustomGumpStyle.StandardFontSize);
        var board = Render(Snapshot(kind, scope) with { Page = 1, ResultsPending = true });
        foreach (Match html in Regex.Matches(board.Layout, @"\{ htmlgump (\d+) (-?\d+) (\d+) (\d+) (\d+)"))
        {
            if (Number(html, 4) != 24) { continue; }
            var text = Regex.Replace(board.Strings[Number(html, 5)], "<[^>]*>", "");
            Assert.True(NativeFontWidth(text) + 4 <= Number(html, 3), $"HTML caption does not fit: {text}");
        }
        foreach (Match label in Regex.Matches(board.Layout, @"\{ croppedtext (\d+) (\d+) (\d+) (\d+) (\d+) (\d+)"))
        {
            var y = Number(label, 2);
            if (y >= 266 && y <= 434) { continue; }
            var text = board.Strings[Number(label, 6)];
            Assert.True(NativeFontWidth(text) + 4 <= Number(label, 3), $"Native label does not fit: {text}");
        }
    }

    [Fact]
    public void ExportNativePreviewWhenRequested()
    {
        var directory = Environment.GetEnvironmentVariable("PVP_GUMP_PREVIEW_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) { return; }
        Directory.CreateDirectory(directory);
        foreach (var kind in Enum.GetValues<EventKind>())
        {
            foreach (var scope in Enum.GetValues<StatsScope>())
            {
                var board = Render(Snapshot(kind, scope) with { Page = 1, ResultsPending = true });
                File.WriteAllText(Path.Combine(directory, $"{kind}-{scope}.json"),
                    JsonSerializer.Serialize(new { board.Layout, board.Strings }));
            }
        }
    }

    [Fact]
    public void EmptyHistoryIsExplicitAndNeverInventsPreviousResults()
    {
        var view = PvpEventsGump.Capture(42, EventKind.TownStruggle, StatsScope.PreviousEvent, 999, Utc(10));
        Assert.False(view.HasRecordedData);
        Assert.False(view.Finalized);
        Assert.Null(view.FinishedUtc);
        Assert.Null(view.OrderTotal);
        Assert.Empty(view.Rows);
        Assert.Null(view.OwnRow);
        Assert.Equal(0, view.Page);
        var board = Render(view);
        Assert.Contains(board.Strings, value => value.Contains("No completed event has been recorded"));
        Assert.Contains(board.Strings, value => value.Contains("Recording begins with this update"));
        Assert.DoesNotContain(board.Strings, value => value.Contains("FINAL TEAM TOTALS"));
        Assert.False(HasButton(board.Layout, 2));
    }

    [Fact]
    public void UnfinalizedPreviousViewCannotRevealRowsOwnStatsOrTeamScores()
    {
        var view = Snapshot(EventKind.TownStruggle, StatsScope.PreviousEvent) with { Finalized = false };
        var board = Render(view);
        Assert.DoesNotContain(board.Strings, value => value.Contains("Player 1") || value.Contains("7654321") || value.Contains("8765432"));
        Assert.DoesNotContain(board.Strings, value => value.Contains("YOUR STATS: Rank") || value.Contains("FINAL TEAM TOTALS"));
        Assert.Equal(1, PvpEventsGump.PageCount(view));
    }

    [Theory]
    [InlineData(StatsScope.Seasonal)]
    [InlineData(StatsScope.Lifetime)]
    public void AggregatesDoNotClaimAParticularTownOrFinalTeamTotals(StatsScope scope)
    {
        var board = Render(Snapshot(EventKind.TownStruggle, scope));
        Assert.DoesNotContain(board.Strings, value => value.Contains("Galven") || value.Contains("7654321") || value.Contains("8765432"));
        Assert.Contains(board.Strings, value => value.Contains("completed, recorded events only"));
    }

    [Fact]
    public void TownPreviousShowsFinalTeamScoresAndAccuratelyNamesKillAssistCredits()
    {
        var board = Render(Snapshot(EventKind.TownStruggle, StatsScope.PreviousEvent));
        Assert.Contains(board.Strings, value => value.Contains("Order 7,654,321 / Chaos 8,765,432"));
        Assert.Contains(board.Strings, value => value.Contains("Galven | Finished 2026-10-13 12:00 UTC"));
        Assert.Contains(board.Strings, value => value.Contains("K/A = credited kills or assists"));
        Assert.Contains(board.Strings, value => value.Contains("Healing"));
        Assert.Contains(board.Strings, value => value == "REW");
        Assert.Contains(board.Strings, value => value == "PENALTIES");
        Assert.DoesNotContain(board.Strings, value => value == "KILLS");
    }

    [Fact]
    public void PendingMvpVoteNoticePreservesTheLastFinalizedEvent()
    {
        var view = Snapshot(EventKind.TownStruggle, StatsScope.PreviousEvent) with { ResultsPending = true };
        var board = Render(view);
        Assert.Contains(board.Strings, value => value.Contains("Current results are pending") && value.Contains("15-minute MVP vote"));
        Assert.Contains(board.Strings, value => value.Contains("Order 7,654,321 / Chaos 8,765,432"));
        Assert.Contains(board.Strings, value => value.Contains("Galven | Finished 2026-10-13 12:00 UTC"));
    }

    [Theory]
    [InlineData(StatsScope.PreviousEvent)]
    [InlineData(StatsScope.Seasonal)]
    [InlineData(StatsScope.Lifetime)]
    public void ExtractionUsesSuccessfulExtractionsWithoutInventedPointsOrControls(StatsScope scope)
    {
        var board = Render(Snapshot(EventKind.Extraction, scope));
        Assert.Contains(board.Strings, value => value == "EXTRACTED");
        Assert.Contains(board.Strings, value => value.Contains("Successful extractions"));
        Assert.Contains(board.Strings, value => value.Contains("Stand in the PvP lobby before the round starts to enter."));
        Assert.Contains(board.Strings, value => value.Contains("Extraction opens after 7 minutes."));
        Assert.Contains(board.Strings, value => value == "KILLS");
        Assert.Contains(board.Strings, value => value.Contains("Kills 12") && value.Contains("Success rate 50"));
        Assert.DoesNotContain(board.Strings, value => value.Contains("FINAL TEAM TOTALS") || value == "POINTS" || value == "K/A");
        Assert.False(HasButton(board.Layout, 5));
    }

    [Theory]
    [InlineData(-50, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(999, 2)]
    public void PageBoundsAreClampedAndAllRankedRowsRemainReachable(int requested, int expected)
    {
        var view = Snapshot(EventKind.TownStruggle, StatsScope.Lifetime) with { Page = requested };
        Assert.Equal(expected, PvpEventsGump.ClampPage(view));
        Assert.Equal(3, PvpEventsGump.PageCount(view));
        var board = Render(view);
        var first = expected * 8 + 1;
        Assert.Contains(board.Strings, value => value == $"Player {first} [Guild {first}]");
        Assert.Equal(expected > 0, HasButton(board.Layout, 3));
        Assert.Equal(expected < 2, HasButton(board.Layout, 4));
        if (expected == 2)
        {
            Assert.Contains(board.Strings, value => value == "Player 17 [Guild 17]");
            Assert.DoesNotContain(board.Strings, value => value == "Player 9 [Guild 9]");
        }
    }

    [Fact]
    public void NavigationPreservesOtherSelectorAndShowMineUsesFullRosterPosition()
    {
        var view = Snapshot(EventKind.TownStruggle, StatsScope.Lifetime) with { Page = 1 };
        Assert.True(PvpEventsGump.TryNavigate(view, 10, out var changedEvent));
        Assert.Equal(new PvpEventsGump.Navigation(EventKind.Extraction, StatsScope.Lifetime, 0), changedEvent);
        Assert.True(PvpEventsGump.TryNavigate(view, 20, out var changedScope));
        Assert.Equal(new PvpEventsGump.Navigation(EventKind.TownStruggle, StatsScope.PreviousEvent, 0), changedScope);
        Assert.True(PvpEventsGump.TryNavigate(view, 1, out var refresh));
        Assert.Equal(1, refresh.Page);
        Assert.True(PvpEventsGump.TryNavigate(view, 2, out var mine));
        Assert.Equal(2, mine.Page);
        Assert.False(PvpEventsGump.TryNavigate(view, 9999, out _));
        Assert.False(PvpEventsGump.TryNavigate(view with { Page = 0 }, 3, out _));
        Assert.False(PvpEventsGump.TryNavigate(view with { Page = 2 }, 4, out _));
    }

    [Fact]
    public void OwnRowIsHighlightedByStableSerialAndOwnStatsAreVisibleOnOtherPages()
    {
        var view = Snapshot(EventKind.TownStruggle, StatsScope.Lifetime) with { Page = 2 };
        var board = Render(view);
        Assert.Contains($"croppedtext 28 266 30 24 {PvpEventsGump.OwnRowHue}", board.Layout);
        Assert.Contains(board.Strings, value => value.Contains("YOUR STATS: Rank 17"));
        board = Render(view with { Page = 0 });
        Assert.Contains($"croppedtext 28 266 30 24 {CustomGumpStyle.TextHue}", board.Layout);
        Assert.Contains(board.Strings, value => value.Contains("YOUR STATS: Rank 17"));
    }

    [Fact]
    public void CaptureMapsRecordedValuesWithoutReinterpretingCreditsAndUsesQuarterLabel()
    {
        PvpEventHistory.Record(new PvpEventResult(EventKind.TownStruggle, 55, 1, Utc(10), "Galven", 400, 300,
            [new PvpEventScore(42, "Recorded Name", "Guild", "Order", 35.5, 4, 12, 3, 1, 1, 1, 12345, "Won")]));
        var view = PvpEventsGump.Capture(42, EventKind.TownStruggle, StatsScope.PreviousEvent, 5, Utc(10));
        Assert.True(view.Finalized);
        Assert.Equal(400, view.OrderTotal);
        Assert.Equal(12, view.OwnRow.Value.KillAssists);
        Assert.Equal(35.5, view.OwnRow.Value.Points);
        Assert.Equal(12345, view.OwnRow.Value.Healing);
        Assert.Equal("Recorded Name", view.Rows[0].Name);
        Assert.Equal("Guild", view.Rows[0].Guild);
        Assert.Equal(0, view.Page);
        var season = PvpEventsGump.Capture(42, EventKind.TownStruggle, StatsScope.Seasonal, 0, Utc(10));
        Assert.Equal("2026 Q4 - UTC CALENDAR QUARTER", season.PeriodLabel);
        Assert.Null(season.OrderTotal);
        Assert.Null(season.Venue);
        Assert.Null(season.FinishedUtc);
    }

    [Fact]
    public void ResponseIsBoundToItsViewerAndSingleUse()
    {
        var viewer = NewPlayer();
        var stranger = NewPlayer();
        using var state = PacketTestUtilities.CreateTestNetState();
        using var otherState = PacketTestUtilities.CreateTestNetState();
        try
        {
            viewer.NetState = state;
            state.Mobile = viewer;
            stranger.NetState = otherState;
            otherState.Mobile = stranger;
            PvpEventsGump.DisplayTo(viewer);
            var original = viewer.FindGump<PvpEventsGump>();
            Assert.NotNull(original);
            var response = Reply(10);
            original.OnResponse(otherState, in response);
            Assert.Same(original, viewer.FindGump<PvpEventsGump>());
            original.OnResponse(state, in response);
            var replacement = viewer.FindGump<PvpEventsGump>();
            Assert.NotSame(original, replacement);
            original.OnResponse(state, in response);
            Assert.Same(replacement, viewer.FindGump<PvpEventsGump>());
        }
        finally
        {
            viewer.NetState = null;
            stranger.NetState = null;
            state.Mobile = null;
            otherState.Mobile = null;
            viewer.Delete();
            stranger.Delete();
        }
    }

    private static PlayerMobile NewPlayer()
    {
        var player = new PlayerMobile(World.NewMobile);
        player.DefaultMobileInit();
        player.Player = true;
        return player;
    }

    private static DateTime Utc(int month) => new(2026, month, 13, 12, 0, 0, DateTimeKind.Utc);
    private static RelayInfo Reply(int id) => new(id, ReadOnlySpan<int>.Empty, ReadOnlySpan<ushort>.Empty,
        ReadOnlySpan<Range>.Empty, ReadOnlySpan<byte>.Empty);

    private static PvpEventsGump.View Snapshot(EventKind kind, StatsScope scope)
    {
        var rows = ImmutableArray.CreateBuilder<PvpEventsGump.Row>(17);
        for (var i = 1; i <= 17; i++)
        {
            rows.Add(new PvpEventsGump.Row(i, (uint)i, $"Player {i}", $"Guild {i}", i % 2 == 0 ? "Chaos" : "Order",
                100 - i + 0.5, 4, 12, 3, 1, 2, 1, 3456, "Extracted"));
        }
        var values = rows.MoveToImmutable();
        return new PvpEventsGump.View(kind, scope, "TEST PERIOD", "Galven", Utc(10), true, true,
            7654321, 8765432, values, values[16], true, 0);
    }

    private static (string Layout, List<string> Strings) Render(PvpEventsGump.View view)
    {
        var builder = new DynamicGumpBuilder();
        try
        {
            PvpEventsGump.BuildBoard(ref builder, view);
            var strings = new List<string>();
            var bytes = builder.StringsData;
            while (!bytes.IsEmpty)
            {
                var length = BinaryPrimitives.ReadUInt16BigEndian(bytes) * 2;
                strings.Add(Encoding.BigEndianUnicode.GetString(bytes.Slice(2, length)));
                bytes = bytes[(length + 2)..];
            }
            return (Encoding.ASCII.GetString(builder.LayoutData), strings);
        }
        finally { builder.Dispose(); }
    }

    private static bool HasButton(string layout, int id) => layout.Contains($" 1 0 {id} }}", StringComparison.Ordinal);
    private static int Number(Match match, int group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

    // Installed unifont1.mul printable-ASCII advances: signed bearing + glyph width + one pixel; space is eight.
    // ClassicUO uses Unicode font 1 for HTML size 4 and current-client cropped text.
    private static int NativeFontWidth(string text)
    {
        ReadOnlySpan<byte> advances =
        [
            8, 3, 4, 12, 9, 10, 11, 3, 4, 4, 10, 7, 3, 6, 3, 9, 8, 4, 8, 8, 8, 8, 8, 8,
            8, 8, 3, 3, 8, 6, 8, 7, 12, 8, 8, 8, 8, 7, 7, 8, 8, 3, 8, 8, 7, 10, 8, 8,
            8, 9, 8, 8, 7, 8, 8, 12, 8, 9, 8, 4, 9, 5, 10, 8, 3, 6, 6, 6, 6, 6, 6, 6,
            6, 3, 6, 6, 3, 9, 6, 6, 6, 6, 6, 6, 6, 6, 6, 8, 6, 6, 6, 5, 2, 5, 6
        ];
        var width = 0;
        foreach (var character in text)
        {
            Assert.InRange(character, ' ', '~');
            width += advances[character - ' '];
        }
        return width;
    }

    private static void AssertInside(int x, int y, int width, int height)
    {
        Assert.InRange(x, CustomGumpStyle.ContentPadding, PvpEventsGump.PanelWidth - CustomGumpStyle.ContentPadding - width);
        Assert.InRange(y, CustomGumpStyle.ContentPadding, PvpEventsGump.PanelHeight - CustomGumpStyle.ContentPadding - height);
    }
}
