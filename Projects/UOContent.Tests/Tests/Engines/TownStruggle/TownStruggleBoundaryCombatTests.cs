using System;
using System.Collections.Generic;
using Server;
using Server.Engines.TownStruggle;
using Server.Items;
using Server.Mobiles;
using Server.Network;
using Server.Spells;
using Server.Spells.Seventh;
using Server.Spells.Third;
using Server.Tests.Network;
using Xunit;

namespace UOContent.Tests.Engines.TownStruggle;

[Collection("Sequential UOContent Tests")]
public class TownStruggleBoundaryCombatTests : IDisposable
{
    private const string VenueJson = """
        {"name":"Boundary test","mapId":0,"x":5800,"y":3000,"width":100,"height":100,
         "neutralBase":{"x":5790,"y":3000,"z":0},"orderBase":{"x":5810,"y":3010,"z":0},
         "chaosBase":{"x":5890,"y":3090,"z":0},
         "capturePoints":[{"x":5830,"y":3030,"z":0},{"x":5850,"y":3050,"z":0},{"x":5870,"y":3070,"z":0}],
         "respawnPoints":[{"x":5810,"y":3090,"z":0},{"x":5890,"y":3010,"z":0},{"x":5870,"y":3030,"z":0}]}
        """;
    private readonly List<IEntity> _created = [];
    private readonly List<NetState> _states = [];
    private readonly DateTime _startTime = Core.Now;
    private readonly long _startTicks = Core.TickCount;
    private readonly TownStruggleController _previous = TownStruggleRuntime.Controller;
    private readonly TownStruggleController _controller;
    private readonly PlayerMobile _first;
    private readonly PlayerMobile _second;

    public TownStruggleBoundaryCombatTests()
    {
        Timer.Init(Core.TickCount);
        SetController(null);
        _controller = Track(new TownStruggleController());
        _controller.ConfigureVenue(VenueJson, new Host(), false);
        SetController(_controller);
        Assert.True(_controller.TryOpenSignup(out var error), error);
        _first = Player();
        _second = Player();
        Assert.True(_controller.TryJoin(_first, out error), error);
        Assert.True(_controller.TryJoin(_second, out error), error);
        Core._now = _controller.SignupDeadline;
        _controller.Advance();
        Assert.Equal(TownStrugglePhase.Active, _controller.Phase);
        foreach (var entry in _controller.Participants)
        {
            _created.Add(entry);
        }
        _first.MoveToWorld(new Point3D(5850, 3050, 0), Map.Felucca);
        _second.MoveToWorld(new Point3D(5851, 3050, 0), Map.Felucca);
    }

    public void Dispose()
    {
        SetController(null);
        foreach (var state in _states)
        {
            state.Dispose();
        }
        for (var index = _created.Count - 1; index >= 0; index--)
        {
            _created[index].Delete();
        }
        Core._now = _startTime;
        Core._tickCount = _startTicks;
        Timer.Init(Core.TickCount);
        SetController(_previous);
        if (_previous != null)
        {
            TownStruggleCombat.Publish(_previous);
        }
    }

    [Fact]
    public void DepartureStartsClockImmediately_ThirtySecondReturnIsAllowed()
    {
        var entry = _controller.GetParticipant(_first);
        _first.MoveToWorld(new Point3D(5799, 3050, 0), Map.Felucca);
        Assert.Equal(Core.Now, entry.OutsideSince);
        Core._now += TimeSpan.FromSeconds(30);
        Assert.True(TownStruggleCombat.AllowsMovement(_first, Direction.East));
        _first.MoveToWorld(new Point3D(5800, 3050, 0), Map.Felucca);
        Assert.Equal(DateTime.MinValue, entry.OutsideSince);
        Assert.False(entry.Disqualified);
    }

    [Fact]
    public void OverdueWalkerCannotReenterBeforeTimerTick()
    {
        _first.MoveToWorld(new Point3D(5799, 3050, 0), Map.Felucca);
        Core._now += TimeSpan.FromSeconds(30) + TimeSpan.FromTicks(1);
        Assert.False(TownStruggleCombat.AllowsMovement(_first, Direction.East));
        Assert.True(TownStruggleCombat.AllowsMovement(_first, Direction.West));
    }

    [Fact]
    public void OverdueForcedTeleportBouncesOutsideWithoutWaitingForTimer()
    {
        var entry = _controller.GetParticipant(_first);
        _first.MoveToWorld(new Point3D(5799, 3050, 0), Map.Felucca);
        Core._now += TimeSpan.FromSeconds(31);
        _first.MoveToWorld(new Point3D(5850, 3050, 0), Map.Felucca);
        Assert.True(entry.Disqualified);
        Assert.True(entry.Returned);
        Assert.False(_controller.Venue.Contains(_first));
        Assert.Equal(0, entry.Rewards);
    }

