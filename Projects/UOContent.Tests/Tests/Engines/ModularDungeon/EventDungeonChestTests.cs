using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Server;
using Server.Engines.ModularDungeon;
using Server.Items;
using Server.Network;
using Server.Systems.FeatureFlags;
using Server.Tests.Network;
using Xunit;

namespace UOContent.Tests.Engines.ModularDungeon;

[Collection("Sequential UOContent Tests")]
public class EventDungeonChestTests : IDisposable
{
    private static readonly FieldInfo _openingTimerField = typeof(EventDungeonChest).GetField(
        "_openingTimer", BindingFlags.Instance | BindingFlags.NonPublic
    );
    private static readonly FieldInfo _openingMobileField = typeof(EventDungeonChest).GetField(
        "_openingMobile", BindingFlags.Instance | BindingFlags.NonPublic
    );
    private static readonly FieldInfo _workingTimerField = typeof(EventDungeonChest).GetField(
        "_workingTimer", BindingFlags.Instance | BindingFlags.NonPublic
    );

    private readonly List<IEntity> _created = new();
    private readonly List<NetState> _states = new();
    private readonly long _startTickCount = Core.TickCount;
    private readonly DateTime _startTime = Core.Now;

    public EventDungeonChestTests() => Timer.Init(Core.TickCount);

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

