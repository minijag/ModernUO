using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Server.Compression;
using Server.Engines.Custom;
using Server.Gumps;
using Server.Mobiles;
using Server.Network;
using Server.Tests.Network;
using Xunit;

namespace Server.Tests.Gumps;

[Collection("Sequential UOContent Tests")]
public class LeaderboardGumpTests : IDisposable
{
    private readonly List<PlayerMobile> _players = [];
    private readonly List<NetState> _states = [];

    public void Dispose()
    {
        foreach (var player in _players)
        {
            player.CloseGump<LeaderboardGump>();
            player.NetState = null;
            player.Delete();
        }

        foreach (var state in _states)
        {
            state.Dispose();
        }
    }

    [Theory]
    [InlineData(1, LeaderboardStat.OreMined, "Ore Mined")]
    [InlineData(2, LeaderboardStat.LumberHarvested, "Lumber Chopped")]
    [InlineData(3, LeaderboardStat.LeatherGathered, "Leather Skinned")]
    [InlineData(4, LeaderboardStat.TreasureMapsCompleted, "Treasure Maps Completed")]
    [InlineData(5, LeaderboardStat.GoldFarmed, "Gold Farmed")]
    [InlineData(6, LeaderboardStat.MonstersKilled, "Monsters Killed")]
    public void EveryVisibleCategoryHasItsOwnSelectionAndStandardTheme(int buttonId, LeaderboardStat stat, string name)
    {
        Assert.Equal(stat, LeaderboardGump.GetStatFromButton(buttonId));
        Assert.Equal(name, LeaderboardGump.GetName(stat));
        var board = Render(stat, new LeaderboardPage([], 0, 1, 0, null, 0));

        for (var i = 1; i <= 6; i++)
        {
            var idleArt = i == buttonId ? CustomGumpStyle.OptionButtonSelectedGumpId : CustomGumpStyle.OptionButtonNormalGumpId;
            Assert.Contains($"button 28 {103 + (i - 1) * 50} {idleArt} {CustomGumpStyle.OptionButtonPressedGumpId} 1 0 {i}", board.Layout);
            Assert.Contains(board.Strings, text => text.Contains(LeaderboardGump.GetName(LeaderboardGump.GetStatFromButton(i).Value)));
        }

        Assert.Contains(board.Strings, text => text.Contains($"COLOR=#F4D27A SIZE=4>{name}"));
        Assert.Contains(board.Strings, text => text.Contains("YOUR TOTAL: 0"));
        Assert.Contains(board.Strings, text => text.Contains("No totals recorded yet."));
        Assert.Contains(board.Strings, text => text.Contains("SHOW MY RANKING"));
        Assert.DoesNotContain(board.Strings, text => text.Contains("CRAFTING"));
        Assert.All(board.Strings, text => Assert.Contains("SIZE=4", text));
        Assert.Contains("gumppic 680 560 60009", board.Layout);
        Assert.Contains("button 560 526 9721 2151 1 0 0", board.Layout);
        Assert.Contains("htmlgump 406 449 158 24", board.Layout);
        Assert.Contains("htmlgump 278 490 250 24", board.Layout);
        Assert.Contains("htmlgump 532 490 140 24", board.Layout);
        Assert.Contains($"button 278 526 9721 2151 1 0 {LeaderboardGump.ShowMyRankingButton}", board.Layout);
        for (var shield = 64032; shield <= 64037; shield++)
        {
            Assert.DoesNotContain(shield.ToString(), board.Layout);
        }
    }