    [Fact]
    public void DepartedPlayerCannotWalkOrSpellBack_ButOrdinaryResidentCan()
    {
        var entry = _controller.GetParticipant(_first);
        entry.Disqualified = true;
        entry.Returned = true;
        _first.MoveToWorld(new Point3D(5799, 3050, 0), Map.Felucca);
        var region = Assert.IsType<TownStruggleRegion>(_second.Region);
        Assert.False(TownStruggleCombat.AllowsMovement(_first, Direction.East));
        Assert.False(region.OnMoveInto(_first, Direction.East, new Point3D(5800, 3050, 0), _first.Location));
        Assert.False(region.CheckTravel(_first, new Point3D(5800, 3050, 0), TravelCheckType.TeleportTo, out _));

        var resident = Player();
        resident.MoveToWorld(new Point3D(5799, 3050, 0), Map.Felucca);
        Assert.True(TownStruggleCombat.AllowsMovement(resident, Direction.East));
        Assert.True(region.OnMoveInto(resident, Direction.East, new Point3D(5800, 3050, 0), resident.Location));
        resident.MoveToWorld(new Point3D(5800, 3050, 0), Map.Felucca);
        Assert.True(_controller.Venue.Contains(resident));
        Assert.Null(_controller.GetParticipant(resident));
        Assert.False(_controller.TryJoin(resident, out _));
    }

    [Fact]
    public void DirectReentryAfterRecordedReturnIsBouncedAgain()
    {
        var entry = _controller.GetParticipant(_first);
        entry.Disqualified = true;
        entry.Returned = true;
        _first.MoveToWorld(new Point3D(5799, 3050, 0), Map.Felucca);
        _first.MoveToWorld(new Point3D(5850, 3050, 0), Map.Felucca);
        Assert.False(_controller.Venue.Contains(_first));
        Assert.True(entry.Returned);
    }

    [Fact]
    public void DisconnectBeginsClockBeforeDelayedLogoutInternalizesPlayer()
    {
        var entry = _controller.GetParticipant(_first);
        _first.NetState = null;
        Assert.Equal(Core.Now, entry.OutsideSince);
    }

