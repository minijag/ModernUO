using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Server;
using Server.Engines.ModularDungeon;
using Server.Items;
using Server.Mobiles;
using Server.Network;
using Server.Tests.Network;
using Xunit;

namespace UOContent.Tests.Engines.ModularDungeon;

[Collection("Sequential UOContent Tests")]
public class EventDungeonDoorTests : IDisposable
{
    private static readonly FieldInfo _timerField = typeof(BaseDoor).GetField(
        "_timer", BindingFlags.Instance | BindingFlags.NonPublic
    );
    private static readonly FieldInfo _openingTimerField = typeof(EventDungeonDoor).GetField(
        "_openingTimer", BindingFlags.Instance | BindingFlags.NonPublic
    );
    private static readonly FieldInfo _openingMobileField = typeof(EventDungeonDoor).GetField(
        "_openingMobile", BindingFlags.Instance | BindingFlags.NonPublic
    );
    private static readonly FieldInfo _workingTimerField = typeof(EventDungeonDoor).GetField(
        "_workingTimer", BindingFlags.Instance | BindingFlags.NonPublic
    );

    private readonly List<IEntity> _created = new();
    private readonly List<NetState> _states = new();
    private readonly long _startTickCount = Core.TickCount;
    private readonly DateTime _startTime = Core.Now;

    public EventDungeonDoorTests() => Timer.Init(Core.TickCount);

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
    [InlineData(DoorFacing.WestCW)]
    [InlineData(DoorFacing.EastCCW)]
    [InlineData(DoorFacing.WestCCW)]
    [InlineData(DoorFacing.EastCW)]
    [InlineData(DoorFacing.SouthCW)]
    [InlineData(DoorFacing.NorthCCW)]
    [InlineData(DoorFacing.SouthCCW)]
    [InlineData(DoorFacing.NorthCW)]
    public void NewDoor_IsLocked_AndUsesMetalDoorGeometry(DoorFacing facing)
    {
        var door = Track(new EventDungeonDoor(facing));

        Assert.True(door.Locked);
        Assert.False(door.Open);
        Assert.False(door.AutoClose);
        Assert.False(door.UseLocks());
        Assert.Equal(0x675 + 2 * (int)facing, door.ItemID);
        Assert.Equal(0x676 + 2 * (int)facing, door.OpenedId);
        Assert.Equal(BaseDoor.GetOffset(facing), door.Offset);
        Assert.False(AutoCloseTimerRunning(door));
    }

    [Theory]
    [InlineData(AccessLevel.Player)]
    [InlineData(AccessLevel.GameMaster)]
    public void LockedDoor_OpensWithoutKeys_AfterExactlyFourSeconds(AccessLevel accessLevel)
    {
        var door = CreateDoor();
        var player = CreateActor(accessLevel);

        Assert.Null(player.Backpack);
        door.OnDoubleClick(player);
        Assert.False(door.Open);
        Assert.True(OpeningTimer(door).Running);
        Assert.True(player.NetState.SendBuffer.GetReadSpan().IndexOf(
            Encoding.BigEndianUnicode.GetBytes("Opening this door will take 4 seconds")
        ) >= 0);

        AdvanceMilliseconds(3992);
        Assert.False(door.Open);
        AdvanceMilliseconds(8);

        Assert.True(door.Open);
        Assert.True(door.Locked);
        Assert.False(AutoCloseTimerRunning(door));
        AssertOpeningCleared(door);
    }

    [Theory]
    [InlineData(0, 0, 4000, "4")]
    [InlineData(50, 0, 3000, "3")]
    [InlineData(100, 0, 2000, "2")]
    [InlineData(0, 50, 3500, "3.5")]
    [InlineData(0, 100, 3000, "3")]
    [InlineData(50, 50, 2500, "2.5")]
    [InlineData(100, 100, 1000, "1")]
    [InlineData(120, 120, 1000, "1")]
    public void Skills_ProportionallyReduceOpeningDelay_AndPromptShowsActualDuration(
        double lockpicking, double removeTrap, int milliseconds, string displayedSeconds
    )
    {
        var door = CreateDoor();
        var player = CreateActor();
        var location = door.Location;
        player.Skills[SkillName.Lockpicking].Base = lockpicking;
        player.Skills[SkillName.RemoveTrap].Base = removeTrap;

        door.OnDoubleClick(player);

        Assert.True(player.NetState.SendBuffer.GetReadSpan().IndexOf(
            Encoding.BigEndianUnicode.GetBytes($"Opening this door will take {displayedSeconds} seconds")
        ) >= 0);
        AdvanceMilliseconds(milliseconds - 8);
        Assert.False(door.Open);
        AdvanceMilliseconds(8);

        Assert.True(door.Open);
        Assert.Equal(ExpectedWorkingSounds(milliseconds), CountSoundPackets(player, EventDungeonSounds.Working, location));
        Assert.Equal(1, CountSoundPackets(player, EventDungeonSounds.Unlocked, location));
        AssertOpeningCleared(door);
        AdvanceMilliseconds(1000);
        Assert.Equal(ExpectedWorkingSounds(milliseconds), CountSoundPackets(player, EventDungeonSounds.Working, location));
        Assert.Equal(1, CountSoundPackets(player, EventDungeonSounds.Unlocked, location));
    }