    [Theory]
    [InlineData(LeaderboardStat.OreMined, 0x19B8)]
    [InlineData(LeaderboardStat.LumberHarvested, 0x1BDD)]
    [InlineData(LeaderboardStat.LeatherGathered, 0x1081)]
    [InlineData(LeaderboardStat.TreasureMapsCompleted, 0x14EB)]
    [InlineData(LeaderboardStat.GoldFarmed, 0xEEF)]
    [InlineData(LeaderboardStat.MonstersKilled, 0x2203)]
    public void NativeCategoryIconAppearsInSidebarAndSelectedHeader(LeaderboardStat stat, int itemId)
    {
        Assert.Equal(itemId, LeaderboardGump.GetIconItemId(stat));
        var board = Render(stat, new LeaderboardPage([], 0, 1, 0, null, 0));

        Assert.Equal(2, board.Layout.Split($" {itemId} }}").Length - 1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(7)]
    [InlineData(100)]
    [InlineData(101)]
    [InlineData(102)]
    [InlineData(int.MaxValue)]
    public void NoncategoryButtonsHaveNoCategorySelection(int buttonId)
    {
        Assert.Null(LeaderboardGump.GetStatFromButton(buttonId));
    }

    [Theory]
    [InlineData(LeaderboardStat.MonsterGoldGenerated)]
    [InlineData(LeaderboardStat.BoardsUsed)]
    [InlineData(LeaderboardStat.LeatherUsed)]
    [InlineData(LeaderboardStat.IngotsUsed)]
    [InlineData((LeaderboardStat)100)]
    public void StoredAndUnknownCountersAreNotVisibleCategories(LeaderboardStat stat)
    {
        Assert.Null(LeaderboardGump.GetName(stat));
    }

    [Fact]
    public void BoardShowsTenRowsWithGlobalRanksAndEscapedNames()
    {
        var rows = new LeaderboardEntry[11];
        for (var i = 0; i < rows.Length; i++)
        {
            rows[i] = new LeaderboardEntry($"Player {i}", i + 1);
        }

        rows[0] = new LeaderboardEntry("<BASEFONT COLOR=red>Player</BASEFONT>", 321);
        rows[10] = new LeaderboardEntry("Eleventh player", 999);
        var board = Render(LeaderboardStat.GoldFarmed, new LeaderboardPage(rows, 2, 4, 35, null, 456));

        Assert.Contains(board.Strings, text => text.Contains("&lt;BASEFONT COLOR=red&gt;Player&lt;/BASEFONT&gt;"));
        Assert.Contains(board.Strings, text => text.Contains("COLOR=#FFFFFF SIZE=4>YOUR TOTAL: 456"));
        Assert.DoesNotContain(board.Strings, text => text.Contains("Eleventh player"));
        Assert.Contains(board.Strings, text => text.Contains("SIZE=4>21<"));
        Assert.Contains(board.Strings, text => text.Contains("SIZE=4>30<"));
        Assert.Contains("htmlgump 278 409 56 24", board.Layout);
        Assert.DoesNotContain("htmlgump 278 438 56 24", board.Layout);
    }

    [Fact]
    public void ViewerRowIsHighlightedWithoutColoringOtherRows()
    {
        LeaderboardEntry[] rows = [new("Other player", 500), new("Viewer player", 456)];
        var board = Render(LeaderboardStat.GoldFarmed, new LeaderboardPage(rows, 1, 2, 12, 12, 456));

        Assert.Contains(board.Strings, text => text.Contains("COLOR=#FFFFFF SIZE=4>Other player"));
        Assert.Contains(board.Strings, text => text.Contains("COLOR=#F4D27A SIZE=4>Viewer player"));
        Assert.Contains(board.Strings, text => text.Contains("COLOR=#F4D27A SIZE=4>12<"));
        Assert.Contains(board.Strings, text => text.Contains("YOUR RANK: 12"));
    }

    [Theory]
    [InlineData(0, 1, false, false)]
    [InlineData(0, 3, false, true)]
    [InlineData(1, 3, true, true)]
    [InlineData(2, 3, true, false)]
    public void PagingButtonsArePresentOnlyWithinBounds(int pageIndex, int pageCount, bool previous, bool next)
    {
        var board = Render(LeaderboardStat.OreMined, new LeaderboardPage([], pageIndex, pageCount, 25, null, 0));

        Assert.Equal(previous, HasButton(board.Layout, LeaderboardGump.PreviousPageButton));
        Assert.Equal(next, HasButton(board.Layout, LeaderboardGump.NextPageButton));
        Assert.Equal(previous, board.Strings.Exists(text => text.Contains("PREVIOUS", StringComparison.Ordinal)));
        Assert.Equal(next, board.Strings.Exists(text => text.Contains("NEXT", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(1, LeaderboardStat.OreMined)]
    [InlineData(2, LeaderboardStat.LumberHarvested)]
    [InlineData(3, LeaderboardStat.LeatherGathered)]
    [InlineData(4, LeaderboardStat.TreasureMapsCompleted)]
    [InlineData(5, LeaderboardStat.GoldFarmed)]
    [InlineData(6, LeaderboardStat.MonstersKilled)]
    public void AllClickableControlsUseNativeDiamondsAndOnlyCurrentCategoryRemainsChecked(int selectedId, LeaderboardStat stat)
    {
        var board = Render(stat, new LeaderboardPage([], 1, 3, 25, 15, 123));
        var buttons = Regex.Matches(board.Layout, @"\{ button (\d+) (\d+) (\d+) (\d+) 1 0 (\d+) \}");
        Assert.Equal(10, buttons.Count);
        var checkedCount = 0;
        var ids = new HashSet<int>();
        foreach (Match button in buttons)
        {
            var x = int.Parse(button.Groups[1].Value);
            var y = int.Parse(button.Groups[2].Value);
            var idle = int.Parse(button.Groups[3].Value);
            var pressed = int.Parse(button.Groups[4].Value);
            var id = int.Parse(button.Groups[5].Value);
            Assert.True(ids.Add(id));
            Assert.Equal(2151, pressed);
            Assert.Equal(id == selectedId ? 2154 : 9721, idle);
            if (idle == 2154)
            {
                checkedCount++;
            }
            Assert.InRange(x, CustomGumpStyle.ContentPadding, 700 - CustomGumpStyle.ContentPadding - 29);
            Assert.InRange(y, CustomGumpStyle.ContentPadding, 580 - CustomGumpStyle.ContentPadding - 29);
        }
        Assert.Equal(1, checkedCount);
        Assert.True(ids.SetEquals(new[] { 0, 1, 2, 3, 4, 5, 6, 100, 101, 102 }));
        Assert.Equal(9721, CustomGumpStyle.OptionButtonNormalGumpId);
        Assert.Equal(2151, CustomGumpStyle.OptionButtonPressedGumpId);
        Assert.Equal(2154, CustomGumpStyle.OptionButtonSelectedGumpId);
        Assert.Equal(29, CustomGumpStyle.OptionButtonSize);
        Assert.DoesNotContain(" 4005 ", board.Layout);
        Assert.DoesNotContain(" 4023 ", board.Layout);
        Assert.Contains("{ button 278 446 9721 2151 1 0 100 }", board.Layout);
        Assert.Contains("{ htmlgump 315 449 90 24", board.Layout);
        Assert.Contains("{ button 576 446 9721 2151 1 0 101 }", board.Layout);
        Assert.Contains("{ htmlgump 612 449 60 24", board.Layout);
    }

    [Fact]
    public void DisplayToSkipsDisconnectedDeletedPlayersAndHiddenCategories()
    {
        LeaderboardGump.DisplayTo(null);
        var player = CreatePlayer();
        LeaderboardGump.DisplayTo(player, LeaderboardStat.BoardsUsed);
        Assert.False(player.HasGump<LeaderboardGump>());

        var state = player.NetState;
        player.NetState = null;
        LeaderboardGump.DisplayTo(player);
        Assert.False(state.HasGump<LeaderboardGump>());
        player.NetState = state;

        player.Delete();
        LeaderboardGump.DisplayTo(player);
        Assert.False(state.HasGump<LeaderboardGump>());
    }

    [Fact]
    public void CloseAndInvalidRepliesDoNotReopenTheBoard()
    {
        var player = CreatePlayer();
        LeaderboardGump.DisplayTo(player);
        var gump = player.FindGump<LeaderboardGump>();
        Assert.NotNull(gump);
        player.CloseGump<LeaderboardGump>();

        foreach (var buttonId in new[] { 0, -1, 7, int.MaxValue })
        {
            gump.OnResponse(player.NetState, Reply(buttonId));
            Assert.False(player.HasGump<LeaderboardGump>());
        }
    }

    [Fact]
    public void NavigationReachesAllEntriesAndUsesGlobalRanks()
    {
        var owner = CreatePlayer();
        PopulateRanking(owner, LeaderboardStat.OreMined, 25);
        LeaderboardGump.DisplayTo(owner);
        var first = ReadOpenBoard(owner);
        Assert.Contains(first.Strings, text => text.Contains("Ranked player 0"));
        Assert.DoesNotContain(first.Strings, text => text.Contains("Ranked player 10"));
        Assert.Contains(first.Strings, text => text.Contains("SIZE=4>1<"));
        Assert.Contains(first.Strings, text => text.Contains("PAGE 1 / 3"));

        owner.FindGump<LeaderboardGump>().OnResponse(owner.NetState, Reply(LeaderboardGump.NextPageButton));
        var second = ReadOpenBoard(owner);
        Assert.Contains(second.Strings, text => text.Contains("Ranked player 10"));
        Assert.DoesNotContain(second.Strings, text => text.Contains("Ranked player 0"));
        Assert.Contains(second.Strings, text => text.Contains("SIZE=4>11<"));
        Assert.Contains(second.Strings, text => text.Contains("PAGE 2 / 3"));

        owner.FindGump<LeaderboardGump>().OnResponse(owner.NetState, Reply(LeaderboardGump.NextPageButton));
        var third = ReadOpenBoard(owner);
        Assert.Contains(third.Strings, text => text.Contains("SIZE=4>21<"));
        Assert.Contains(third.Strings, text => text.Contains($"SIZE=4>{owner.Name}<"));
        Assert.Contains(third.Strings, text => text.Contains("YOUR RANK: 25"));
        Assert.Contains(third.Strings, text => text.Contains("PAGE 3 / 3"));
        Assert.False(HasButton(third.Layout, LeaderboardGump.NextPageButton));

        owner.FindGump<LeaderboardGump>().OnResponse(owner.NetState, Reply(LeaderboardGump.PreviousPageButton));
        Assert.Contains(ReadOpenBoard(owner).Strings, text => text.Contains("SIZE=4>11<"));
    }

    [Theory]
    [InlineData(0, LeaderboardGump.PreviousPageButton)]
    [InlineData(2, LeaderboardGump.NextPageButton)]
    public void ForgedOutOfBoundsNavigationDoesNotReopen(int pageIndex, int buttonId)
    {
        var owner = CreatePlayer();
        PopulateRanking(owner, LeaderboardStat.OreMined, 25);
        LeaderboardGump.DisplayTo(owner, LeaderboardStat.OreMined, pageIndex);
        var gump = owner.FindGump<LeaderboardGump>();
        owner.CloseGump<LeaderboardGump>();

        gump.OnResponse(owner.NetState, Reply(buttonId));

        Assert.False(owner.HasGump<LeaderboardGump>());
    }

    [Fact]
    public void CategorySelectionAlwaysResetsToFirstPage()
    {
        var owner = CreatePlayer();
        PopulateRanking(owner, LeaderboardStat.OreMined, 25);
        PopulateRanking(owner, LeaderboardStat.LumberHarvested, 25);
        LeaderboardGump.DisplayTo(owner, LeaderboardStat.OreMined, 2);
        Assert.Contains(ReadOpenBoard(owner).Strings, text => text.Contains("SIZE=4>21<"));

        owner.FindGump<LeaderboardGump>().OnResponse(owner.NetState, Reply(2));

        var board = ReadOpenBoard(owner);
        Assert.Contains(board.Strings, text => text.Contains("COLOR=#F4D27A SIZE=4>Lumber Chopped"));
        Assert.Contains("button 28 153 2154 2151 1 0 2", board.Layout);
        Assert.Contains("button 28 103 9721 2151 1 0 1", board.Layout);
        Assert.Contains(board.Strings, text => text.Contains("SIZE=4>1<"));
        Assert.DoesNotContain(board.Strings, text => text.Contains($"SIZE=4>{owner.Name}<"));
        Assert.False(HasButton(board.Layout, LeaderboardGump.PreviousPageButton));
    }

    [Fact]
    public void ShowMyRankingJumpsToViewerOutsideFirstTenAndHighlightsTheirRow()
    {
        var owner = CreatePlayer();
        PopulateRanking(owner, LeaderboardStat.OreMined, 25);
        LeaderboardGump.DisplayTo(owner);
        Assert.DoesNotContain(ReadOpenBoard(owner).Strings, text => text.Contains($"SIZE=4>{owner.Name}<"));

        owner.FindGump<LeaderboardGump>().OnResponse(owner.NetState, Reply(LeaderboardGump.ShowMyRankingButton));

        var board = ReadOpenBoard(owner);
        Assert.Contains(board.Strings, text => text.Contains($"COLOR=#F4D27A SIZE=4>{owner.Name}<"));
        Assert.Contains(board.Strings, text => text.Contains("COLOR=#F4D27A SIZE=4>25<"));
        Assert.Contains(board.Strings, text => text.Contains("YOUR RANK: 25"));
        Assert.False(HasButton(board.Layout, LeaderboardGump.NextPageButton));
    }

    [Fact]
    public void ShowMyRankingUsesCurrentTotalsRatherThanTheOldBoardSnapshot()
    {
        var owner = CreatePlayer();
        PopulateRanking(owner, LeaderboardStat.OreMined, 25);
        LeaderboardGump.DisplayTo(owner, LeaderboardStat.OreMined, 2);
        var staleGump = owner.FindGump<LeaderboardGump>();
        owner.SetLeaderboardStat(LeaderboardStat.OreMined, 2000);

        staleGump.OnResponse(owner.NetState, Reply(LeaderboardGump.ShowMyRankingButton));

        var board = ReadOpenBoard(owner);
        Assert.Contains(board.Strings, text => text.Contains($"COLOR=#F4D27A SIZE=4>{owner.Name}<"));
        Assert.Contains(board.Strings, text => text.Contains("YOUR RANK: 1"));
        Assert.Contains(board.Strings, text => text.Contains("YOUR TOTAL: 2,000"));
        Assert.False(HasButton(board.Layout, LeaderboardGump.PreviousPageButton));
    }

    [Fact]
    public void UnrankedShowMyRankingKeepsRequestedPageAndReopensVisibleBoard()
    {
        var owner = CreatePlayer();
        PopulateRanking(owner, LeaderboardStat.OreMined, 25);
        owner.SetLeaderboardStat(LeaderboardStat.OreMined, 0);
        LeaderboardGump.DisplayTo(owner, LeaderboardStat.OreMined, 1);
        var gump = owner.FindGump<LeaderboardGump>();
        owner.CloseGump<LeaderboardGump>();

        gump.OnResponse(owner.NetState, Reply(LeaderboardGump.ShowMyRankingButton));

        var board = ReadOpenBoard(owner);
        Assert.Contains(board.Strings, text => text.Contains("YOUR TOTAL: 0"));
        Assert.Contains(board.Strings, text => text.Contains("Unranked", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(board.Strings, text => text.Contains("SIZE=4>11<"));
        Assert.Contains("gumppic 680 560 60009", board.Layout);
        Assert.True(HasButton(board.Layout, 0));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(LeaderboardGump.PreviousPageButton)]
    [InlineData(LeaderboardGump.NextPageButton)]
    [InlineData(LeaderboardGump.ShowMyRankingButton)]
    public void AllRepliesRequireTheOwnerAndCurrentConnection(int buttonId)
    {
        var owner = CreatePlayer();
        var other = CreatePlayer();
        PopulateRanking(owner, LeaderboardStat.OreMined, 25);
        var ownerState = owner.NetState;
        var otherState = other.NetState;
        LeaderboardGump.DisplayTo(owner, LeaderboardStat.OreMined, 1);
        var gump = owner.FindGump<LeaderboardGump>();
        Assert.NotNull(gump);

        gump.OnResponse(null, Reply(buttonId));
        Assert.Same(gump, owner.FindGump<LeaderboardGump>());

        gump.OnResponse(otherState, Reply(buttonId));
        Assert.Same(gump, owner.FindGump<LeaderboardGump>());
        Assert.False(other.HasGump<LeaderboardGump>());

        otherState.Mobile = owner;
        gump.OnResponse(otherState, Reply(buttonId));
        Assert.Same(gump, owner.FindGump<LeaderboardGump>());
        otherState.Mobile = other;

        ownerState.Mobile = other;
        gump.OnResponse(ownerState, Reply(buttonId));
        Assert.Same(gump, owner.FindGump<LeaderboardGump>());
        ownerState.Mobile = owner;

        owner.NetState = null;
        gump.OnResponse(ownerState, Reply(buttonId));
        Assert.Same(gump, ownerState.FindGump<LeaderboardGump>());
        owner.NetState = ownerState;

        gump.OnResponse(ownerState, Reply(buttonId));
        Assert.NotSame(gump, owner.FindGump<LeaderboardGump>());
    }

    private void PopulateRanking(PlayerMobile owner, LeaderboardStat stat, int count)
    {
        owner.SetLeaderboardStat(stat, 1);
        for (var i = 0; i < count - 1; i++)
        {
            var player = CreatePlayer(false);
            player.Name = $"Ranked player {i}";
            player.SetLeaderboardStat(stat, 1000 - i);
        }
    }

    private PlayerMobile CreatePlayer(bool connected = true)
    {
        var player = new PlayerMobile(World.NewMobile);
        player.DefaultMobileInit();
        _players.Add(player);
        player.Player = true;
        player.Name = $"Viewer {_players.Count}";
        if (connected)
        {
            var state = PacketTestUtilities.CreateTestNetState();
            _states.Add(state);
            player.NetState = state;
            state.Mobile = player;
        }

        return player;
    }

    private static RelayInfo Reply(int buttonId) =>
        new(buttonId, ReadOnlySpan<int>.Empty, ReadOnlySpan<ushort>.Empty, ReadOnlySpan<Range>.Empty, ReadOnlySpan<byte>.Empty);

    private static bool HasButton(string layout, int buttonId) => layout.Contains($" 1 0 {buttonId} }}");

    private static Board Render(LeaderboardStat stat, in LeaderboardPage page)
    {
        var builder = new DynamicGumpBuilder();
        try
        {
            LeaderboardGump.BuildBoard(ref builder, stat, in page);
            return new Board(Encoding.ASCII.GetString(builder.LayoutData), ReadStrings(builder.StringsData));
        }
        finally
        {
            builder.Dispose();
        }
    }

    private static Board ReadOpenBoard(PlayerMobile owner)
    {
        var gump = owner.FindGump<LeaderboardGump>();
        Assert.NotNull(gump);
        var writer = new SpanWriter(new byte[0x10000]);
        try
        {
            gump.Compile(ref writer);
            ReadOnlySpan<byte> packet = writer.Span[19..];
            var layout = ReadPacked(ref packet);
            Assert.True(BinaryPrimitives.ReadInt32BigEndian(packet) > 0);
            packet = packet[4..];
            var strings = ReadPacked(ref packet);
            Assert.True(packet.IsEmpty);
            return new Board(Encoding.ASCII.GetString(layout), ReadStrings(strings));
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static byte[] ReadPacked(ref ReadOnlySpan<byte> packet)
    {
        var packedLength = BinaryPrimitives.ReadInt32BigEndian(packet);
        if (packedLength == 0)
        {
            packet = packet[4..];
            return [];
        }

        var unpackedLength = BinaryPrimitives.ReadInt32BigEndian(packet[4..]);
        var data = new byte[unpackedLength];
        Assert.Equal(LibDeflateResult.Success, Deflate.Standard.Unpack(data, packet.Slice(8, packedLength - 4), out var written));
        Assert.Equal(unpackedLength, written);
        packet = packet[(packedLength + 4)..];
        return data;
    }

    private static List<string> ReadStrings(ReadOnlySpan<byte> data)
    {
        var strings = new List<string>();
        while (!data.IsEmpty)
        {
            var length = BinaryPrimitives.ReadUInt16BigEndian(data) * 2;
            strings.Add(Encoding.BigEndianUnicode.GetString(data.Slice(2, length)));
            data = data[(length + 2)..];
        }

        return strings;
    }

    private readonly record struct Board(string Layout, List<string> Strings);
}