    [Fact]
    public void CrossEventBodiesAndPetsNeverShoveOrConsumeStamina()
    {
        var resident = Player();
        resident.MoveToWorld(_first.Location, _first.Map);
        var pet = Track(new Horse());
        Assert.True(pet.SetControlMaster(resident));
        pet.MoveToWorld(_first.Location, _first.Map);
        _first.Stam = 0;
        resident.Stam = 0;
        Assert.True(_first.OnMoveOver(resident));
        Assert.True(resident.OnMoveOver(_first));
        Assert.True(_first.CheckShove(resident));
        Assert.True(_first.OnMoveOver(pet));
        Assert.True(pet.OnMoveOver(_first));
        Assert.Equal(0, _first.Stam);
        Assert.Equal(0, resident.Stam);
        Assert.False(TownStruggleCombat.IgnoreCollision(_first, _second));
        Assert.False(TownStruggleCombat.IgnoreCollision(resident, pet));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    public void OutsideBlockingFieldCannotSpillOverBoundary(int radius, bool eastToWest)
    {
        var resident = Player();
        resident.MoveToWorld(new Point3D(5790, 2990, 0), Map.Felucca);
        var center = eastToWest ? new Point3D(5800 - radius, 3050, 0) : new Point3D(5850, 3000 - radius, 0);
        Assert.False(TownStruggleCombat.AllowsBlockingField(resident, Map.Felucca, center, eastToWest, radius));
        var distant = eastToWest ? new Point3D(center.X - 1, center.Y, 0) : new Point3D(center.X, center.Y - 1, 0);
        Assert.True(TownStruggleCombat.AllowsBlockingField(resident, Map.Felucca, distant, eastToWest, radius));
    }

    [Fact]
    public void EnrolledBlockingFieldsStayInsideAndOrdinaryOtherMapsAreUnaffected()
    {
        Assert.True(TownStruggleCombat.AllowsBlockingField(_first, Map.Felucca, _first.Location, true, 2));
        Assert.False(TownStruggleCombat.AllowsBlockingField(_first, Map.Felucca, new Point3D(5800, 3050, 0), true, 2));
        var resident = Player();
        Assert.True(TownStruggleCombat.AllowsBlockingField(resident, Map.Trammel, _first.Location, true, 2));
    }

    [Fact]
    public void SignupPreventsPreplacedBlockingFieldsFromBothParticipantsAndResidents()
    {
        var resident = Player();
        typeof(TownStruggleController).GetProperty(nameof(TownStruggleController.Phase))
            .SetValue(_controller, TownStrugglePhase.Signup);
        Assert.False(TownStruggleCombat.AllowsBlockingField(resident, Map.Felucca, new Point3D(5850, 3050, 0), true, 2));
        Assert.False(TownStruggleCombat.AllowsBlockingField(_first, Map.Felucca, new Point3D(5850, 3050, 0), true, 2));
        Assert.False(TownStruggleCombat.AllowsBlockingField(resident, Map.Felucca, new Point3D(5798, 3050, 0), true, 2));
        Assert.True(TownStruggleCombat.AllowsBlockingField(resident, Map.Felucca, new Point3D(5797, 3050, 0), true, 2));
    }

    [Fact]
    public void BlockingSpellTargetsRejectOutsidersBeforeSpendingMana()
    {
        var resident = Player();
        resident.MoveToWorld(new Point3D(5849, 3050, 0), Map.Felucca);
        resident.Mana = resident.ManaMax;
        var mana = resident.Mana;
        new WallOfStoneSpell(resident).Target(_first.Location);
        new EnergyFieldSpell(resident).Target(_first.Location);
        Assert.Equal(mana, resident.Mana);
        foreach (var item in Map.Felucca.GetItemsInRange<Item>(_first.Location, 2))
        {
            Assert.False(item is WallOfStone or EnergyField);
        }
    }

    [Fact]
    public void VendorAccessDeniedToEnrolleeButNotPassingResident()
    {
        var region = Assert.IsType<TownStruggleRegion>(_first.Region);
        var resident = Player();
        resident.MoveToWorld(_first.Location, _first.Map);
        Assert.False(region.CheckVendorAccess(null, _first));
        Assert.True(region.CheckVendorAccess(null, resident));
    }

    [Fact]
    public void VendorOutsideOverlayCannotBypassEnrolledAccessDenial()
    {
        var resident = Player();
        var vendor = Track(new AnimalTrainer());
        vendor.MoveToWorld(new Point3D(5780, 2990, 0), Map.Felucca);
        Assert.False(vendor.CheckVendorAccess(_first));
        Assert.True(vendor.CheckVendorAccess(resident));
    }

    [Fact]
    public void PreviouslyStartedOutsideBandageCannotCurePoisonOrBleeding()
    {
        var healer = Player();
        healer.MoveToWorld(_first.Location, _first.Map);
        healer.Skills.Healing.Base = 120;
        healer.Skills.Anatomy.Base = 120;
        _first.Poison = new PoisonImpl("Boundary Test Poison", 0, 0, 1, 1, 1, 60, 60, 1, 1);
        try
        {
            Assert.True(_first.Poisoned);
            new ProbeBandage(healer, _first).Complete();
            Assert.True(_first.Poisoned);
        }
        finally
        {
            _first.Poison = null;
        }

        BleedAttack.BeginBleed(_first, _second);
        try
        {
            Assert.True(BleedAttack.IsBleeding(_first));
            new ProbeBandage(healer, _first).Complete();
            Assert.True(BleedAttack.IsBleeding(_first));
        }
        finally
        {
            BleedAttack.EndBleed(_first, false);
        }
    }

    [Fact]
    public void NonparticipantBandagesRemainFunctional()
    {
        var healer = Player();
        var patient = Player();
        patient.MoveToWorld(healer.Location, healer.Map);
        BleedAttack.BeginBleed(patient, _second);
        try
        {
            new ProbeBandage(healer, patient).Complete();
            Assert.False(BleedAttack.IsBleeding(patient));
        }
        finally
        {
            BleedAttack.EndBleed(patient, false);
        }
    }

    private PlayerMobile Player()
    {
        var state = PacketTestUtilities.CreateTestNetState();
        _states.Add(state);
        var account = new MockAccount();
        state.Account = account;
        var player = Track(new PlayerMobile { Player = true, Body = 0x190, RawStr = 100, RawDex = 100, RawInt = 100, Account = account });
        player.NetState = state;
        state.Mobile = player;
        player.MoveToWorld(new Point3D(5780, 2990, 0), Map.Felucca);
        return player;
    }

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }

    private static void SetController(TownStruggleController controller) =>
        typeof(TownStruggleRuntime).GetProperty(nameof(TownStruggleRuntime.Controller)).SetValue(null, controller);

    private sealed class ProbeBandage(Mobile healer, Mobile patient) : BandageContext(healer, patient, TimeSpan.FromSeconds(1))
    {
        internal void Complete() => base.OnTick();
    }

    private sealed class Host : ITownStruggleHost
    {
        public DateTime Now => Core.Now;
        public bool Eligible(Mobile player) => player is { Deleted: false, Alive: true, NetState: not null };
        public bool AtNeutralBase(Mobile player, TownStruggleVenue venue) => player.Map == venue.Map && player.Location == venue.NeutralBase;
        public bool Move(Mobile player, Point3D point, Map map)
        {
            player.MoveToWorld(point, map);
            return true;
        }
        public bool MoveOutside(Mobile player, TownStruggleVenue venue, Point3D requestedPoint, Map requestedMap)
        {
            player.MoveToWorld(new Point3D(venue.Area.Start.X - 2, venue.Area.Start.Y, 0), venue.Map);
            return true;
        }
        public void Notify(TownStruggleController controller)
        {
            if (controller.Phase == TownStrugglePhase.Active)
            {
                controller.RegisterRegion();
            }
        }
    }
}