    [Fact]
    public void WalkingAway_ThenReturningBeforeTheDeadline_DoesNotResumeTheCancelledOpening()
    {
        var door = CreateDoor();
        var player = CreateActor();
        var location = door.Location;
        door.OnDoubleClick(player);
        AdvanceMilliseconds(1000);
        var workingSounds = CountSoundPackets(player, EventDungeonSounds.Working, location);
        player.MoveToWorld(new Point3D(110, 110, 0), Map.Felucca);

        AdvanceMilliseconds(EventDungeonOpening.ValidationIntervalMilliseconds);
        AssertOpeningCleared(door);
        player.MoveToWorld(location, Map.Felucca);
        AdvanceMilliseconds(3000);

        Assert.False(door.Open);
        Assert.Equal(workingSounds, CountSoundPackets(player, EventDungeonSounds.Working, location));
        Assert.Equal(0, CountSoundPackets(player, EventDungeonSounds.Unlocked, location));
        door.OnDoubleClick(player);
        AdvanceMilliseconds(4000);
        Assert.True(door.Open);
        Assert.Equal(1, CountSoundPackets(player, EventDungeonSounds.Unlocked, location));
        AssertOpeningCleared(door);
    }

    [Fact]
    public void OpeningAudio_ReplaysAndFinishes_WhenTheTickCounterWraps()
    {
        Core._tickCount = long.MaxValue - 400;
        Timer.Init(Core.TickCount);
        var door = CreateDoor();
        var player = CreateActor();
        var location = door.Location;
        door.OnDoubleClick(player);

        AdvanceMilliseconds(3992);
        Assert.False(door.Open);
        AdvanceMilliseconds(8);

        Assert.True(door.Open);
        Assert.Equal(ExpectedWorkingSounds(4000), CountSoundPackets(player, EventDungeonSounds.Working, location));
        Assert.Equal(1, CountSoundPackets(player, EventDungeonSounds.Unlocked, location));
        AssertOpeningCleared(door);
    }

    [Fact]
    public void SkillChangesDuringOpening_ApplyOnlyToTheNextAttempt()
    {
        var door = CreateDoor();
        var player = CreateActor();
        door.OnDoubleClick(player);
        var deadline = OpeningTimer(door).Next;
        AdvanceMilliseconds(1000);
        player.Skills[SkillName.Lockpicking].Base = 100;
        player.Skills[SkillName.RemoveTrap].Base = 100;

        door.OnDoubleClick(player);

        Assert.Equal(deadline, OpeningTimer(door).Next);
        AdvanceMilliseconds(2992);
        Assert.False(door.Open);
        AdvanceMilliseconds(8);
        Assert.True(door.Open);
        player.MoveToWorld(new Point3D(101, 100, 0), Map.Felucca);
        door.OnDoubleClick(player);
        Assert.False(door.Open);
        door.OnDoubleClick(player);
        AdvanceMilliseconds(992);
        Assert.False(door.Open);
        AdvanceMilliseconds(8);
        Assert.True(door.Open);
        AssertOpeningCleared(door);
    }

