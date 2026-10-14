using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Server;
using Server.Engines.ModularDungeon;
using Server.Gumps;
using Server.Items;
using Server.Mobiles;
using Server.Network;
using Server.Tests;
using Server.Tests.Network;
using Xunit;
using Movement = Server.Movement.Movement;

namespace UOContent.Tests.Engines.ModularDungeon;

[Collection("Sequential UOContent Tests")]
public class ExtractionEventRuntimeTests : IDisposable
{
    private readonly List<IEntity> _created = [];
    private readonly List<NetState> _states = [];
    private readonly DateTime _now = Core.Now;
    private readonly long _ticks = Core.TickCount;
    private readonly Expansion _expansion = Core.Expansion;
    private readonly CreateCorpseHandler _corpseHandler = Mobile.CreateCorpseHandler;

    public ExtractionEventRuntimeTests()
    {
        Core.Expansion = Expansion.T2A;
        Timer.Init(Core.TickCount);
    }

    public void Dispose()
    {
        SetDeploymentSuspended(false);
        var controller = ExtractionEventRuntime.Controller;
        if (controller != null)
        {
            foreach (var item in controller.ManagedItems)
            {
                _created.Add(item);
            }
            foreach (var creature in controller.OwnedCreatures)
            {
                _created.Add(creature);
            }
            _created.Add(controller.RecoveryStorage);
            _created.Add(controller);
        }
        InvokeRuntime("Shutdown");
        typeof(ExtractionEventRuntime).GetProperty(nameof(ExtractionEventRuntime.Controller)).SetValue(null, null);
        ExtractionEventDeath.ControllerLookup = null;
        ExtractionDungeonReset.ManagedChestLookup = null;
        for (var index = _created.Count - 1; index >= 0; index--)
        {
            if (_created[index] is Mobile mobile)
            {
                mobile.NetState = null;
            }
            _created[index]?.Delete();
        }
        foreach (var state in _states)
        {
            state.Dispose();
        }
        Mobile.CreateCorpseHandler = _corpseHandler;
        Core._now = _now;
        Core._tickCount = _ticks;
        Core.Expansion = _expansion;
        Timer.Init(Core.TickCount);
    }

    [Fact]
    public void LobbyBuild_IsWalkableSealedAndHasGreenEntryBlueReturnAndThreeExtractionRunes()
    {
        var controller = BuildLobby();
        Assert.True(Map.Felucca.CanFit(ExtractionDungeonLayout.LobbyEntry, 16, true, true));
        var walker = Player(ExtractionDungeonLayout.LobbyEntry);
        Assert.True(Movement.CheckMovement(walker, Direction.North, out var z));
        Assert.Equal(0, z);
        for (var index = 1; index < 24; index++)
        {
            Assert.False(Map.Felucca.CanFit(new Point3D(5384 + index, 808, 0), 16, true, false));
            Assert.False(Map.Felucca.CanFit(new Point3D(5384 + index, 832, 0), 16, true, false));
            Assert.False(Map.Felucca.CanFit(new Point3D(5384, 808 + index, 0), 16, true, false));
            Assert.False(Map.Felucca.CanFit(new Point3D(5408, 808 + index, 0), 16, true, false));
        }
        var gates = Gates(controller);
        Assert.Equal(5, gates.Count);
        Assert.Single(gates, gate => gate.Kind == ExtractionEventGateKind.Entry && gate.Hue == ExtractionEventGate.GreenHue);
        Assert.Single(gates, gate => gate.Kind == ExtractionEventGateKind.LobbyReturn && gate.Hue == 0x482);
        Assert.Equal(3, gates.FindAll(gate => gate.Kind == ExtractionEventGateKind.Extraction).Count);
        Assert.Equal(TimeSpan.FromMinutes(15), controller.WaitingInterval);
        Assert.True(controller.AutomaticRounds);
        var arrival = Player(ExtractionDungeonLayout.GatePoint);
        Assert.True(ExtractionEventRuntime.TryUseGate(gates.Find(gate => gate.Kind == ExtractionEventGateKind.Entry), arrival, out var error), error);
        Assert.True(ExtractionDungeonLayout.LobbyBounds.Contains(arrival.Location));
        Assert.False(arrival.CanBeHarmful(walker, false));
        var previousHits = arrival.Hits;
        arrival.Damage(1000, walker);
        Assert.Equal(previousHits, arrival.Hits);
        Assert.True(arrival.Alive);
    }