        Core._tickCount = _startTickCount;
        Core._now = _startTime;
        Timer.Init(Core.TickCount);
    }

    [Theory]
    [InlineData(0xE40)]
    [InlineData(0xE41)]
    [InlineData(0xE42)]
    [InlineData(0xE43)]
    [InlineData(0x9AB)]
    [InlineData(0xE7C)]
    public void NewChest_IsAnImmovableEmptyContainer_WithNoKeyOrLockpickInterface(int artId)
    {
        var chest = Track(new EventDungeonChest(artId));

        Assert.Equal(artId, chest.ItemID);
        Assert.False(chest.Movable);
        Assert.False(chest.VerifyMove(null));
        Assert.False(chest.IsDecoContainer);
        Assert.Empty(chest.Items);
        Assert.Null(chest.Openers);
        Assert.False((object)chest is ILockable);
        Assert.False((object)chest is ILockpickable);
    }

    [Theory]
    [InlineData(AccessLevel.Player)]
    [InlineData(AccessLevel.GameMaster)]
    public void DoubleClick_DisplaysARealContainer_AfterExactlySixSeconds(AccessLevel accessLevel)
    {
        var chest = CreateChest();
        var player = CreateActor(accessLevel);

        chest.OnDoubleClick(player);

        Assert.Null(chest.Openers);
        Assert.True(OpeningTimer(chest).Running);
        Assert.True(player.NetState.SendBuffer.GetReadSpan().IndexOf(
            Encoding.BigEndianUnicode.GetBytes("Opening this chest will take 6 seconds")
        ) >= 0);
        Assert.False(HasDisplayPacket(chest, player));
        AdvanceMilliseconds(5992);
        Assert.Null(chest.Openers);
        Assert.False(HasDisplayPacket(chest, player));
        AdvanceMilliseconds(8);

        Assert.Contains(player, chest.Openers);
        Assert.True(HasDisplayPacket(chest, player));
        Assert.Equal(ExpectedWorkingSounds(6000), CountSoundPackets(player, EventDungeonSounds.Working, chest.Location));
        Assert.Equal(1, CountSoundPackets(player, EventDungeonSounds.Unlocked, chest.Location));
        AssertOpeningCleared(chest);
        AdvanceMilliseconds(1000);
        Assert.Equal(ExpectedWorkingSounds(6000), CountSoundPackets(player, EventDungeonSounds.Working, chest.Location));
        Assert.Equal(1, CountSoundPackets(player, EventDungeonSounds.Unlocked, chest.Location));
    }

    [Fact]
    public void WalkingAway_ThenReturningBeforeTheDeadline_DoesNotResumeTheCancelledOpening()
    {
        var chest = CreateChest();
        var player = CreateActor();
        var location = chest.Location;
        chest.OnDoubleClick(player);
        AdvanceMilliseconds(1000);
        var workingSounds = CountSoundPackets(player, EventDungeonSounds.Working, location);
        player.MoveToWorld(new Point3D(110, 110, 0), Map.Felucca);

        AdvanceMilliseconds(EventDungeonOpening.ValidationIntervalMilliseconds);
        AssertOpeningCleared(chest);
        player.MoveToWorld(location, Map.Felucca);
        AdvanceMilliseconds(5000);

        Assert.Null(chest.Openers);
        Assert.Equal(workingSounds, CountSoundPackets(player, EventDungeonSounds.Working, location));
        Assert.Equal(0, CountSoundPackets(player, EventDungeonSounds.Unlocked, location));
        chest.OnDoubleClick(player);
        AdvanceMilliseconds(6000);
        Assert.Contains(player, chest.Openers);
        Assert.Equal(1, CountSoundPackets(player, EventDungeonSounds.Unlocked, location));
        AssertOpeningCleared(chest);
    }

    [Fact]
    public void OpeningAudio_ReplaysAndFinishes_WhenTheTickCounterWraps()
    {
        Core._tickCount = long.MaxValue - 400;
        Timer.Init(Core.TickCount);
        var chest = CreateChest();
        var player = CreateActor();
        chest.OnDoubleClick(player);

        AdvanceMilliseconds(5992);
        Assert.Null(chest.Openers);
        AdvanceMilliseconds(8);

        Assert.Contains(player, chest.Openers);
        Assert.Equal(ExpectedWorkingSounds(6000), CountSoundPackets(player, EventDungeonSounds.Working, chest.Location));
        Assert.Equal(1, CountSoundPackets(player, EventDungeonSounds.Unlocked, chest.Location));
        AssertOpeningCleared(chest);
    }

    [Theory]
    [InlineData(0, 0, 6000, "6")]
    [InlineData(50, 0, 5000, "5")]
    [InlineData(100, 0, 4000, "4")]
    [InlineData(0, 50, 5500, "5.5")]
    [InlineData(0, 100, 5000, "5")]
    [InlineData(50, 50, 4500, "4.5")]
    [InlineData(100, 100, 3000, "3")]
    [InlineData(120, 120, 3000, "3")]
    public void Skills_ProportionallyReduceOpeningDelay_AndPromptShowsActualDuration(
        double lockpicking, double removeTrap, int milliseconds, string displayedSeconds
    )
    {
        var chest = CreateChest();
        var player = CreateActor();
        player.Skills[SkillName.Lockpicking].Base = lockpicking;
        player.Skills[SkillName.RemoveTrap].Base = removeTrap;

        chest.OnDoubleClick(player);

        Assert.True(player.NetState.SendBuffer.GetReadSpan().IndexOf(
            Encoding.BigEndianUnicode.GetBytes($"Opening this chest will take {displayedSeconds} seconds")
        ) >= 0);
        AdvanceMilliseconds(milliseconds - 8);
        Assert.Null(chest.Openers);
        Assert.False(HasDisplayPacket(chest, player));
        AdvanceMilliseconds(8);

        Assert.Contains(player, chest.Openers);
        Assert.True(HasDisplayPacket(chest, player));
        Assert.Equal(ExpectedWorkingSounds(milliseconds), CountSoundPackets(player, EventDungeonSounds.Working, chest.Location));
        Assert.Equal(1, CountSoundPackets(player, EventDungeonSounds.Unlocked, chest.Location));
        AssertOpeningCleared(chest);
        AdvanceMilliseconds(1000);
        Assert.Equal(ExpectedWorkingSounds(milliseconds), CountSoundPackets(player, EventDungeonSounds.Working, chest.Location));
        Assert.Equal(1, CountSoundPackets(player, EventDungeonSounds.Unlocked, chest.Location));
    }

    [Fact]
    public void SkillChangesDuringOpening_ApplyOnlyToTheNextAttempt()
    {
        var chest = CreateChest();
        var player = CreateActor();
        chest.OnDoubleClick(player);
        var deadline = OpeningTimer(chest).Next;
        AdvanceMilliseconds(1000);
        player.Skills[SkillName.Lockpicking].Base = 100;
        player.Skills[SkillName.RemoveTrap].Base = 100;

        chest.OnDoubleClick(player);

        Assert.Equal(deadline, OpeningTimer(chest).Next);
        AdvanceMilliseconds(4992);
        Assert.Null(chest.Openers);
        AdvanceMilliseconds(8);
        Assert.Contains(player, chest.Openers);
        var packetLength = player.NetState.SendBuffer.GetReadSpan().Length;
        chest.OnDoubleClick(player);
        var promptLength = player.NetState.SendBuffer.GetReadSpan().Length;
        Assert.True(promptLength > packetLength);
        AdvanceMilliseconds(2992);
        Assert.False(HasDisplayPacket(chest, player, promptLength));
        AdvanceMilliseconds(8);
        Assert.True(HasDisplayPacket(chest, player, promptLength));
        AssertOpeningCleared(chest);
    }

    [Fact]
    public void AcceptedOpening_ReplaysWorkingSound_ThenUnlockClickAfterDisplay_ForNearbyPlayers()
    {
        var chest = CreateChest();
        var player = CreateActor();
        var observer = CreateActor();
        observer.MoveToWorld(new Point3D(101, 100, 0), Map.Felucca);
        var distant = CreateActor();
        distant.MoveToWorld(new Point3D(200, 200, 0), Map.Felucca);

        chest.OnDoubleClick(player);

        Assert.Equal(1, CountSoundPackets(player, EventDungeonSounds.Working, chest.Location));
        Assert.Equal(1, CountSoundPackets(observer, EventDungeonSounds.Working, chest.Location));
        Assert.Equal(0, CountSoundPackets(distant, EventDungeonSounds.Working, chest.Location));
        Assert.Equal(0, CountSoundPackets(observer, 0xF00, chest.Location));
        Assert.Equal(0, CountSoundPackets(player, EventDungeonSounds.Unlocked, chest.Location));
        Assert.False(HasDisplayPacket(chest, player));
        chest.OnDoubleClick(player);
        chest.OnDoubleClick(observer);

        Assert.Equal(1, CountSoundPackets(player, EventDungeonSounds.Working, chest.Location));
        Assert.Equal(1, CountSoundPackets(observer, EventDungeonSounds.Working, chest.Location));
        AdvanceMilliseconds(5992);
        Assert.Equal(0, CountSoundPackets(observer, EventDungeonSounds.Unlocked, chest.Location));
        AdvanceMilliseconds(8);

        Assert.True(HasDisplayPacket(chest, player));
        Assert.False(HasDisplayPacket(chest, observer));
        Assert.Equal(ExpectedWorkingSounds(6000), CountSoundPackets(player, EventDungeonSounds.Working, chest.Location));
        Assert.Equal(ExpectedWorkingSounds(6000), CountSoundPackets(observer, EventDungeonSounds.Working, chest.Location));
        Assert.Equal(1, CountSoundPackets(player, EventDungeonSounds.Unlocked, chest.Location));
        Assert.Equal(1, CountSoundPackets(observer, EventDungeonSounds.Unlocked, chest.Location));
        Assert.Equal(0, CountSoundPackets(distant, EventDungeonSounds.Unlocked, chest.Location));
        Assert.Equal(0, CountSoundPackets(observer, 0xF00, chest.Location));
        var packets = player.NetState.SendBuffer.GetReadSpan();
        var displayIndex = packets.IndexOf(new ContainerDisplay(chest.Serial, chest.GumpID).Compile());
        var clickIndex = packets.IndexOf(new PlaySound(EventDungeonSounds.Unlocked, chest.Location).Compile());
        Assert.True(clickIndex > displayIndex);
        AssertOpeningCleared(chest);
    }

    [Theory]
    [InlineData("Distant")]
    [InlineData("Dead")]
    [InlineData("WrongMap")]
    [InlineData("NestedChest")]
    public void RejectedOpening_DoesNotPlayWorkingOrUnlockSounds(string invalidation)
    {
        var chest = CreateChest();
        var player = CreateActor();
        var observer = CreateActor();
        var location = chest.Location;

        switch (invalidation)
        {
            case "Distant":
            {
                player.MoveToWorld(new Point3D(110, 110, 0), Map.Felucca);
                break;
            }
            case "Dead":
            {
                player.Body = 0x192;
                break;
            }
            case "WrongMap":
            {
                player.MoveToWorld(player.Location, Map.Trammel);
                break;
            }
            case "NestedChest":
            {
                Track(new Backpack()).DropItem(chest);
                break;
            }
        }

        chest.OnDoubleClick(player);
        AdvanceMilliseconds(6000);

        Assert.Equal(0, CountSoundPackets(observer, EventDungeonSounds.Working, location));
        Assert.Equal(0, CountSoundPackets(observer, EventDungeonSounds.Unlocked, location));
        Assert.False(HasDisplayPacket(chest, player));
        AssertOpeningCleared(chest);
    }

    [Fact]
    public void BlockedDisplayAfterPreviousOpening_DoesNotPlayAFalseCompletionClick()
    {
        var chest = CreateChest();
        var player = CreateActor();
        chest.OnDoubleClick(player);
        AdvanceMilliseconds(6000);
        Assert.Equal(1, CountSoundPackets(player, EventDungeonSounds.Unlocked, chest.Location));
        Assert.Contains(player, chest.Openers);
        chest.OnDoubleClick(player);
        var afterPromptLength = player.NetState.SendBuffer.GetReadSpan().Length;
        var blocksField = typeof(FeatureFlagManager).GetField("_itemBlocks", BindingFlags.Static | BindingFlags.NonPublic);
        var activeField = typeof(FeatureFlagManager).GetField("_hasActiveItemBlocks", BindingFlags.Static | BindingFlags.NonPublic);
        var blocks = (Dictionary<Type, ItemBlockEntry>)blocksField.GetValue(null);
        var hadBlock = blocks.TryGetValue(typeof(EventDungeonChest), out var previousBlock);
        var wasActive = (bool)activeField.GetValue(null);
        var requiredAccess = FeatureFlagSettings.RequiredAccessLevel;

        try
        {
            FeatureFlagSettings.RequiredAccessLevel = AccessLevel.Administrator;
            blocks[typeof(EventDungeonChest)] = new ItemBlockEntry { Active = true, BlockUse = true, Reason = "test blocked display" };
            activeField.SetValue(null, true);
            AdvanceMilliseconds(6000);

            Assert.Contains(player, chest.Openers);
            Assert.Equal(1, CountSoundPackets(player, EventDungeonSounds.Unlocked, chest.Location));
            Assert.True(player.NetState.SendBuffer.GetReadSpan()[afterPromptLength..].IndexOf(
                new ContainerDisplay(chest.Serial, chest.GumpID).Compile()
            ) < 0);
            AssertOpeningCleared(chest);
        }
        finally
        {
            if (hadBlock)
            {
                blocks[typeof(EventDungeonChest)] = previousBlock;
            }
            else
            {
                blocks.Remove(typeof(EventDungeonChest));
            }

            activeField.SetValue(null, wasActive);
            FeatureFlagSettings.RequiredAccessLevel = requiredAccess;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EveryDisplayRoute_EnforcesTheSixSecondDelay(int route)
    {
        var chest = CreateChest();
        var player = CreateActor();

        UseRoute(chest, player, route);

        Assert.Null(chest.Openers);
        AdvanceMilliseconds(5992);
        Assert.Null(chest.Openers);
        AdvanceMilliseconds(8);

        Assert.Contains(player, chest.Openers);
        Assert.True(HasDisplayPacket(chest, player));
        AssertOpeningCleared(chest);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EveryDisplayRoute_RejectsDistantStaff(int route)
    {
        var chest = CreateChest();
        var player = CreateActor(AccessLevel.GameMaster);
        player.MoveToWorld(new Point3D(110, 110, 0), Map.Felucca);

        UseRoute(chest, player, route);
        AdvanceMilliseconds(6000);

        Assert.Null(chest.Openers);
        Assert.False(HasDisplayPacket(chest, player));
        AssertOpeningCleared(chest);
    }

    [Fact]
    public void RepeatedClicks_DoNotResetDelay_OrReplaceTheOpener()
    {
        var chest = CreateChest();
        var first = CreateActor();
        var second = CreateActor();
        chest.OnDoubleClick(first);
        var deadline = OpeningTimer(chest).Next;
        AdvanceMilliseconds(1000);

        var workingSounds = CountSoundPackets(first, EventDungeonSounds.Working, chest.Location);
        chest.OnDoubleClick(first);
        chest.OnDoubleClick(second);

        Assert.Equal(deadline, OpeningTimer(chest).Next);
        Assert.Equal(workingSounds, CountSoundPackets(first, EventDungeonSounds.Working, chest.Location));
        Assert.Same(first, _openingMobileField.GetValue(chest));
        Assert.True(second.NetState.SendBuffer.GetReadSpan().IndexOf(
            Encoding.BigEndianUnicode.GetBytes("This chest is already being opened.")
        ) >= 0);
        AdvanceMilliseconds(5000);
        Assert.Contains(first, chest.Openers);
        Assert.DoesNotContain(second, chest.Openers);
        AssertOpeningCleared(chest);
    }

    [Fact]
    public void EveryReopen_TakesSixSeconds_WithoutAnInstantDisplayShortcut()
    {
        var chest = CreateChest();
        var player = CreateActor();
        chest.OnDoubleClick(player);
        AdvanceMilliseconds(6000);
        Assert.Contains(player, chest.Openers);
        var packetLength = player.NetState.SendBuffer.GetReadSpan().Length;

        chest.DisplayTo(player);
        var afterPromptLength = player.NetState.SendBuffer.GetReadSpan().Length;
        Assert.True(afterPromptLength > packetLength);
        AdvanceMilliseconds(5992);
        Assert.False(HasDisplayPacket(chest, player, afterPromptLength));
        AdvanceMilliseconds(8);

        Assert.True(HasDisplayPacket(chest, player, afterPromptLength));
        AssertOpeningCleared(chest);
    }

    [Theory]
    [InlineData("MovedAway")]
    [InlineData("ChangedMap")]
    [InlineData("Dead")]
    [InlineData("LoggedOut")]
    [InlineData("DeletedActor")]
    [InlineData("DeletedChest")]
    [InlineData("MovedChest")]
    [InlineData("ChangedChestMap")]
    [InlineData("NestedChest")]
    public void InvalidatedOpening_DoesNotDisplayContents_AndClearsReferences(string invalidation)
    {
        var chest = CreateChest();
        var player = CreateActor();
        var observer = CreateActor();
        var location = chest.Location;
        chest.OnDoubleClick(player);
        AdvanceMilliseconds(2800);
        var workingSounds = CountSoundPackets(observer, EventDungeonSounds.Working, location);
        Assert.True(workingSounds > 1);

        switch (invalidation)
        {
            case "MovedAway":
            {
                player.MoveToWorld(new Point3D(110, 110, 0), Map.Felucca);
                break;
            }
            case "ChangedMap":
            {
                player.MoveToWorld(player.Location, Map.Trammel);
                break;
            }
            case "Dead":
            {
                player.Body = 0x192;
                Assert.False(player.Alive);
                break;
            }
            case "LoggedOut":
            {
                player.NetState = null;
                break;
            }
            case "DeletedActor":
            {
                player.Delete();
                break;
            }
            case "DeletedChest":
            {
                chest.Delete();
                break;
            }
            case "MovedChest":
            {
                chest.MoveToWorld(new Point3D(101, 100, 0), Map.Felucca);
                break;
            }
            case "ChangedChestMap":
            {
                chest.MoveToWorld(chest.Location, Map.Trammel);
                break;
            }
            case "NestedChest":
            {
                Track(new Backpack()).DropItem(chest);
                break;
            }
        }

        AdvanceMilliseconds(EventDungeonOpening.ValidationIntervalMilliseconds);
        AssertOpeningCleared(chest);
        AdvanceMilliseconds(5000);

        Assert.Null(chest.Openers);
        Assert.Equal(workingSounds, CountSoundPackets(observer, EventDungeonSounds.Working, location));
        Assert.Equal(0, CountSoundPackets(observer, EventDungeonSounds.Unlocked, location));
        AssertOpeningCleared(chest);
    }

    [Fact]
    public void LosingLineOfSight_CancelsOpening()
    {
        var chest = CreateChest();
        var player = CreateActor();
        var location = chest.Location;
        chest.OnDoubleClick(player);
        AdvanceMilliseconds(1000);
        var workingSounds = CountSoundPackets(player, EventDungeonSounds.Working, location);
        var probeId = Math.Min(1, TileData.MaxItemValue);
        var originalData = TileData.ItemTable[probeId];

        try
        {
            TileData.ItemTable[probeId] = new ItemData("test obstruction", TileFlag.NoShoot, 0, 0, 0, 0, 0, 40);
            var blocker = Track(new Item(probeId));
            blocker.MoveToWorld(new Point3D(101, 100, 0), Map.Felucca);
            player.MoveToWorld(new Point3D(102, 100, 0), Map.Felucca);
            Assert.True(player.InRange(chest.Location, 2));
            Assert.False(player.InLOS(chest));

            AdvanceMilliseconds(EventDungeonOpening.ValidationIntervalMilliseconds);
            AssertOpeningCleared(chest);
            blocker.Delete();
            player.MoveToWorld(location, Map.Felucca);
            Assert.True(player.InLOS(chest));
            AdvanceMilliseconds(5000);

            Assert.Null(chest.Openers);
            Assert.Equal(workingSounds, CountSoundPackets(player, EventDungeonSounds.Working, location));
            Assert.Equal(0, CountSoundPackets(player, EventDungeonSounds.Unlocked, location));
            AssertOpeningCleared(chest);
        }
        finally
        {
            TileData.ItemTable[probeId] = originalData;
        }
    }

    [Fact]
    public void MatchReset_CancelsPendingOpening_WithoutAnUnlockClick()
    {
        var chest = CreateChest();
        var player = CreateActor();
        var location = chest.Location;
        chest.OnDoubleClick(player);
        AdvanceMilliseconds(1000);
        var workingSounds = CountSoundPackets(player, EventDungeonSounds.Working, location);

        chest.ResetForMatch(Map.Felucca, location, chest.ItemID);
        AdvanceMilliseconds(6000);

        Assert.Null(chest.Openers);
        Assert.Equal(workingSounds, CountSoundPackets(player, EventDungeonSounds.Working, location));
        Assert.Equal(0, CountSoundPackets(player, EventDungeonSounds.Unlocked, location));
        AssertOpeningCleared(chest);
    }

    [Fact]
    public void Contents_CannotBeUsedOrLootedBeforeTheDelay_AndAreUsableAfterward()
    {
        var chest = CreateChest();
        var gold = Track(new Gold(10));
        chest.DropItem(gold);
        var player = CreateActor();
        var reason = LRReason.Inspecific;

        Assert.False(chest.CheckItemUse(player, gold));
        Assert.False(chest.CheckLift(player, gold, ref reason));
        chest.OnDoubleClick(player);
        AdvanceMilliseconds(5992);
        Assert.False(chest.CheckItemUse(player, gold));
        Assert.False(chest.CheckLift(player, gold, ref reason));
        AdvanceMilliseconds(8);

        Assert.True(chest.CheckItemUse(player, gold));
        Assert.True(chest.CheckLift(player, gold, ref reason));
    }

    [Fact]
    public void OnePlayersCompletedOpening_DoesNotBypassAnotherPlayersDelay()
    {
        var chest = CreateChest();
        var gold = Track(new Gold(10));
        chest.DropItem(gold);
        var first = CreateActor();
        var second = CreateActor();
        chest.OnDoubleClick(first);
        AdvanceMilliseconds(6000);

        Assert.True(chest.CheckItemUse(first, gold));
        Assert.False(chest.CheckItemUse(second, gold));
        chest.OnDoubleClick(second);
        AdvanceMilliseconds(5992);
        Assert.False(chest.CheckItemUse(second, gold));
        AdvanceMilliseconds(8);

        Assert.True(chest.CheckItemUse(second, gold));
    }

    [Fact]
    public void PendingOpening_DoesNotSurviveSaveLoad_WhileContentsAndArtDo()
    {
        var chest = CreateChest(0xE40);
        var gold = Track(new Gold(10));
        chest.DropItem(gold);
        var player = CreateActor();
        chest.OnDoubleClick(player);
        AdvanceMilliseconds(1000);
        var bytes = Snapshot(chest);
        // The child is saved separately by World; keep it alive for the reference round-trip.
        chest.RemoveItem(gold);
        chest.Delete();
        var restored = Track(new EventDungeonChest(World.NewItem));
        var reader = new BufferReader(bytes);
        restored.Deserialize(reader);

        Assert.Equal(bytes.Length, reader.Position);
        Assert.Equal(0xE40, restored.ItemID);
        Assert.False(restored.Movable);
        Assert.False(restored.IsDecoContainer);
        Assert.Contains(gold, restored.Items);
        Assert.Null(restored.Openers);
        AssertOpeningCleared(restored);
        AdvanceMilliseconds(6000);
        Assert.Null(restored.Openers);
        restored.OnDoubleClick(player);
        AdvanceMilliseconds(5992);
        Assert.Null(restored.Openers);
        AdvanceMilliseconds(8);

        Assert.Contains(player, restored.Openers);
        AssertOpeningCleared(restored);
    }

    [Fact]
    public void NormalChest_StillOpensImmediately()
    {
        var chest = Track(new MetalGoldenChest());
        chest.MoveToWorld(new Point3D(100, 100, 0), Map.Felucca);
        var player = CreateActor();

        chest.OnDoubleClick(player);

        Assert.Contains(player, chest.Openers);
        Assert.Equal(0, CountSoundPackets(player, EventDungeonSounds.Working, chest.Location));
        Assert.Equal(0, CountSoundPackets(player, EventDungeonSounds.Unlocked, chest.Location));
    }

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }

    private EventDungeonChest CreateChest(int artId = 0xE41)
    {
        var chest = Track(new EventDungeonChest(artId));
        chest.MoveToWorld(new Point3D(100, 100, 0), Map.Felucca);
        return chest;
    }

    private Mobile CreateActor(AccessLevel accessLevel = AccessLevel.Player)
    {
        var state = PacketTestUtilities.CreateTestNetState();
        _states.Add(state);
        state.Account = new MockAccount();
        var player = Track(new Mobile { Player = true, Body = 0x190, AccessLevel = accessLevel });
        player.NetState = state;
        state.Mobile = player;
        player.MoveToWorld(new Point3D(100, 100, 0), Map.Felucca);
        return player;
    }

    private static void UseRoute(EventDungeonChest chest, Mobile player, int route)
    {
        switch (route)
        {
            case 0:
            {
                chest.OnDoubleClick(player);
                break;
            }
            case 1:
            {
                chest.Open(player);
                break;
            }
            case 2:
            {
                chest.DisplayTo(player);
                break;
            }
            case 3:
            {
                ((ITelekinesisable)chest).OnTelekinesis(player);
                break;
            }
            case 4:
            {
                chest.OnSnoop(player);
                break;
            }
            case 5:
            {
                chest.OnDoubleClickSecureTrade(player);
                break;
            }
        }
    }

    private static bool HasDisplayPacket(EventDungeonChest chest, Mobile player, int startIndex = 0) =>
        player.NetState.SendBuffer.GetReadSpan()[startIndex..].IndexOf(new ContainerDisplay(chest.Serial, chest.GumpID).Compile()) >= 0;

    private static int CountSoundPackets(Mobile player, int soundId, Point3D location)
    {
        var packet = new PlaySound(soundId, location).Compile();
        var buffer = player.NetState.SendBuffer.GetReadSpan();
        var count = 0;
        var index = buffer.IndexOf(packet);

        while (index >= 0)
        {
            count++;
            buffer = buffer[(index + packet.Length)..];
            index = buffer.IndexOf(packet);
        }

        return count;
    }

    private static TimerExecutionToken OpeningTimer(EventDungeonChest chest) =>
        (TimerExecutionToken)_openingTimerField.GetValue(chest);

    private static void AssertOpeningCleared(EventDungeonChest chest)
    {
        Assert.False(OpeningTimer(chest).Running);
        Assert.False(((TimerExecutionToken)_workingTimerField.GetValue(chest)).Running);
        Assert.Null(_openingMobileField.GetValue(chest));
    }

    private static int ExpectedWorkingSounds(int milliseconds) =>
        1 + (milliseconds - 1) / EventDungeonSounds.WorkingCadenceMilliseconds;

    private static byte[] Snapshot(Item item)
    {
        var writer = new BufferWriter(true);
        item.Serialize(writer);
        return writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();
    }

    private static void AdvanceMilliseconds(long milliseconds)
    {
        var deadline = Core.TickCount + milliseconds;

        while (Core.TickCount - deadline < 0)
        {
            Core._tickCount += Timer.TickRate;
            Core._now += TimeSpan.FromMilliseconds(Timer.TickRate);
            Timer.Slice(Core.TickCount);
        }
    }
}
