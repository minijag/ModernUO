using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO.Compression;
using System.Text;
using Server;
using Server.Compression;
using Server.Gumps;
using Server.Items;
using Server.Mobiles;
using Server.Network;
using Server.Spells.Seventh;
using Server.Tests.Network;
using Xunit;

namespace UOContent.Tests.Items;

[Collection("Sequential UOContent Tests")]
public class MoongateTravelConfirmationTests : IDisposable
{
    private static Point3D Source => new(100, 100, 0);
    private static Point3D Destination => new(110, 110, 0);
    private readonly List<IEntity> _entities = [];
    private readonly List<NetState> _states = [];

    [Theory]
    [InlineData("Shared")]
    [InlineData("Public")]
    [InlineData("Warning")]
    [InlineData("ConfiguredWarning")]
    public void MoongateDialogsUseSharedFrameAndBlackBacking(string kind)
    {
        var (player, _) = Player();
        BaseGump gump;
        switch (kind)
        {
            case "Public":
                var publicGate = Track(new PublicMoongate());
                publicGate.MoveToWorld(Source, Map.Felucca);
                publicGate.OnDoubleClick(player);
                gump = player.FindGump<MoongateGump>();
                break;
            case "Warning":
                var warningGate = Track(new Moongate(Destination, Map.Felucca));
                warningGate.MoveToWorld(Source, Map.Felucca);
                MoongateConfirmGump.DisplayTo(player, warningGate);
                gump = player.FindGump<MoongateConfirmGump>();
                break;
            case "ConfiguredWarning":
                var configuredGate = Track(new ConfirmationMoongate(Destination, Map.Felucca)
                {
                    GumpWidth = 420,
                    GumpHeight = 280,
                    TitleNumber = 1060635,
                    Message = "Confirm this journey."
                });
                configuredGate.MoveToWorld(Source, Map.Felucca);
                configuredGate.BeginConfirmation(player);
                gump = player.FindGump<WarningGump>();
                break;
            default:
                var gate = Track(new Teleporter(Destination, Map.Felucca) { ItemID = 0xF6C });
                gate.MoveToWorld(Source, Map.Felucca);
                gate.OnDoubleClick(player);
                gump = player.FindGump<MoongateTravelConfirmation>();
                break;
        }
        Assert.NotNull(gump);
        var writer = new SpanWriter(new byte[0x10000]);
        try
        {
            gump.Compile(ref writer);
            var packedLength = BinaryPrimitives.ReadInt32BigEndian(writer.Span[19..]);
            var layoutLength = BinaryPrimitives.ReadInt32BigEndian(writer.Span[23..]);
            var bytes = new byte[layoutLength];
            Assert.Equal(LibDeflateResult.Success,
                Deflate.Standard.Unpack(bytes, writer.Span.Slice(27, packedLength - 4), out var written));
            Assert.Equal(layoutLength, written);
            var layout = Encoding.ASCII.GetString(bytes);
            for (var art = 60002; art <= 60009; art++)
            {
                Assert.Contains($" {art} }}", layout);
            }
            Assert.Contains(" 2624 }", layout);
            Assert.Equal(3, layout.Split("checkertrans").Length - 1);
            Assert.DoesNotContain("resizepic", layout);
            Assert.Contains(" 1 0 1 }", layout);
            Assert.Contains(" 1 0 0 }", layout);
            Assert.Equal(Source, player.Location);
        }
        finally
        {
            writer.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var state in _states)
        {
            if (state.Mobile != null)
            {
                state.Mobile.NetState = null;
                state.Mobile = null;
            }
            state.Dispose();
        }
        for (var i = _entities.Count - 1; i >= 0; i--)
        {
            _entities[i].Delete();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryNonTownMoongate_StepAndDoubleClickRequirePositiveConfirmation(bool doubleClick)
    {
        var (player, state) = Player();
        var gate = Track(new Moongate(Destination, Map.Felucca));
        gate.MoveToWorld(Source, Map.Felucca);
        Assert.False(Moongate.IsInTown(Source, Map.Felucca));
        if (doubleClick)
        {
            gate.OnDoubleClick(player);
        }
        else
        {
            Assert.True(gate.OnMoveOver(player));
        }
        gate.DelayCallback(player, doubleClick ? 1 : 0);
        Assert.Equal(Source, player.Location);
        Confirm(player, state, 0);
        Assert.Equal(Source, player.Location);
        gate.DelayCallback(player, 0);
        Confirm(player, state, 1);
        Assert.Equal(Destination, player.Location);
    }

    [Theory]
    [InlineData("OwnArt", MoongateAppearance.LegacyBlue)]
    [InlineData("RuntimeArt", MoongateAppearance.LegacyBlue)]
    [InlineData("NativeArt", MoongateAppearance.LegacyBlue)]
    [InlineData("OwnArt", MoongateAppearance.Blue)]
    [InlineData("RuntimeArt", MoongateAppearance.Blue)]
    [InlineData("NativeArt", MoongateAppearance.Blue)]
    [InlineData("OwnArt", MoongateAppearance.Dark)]
    [InlineData("RuntimeArt", MoongateAppearance.Dark)]
    [InlineData("NativeArt", MoongateAppearance.Dark)]
    [InlineData("OwnArt", MoongateAppearance.Blue + 15)]
    [InlineData("RuntimeArt", MoongateAppearance.Blue + 15)]
    [InlineData("NativeArt", MoongateAppearance.Blue + 15)]
    public void MoongateLookingTeleportersRequireConfirmation(string appearance, int itemId)
    {
        var (player, state) = Player();
        var gate = Track(new Teleporter(Destination, Map.Felucca) { ItemID = appearance == "OwnArt" ? itemId : 0x1BC3 });
        gate.MoveToWorld(Source, Map.Felucca);
        var tiles = Map.Felucca.Tiles;
        var oldBlock = tiles.GetStaticBlock(Source.X >> 3, Source.Y >> 3);
        if (appearance == "RuntimeArt")
        {
            Track(new Static(itemId)).MoveToWorld(Source, Map.Felucca);
        }
        if (appearance == "NativeArt")
        {
            var block = (StaticTile[][][])oldBlock.Clone();
            block[Source.X & 7] = (StaticTile[][])block[Source.X & 7].Clone();
            block[Source.X & 7][Source.Y & 7] = [.. block[Source.X & 7][Source.Y & 7], new StaticTile((ushort)itemId, 0)];
            tiles.SetStaticBlock(Source.X >> 3, Source.Y >> 3, block);
        }
        try
        {
            Assert.True(gate.HasMoongateAppearance());
            Assert.False(gate.OnMoveOver(player));
            Assert.Equal(Source, player.Location);
            Confirm(player, state, 1);
            Assert.Equal(Destination, player.Location);
        }
        finally
        {
            if (appearance == "NativeArt")
            {
                tiles.SetStaticBlock(Source.X >> 3, Source.Y >> 3, oldBlock);
            }
        }
    }

    [Fact]
    public void OrdinaryPadStillTravelsImmediately_MoongateDoubleClickAndDirectTravelWait()
    {
        var (player, state) = Player();
        var gate = Track(new Teleporter(Destination, Map.Felucca));
        gate.MoveToWorld(Source, Map.Felucca);
        Assert.False(gate.HasMoongateAppearance());
        gate.OnMoveOver(player);
        Assert.Equal(Destination, player.Location);
        Assert.Null(player.FindGump<MoongateTravelConfirmation>());
        player.MoveToWorld(Source, Map.Felucca);
        gate.ItemID = 0xF6C;
        gate.OnDoubleClick(player);
        Assert.Equal(Source, player.Location);
        Confirm(player, state, 0);
        gate.DoTeleport(player);
        Assert.Equal(Source, player.Location);
        Confirm(player, state, 1);
        Assert.Equal(Destination, player.Location);
    }

    [Theory]
    [InlineData("Cancel")]
    [InlineData("UnknownButton")]
    [InlineData("Away")]
    [InlineData("OtherMap")]
    [InlineData("Vertical")]
    [InlineData("Deleted")]
    [InlineData("MovedGate")]
    [InlineData("Destination")]
    [InlineData("Inactive")]
    [InlineData("OtherSender")]
    public void UnsafeOrCancelledConfirmationNeverTravels(string condition)
    {
        var (player, state) = Player();
        var gate = Track(new Teleporter(Destination, Map.Felucca) { ItemID = 0xF6C });
        gate.MoveToWorld(Source, Map.Felucca);
        gate.OnMoveOver(player);
        var gump = Assert.IsType<MoongateTravelConfirmation>(player.FindGump<MoongateTravelConfirmation>());
        var button = 1;
        switch (condition)
        {
            case "Cancel": button = 0; break;
            case "UnknownButton": button = 2; break;
            case "Away": player.MoveToWorld(new Point3D(103, 100, 0), Map.Felucca); break;
            case "OtherMap":
                player.NetState = null;
                player.MoveToWorld(Source, Map.Trammel);
                player.NetState = state;
                break;
            case "Vertical": player.MoveToWorld(new Point3D(100, 100, 16), Map.Felucca); break;
            case "Deleted": gate.Delete(); break;
            case "MovedGate": gate.MoveToWorld(new Point3D(101, 100, 0), Map.Felucca); break;
            case "Destination": gate.PointDest = new Point3D(120, 120, 0); break;
            case "Inactive": gate.Active = false; break;
            case "OtherSender": (_, state) = Player(); break;
        }
        var location = player.Location;
        var map = player.Map;
        gump.OnResponse(state, Info(button));
        Assert.Equal(location, player.Location);
        Assert.Same(map, player.Map);
    }

    [Fact]
    public void AcceptedRequestIsSingleUse_AndLinkedGateMovementInvalidatesMoongateWarning()
    {
        var (player, state) = Player();
        var item = Track(new Item(0xF6C));
        item.MoveToWorld(Source, Map.Felucca);
        var count = 0;
        MoongateTravelConfirmation.DisplayTo(player, item, _ => true, _ => count++);
        var gump = Assert.IsType<MoongateTravelConfirmation>(player.FindGump<MoongateTravelConfirmation>());
        gump.OnResponse(state, Info(1));
        gump.OnResponse(state, Info(1));
        Assert.Equal(1, count);

        var linked = Track(new Moongate(Source, Map.Felucca));
        linked.MoveToWorld(Destination, Map.Felucca);
        var gate = Track(new GateTravelMoongate(Destination, Map.Felucca) { LinkedGate = linked });
        gate.MoveToWorld(Source, Map.Felucca);
        MoongateConfirmGump.DisplayTo(player, gate);
        var warning = Assert.IsType<MoongateConfirmGump>(player.FindGump<MoongateConfirmGump>());
        linked.MoveToWorld(new Point3D(120, 120, 0), Map.Felucca);
        warning.OnResponse(state, Info(1));
        Assert.Equal(Source, player.Location);
    }

    [Fact]
    public void PublicGateKeepsDestinationMenu_CancelAndUnknownButtonDoNotTravel_PositiveSelectionDoes()
    {
        var (player, state) = Player();
        var gate = Track(new PublicMoongate());
        gate.MoveToWorld(Source, Map.Felucca);
        gate.OnMoveOver(player);
        var menu = Assert.IsType<MoongateGump>(player.FindGump<MoongateGump>());
        Assert.Equal(Source, player.Location);
        menu.OnResponse(state, Info(0, 0));
        Assert.Equal(Source, player.Location);
        gate.OnDoubleClick(player);
        menu = Assert.IsType<MoongateGump>(player.FindGump<MoongateGump>());
        menu.OnResponse(state, Info(7, 0));
        Assert.Equal(Source, player.Location);
        gate.OnDoubleClick(player);
        menu = Assert.IsType<MoongateGump>(player.FindGump<MoongateGump>());
        menu.OnResponse(state, Info(1, 0));
        Assert.NotEqual(Source, player.Location);
    }

    private (PlayerMobile Player, NetState State) Player()
    {
        var state = PacketTestUtilities.CreateTestNetState();
        _states.Add(state);
        var player = Track(new PlayerMobile { Player = true, Body = 0x190 });
        player.MoveToWorld(Source, Map.Felucca);
        player.NetState = state;
        state.Mobile = player;
        return (player, state);
    }

    private static void Confirm(Mobile player, NetState state, int button) =>
        Assert.IsType<MoongateTravelConfirmation>(player.FindGump<MoongateTravelConfirmation>()).OnResponse(state, Info(button));

    private static RelayInfo Info(int button, int? selected = null) => new(button,
        selected.HasValue ? new[] { selected.Value } : ReadOnlySpan<int>.Empty,
        ReadOnlySpan<ushort>.Empty, ReadOnlySpan<Range>.Empty, ReadOnlySpan<byte>.Empty);

    private T Track<T>(T entity) where T : IEntity
    {
        if (!ReferenceEquals(World.FindEntity(entity.Serial), entity))
        {
            World.AddEntity(entity);
        }
        _entities.Add(entity);
        return entity;
    }
}
