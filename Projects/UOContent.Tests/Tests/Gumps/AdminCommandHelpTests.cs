using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Server;
using Server.Accounting;
using Server.Compression;
using Server.Gumps;
using Server.Mobiles;
using Server.Misc;
using Server.Network;
using Server.Saves;
using Server.Tests.Network;
using Xunit;

namespace UOContent.Tests.Gumps;

[Collection("Sequential UOContent Tests")]
public sealed class AdminCommandHelpTests : IDisposable
{
    private readonly List<PlayerMobile> _players = [];
    private readonly List<NetState> _states = [];

    public void Dispose()
    {
        foreach (var player in _players)
        {
            player.NetState = null;
            player.Delete();
        }
        foreach (var state in _states)
        {
            state.Mobile = null;
            state.Dispose();
        }
    }

    [Fact]
    public void MetadataCoversExactlyEveryDisplayedCommandAndFourCategories()
    {
        var expected = new[] { 0, 1, 2, 3, 101, 104, 106, 112,
            200, 201, 202, 203, 204, 205, 210, 211, 300, 301, 302, 303, 310, 311, 312, 313, 314, 315, 316,
            400, 401, 402, 403, 404, 405, 406, 407, 408, 409, 412, 413, 414, 415, 416, 417, 418, 419,
            500, 501, 502, 503, 504, 510 };
        Assert.Equal(expected, AdminCommandHelp.Entries.Select(entry => entry.Index).Order().ToArray());
        foreach (var entry in AdminCommandHelp.Entries)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Title));
            Assert.False(string.IsNullOrWhiteSpace(entry.Command));
            Assert.False(string.IsNullOrWhiteSpace(entry.Behavior));
            Assert.False(string.IsNullOrWhiteSpace(entry.Scope));
            Assert.False(string.IsNullOrWhiteSpace(entry.Caution));
            Assert.True(AdminCommandHelp.ActionButton(entry.Index) < AdminCommandHelp.FirstInfoButton);
            Assert.True(AdminCommandHelp.InfoButton(entry.Index) >= AdminCommandHelp.FirstInfoButton);
        }
        Assert.False(AdminCommandHelp.TryGet(410, out _));
        Assert.False(AdminCommandHelp.TryGet(411, out _));
    }

    [Theory]
    [InlineData(AdminGumpPage.Administer, 4)]
    [InlineData(AdminGumpPage.Administer_WorldBuilding, 8)]
    [InlineData(AdminGumpPage.Administer_Server, 12)]
    [InlineData(AdminGumpPage.Administer_Access, 15)]
    [InlineData(AdminGumpPage.Administer_Access_Lockdown, 10)]
    [InlineData(AdminGumpPage.Administer_Commands, 22)]
    public void EveryDisplayedCommandHasDistinctInfoButtonAndOriginalActionId(AdminGumpPage page, int count)
    {
        var (player, _) = Player();
        var gump = new AdminGump(player, page);
        var buttons = gump.Entries.OfType<GumpButton>().ToArray();
        var infos = buttons.Where(button => button.ButtonID >= AdminCommandHelp.FirstInfoButton).ToArray();
        Assert.Equal(count, infos.Length);
        Assert.Equal(count, infos.Select(button => button.ButtonID).Distinct().Count());
        foreach (var info in infos)
        {
            var index = info.ButtonID - AdminCommandHelp.FirstInfoButton;
            Assert.True(AdminCommandHelp.TryGet(index, out var entry));
            Assert.True(AdminCommandHelp.CanRead(player, page, entry));
            var action = Assert.Single(buttons, button => button.ButtonID == AdminCommandHelp.ActionButton(index));
            Assert.Equal(gump.GetButtonID(3, index), action.ButtonID);
            Assert.Equal(0x4B9, info.NormalID);
            Assert.Equal(0x4BA, info.PressedID);
            Assert.True(info.X > action.X + 30);
            Assert.InRange(info.X + 14, 0, 650);
            Assert.InRange(info.Y + 14, 0, 470);
        }
        var packet = Read(gump);
        AssertFrame(packet.Layout, 4);
        Assert.Contains("{ gumppic 664 484 60009 }", packet.Layout);
        Assert.DoesNotContain("resizepic 0 0 5054", packet.Layout);
    }

    [Fact]
    public void EveryInfoReplyOpensReadOnlyHelpWithoutExecutingCommandsOrChangingState()
    {
        var (player, state) = Player();
        var dispatched = 0;
        void OnCommand(CommandEventArgs _) => dispatched++;
        EventSink.Command += OnCommand;
        var access = player.AccessLevel;
        var location = player.Location;
        var lockdown = AccountHandler.LockdownLevel;
        var savesEnabled = AutoSave.SavesEnabled;
        var items = World.Items.Count;
        var mobiles = World.Mobiles.Count;
        try
        {
            foreach (var entry in AdminCommandHelp.Entries)
            {
                var page = entry.Index < 4 ? AdminGumpPage.Administer : entry.Page;
                var admin = new AdminGump(player, page);
                admin.OnResponse(state, Reply(AdminCommandHelp.InfoButton(entry.Index)));
                var help = Assert.IsType<AdminCommandHelpGump>(player.FindGump<AdminCommandHelpGump>());
                Assert.Equal(entry.Index, help.CommandIndex);
                Assert.Equal(page, help.ReturnPage);
                Assert.Null(player.Target);
                var packet = Read(help);
                AssertFrame(packet.Layout);
                Assert.Contains("{ htmlgump 32 116 576 344", packet.Layout);
                Assert.Contains(" 0 1 }", packet.Layout);
                Assert.Contains(packet.Strings, value => value.Contains("Read-only help", StringComparison.Ordinal));
                var replyIds = Regex.Matches(packet.Layout, @"\{ button \d+ \d+ \d+ \d+ 1 0 (\d+) \}")
                    .Select(match => int.Parse(match.Groups[1].Value)).Order().ToArray();
                Assert.Equal(new[] { 0, 1 }, replyIds);
                player.CloseGump<AdminCommandHelpGump>();
            }
            Assert.Equal(0, dispatched);
            Assert.Equal(access, player.AccessLevel);
            Assert.Equal(location, player.Location);
            Assert.Equal(lockdown, AccountHandler.LockdownLevel);
            Assert.Equal(savesEnabled, AutoSave.SavesEnabled);
            Assert.Equal(items, World.Items.Count);
            Assert.Equal(mobiles, World.Mobiles.Count);
        }
        finally
        {
            EventSink.Command -= OnCommand;
        }
    }

    [Theory]
    [InlineData(AccessLevel.Administrator, 314, false)]
    [InlineData(AccessLevel.Developer, 314, true)]
    [InlineData(AccessLevel.Developer, 315, false)]
    [InlineData(AccessLevel.Developer, 316, false)]
    [InlineData(AccessLevel.Owner, 315, true)]
    [InlineData(AccessLevel.Owner, 316, true)]
    [InlineData(AccessLevel.GameMaster, 300, false)]
    public void HelpMatchesAdministratorAndHigherMakePermissions(AccessLevel access, int index, bool allowed)
    {
        var (player, state) = Player(access);
        var gump = new AdminGump(player, AdminGumpPage.Administer_Access);
        gump.OnResponse(state, Reply(AdminCommandHelp.InfoButton(index)));
        Assert.Equal(allowed, player.FindGump<AdminCommandHelpGump>() != null);
        Assert.Null(player.Target);
        Assert.Equal(access, player.AccessLevel);
        if (allowed)
        {
            Assert.Contains(gump.Entries.OfType<GumpButton>(), button => button.ButtonID == AdminCommandHelp.InfoButton(index));
        }
    }

    [Fact]
    public void ForgedWrongPageUnknownAndOtherSenderRepliesNeverFallThroughToActions()
    {
        var (player, state) = Player();
        var (_, otherState) = Player();
        var admin = new AdminGump(player, AdminGumpPage.Administer_Commands);
        foreach (var id in new[] { AdminCommandHelp.InfoButton(200), AdminCommandHelp.InfoButton(410), AdminCommandHelp.InfoButton(999), int.MaxValue })
        {
            admin.OnResponse(state, Reply(id));
            Assert.Null(player.FindGump<AdminCommandHelpGump>());
        }
        admin.OnResponse(otherState, Reply(AdminCommandHelp.InfoButton(417)));
        Assert.Null(player.FindGump<AdminCommandHelpGump>());
        Assert.Null(player.Target);
        AdminCommandHelpGump.DisplayTo(player, 999, AdminGumpPage.Administer_Commands);
        Assert.Null(player.FindGump<AdminCommandHelpGump>());
    }

    [Theory]
    [InlineData(AdminGumpPage.Administer_WorldBuilding, 112)]
    [InlineData(AdminGumpPage.Administer_Server, 205)]
    [InlineData(AdminGumpPage.Administer_Access, 300)]
    [InlineData(AdminGumpPage.Administer_Access_Lockdown, 510)]
    [InlineData(AdminGumpPage.Administer_Commands, 417)]
    public void BackReturnsToExactSourcePageAndNeverRunsTheExplainedAction(AdminGumpPage page, int index)
    {
        var (player, state) = Player();
        AdminCommandHelpGump.DisplayTo(player, index, page);
        var help = player.FindGump<AdminCommandHelpGump>();
        help.OnResponse(state, Reply(AdminCommandHelp.ActionButton(index)));
        Assert.Null(player.FindGump<AdminGump>());
        help.OnResponse(state, Reply(1));
        Assert.Equal(page, player.FindGump<AdminGump>().CurrentPage);
        Assert.Null(player.Target);
    }

    [Fact]
    public void BroadcastDraftSurvivesInfoAndBackAndSyntaxIsHtmlEncoded()
    {
        var (player, state) = Player();
        const string draft = "Please log out in five minutes.";
        var bytes = Encoding.BigEndianUnicode.GetBytes(draft);
        var reply = new RelayInfo(AdminCommandHelp.InfoButton(210), ReadOnlySpan<int>.Empty, new ushort[] { 0 },
            new[] { new Range(0, bytes.Length) }, bytes);
        var admin = new AdminGump(player, AdminGumpPage.Administer_Server);
        admin.OnResponse(state, reply);
        var help = player.FindGump<AdminCommandHelpGump>();
        Assert.Contains(Read(help).Strings, text => text.Contains("BC &lt;broadcast text&gt;", StringComparison.Ordinal));
        help.OnResponse(state, Reply(1));
        var restored = player.FindGump<AdminGump>();
        Assert.Equal(draft, restored.BroadcastDraft);
        Assert.Contains(draft, Read(restored).Strings);
    }

    [Fact]
    public void DemotionAndOtherSenderPreventBackAndDirectHelpDisplay()
    {
        var (player, state) = Player();
        var (_, otherState) = Player();
        AdminCommandHelpGump.DisplayTo(player, 316, AdminGumpPage.Administer_Access);
        var help = player.FindGump<AdminCommandHelpGump>();
        help.OnResponse(otherState, Reply(1));
        Assert.Null(player.FindGump<AdminGump>());
        player.AccessLevel = AccessLevel.Administrator;
        help.OnResponse(state, Reply(1));
        Assert.Null(player.FindGump<AdminGump>());
        player.CloseGump<AdminCommandHelpGump>();
        AdminCommandHelpGump.DisplayTo(player, 316, AdminGumpPage.Administer_Access);
        Assert.Null(player.FindGump<AdminCommandHelpGump>());
    }

    [Theory]
    [InlineData(102)]
    [InlineData(105)]
    [InlineData(107)]
    [InlineData(108)]
    [InlineData(109)]
    [InlineData(110)]
    [InlineData(111)]
    [InlineData(113)]
    [InlineData(114)]
    public void RemovedWorldBuildingControlsHaveNoButtonsHelpOrExecutableReplies(int index)
    {
        var (player, state) = Player();
        var admin = new AdminGump(player, AdminGumpPage.Administer_WorldBuilding);
        Assert.False(AdminCommandHelp.TryGet(index, out _));
        Assert.DoesNotContain(admin.Entries.OfType<GumpButton>(), button =>
            button.ButtonID == AdminCommandHelp.ActionButton(index) || button.ButtonID == AdminCommandHelp.InfoButton(index));
        var dispatched = 0;
        void OnCommand(CommandEventArgs _) => dispatched++;
        EventSink.Command += OnCommand;
        var items = World.Items.Count;
        var mobiles = World.Mobiles.Count;
        try
        {
            admin.OnResponse(state, Reply(AdminCommandHelp.ActionButton(index)));
            admin.OnResponse(state, Reply(AdminCommandHelp.InfoButton(index)));
            AdminCommandHelpGump.DisplayTo(player, index, AdminGumpPage.Administer_WorldBuilding);
            Assert.Null(player.FindGump<AdminCommandHelpGump>());
            Assert.Null(player.Target);
            Assert.Equal(0, dispatched);
            Assert.Equal(items, World.Items.Count);
            Assert.Equal(mobiles, World.Mobiles.Count);
        }
        finally
        {
            EventSink.Command -= OnCommand;
        }
    }

    private (PlayerMobile, NetState) Player(AccessLevel access = AccessLevel.Owner)
    {
        var state = PacketTestUtilities.CreateTestNetState();
        _states.Add(state);
        var player = new PlayerMobile { AccessLevel = access, Player = true, Body = 0x190 };
        _players.Add(player);
        player.MoveToWorld(new Point3D(100, 100, 0), Map.Felucca);
        player.NetState = state;
        state.Mobile = player;
        return (player, state);
    }

    private static RelayInfo Reply(int buttonId) => new(buttonId, ReadOnlySpan<int>.Empty,
        ReadOnlySpan<ushort>.Empty, ReadOnlySpan<Range>.Empty, ReadOnlySpan<byte>.Empty);

    private static void AssertFrame(string layout, int alphaCount = 3)
    {
        for (var id = 60002; id <= 60009; id++)
        {
            Assert.Contains($" {id} }}", layout);
        }
        Assert.Contains(" 2624 }", layout);
        Assert.Equal(alphaCount, Regex.Matches(layout, "checkertrans").Count);
    }

    private static (string Layout, string[] Strings) Read(BaseGump gump)
    {
        var writer = new SpanWriter(new byte[0x10000]);
        try
        {
            gump.Compile(ref writer);
            ReadOnlySpan<byte> remaining = writer.Span[19..];
            var layout = Encoding.ASCII.GetString(ReadPacked(ref remaining));
            remaining = remaining[4..];
            ReadOnlySpan<byte> stringsData = ReadPacked(ref remaining);
            var strings = new List<string>();
            while (!stringsData.IsEmpty)
            {
                var length = BinaryPrimitives.ReadUInt16BigEndian(stringsData) * 2;
                strings.Add(Encoding.BigEndianUnicode.GetString(stringsData.Slice(2, length)));
                stringsData = stringsData[(length + 2)..];
            }
            return (layout, strings.ToArray());
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static byte[] ReadPacked(ref ReadOnlySpan<byte> data)
    {
        var packedLength = BinaryPrimitives.ReadInt32BigEndian(data);
        var unpackedLength = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
        var result = new byte[unpackedLength];
        Assert.Equal(LibDeflateResult.Success, Deflate.Standard.Unpack(result, data.Slice(8, packedLength - 4), out var written));
        Assert.Equal(unpackedLength, written);
        data = data[(packedLength + 4)..];
        return result;
    }
}