    [Theory]
    [InlineData(ExtractionEventGateKind.Entry)]
    [InlineData(ExtractionEventGateKind.LobbyReturn)]
    public void EventMoongates_RequireConfirmation_AndCancelDoesNotTravel(ExtractionEventGateKind kind)
    {
        var controller = BuildLobby();
        var gate = Gates(controller).Find(candidate => candidate.Kind == kind);
        var player = Player(gate.Location);
        var origin = player.Location;
        Assert.True(gate.OnMoveOver(player));
        Assert.Equal(origin, player.Location);
        var cancelled = player.FindGump<MoongateTravelConfirmation>();
        Assert.NotNull(cancelled);
        cancelled.OnResponse(player.NetState, Reply(0));
        Assert.Equal(origin, player.Location);
        gate.OnDoubleClick(player);
        var accepted = player.FindGump<MoongateTravelConfirmation>();
        Assert.NotNull(accepted);
        accepted.OnResponse(player.NetState, Reply(1));
        if (kind == ExtractionEventGateKind.Entry)
        {
            Assert.True(ExtractionDungeonLayout.LobbyBounds.Contains(player.Location));
        }
        else
        {
            Assert.InRange(player.GetDistanceToSqrt(ExtractionDungeonLayout.TownReturn), 0, 4);
        }
    }

    [Fact]
    public void EventMoongate_RejectsConfirmationAfterOwnershipSuspended()
    {
        var controller = BuildLobby();
        var gate = Gates(controller).Find(candidate => candidate.Kind == ExtractionEventGateKind.Entry);
        var player = Player(gate.Location);
        gate.OnDoubleClick(player);
        var gump = player.FindGump<MoongateTravelConfirmation>();
        Assert.NotNull(gump);
        SetDeploymentSuspended(true);
        gump.OnResponse(player.NetState, Reply(1));
        Assert.Equal(gate.Location, player.Location);
    }

    private static RelayInfo Reply(int buttonId) =>
        new(buttonId, ReadOnlySpan<int>.Empty, ReadOnlySpan<ushort>.Empty, ReadOnlySpan<Range>.Empty, ReadOnlySpan<byte>.Empty);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealRound_ExtractKeepsLoot_TimeoutKillsAndReturnsGhost_AllLootStaysInDungeonCorpse(bool offline)
    {
        var controller = BuildLobby();
        var extractor = Player(new Point3D(5396, 820, 0));
        var late = Player(new Point3D(5397, 820, 0));
        var extractedLoot = Track(new Gold(222));
        extractor.Backpack.DropItem(extractedLoot);
        var lostGear = Track(new Item(0xF5E) { Layer = Layer.OneHanded, LootType = LootType.Blessed });
        late.AddItem(lostGear);
        var bankGold = Track(new Gold(999));
        late.BankBox.DropItem(bankGold);
        Mobile.CreateCorpseHandler = Corpse.Mobile_CreateCorpseHandler;
        ExtractionEventDeath.ControllerLookup = () => controller;
        Assert.True(controller.TryStart(out var error), error);
        AdvanceTo(controller, ExtractionEventPhase.Active);
        Assert.Equal(111, controller.OwnedCreatures.Count);
        Assert.All(controller.OwnedCreatures, creature => Assert.Null(creature.Spawner));
        Assert.All(controller.OwnedCreatures, creature => Assert.False(creature.Frozen));
        Assert.Equal(ExtractionDungeonLayout.Rooms[0].PlayerStart, extractor.Location);
        Assert.Equal(ExtractionDungeonLayout.Rooms[1].PlayerStart, late.Location);
        var gates = Gates(controller);
        Core._now = controller.MatchDeadline - ExtractionEventController.MatchDuration +
                    ExtractionEventController.ExtractionDelay - TimeSpan.FromMilliseconds(1);
        foreach (var exteriorExit in gates.FindAll(gate => gate.Kind == ExtractionEventGateKind.Extraction))
        {
            extractor.MoveToWorld(exteriorExit.Location, Map.Felucca);
            Assert.False(ExtractionEventRuntime.TryUseGate(exteriorExit, extractor, out error));
            Assert.Contains("seven minutes", error);
            Assert.Equal(exteriorExit.Location, extractor.Location);
            Assert.True(controller.IsParticipant(extractor));
            Assert.Same(extractor.Backpack, extractedLoot.Parent);
        }
        Core._now += TimeSpan.FromMilliseconds(1);
        var exit = gates.Find(gate => gate.Kind == ExtractionEventGateKind.Extraction);
        extractor.MoveToWorld(exit.Location, Map.Felucca);
        Assert.True(ExtractionEventRuntime.TryUseGate(exit, extractor, out error), error);
        Assert.True(ExtractionDungeonLayout.LobbyBounds.Contains(extractor.Location));
        Assert.True(extractor.Alive);
        Assert.Same(extractor.Backpack, extractedLoot.Parent);
        Assert.False(controller.IsParticipant(extractor));
        Assert.False(late.CanBeHarmful(extractor, false));
        Assert.False(extractor.CanBeHarmful(late, false));
        var extractedHits = extractor.Hits;
        extractor.Damage(1000, late);
        Assert.Equal(extractedHits, extractor.Hits);
        Assert.Equal(ExtractionEventPhase.Active, controller.Phase);
        Assert.False(controller.TryJoinLobby(late, out _));
        var connection = late.NetState;
        if (offline)
        {
            late.LogoutLocation = late.Location;
            late.LogoutMap = Map.Felucca;
            late.NetState = null;
            late.Internalize();
        }
        Core._now = controller.MatchDeadline;
        controller.Advance();
        controller.Advance();
        Assert.False(late.Alive);
        Assert.True(ExtractionDungeonLayout.LobbyBounds.Contains(offline ? late.LogoutLocation : late.Location));
        var corpse = Track(Assert.IsType<Corpse>(late.Corpse));
        Assert.Same(corpse, lostGear.Parent);
        Assert.True(ExtractionDungeonLayout.DungeonArea.Contains(corpse.Location));
        Assert.Same(late.BankBox, bankGold.Parent);
        Assert.Equal(999, bankGold.Amount);
        Assert.Empty(controller.Participants);
        if (offline)
        {
            Assert.Equal(Map.Internal, late.Map);
            late.NetState = connection;
            connection.Mobile = late;
            Assert.True(ExtractionDungeonLayout.LobbyBounds.Contains(late.Location));
        }
        var blue = gates.Find(gate => gate.Kind == ExtractionEventGateKind.LobbyReturn);
        late.MoveToWorld(blue.Location, Map.Felucca);
        Assert.True(ExtractionEventRuntime.TryUseGate(blue, late, out error), error);
        Assert.False(late.Alive);
        Assert.InRange(late.GetDistanceToSqrt(ExtractionDungeonLayout.TownReturn), 0, 4);
    }

