using System;
using System.Collections.Generic;
using Server;
using Server.Items;
using Server.Mobiles;
using Server.Network;
using Server.Tests.Network;
using Xunit;

namespace UOContent.Tests.Items;

[Collection("Sequential UOContent Tests")]
public class DoorRequestThrottleTests : IDisposable
{
    private readonly List<IEntity> _created = [];
    private readonly List<NetState> _states = [];
    private readonly long _ticks = Core.TickCount;
    private readonly DateTime _now = Core.Now;

    public DoorRequestThrottleTests() => Timer.Init(Core.TickCount);

    public void Dispose()
    {
        for (var i = _created.Count - 1; i >= 0; i--)
        {
            if (_created[i] is Mobile mobile)
            {
                mobile.NetState = null;
            }
            _created[i].Delete();
        }
        foreach (var state in _states)
        {
            state.Dispose();
        }
        Core._tickCount = _ticks;
        Core._now = _now;
        Timer.Init(Core.TickCount);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-10000L)]
    [InlineData(long.MaxValue - 100)]
    [InlineData(long.MinValue + 100)]
    public void FirstRequestsWorkAtAnyTickAndCooldownsAreWrapSafe(long start)
    {
        var player = Player();
        Core._tickCount = start;
        Assert.True(player.TryDoorMacroRequest());
        Assert.True(player.TryDoorUseRequest());
        Assert.True(player.TryDoorErrorMessage());
        for (var i = 0; i < 1000; i++)
        {
            Assert.False(player.TryDoorMacroRequest());
            Assert.False(player.TryDoorUseRequest());
            Assert.False(player.TryDoorErrorMessage());
        }
        Core._tickCount = unchecked(start + 249);
        Assert.False(player.TryDoorMacroRequest());
        Core._tickCount = unchecked(start + 250);
        Assert.True(player.TryDoorMacroRequest());
        Assert.False(player.TryDoorUseRequest());
        Core._tickCount = unchecked(start + 500);
        Assert.True(player.TryDoorUseRequest());
        Assert.False(player.TryDoorErrorMessage());
        Core._tickCount = unchecked(start + 5000);
        Assert.True(player.TryDoorErrorMessage());
    }

    [Fact]
    public void RejectedRetriesDoNotExtendTheDeadlineOrThrottleAnotherPlayer()
    {
        var first = Player();
        var second = Player();
        var start = Core.TickCount;
        Assert.True(first.TryDoorMacroRequest());
        Assert.True(first.TryDoorUseRequest());
        for (var offset = 1; offset < 250; offset++)
        {
            Core._tickCount = start + offset;
            Assert.False(first.TryDoorMacroRequest());
            Assert.False(first.TryDoorUseRequest());
        }
        Assert.True(second.TryDoorMacroRequest());
        Assert.True(second.TryDoorUseRequest());
        Core._tickCount = start + 250;
        Assert.True(first.TryDoorMacroRequest());
        Core._tickCount = start + 500;
        Assert.True(first.TryDoorUseRequest());
    }

    [Fact]
    public void BurstMacrosReachTheDoorOnlyOnceAndPreserveOrdinaryDispatch()
    {
        var player = Player();
        var door = Track(new MacroProbeDoor());
        door.MoveToWorld(new Point3D(100, 100, 0), Map.Felucca);
        WithDoorHeight(door, () =>
        {
            BaseDoor.OpenDoorMacroUsed(player);
            Assert.Equal(1, door.Uses);
            var bytes = player.NetState.SendBuffer.GetReadSpan().Length;
            Assert.True(bytes > 0);
            for (var i = 0; i < 1000; i++)
            {
                BaseDoor.OpenDoorMacroUsed(player);
            }
            Assert.Equal(1, door.Uses);
            Assert.Equal(bytes, player.NetState.SendBuffer.GetReadSpan().Length);
            Core._tickCount += PlayerMobile.DoorMacroCooldownMilliseconds;
            BaseDoor.OpenDoorMacroUsed(player);
            Assert.Equal(2, door.Uses);
            Assert.True(player.NetState.SendBuffer.GetReadSpan().Length > bytes);
        });
    }

    [Fact]
    public void DeadPlayerMacroBurstDoesNotRepeatCheckAliveFeedback()
    {
        var player = Player();
        player.Body = 0x192;
        Assert.False(player.Alive);
        BaseDoor.OpenDoorMacroUsed(player);
        var bytes = player.NetState.SendBuffer.GetReadSpan().Length;
        for (var i = 0; i < 1000; i++)
        {
            BaseDoor.OpenDoorMacroUsed(player);
        }
        Assert.Equal(bytes, player.NetState.SendBuffer.GetReadSpan().Length);
    }

    [Fact]
    public void OrdinaryDoorCanStillOpenAndCloseAfterMacroCooldown()
    {
        var player = Player();
        var door = Track(new MetalDoor(DoorFacing.WestCW) { Offset = Point3D.Zero });
        door.MoveToWorld(new Point3D(100, 100, 0), Map.Felucca);
        var closed = TileData.ItemTable[door.ClosedId];
        var opened = TileData.ItemTable[door.OpenedId];
        try
        {
            TileData.ItemTable[door.ClosedId] = new ItemData("test door", TileFlag.Door, 0, 0, 0, 0, 0, 20);
            TileData.ItemTable[door.OpenedId] = new ItemData("test door", TileFlag.Door, 0, 0, 0, 0, 0, 20);
            BaseDoor.OpenDoorMacroUsed(player);
            Assert.True(door.Open);
            Core._tickCount += PlayerMobile.DoorMacroCooldownMilliseconds;
            BaseDoor.OpenDoorMacroUsed(player);
            Assert.False(door.Open);
        }
        finally
        {
            TileData.ItemTable[door.ClosedId] = closed;
            TileData.ItemTable[door.OpenedId] = opened;
        }
    }

    private PlayerMobile Player()
    {
        var player = Track(new PlayerMobile(World.NewMobile));
        player.DefaultMobileInit();
        player.Player = true;
        player.Body = 400;
        player.RawStr = 100;
        player.Hits = player.HitsMax;
        var state = PacketTestUtilities.CreateTestNetState();
        _states.Add(state);
        state.Account = new MockAccount();
        player.NetState = state;
        state.Mobile = player;
        player.MoveToWorld(new Point3D(100, 101, 0), Map.Felucca);
        player.Direction = Direction.North;
        return player;
    }

    private static void WithDoorHeight(BaseDoor door, Action action)
    {
        var artId = door.ItemID & TileData.MaxItemValue;
        var original = TileData.ItemTable[artId];
        try
        {
            TileData.ItemTable[artId] = new ItemData("test door", TileFlag.Door, 0, 0, 0, 0, 0, 20);
            action();
        }
        finally
        {
            TileData.ItemTable[artId] = original;
        }
    }

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }

    private sealed class MacroProbeDoor : MetalDoor
    {
        public MacroProbeDoor() : base(DoorFacing.WestCW)
        {
        }
        public int Uses { get; private set; }
        public override void OnDoubleClick(Mobile from) => Uses++;
    }
}
