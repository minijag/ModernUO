using System;
using System.Collections.Generic;
using Server;
using Server.Engines.TownStruggle;
using Server.Gumps;
using Server.Items;
using Server.Mobiles;
using Server.Network;
using Server.Tests;
using Server.Tests.Network;
using Xunit;

namespace UOContent.Tests.Engines.TownStruggle;

[Collection("Sequential UOContent Tests")]
public class BattleCommendationTests : IDisposable
{
    private readonly List<PlayerMobile> _players = [];
    private readonly List<Item> _items = [];
    private readonly List<NetState> _states = [];

    public void Dispose()
    {
        foreach (var player in _players)
        {
            player.NetState = null;
            player.Delete();
        }
        foreach (var item in _items)
        {
            item.Delete();
        }
        foreach (var state in _states)
        {
            state.Dispose();
        }
    }

    [Fact]
    public void TokenIsStackableBlessedAndUsesExpectedValue()
    {
        var token = Token(5);
        Assert.True(token.Stackable);
        Assert.Equal(LootType.Blessed, token.LootType);
        Assert.Equal(5, token.Amount);
        Assert.Equal(2500, BattleCommendation.GoldPerToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnBackpackOrBankTokenConsumesOneAndDepositsOnlyInBank(bool bank)
    {
        var player = Player();
        var token = Token(2);
        (bank ? player.BankBox : player.Backpack).DropItem(token);
        Assert.True(token.TryRedeem(player, out _));
        Assert.Equal(1, token.Amount);
        Assert.Equal(2500, player.BankBox.GetAmount(typeof(Gold)));
        Assert.Equal(0, player.Backpack.GetAmount(typeof(Gold)));
        Assert.True(token.TryRedeem(player, out _));
        Assert.True(token.Deleted);
        Assert.Equal(5000, player.BankBox.GetAmount(typeof(Gold)));
        Assert.False(token.TryRedeem(player, out _));
        Assert.Equal(5000, player.BankBox.GetAmount(typeof(Gold)));
    }

    [Fact]
    public void FullBankPreservesTokenAndDoesNotDropGoldToGround()
    {
        var player = Player();
        var token = Token(2);
        player.Backpack.DropItem(token);
        var filler = new Item(1);
        _items.Add(filler);
        player.BankBox.DropItem(filler);
        player.BankBox.MaxItems = 1;
        Assert.False(token.TryRedeem(player, out _));
        Assert.Equal(2, token.Amount);
        Assert.Equal(0, player.BankBox.GetAmount(typeof(Gold)));
        Assert.Equal(0, player.Backpack.GetAmount(typeof(Gold)));
    }

    [Fact]
    public void ForeignOrUnownedTokenCannotBeRedeemed()
    {
        var owner = Player();
        var other = Player();
        var token = Token(1);
        Assert.False(token.TryRedeem(owner, out _));
        owner.Backpack.DropItem(token);
        Assert.False(token.TryRedeem(other, out _));
        Assert.False(token.Deleted);
        Assert.Equal(0, other.BankBox.GetAmount(typeof(Gold)));
    }

    [Fact]
    public void ConfirmationRequiresBoundViewerAndDoesNotPermitReplyReplay()
    {
        var owner = Player();
        var other = Player();
        var token = Token(3);
        owner.Backpack.DropItem(token);
        token.OnDoubleClick(owner);
        var gump = owner.FindGump<BattleCommendation.RedeemGump>();
        Assert.NotNull(gump);
        var reply = new RelayInfo(1, ReadOnlySpan<int>.Empty, ReadOnlySpan<ushort>.Empty, ReadOnlySpan<Range>.Empty, ReadOnlySpan<byte>.Empty);
        gump.OnResponse(other.NetState, in reply);
        Assert.Equal(3, token.Amount);
        gump.OnResponse(owner.NetState, in reply);
        Assert.Equal(2, token.Amount);
        gump.OnResponse(owner.NetState, in reply);
        Assert.Equal(2, token.Amount);
        Assert.Equal(2500, owner.BankBox.GetAmount(typeof(Gold)));
    }

    [Fact]
    public void MovingTokenAfterConfirmationRefusesPayment()
    {
        var owner = Player();
        var other = Player();
        var token = Token(1);
        owner.Backpack.DropItem(token);
        token.OnDoubleClick(owner);
        var gump = owner.FindGump<BattleCommendation.RedeemGump>();
        other.Backpack.DropItem(token);
        var reply = new RelayInfo(1, ReadOnlySpan<int>.Empty, ReadOnlySpan<ushort>.Empty, ReadOnlySpan<Range>.Empty, ReadOnlySpan<byte>.Empty);
        gump.OnResponse(owner.NetState, in reply);
        Assert.False(token.Deleted);
        Assert.Equal(0, owner.BankBox.GetAmount(typeof(Gold)));
    }

    private BattleCommendation Token(int amount)
    {
        var token = new BattleCommendation(amount);
        _items.Add(token);
        return token;
    }

    private PlayerMobile Player()
    {
        var player = new PlayerMobile(World.NewMobile);
        player.DefaultMobileInit();
        player.Name = "Town Struggle Tester";
        player.Player = true;
        player.AddItem(new Backpack());
        var state = PacketTestUtilities.CreateTestNetState();
        player.NetState = state;
        state.Mobile = player;
        _players.Add(player);
        _states.Add(state);
        return player;
    }
}