    [Fact]
    public void AcceptedOpening_ReplaysLockpickSoundAtDoor_ThenClicks_ForNearbyPlayers()
    {
        var door = CreateDoor();
        var player = CreateActor();
        var observer = CreateActor();
        observer.MoveToWorld(new Point3D(101, 100, 0), Map.Felucca);
        var distant = CreateActor();
        distant.MoveToWorld(new Point3D(200, 200, 0), Map.Felucca);
        var closedLocation = door.Location;

        door.OnDoubleClick(player);

        Assert.Equal(1, CountSoundPackets(player, EventDungeonSounds.Working, closedLocation));
        Assert.Equal(1, CountSoundPackets(observer, EventDungeonSounds.Working, closedLocation));
        Assert.Equal(0, CountSoundPackets(distant, EventDungeonSounds.Working, closedLocation));
        Assert.Equal(0, CountSoundPackets(observer, 0xF00, closedLocation));
        Assert.Equal(0, CountSoundPackets(player, EventDungeonSounds.Unlocked, closedLocation));
        door.OnDoubleClick(player);
        door.OnDoubleClick(observer);
        Assert.Equal(1, CountSoundPackets(player, EventDungeonSounds.Working, closedLocation));
        Assert.Equal(1, CountSoundPackets(observer, EventDungeonSounds.Working, closedLocation));
        AdvanceMilliseconds(4000);

        Assert.True(door.Open);
        Assert.Equal(ExpectedWorkingSounds(4000), CountSoundPackets(player, EventDungeonSounds.Working, closedLocation));
        Assert.Equal(ExpectedWorkingSounds(4000), CountSoundPackets(observer, EventDungeonSounds.Working, closedLocation));
        Assert.Equal(1, CountSoundPackets(player, EventDungeonSounds.Unlocked, closedLocation));
        Assert.Equal(1, CountSoundPackets(observer, EventDungeonSounds.Unlocked, closedLocation));
        Assert.Equal(0, CountSoundPackets(distant, EventDungeonSounds.Unlocked, closedLocation));
        Assert.Equal(0, CountSoundPackets(observer, 0xF00, closedLocation));
        Assert.Equal(1, CountSoundPackets(player, door.OpenedSound, door.Location));
        var packets = player.NetState.SendBuffer.GetReadSpan();
        var openingIndex = packets.IndexOf(new PlaySound(door.OpenedSound, door.Location).Compile());
        var clickIndex = packets.IndexOf(new PlaySound(EventDungeonSounds.Unlocked, closedLocation).Compile());
        Assert.True(clickIndex > openingIndex);
        player.MoveToWorld(new Point3D(101, 100, 0), Map.Felucca);
        Assert.True(door.CanClose());
        door.OnDoubleClick(player);

        Assert.False(door.Open);
        Assert.Equal(ExpectedWorkingSounds(4000), CountSoundPackets(player, EventDungeonSounds.Working, closedLocation));
        Assert.Equal(1, CountSoundPackets(player, EventDungeonSounds.Unlocked, closedLocation));
        Assert.Equal(1, CountSoundPackets(player, door.ClosedSound, closedLocation));
        AssertOpeningCleared(door);
    }

    [Fact]
    public void FailedBaseOpening_DoesNotPlayUnlockClick()
    {
        var door = Track(new LockVetoDoor());
        door.MoveToWorld(new Point3D(100, 100, 0), Map.Felucca);
        var player = CreateActor();
        door.OnDoubleClick(player);

        AdvanceMilliseconds(4000);

        Assert.False(door.Open);
        Assert.Equal(ExpectedWorkingSounds(4000), CountSoundPackets(player, EventDungeonSounds.Working, door.Location));
        Assert.Equal(0, CountSoundPackets(player, EventDungeonSounds.Unlocked, door.Location));
        AssertOpeningCleared(door);
    }

    [Theory]
    [InlineData("Distant")]
    [InlineData("Dead")]
    [InlineData("WrongMap")]
    public void RejectedOpening_DoesNotPlayALockpickSound(string invalidation)
    {
        var door = CreateDoor();
        var player = CreateActor();
        var observer = CreateActor();

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
        }

        door.OnDoubleClick(player);