    [Theory]
    [InlineData("ModularDungeonV17")]
    [InlineData("ModularDungeonV35")]
    public void ManagedDoors_OnlyExteriorExitsWaitUntilMinuteSeven(string manifestVersion)
    {
        var controller = BuildLobby(manifestVersion);
        var installation = Assert.IsType<ExtractionEventInstallation>(
            Assert.Single(controller.ManagedItems, item => item is ExtractionEventInstallation));
        Assert.Equal(71, installation.Doors.Count);
        Assert.All(installation.Doors, door => Assert.False(ExtractionEventRuntime.CanOpenEventDoor(door, out _)));
        var unmanaged = Track(new EventDungeonDoor());
        Assert.True(ExtractionEventRuntime.CanOpenEventDoor(unmanaged, out _));
        Player(ExtractionDungeonLayout.LobbyEntry);
        Assert.True(controller.TryStart(out var error), error);
        Assert.Equal(ExtractionEventPhase.Starting, controller.Phase);
        Assert.All(installation.Doors, door => Assert.False(ExtractionEventRuntime.CanOpenEventDoor(door, out _)));
        AdvanceTo(controller, ExtractionEventPhase.Active);
        var unlockAt = controller.MatchDeadline - ExtractionEventController.MatchDuration +
                       ExtractionEventController.ExtractionDelay;
        AssertModuleDoorsAvailable(installation);
        Core._now = unlockAt - TimeSpan.FromMilliseconds(1);
        AssertModuleDoorsAvailable(installation);
        Core._now = unlockAt;
        Assert.All(installation.Doors, door => Assert.True(ExtractionEventRuntime.CanOpenEventDoor(door, out _)));
        Core._now = controller.MatchDeadline - TimeSpan.FromMilliseconds(1);
        Assert.All(installation.Doors, door => Assert.True(ExtractionEventRuntime.CanOpenEventDoor(door, out _)));
        Core._now = controller.MatchDeadline;
        Assert.Equal(ExtractionEventPhase.Active, controller.Phase);
        Assert.All(installation.Doors, door => Assert.False(ExtractionEventRuntime.CanOpenEventDoor(door, out _)));
        controller.Advance();
        Assert.Equal(ExtractionEventPhase.Finishing, controller.Phase);
        Assert.All(installation.Doors, door => Assert.False(ExtractionEventRuntime.CanOpenEventDoor(door, out _)));
        Assert.True(ExtractionEventRuntime.CanOpenEventDoor(unmanaged, out _));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public void ManagedDoors_PreserveTimedOpening_AndCancelAtMatchDeadline(int doorIndex, bool directCompletion)
    {
        var controller = BuildLobby();
        var installation = Assert.IsType<ExtractionEventInstallation>(
            Assert.Single(controller.ManagedItems, item => item is ExtractionEventInstallation));
        var door = installation.Doors[doorIndex];
        var player = Player(ExtractionDungeonLayout.LobbyEntry);
        Assert.True(controller.TryStart(out var error), error);
        AdvanceTo(controller, ExtractionEventPhase.Active);
        var openingTimerField = typeof(EventDungeonDoor).GetField("_openingTimer", BindingFlags.Instance | BindingFlags.NonPublic);
        var openingMobileField = typeof(EventDungeonDoor).GetField("_openingMobile", BindingFlags.Instance | BindingFlags.NonPublic);
        player.MoveToWorld(door.Location, Map.Felucca);
        Assert.True(player.InLOS(door));
        if (doorIndex == 0)
        {
            var unlockAt = controller.MatchDeadline - ExtractionEventController.MatchDuration +
                           ExtractionEventController.ExtractionDelay;
            Core._now = unlockAt - TimeSpan.FromMilliseconds(1);
            door.Use(player);
            Assert.False(door.Open);
            Assert.False(((TimerExecutionToken)openingTimerField.GetValue(door)).Running);
            Assert.Null(openingMobileField.GetValue(door));
            AdvanceMilliseconds(PlayerMobile.DoorUseCooldownMilliseconds);
        }
        door.Use(player);
        Assert.True(((TimerExecutionToken)openingTimerField.GetValue(door)).Running);
        Assert.False(door.Open);
        AdvanceMilliseconds(3960);
        Assert.False(door.Open);
        AdvanceMilliseconds(40);
        Assert.True(door.Open);
        door.Open = false;
        door.Use(player);
        Assert.True(((TimerExecutionToken)openingTimerField.GetValue(door)).Running);
        Core._now = controller.MatchDeadline;
        Assert.False(ExtractionEventRuntime.CanOpenEventDoor(door, out _));
        if (directCompletion)
        {
            typeof(EventDungeonDoor).GetMethod("CompleteOpening", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(door, null);
        }
        else
        {
            AdvanceMilliseconds(EventDungeonOpening.ValidationIntervalMilliseconds);
        }
        Assert.False(door.Open);
        Assert.False(((TimerExecutionToken)openingTimerField.GetValue(door)).Running);
        Assert.Null(openingMobileField.GetValue(door));
    }

    [Fact]
    public void ManagedDoors_RemainSealedWhenRuntimeBindingIsUnavailable()
    {
        var controller = BuildLobby();
        var installation = Assert.IsType<ExtractionEventInstallation>(
            Assert.Single(controller.ManagedItems, item => item is ExtractionEventInstallation));
        Player(ExtractionDungeonLayout.LobbyEntry);
        Assert.True(controller.TryStart(out var error), error);
        AdvanceTo(controller, ExtractionEventPhase.Active);
        AssertModuleDoorsAvailable(installation);
        var adapterField = typeof(ExtractionEventRuntime).GetField("_adapter", BindingFlags.Static | BindingFlags.NonPublic);
        var adapter = adapterField.GetValue(null);
        adapterField.SetValue(null, null);
        try
        {
            Assert.All(installation.Doors, door => Assert.False(ExtractionEventRuntime.CanOpenEventDoor(door, out _)));
            Assert.True(ExtractionEventRuntime.CanOpenEventDoor(Track(new EventDungeonDoor()), out _));
        }
        finally
        {
            adapterField.SetValue(null, adapter);
        }
    }

    [Fact]
    public void PendingDeployment_PreservesWaitingDeadlineAndWorldStateAndPermitsOnlyExactOwnedReferences()
    {
        var controller = BuildLobby();
        var deadline = controller.WaitingDeadline;
        var round = controller.RoundNumber;
        var monster = Track(new Skeleton());
        controller.AddOwnedCreature(monster);
        var unrelated = Track(new Skeleton());
        var loot = Track(new Gold(50));
        controller.AddOwnedLoot(loot);
        loot.MoveToWorld(new Point3D(5268, 820, 0), Map.Felucca);
        var ownedGate = Gates(controller).Find(gate => gate.Kind == ExtractionEventGateKind.Extraction);
        var forgedGate = Track(new ExtractionEventGate(controller, ExtractionEventGateKind.Extraction));
        forgedGate.MoveToWorld(ownedGate.Location, Map.Felucca);
        Assert.False(ExtractionEventRuntime.TryGetDeploymentOwnership(out _, out _));
        SetDeploymentSuspended(true);
        Assert.True(ExtractionEventRuntime.TryGetDeploymentOwnership(out var ownership, out var error), error);
        Assert.True(ownership.IsValid(out error), error);
        Assert.True(ownership.OwnsDeploymentEntity(monster));
        Assert.True(ownership.OwnsDeploymentEntity(loot));
        Assert.True(ownership.OwnsDeploymentEntity(ownedGate));
        Assert.False(ownership.OwnsDeploymentEntity(unrelated));
        Assert.False(ownership.OwnsDeploymentEntity(forgedGate));
        Core._now = deadline + TimeSpan.FromMinutes(1);
        controller.Advance();
        Assert.Equal(ExtractionEventPhase.Waiting, controller.Phase);
        Assert.Equal(deadline, controller.WaitingDeadline);
        Assert.Equal(round, controller.RoundNumber);
        Assert.False(monster.Deleted);
        Assert.Equal(50, loot.Amount);
        Assert.False(controller.TryStart(out _));
        var anotherLoot = Track(new Gold(10));
        controller.AddOwnedLoot(anotherLoot);
        Assert.False(ownership.IsValid(out _));
    }

    [Fact]
    public void DeploymentOwnership_RefusesActiveParticipantsAndControlledCreatures()
    {
        var controller = BuildLobby();
        var monster = Track(new Skeleton());
        controller.AddOwnedCreature(monster);
        SetDeploymentSuspended(true);
        monster.Controlled = true;
        Assert.False(ExtractionEventRuntime.TryGetDeploymentOwnership(out _, out _));
        monster.Controlled = false;
        var participants = (List<Mobile>)typeof(ExtractionEventController)
            .GetField("_participantReferences", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        participants.Add(Player(ExtractionDungeonLayout.LobbyEntry));
        Assert.False(ExtractionEventRuntime.TryGetDeploymentOwnership(out _, out _));
        participants.Clear();
        var queue = (List<Mobile>)typeof(ExtractionEventController)
            .GetField("_queuedReferences", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
        queue.Add(Player(ExtractionDungeonLayout.LobbyEntry));
        Assert.False(ExtractionEventRuntime.TryGetDeploymentOwnership(out _, out _));
        queue.Clear();
    }

    [Fact]
    public void MovedOrUnregisteredGate_CannotTransportOrExtractPlayers()
    {
        var controller = BuildLobby();
        var player = Player(ExtractionDungeonLayout.GatePoint);
        var forged = Track(new ExtractionEventGate(controller, ExtractionEventGateKind.Entry));
        forged.MoveToWorld(ExtractionDungeonLayout.GatePoint, Map.Felucca);
        Assert.False(ExtractionEventRuntime.TryUseGate(forged, player, out _));
        var owned = Gates(controller).Find(gate => gate.Kind == ExtractionEventGateKind.Entry);
        var moved = new Point3D(owned.X + 2, owned.Y, owned.Z);
        owned.MoveToWorld(moved, Map.Felucca);
        player.MoveToWorld(moved, Map.Felucca);
        Assert.False(ExtractionEventRuntime.TryUseGate(owned, player, out _));
        Assert.Equal(moved, player.Location);
    }

    [Theory]
    [InlineData(5396, 823, true)]
    [InlineData(5405, 829, true)]
    [InlineData(5404, 829, false)]
    public void LobbyReturnGate_AcceptsOnlyItsOwnedLegacyOrSoutheastAnchor(int x, int y, bool accepted)
    {
        var controller = BuildLobby();
        var gate = Gates(controller).Find(candidate => candidate.Kind == ExtractionEventGateKind.LobbyReturn);
        gate.MoveToWorld(new Point3D(x, y, 0), Map.Felucca);
        var player = Player(gate.Location);
        Assert.Equal(accepted, ExtractionEventRuntime.TryUseGate(gate, player, out _));
        if (accepted)
        {
            Assert.InRange(player.GetDistanceToSqrt(ExtractionDungeonLayout.TownReturn), 0, 4);
        }
        else
        {
            Assert.Equal(gate.Location, player.Location);
        }
    }

    private static void AssertModuleDoorsAvailable(ExtractionEventInstallation installation)
    {
        for (var index = 0; index < installation.Doors.Count; index++)
        {
            var exteriorExit = index is 0 or 36 or 58;
            Assert.Equal(!exteriorExit, ExtractionEventRuntime.CanOpenEventDoor(installation.Doors[index], out _));
        }
    }

    private ExtractionEventController BuildLobby(string manifestVersion = "ModularDungeonV17")
    {
        Assert.True(TestServerInitializer.TileDataLoaded);
        var manifest = JsonSerializer.Deserialize<ModularDungeonDeployment.Manifest>(File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Data", manifestVersion, "install-manifest.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var doors = new EventDungeonDoor[71];
        for (var index = 0; index < doors.Length; index++)
        {
            var definition = manifest.Doors[index];
            Assert.Equal($"D{index:D3}", definition.Id);
            doors[index] = Track(new EventDungeonDoor(Enum.Parse<DoorFacing>(definition.Facing)));
            doors[index].MoveToWorld(definition.Closed.Point, Map.Felucca);
        }
        var chests = new EventDungeonChest[100];
        for (var index = 0; index < chests.Length; index++)
        {
            var definition = manifest.Chests[index];
            chests[index] = Track(definition.Type == "RootboundCellarChest" ? new RootboundCellarChest(definition.ItemId) : new EventDungeonChest(definition.ItemId));
            chests[index].MoveToWorld(definition.Position.Point, Map.Felucca);
        }
        var cellar = manifest.HiddenCellar;
        var features = RootboundCellarFeature.Create(Map.Felucca, cellar.Hatch.Point, cellar.Entry.Point, cellar.ReturnRune.Point,
            cellar.SurfaceReturn.Point, cellar.CreaturePosition.Point, Array.Find(chests, chest => chest is RootboundCellarChest));
        Track(features.Cover);
        Track(features.Hatch);
        Track(features.ReturnRune);
        Track(features.Lich);
        var type = typeof(ExtractionEventRuntime).GetNestedType("LobbyBuild", BindingFlags.NonPublic);
        var build = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, [manifest, doors, chests, features], null);
        var tick = type.GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic);
        for (var iteration = 0; iteration < 200; iteration++)
        {
            tick.Invoke(build, null);
            if (ExtractionEventRuntime.Controller?.IsBound == true)
            {
                return ExtractionEventRuntime.Controller;
            }
        }
        throw new InvalidOperationException("The real lobby build did not finish safely.");
    }

    private static void AdvanceTo(ExtractionEventController controller, ExtractionEventPhase phase)
    {
        for (var iteration = 0; iteration < 2000 && controller.Phase != phase; iteration++)
        {
            Assert.True(controller.Phase != ExtractionEventPhase.Faulted, controller.FaultMessage);
            controller.Advance();
        }
        Assert.Equal(phase, controller.Phase);
    }

    private static List<ExtractionEventGate> Gates(ExtractionEventController controller)
    {
        var gates = new List<ExtractionEventGate>();
        foreach (var item in controller.ManagedItems)
        {
            if (item is ExtractionEventGate gate)
            {
                gates.Add(gate);
            }
        }
        return gates;
    }

    private PlayerMobile Player(Point3D point)
    {
        var state = PacketTestUtilities.CreateTestNetState();
        _states.Add(state);
        state.Account = new MockAccount();
        var player = Track(new PlayerMobile { Body = 0x190, Player = true });
        player.AddItem(Track(new Backpack()));
        player.NetState = state;
        state.Mobile = player;
        player.MoveToWorld(point, Map.Felucca);
        return player;
    }

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }

    private static void InvokeRuntime(string method) => typeof(ExtractionEventRuntime).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);

    private static void SetDeploymentSuspended(bool suspended) => typeof(ExtractionEventRuntime)
        .GetField("_deploymentSuspended", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, suspended);

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