        Assert.Equal(0, CountSoundPackets(observer, EventDungeonSounds.Working, door.Location));
        Assert.Equal(0, CountSoundPackets(observer, EventDungeonSounds.Unlocked, door.Location));
        Assert.False(door.Open);
        AssertOpeningCleared(door);
    }

    [Fact]
    public void RepeatedClicks_DoNotResetTheDelay_OrReplaceTheOpener()
    {
        var door = CreateDoor();
        var first = CreateActor();
        var second = CreateActor();
        door.OnDoubleClick(first);
        var deadline = OpeningTimer(door).Next;

        AdvanceMilliseconds(1000);
        var workingSounds = CountSoundPackets(first, EventDungeonSounds.Working, door.Location);
        door.OnDoubleClick(first);
        door.OnDoubleClick(second);

        Assert.Equal(deadline, OpeningTimer(door).Next);
        Assert.Equal(workingSounds, CountSoundPackets(first, EventDungeonSounds.Working, door.Location));
        Assert.Same(first, _openingMobileField.GetValue(door));
        Assert.Equal(0, CountMessages(second, "This door is already being opened."));
        AdvanceMilliseconds(2992);
        Assert.False(door.Open);
        AdvanceMilliseconds(8);

        Assert.True(door.Open);
        AssertOpeningCleared(door);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PendingOpening_SilentlyIgnoresRequestBursts_WithoutChangingOpenerOrDeadline(
        bool realPlayer, bool startWithMacro
    )
    {
        var door = CreateDoor();
        var first = CreateActor(realPlayer: realPlayer);
        var second = CreateActor(realPlayer: realPlayer);
        var location = door.Location;
        if (startWithMacro)
        {
            door.OnOpenDoorMacro(first);
        }
        else
        {
            door.OnDoubleClick(first);
        }
        var deadline = OpeningTimer(door).Next;

        for (var interval = 0; interval < 16; interval++)
        {
            var firstBytes = first.NetState.SendBuffer.GetReadSpan().Length;
            var secondBytes = second.NetState.SendBuffer.GetReadSpan().Length;
            for (var request = 0; request < 100; request++)
            {
                door.OnDoubleClick(first);
                door.OnDoubleClick(second);
                door.OnOpenDoorMacro(first);
                door.OnOpenDoorMacro(second);
            }

            Assert.Equal(firstBytes, first.NetState.SendBuffer.GetReadSpan().Length);
            Assert.Equal(secondBytes, second.NetState.SendBuffer.GetReadSpan().Length);
            Assert.Same(first, _openingMobileField.GetValue(door));
            Assert.Equal(deadline, OpeningTimer(door).Next);
            AdvanceMilliseconds(248);
            Assert.False(door.Open);
        }

        Assert.Equal(1, CountMessages(first, "Opening this door will take 4 seconds"));
        Assert.Equal(0, CountMessages(second, "Opening this door will take"));
        AdvanceMilliseconds(32);

        Assert.True(door.Open);
        Assert.Equal(ExpectedWorkingSounds(4000), CountSoundPackets(first, EventDungeonSounds.Working, location));
        Assert.Equal(1, CountSoundPackets(first, EventDungeonSounds.Unlocked, location));
        AssertOpeningCleared(door);
    }

    [Fact]
    public void OpenDoorMacro_DoesNotCloseTheCompletedDoor_OrDelayManualClosing()
    {
        var door = CreateDoor();
        var player = CreateActor(realPlayer: true);
        door.OnOpenDoorMacro(player);
        AdvanceMilliseconds(4000);
        player.MoveToWorld(new Point3D(101, 100, 0), Map.Felucca);
        Assert.True(door.Open);
        Assert.True(door.CanClose());
        var bytes = player.NetState.SendBuffer.GetReadSpan().Length;

        for (var request = 0; request < 500; request++)
        {
            door.OnOpenDoorMacro(player);
        }

        Assert.True(door.Open);
        Assert.Equal(bytes, player.NetState.SendBuffer.GetReadSpan().Length);
        door.OnDoubleClick(player);
        Assert.False(door.Open);
        AssertOpeningCleared(door);
    }

    [Fact]
    public void SealedEventDoor_ThrottlesRejectedRequestMessages_PerPlayer()
    {
        var door = CreateDoor();
        var player = CreateActor(realPlayer: true);
        var second = CreateActor(realPlayer: true);
        var controller = Track(new ExtractionEventController());
        var installation = Track(new ExtractionEventInstallation());
        installation.Doors.Add(door);
        controller.AddManagedItem(installation);
        var controllerProperty = typeof(ExtractionEventRuntime).GetProperty(nameof(ExtractionEventRuntime.Controller));
        var adapterField = typeof(ExtractionEventRuntime).GetField("_adapter", BindingFlags.Static | BindingFlags.NonPublic);
        var previousController = ExtractionEventRuntime.Controller;
        var previousAdapter = adapterField.GetValue(null);

        try
        {
            controllerProperty.SetValue(null, controller);
            adapterField.SetValue(null, null);
            Assert.False(ExtractionEventRuntime.CanOpenEventDoor(door, out var error));

            for (var interval = 0; interval < 10; interval++)
            {
                for (var request = 0; request < 100; request++)
                {
                    door.OnDoubleClick(player);
                    door.OnOpenDoorMacro(player);
                }
                Assert.Equal(1, CountMessages(player, error));
                Assert.False(door.Open);
                AssertOpeningCleared(door);
                AdvanceMilliseconds(496);
            }

            door.OnOpenDoorMacro(second);
            Assert.Equal(1, CountMessages(second, error));
            AdvanceMilliseconds(544);
            door.OnDoubleClick(player);
            Assert.Equal(2, CountMessages(player, error));
            Assert.False(door.Open);
            AssertOpeningCleared(door);
        }
        finally
        {
            adapterField.SetValue(null, previousAdapter);
            controllerProperty.SetValue(null, previousController);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EveryUseRoute_EnforcesTheDelay(int route)
    {
        var door = CreateDoor();
        var player = CreateActor();
        UseRoute(door, player, route);

        Assert.False(door.Open);
        AdvanceMilliseconds(3992);
        Assert.False(door.Open);
        AdvanceMilliseconds(8);

        Assert.True(door.Open);
        AssertOpeningCleared(door);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EveryUseRoute_RejectsDistantActors_EvenStaff(int route)
    {
        var door = CreateDoor();
        var gm = CreateActor(AccessLevel.GameMaster);
        gm.MoveToWorld(new Point3D(110, 110, 0), Map.Felucca);

        UseRoute(door, gm, route);
        AdvanceMilliseconds(4000);

        Assert.False(door.Open);
        AssertOpeningCleared(door);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DoorMacro_StartsTheSameFourSecondOpening_WithoutGenericOpeningMessages(bool realPlayer)
    {
        var door = CreateDoor();
        var player = CreateActor(realPlayer: realPlayer);
        player.MoveToWorld(new Point3D(100, 101, 0), Map.Felucca);
        player.Direction = Direction.North;
        var artId = door.ItemID & TileData.MaxItemValue;
        var originalData = TileData.ItemTable[artId];

        try
        {
            TileData.ItemTable[artId] = new ItemData("test door", TileFlag.Door, 0, 0, 0, 0, 0, 20);
            Assert.True(player.InLOS(door));
            BaseDoor.OpenDoorMacroUsed(player);

            Assert.False(door.Open);
            Assert.True(OpeningTimer(door).Running);
            Assert.Equal(1, CountMessages(player, "Opening this door will take 4 seconds"));
            Assert.Equal(0, CountPacketBytes(player, new MessageLocalized(
                Serial.MinusOne, -1, MessageType.Regular, 0x3B2, 3, 500024, "System", ""
            ).Compile()));
            var bytes = player.NetState.SendBuffer.GetReadSpan().Length;
            for (var request = 0; request < 500; request++)
            {
                BaseDoor.OpenDoorMacroUsed(player);
            }
            Assert.Equal(bytes, player.NetState.SendBuffer.GetReadSpan().Length);
            AdvanceMilliseconds(3992);
            Assert.False(door.Open);
            AdvanceMilliseconds(8);

            Assert.True(door.Open);
            AssertOpeningCleared(door);
        }
        finally
        {
            TileData.ItemTable[artId] = originalData;
        }
    }

    [Theory]
    [InlineData("MovedAway")]
    [InlineData("ChangedMap")]
    [InlineData("Dead")]
    [InlineData("LoggedOut")]
    [InlineData("DeletedActor")]
    [InlineData("DeletedDoor")]
    [InlineData("MovedDoor")]
    [InlineData("ChangedDoorMap")]
    [InlineData("NestedDoor")]
    public void InvalidatedOpening_IsCancelled_AndClearsItsReferences(string invalidation)
    {
        var door = CreateDoor();
        var player = CreateActor();
        var observer = CreateActor();
        var closedLocation = door.Location;
        door.OnDoubleClick(player);
        AdvanceMilliseconds(2800);
        var workingSounds = CountSoundPackets(observer, EventDungeonSounds.Working, closedLocation);
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
            case "DeletedDoor":
            {
                door.Delete();
                AssertOpeningCleared(door);
                break;
            }
            case "MovedDoor":
            {
                door.MoveToWorld(new Point3D(101, 100, 0), Map.Felucca);
                break;
            }
            case "ChangedDoorMap":
            {
                door.MoveToWorld(door.Location, Map.Trammel);
                break;
            }
            case "NestedDoor":
            {
                Track(new Backpack()).DropItem(door);
                break;
            }
        }

        AdvanceMilliseconds(EventDungeonOpening.ValidationIntervalMilliseconds);
        AssertOpeningCleared(door);
        AdvanceMilliseconds(3000);

        Assert.False(door.Open);
        Assert.Equal(workingSounds, CountSoundPackets(observer, EventDungeonSounds.Working, closedLocation));
        Assert.Equal(0, CountSoundPackets(observer, EventDungeonSounds.Unlocked, closedLocation));
        AssertOpeningCleared(door);
    }

    [Fact]
    public void LosingLineOfSight_CancelsTheOpening()
    {
        var door = CreateDoor();
        var player = CreateActor();
        var location = door.Location;
        door.OnDoubleClick(player);
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
            Assert.False(player.InLOS(door));
            Assert.True(player.InRange(door.Location, 2));

            AdvanceMilliseconds(EventDungeonOpening.ValidationIntervalMilliseconds);
            AssertOpeningCleared(door);
            blocker.Delete();
            player.MoveToWorld(location, Map.Felucca);
            Assert.True(player.InLOS(door));
            AdvanceMilliseconds(3000);

            Assert.False(door.Open);
            Assert.Equal(workingSounds, CountSoundPackets(player, EventDungeonSounds.Working, location));
            Assert.Equal(0, CountSoundPackets(player, EventDungeonSounds.Unlocked, location));
            AssertOpeningCleared(door);
        }
        finally
        {
            TileData.ItemTable[probeId] = originalData;
        }
    }

    [Fact]
    public void PendingOpening_IsNotRestoredBySaveLoad()
    {
        var door = CreateDoor();
        var player = CreateActor();
        door.OnDoubleClick(player);
        AdvanceMilliseconds(1000);
        var bytes = Snapshot(door);
        door.Delete();
        var restored = Track(new EventDungeonDoor(World.NewItem));
        var reader = new BufferReader(bytes);
        restored.Deserialize(reader);

        Assert.Equal(bytes.Length, reader.Position);
        Assert.False(restored.Open);
        Assert.True(restored.Locked);
        AssertOpeningCleared(restored);
        AdvanceMilliseconds(4000);
        Assert.False(restored.Open);

        restored.OnDoubleClick(player);
        AdvanceMilliseconds(3992);
        Assert.False(restored.Open);
        AdvanceMilliseconds(8);

        Assert.True(restored.Open);
        AssertOpeningCleared(restored);
    }

    [Fact]
    public void DoorOpenedDuringTheDelay_IsNotClosedByCompletion()
    {
        var door = CreateDoor();
        var player = CreateActor();
        var closedLocation = door.Location;
        door.OnDoubleClick(player);
        AdvanceMilliseconds(1000);
        door.Open = true;

        AdvanceMilliseconds(3000);

        Assert.True(door.Open);
        Assert.Equal(0, CountSoundPackets(player, EventDungeonSounds.Unlocked, closedLocation));
        AssertOpeningCleared(door);
    }

    [Fact]
    public void OpenDoor_StaysOpen_WhenTimePasses()
    {
        var door = CreateDoor();
        var player = CreateActor();

        door.Use(player);
        AdvanceMilliseconds(4000);
        Assert.True(door.Open);
        var openLocation = door.Location;
        var openId = door.ItemID;
        AdvanceMilliseconds(60000);

        Assert.True(door.Open);
        Assert.Equal(openLocation, door.Location);
        Assert.Equal(openId, door.ItemID);
        Assert.False(AutoCloseTimerRunning(door));
        AssertOpeningCleared(door);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void OpenDoor_CanBeClosedImmediately_AndReopeningStillTakesFourSeconds(int route)
    {
        var door = CreateDoor();
        var player = CreateActor();
        door.Use(player);
        AdvanceMilliseconds(4000);
        player.MoveToWorld(new Point3D(101, 100, 0), Map.Felucca);
        Assert.True(door.CanClose());

        UseRoute(door, player, route);

        Assert.False(door.Open);
        Assert.False(AutoCloseTimerRunning(door));
        AssertOpeningCleared(door);
        UseRoute(door, player, route);
        AdvanceMilliseconds(3992);
        Assert.False(door.Open);
        AdvanceMilliseconds(8);
        Assert.True(door.Open);
    }

    [Fact]
    public void DoorwayOccupant_PreventsManualClosing()
    {
        var door = CreateDoor();
        var player = CreateActor();
        door.Open = true;
        Assert.False(door.CanClose());

        door.OnDoubleClick(player);

        Assert.True(door.Open);
        AssertOpeningCleared(door);
    }

    [Fact]
    public void DistantActor_CannotManuallyCloseDoor()
    {
        var door = CreateDoor();
        var gm = CreateActor(AccessLevel.GameMaster);
        door.Open = true;
        gm.MoveToWorld(new Point3D(110, 110, 0), Map.Felucca);
        Assert.True(door.CanClose());

        door.OnDoubleClick(gm);

        Assert.True(door.Open);
        AssertOpeningCleared(door);
    }

    [Fact]
    public void ManuallyClosingDuringAnOpeningAttempt_CancelsThePendingAttempt()
    {
        var door = CreateDoor();
        var player = CreateActor();
        var location = door.Location;
        door.OnDoubleClick(player);
        AdvanceMilliseconds(1000);
        door.Open = true;
        player.MoveToWorld(new Point3D(101, 100, 0), Map.Felucca);

        door.OnDoubleClick(player);
        Assert.False(door.Open);
        AssertOpeningCleared(door);
        AdvanceMilliseconds(3000);

        Assert.False(door.Open);
        Assert.Equal(0, CountSoundPackets(player, EventDungeonSounds.Unlocked, location));
        AssertOpeningCleared(door);
    }

    [Fact]
    public void MatchReset_CancelsPendingOpening_WithoutAnUnlockClick()
    {
        var door = CreateDoor();
        var player = CreateActor();
        var location = door.Location;
        var offset = BaseDoor.GetOffset(DoorFacing.WestCW);
        var definition = new ModularDungeonDeployment.DoorDefinition("door", new(100, 100, 0),
            new(100 + offset.X, 100 + offset.Y, offset.Z), "WestCW", door.ClosedId, door.OpenedId, []);
        door.OnDoubleClick(player);
        AdvanceMilliseconds(1000);

        var workingSounds = CountSoundPackets(player, EventDungeonSounds.Working, location);

        door.ResetForMatch(Map.Felucca, definition);
        AdvanceMilliseconds(4000);

        Assert.False(door.Open);
        Assert.Equal(workingSounds, CountSoundPackets(player, EventDungeonSounds.Working, location));
        Assert.Equal(0, CountSoundPackets(player, EventDungeonSounds.Unlocked, location));
        AssertOpeningCleared(door);
    }

    [Fact]
    public void OpenProperty_CanResetDoor_WithoutAnAutoCloseTimer()
    {
        var door = Track(new EventDungeonDoor());
        var closedLocation = new Point3D(100, 100, 0);
        door.MoveToWorld(closedLocation, Map.Felucca);
        door.Open = true;

        Assert.False(AutoCloseTimerRunning(door));
        door.Open = false;

        Assert.False(door.Open);
        Assert.Equal(closedLocation, door.Location);
        Assert.Equal(door.ClosedId, door.ItemID);
        Assert.False(AutoCloseTimerRunning(door));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DoorState_RoundTrips_WithoutStartingAnAutoCloseTimer(bool open, bool locked)
    {
        var door = Track(new EventDungeonDoor(DoorFacing.NorthCCW) { Locked = locked });
        door.MoveToWorld(new Point3D(100, 100, 0), Map.Felucca);
        door.Open = open;
        var savedLocation = door.Location;
        var savedId = door.ItemID;
        var savedOffset = door.Offset;
        var bytes = Snapshot(door);
        door.Delete();

        var restored = Track(new EventDungeonDoor(World.NewItem));
        var reader = new BufferReader(bytes);
        restored.Deserialize(reader);

        Assert.Equal(bytes.Length, reader.Position);
        Assert.Equal(open, restored.Open);
        Assert.Equal(locked, restored.Locked);
        Assert.Equal(savedLocation, restored.Location);
        Assert.Equal(savedId, restored.ItemID);
        Assert.Equal(savedOffset, restored.Offset);
        Assert.False(AutoCloseTimerRunning(restored));

        AdvanceMilliseconds(60000);

        Assert.Equal(open, restored.Open);
        Assert.Equal(savedLocation, restored.Location);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NormalDoor_AutoCloses_AfterCreationOrDeserialization(bool reload)
    {
        var door = Track(new MetalDoor(DoorFacing.WestCW));
        var closedLocation = new Point3D(100, 100, 0);
        door.MoveToWorld(closedLocation, Map.Felucca);
        door.Open = true;

        if (reload)
        {
            var bytes = Snapshot(door);
            door.Delete();
            door = Track(new MetalDoor(World.NewItem));
            var reader = new BufferReader(bytes);
            door.Deserialize(reader);
            Assert.Equal(bytes.Length, reader.Position);
        }

        Assert.True(door.AutoClose);
        Assert.True(door.Open);
        Assert.True(AutoCloseTimerRunning(door));

        AdvanceMilliseconds(19992);
        Assert.True(door.Open);
        AdvanceMilliseconds(16);

        Assert.False(door.Open);
        Assert.Equal(closedLocation, door.Location);
        Assert.Equal(door.ClosedId, door.ItemID);
        Assert.False(AutoCloseTimerRunning(door));
    }

    [Fact]
    public void NormalDoor_CanStillBeClosedByPlayer()
    {
        var door = Track(new MetalDoor(DoorFacing.WestCW));
        var player = CreateActor();
        var closedLocation = new Point3D(100, 100, 0);
        door.MoveToWorld(closedLocation, Map.Felucca);
        player.MoveToWorld(new Point3D(101, 100, 0), Map.Felucca);

        door.OnDoubleClick(player);
        Assert.True(door.Open);

        door.OnDoubleClick(player);

        Assert.False(door.Open);
        Assert.Equal(closedLocation, door.Location);
        Assert.False(AutoCloseTimerRunning(door));
        Assert.Equal(0, CountSoundPackets(player, EventDungeonSounds.Working, closedLocation));
        Assert.Equal(0, CountSoundPackets(player, EventDungeonSounds.Unlocked, closedLocation));
    }

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }

    private sealed class LockVetoDoor : EventDungeonDoor
    {
        public override bool UseLocks() => true;
    }

    private EventDungeonDoor CreateDoor()
    {
        var door = Track(new EventDungeonDoor());
        door.MoveToWorld(new Point3D(100, 100, 0), Map.Felucca);
        return door;
    }

    private Mobile CreateActor(AccessLevel accessLevel = AccessLevel.Player, bool realPlayer = false)
    {
        var state = PacketTestUtilities.CreateTestNetState();
        _states.Add(state);
        state.Account = new MockAccount();
        var player = Track(realPlayer ? new PlayerMobile() : new Mobile());
        player.Player = true;
        // Keep effective opening skills at zero even when the fixture runs with the human racial minimum.
        player.Race = Race.Elf;
        player.Skills[SkillName.Lockpicking].Base = 0;
        player.Skills[SkillName.RemoveTrap].Base = 0;
        Assert.Equal(0, player.Skills[SkillName.Lockpicking].Value);
        Assert.Equal(0, player.Skills[SkillName.RemoveTrap].Value);
        player.Body = 0x190;
        player.AccessLevel = accessLevel;
        player.NetState = state;
        state.Mobile = player;
        player.MoveToWorld(new Point3D(100, 100, 0), Map.Felucca);
        return player;
    }

    private static void UseRoute(EventDungeonDoor door, Mobile from, int route)
    {
        switch (route)
        {
            case 0:
            {
                door.Use(from);
                break;
            }
            case 1:
            {
                door.OnDoubleClick(from);
                break;
            }
            case 2:
            {
                door.OnTelekinesis(from);
                break;
            }
        }
    }

    private static TimerExecutionToken OpeningTimer(EventDungeonDoor door) =>
        (TimerExecutionToken)_openingTimerField.GetValue(door);

    private static int CountSoundPackets(Mobile player, int soundId, Point3D location)
    {
        var packet = new PlaySound(soundId, location).Compile();
        return CountPacketBytes(player, packet);
    }

    private static int CountMessages(Mobile player, string text) =>
        CountPacketBytes(player, Encoding.BigEndianUnicode.GetBytes(text));

    private static int CountPacketBytes(Mobile player, ReadOnlySpan<byte> packet)
    {
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

    private static void AssertOpeningCleared(EventDungeonDoor door)
    {
        Assert.False(OpeningTimer(door).Running);
        Assert.False(((TimerExecutionToken)_workingTimerField.GetValue(door)).Running);
        Assert.Null(_openingMobileField.GetValue(door));
    }

    private static int ExpectedWorkingSounds(int milliseconds) =>
        1 + (milliseconds - 1) / EventDungeonSounds.WorkingCadenceMilliseconds;

    private static bool AutoCloseTimerRunning(BaseDoor door) =>
        (_timerField.GetValue(door) as Timer)?.Running == true;

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
