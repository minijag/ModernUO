using System;
using System.Collections.Generic;
using System.Reflection;
using Server;
using Server.Engines.TownStruggle;
using Server.Items;
using Server.Misc;
using Server.Mobiles;
using Server.Regions;
using Server.Spells.Fifth;
using Server.Spells.First;
using Server.Spells.Seventh;
using Xunit;

namespace UOContent.Tests.Engines.TownStruggle;

[Collection("Sequential UOContent Tests")]
public class TownStruggleCombatTests : IDisposable
{
    private static readonly TownStruggleVenue Venue = new("Combat test", Map.Felucca,
        new Rectangle2D(5800, 3000, 100, 100), new Point3D(100, 100, 0),
        new Point3D(5810, 3010, 0), new Point3D(5890, 3090, 0), [], []);
    private readonly List<IEntity> _created = [];
    private readonly List<Region> _regions = [];
    private readonly NotorietyHandler _notoriety = Notoriety.Handler;
    private readonly DateTime _startTime = Core.Now;
    private readonly long _startTicks = Core.TickCount;
    private readonly TownStruggleController _previousController = TownStruggleRuntime.Controller;

    public TownStruggleCombatTests()
    {
        Timer.Init(Core.TickCount);
        TownStruggleCombat.Clear(null);
        SetController(null);
    }

    public void Dispose()
    {
        TownStruggleCombat.Clear(null);
        foreach (var region in _regions)
        {
            region.Unregister();
        }
        for (var index = _created.Count - 1; index >= 0; index--)
        {
            _created[index].Delete();
        }
        Notoriety.Handler = _notoriety;
        Core._now = _startTime;
        Core._tickCount = _startTicks;
        Timer.Init(Core.TickCount);
        SetController(_previousController);
        if (_previousController != null)
        {
            TownStruggleCombat.Publish(_previousController);
        }
    }

    [Theory]
    [InlineData(TownStruggleTeam.Order, TownStruggleTeam.Order, Notoriety.Ally)]
    [InlineData(TownStruggleTeam.Order, TownStruggleTeam.Chaos, Notoriety.Enemy)]
    [InlineData(TownStruggleTeam.Chaos, TownStruggleTeam.Order, Notoriety.Enemy)]
    public void ActiveSnapshot_OverridesOrdinaryCriminalAndGuildNotoriety(
        TownStruggleTeam firstTeam, TownStruggleTeam secondTeam, int expected)
    {
        var first = Participant(firstTeam, 5850, 3050);
        var second = Participant(secondTeam, 5851, 3050);
        second.Player.Criminal = true;
        Publish(TownStrugglePhase.Active, first, second);

        Assert.Equal(expected, NotorietyHandlers.MobileNotoriety(first.Player, second.Player));
    }

    [Fact]
    public void Snapshot_DoesNotObserveMutableRosterUntilPublication()
    {
        var first = Participant(TownStruggleTeam.Order, 5850, 3050);
        var second = Participant(TownStruggleTeam.Chaos, 5851, 3050);
        var roster = new List<TownStruggleParticipant> { first, second };
        TownStruggleCombat.Publish((Serial)0x40001000u, TownStrugglePhase.Active, Venue, roster);
        second.Team = TownStruggleTeam.Order;
        roster.Clear();

        Assert.True(TownStruggleCombat.TryGetNotoriety(first.Player, second.Player, out var notoriety));
        Assert.Equal(Notoriety.Enemy, notoriety);

        Publish(TownStrugglePhase.Active, first, second);
        Assert.True(TownStruggleCombat.TryGetNotoriety(first.Player, second.Player, out notoriety));
        Assert.Equal(Notoriety.Ally, notoriety);
    }

    [Theory]
    [InlineData(TownStrugglePhase.Dormant)]
    [InlineData(TownStrugglePhase.Signup)]
    [InlineData(TownStrugglePhase.Results)]
    [InlineData(TownStrugglePhase.Faulted)]
    public void InactiveSnapshot_DoesNotCreateEnemies(TownStrugglePhase phase)
    {
        var first = Participant(TownStruggleTeam.Order, 5850, 3050);
        var second = Participant(TownStruggleTeam.Chaos, 5851, 3050);
        Publish(phase, first, second);
        Assert.False(TownStruggleCombat.TryGetNotoriety(first.Player, second.Player, out _));
    }

    [Theory]
    [InlineData("Outside")]
    [InlineData("OtherMap")]
    [InlineData("Disqualified")]
    [InlineData("Returned")]
    [InlineData("Neutral")]
    [InlineData("Staff")]
    [InlineData("Deleted")]
    public void InvalidOpponent_DoesNotReceiveEventNotoriety(string invalidation)
    {
        var first = Participant(TownStruggleTeam.Order, 5850, 3050);
        var second = Participant(TownStruggleTeam.Chaos, 5851, 3050);
        switch (invalidation)
        {
            case "Outside": second.Player.MoveToWorld(new Point3D(5900, 3000, 0), Map.Felucca); break;
            case "OtherMap": second.Player.MoveToWorld(second.Player.Location, Map.Trammel); break;
            case "Disqualified": second.Disqualified = true; break;
            case "Returned": second.Returned = true; break;
            case "Neutral": second.Team = TownStruggleTeam.Neutral; break;
            case "Staff": second.Player.AccessLevel = AccessLevel.GameMaster; break;
            case "Deleted": second.Player.Delete(); break;
        }
        Publish(TownStrugglePhase.Active, first, second);
        Assert.False(TownStruggleCombat.TryGetNotoriety(first.Player, second.Player, out _));
    }

    [Fact]
    public void OpponentAggression_DoesNotCreateCriminalOrMurderReports()
    {
        var first = Participant(TownStruggleTeam.Order, 5850, 3050);
        var second = Participant(TownStruggleTeam.Chaos, 5851, 3050);
        Publish(TownStrugglePhase.Active, first, second);
        Notoriety.Handler = NotorietyHandlers.MobileNotoriety;

        first.Player.DoHarmful(second.Player);

        Assert.False(first.Player.Criminal);
        var aggression = Assert.Single(second.Player.Aggressors);
        Assert.False(aggression.CriminalAggression);
        Assert.False(aggression.CanReportMurder);
    }

    [Fact]
    public void TeamBases_IncludeTheirEdgesAndOnlyExcludeOpponents()
    {
        Assert.True(TownStruggleCombat.IsInBase(Venue, new Point3D(5815, 3015, 0)));
        Assert.False(TownStruggleCombat.IsInBase(Venue, new Point3D(5816, 3015, 0)));
        Assert.True(TownStruggleCombat.IsInEnemyBase(Venue, TownStruggleTeam.Chaos, Venue.OrderBase));
        Assert.False(TownStruggleCombat.IsInEnemyBase(Venue, TownStruggleTeam.Order, Venue.OrderBase));
        Assert.True(TownStruggleCombat.IsInEnemyBase(Venue, TownStruggleTeam.Order, Venue.ChaosBase));
    }

    [Fact]
    public void IdentityAndPetSpells_AreRestrictedButHealingIsNot()
    {
        var player = Participant(TownStruggleTeam.Order, 5850, 3050).Player;
        Assert.True(TownStruggleRegion.IsRestrictedSpell(new IncognitoSpell(player)));
        Assert.True(TownStruggleRegion.IsRestrictedSpell(new PolymorphSpell(player, null)));
        Assert.True(TownStruggleRegion.IsRestrictedSpell(new SummonCreatureSpell(player)));
        Assert.False(TownStruggleRegion.IsRestrictedSpell(new HealSpell(player)));
    }

    [Fact]
    public void AuthoritativeRoster_IsolatesTeamsAndOutsidersInBothDirections()
    {
        var (controller, first, enemy, teammate) = ActiveMatch();
        var outsider = Player(5853, 3050);

        Assert.True(NotorietyHandlers.Mobile_AllowHarmful(first, enemy));
        Assert.False(NotorietyHandlers.Mobile_AllowHarmful(first, teammate));
        Assert.False(NotorietyHandlers.Mobile_AllowHarmful(first, outsider));
        Assert.False(NotorietyHandlers.Mobile_AllowHarmful(outsider, first));
        Assert.True(NotorietyHandlers.Mobile_AllowBeneficial(first, teammate));
        Assert.False(NotorietyHandlers.Mobile_AllowBeneficial(first, enemy));
        Assert.False(NotorietyHandlers.Mobile_AllowBeneficial(first, outsider));
        Assert.False(NotorietyHandlers.Mobile_AllowBeneficial(outsider, first));

        controller.GetParticipant(first).Returned = true;
        TownStruggleCombat.Publish(controller);
        Assert.False(TownStruggleCombat.TryAllowHarmful(first, outsider, out _));
        Assert.False(TownStruggleCombat.TryAllowBeneficial(first, outsider, out _));
    }

    [Fact]
    public void DirectDelayedDamage_CannotCrossTeamOrAreaBoundaries()
    {
        var (_, first, enemy, teammate) = ActiveMatch();
        var outsider = Player(5853, 3050);
        enemy.Hits = enemy.HitsMax;
        first.Damage(20, teammate);
        first.Damage(20, outsider);
        Assert.Equal(first.HitsMax, first.Hits);

        enemy.Damage(10, first);
        Assert.Equal(enemy.HitsMax - 10, enemy.Hits);

        first.MoveToWorld(new Point3D(5901, 3050, 0), Map.Felucca);
        enemy.Damage(10, first);
        Assert.Equal(enemy.HitsMax - 10, enemy.Hits);
    }

    [Fact]
    public void Healing_IsTeamOnlyAndCreditsActualMissingHits()
    {
        var (controller, first, enemy, teammate) = ActiveMatch();
        teammate.Hits = teammate.HitsMax - 7;
        teammate.Heal(100, enemy, false);
        Assert.Equal(teammate.HitsMax - 7, teammate.Hits);

        teammate.Heal(100, first, false);
        Assert.Equal(teammate.HitsMax, teammate.Hits);
        Assert.Equal(7, controller.GetParticipant(first).Healing);
        teammate.Heal(100, first, false);
        Assert.Equal(7, controller.GetParticipant(first).Healing);
    }

    [Fact]
    public void LethalDamage_PreservesOrdinaryEquipmentAndDoesNotCreateCorpse()
    {
        var (controller, first, enemy, _) = ActiveMatch();
        var backpack = new Backpack();
        _created.Add(backpack);
        enemy.AddItem(backpack);
        var robe = new Robe();
        _created.Add(robe);
        Assert.True(enemy.EquipItem(robe));
        enemy.Hits = 25;

        enemy.Damage(100, first);

        Assert.True(enemy.Alive);
        Assert.Null(enemy.Corpse);
        Assert.Same(enemy, robe.Parent);
        Assert.Same(enemy, backpack.Parent);
        Assert.Equal(enemy.HitsMax, enemy.Hits);
        Assert.Equal(controller.Venue.TeamBase(controller.GetParticipant(enemy).Team), enemy.Location);
        Assert.Equal(1, controller.GetParticipant(enemy).Deaths);
        Assert.Equal(10, controller.GetParticipant(first).KillPoints);
    }

    [Fact]
    public void RespawnMovement_StaysInsideOwnBaseUntilReadyAndNeverEntersEnemyBase()
    {
        var (controller, first, _, _) = ActiveMatch();
        var participant = controller.GetParticipant(first);
        first.MoveToWorld(new Point3D(5815, 3010, 0), Map.Felucca);
        participant.RespawnAt = Core.Now + TimeSpan.FromSeconds(30);

        Assert.False(TownStruggleCombat.AllowsMovement(first, Direction.East));
        Assert.True(TownStruggleCombat.AllowsMovement(first, Direction.West));
        Core._now = participant.RespawnAt;
        Assert.True(TownStruggleCombat.AllowsMovement(first, Direction.East));
        first.MoveToWorld(new Point3D(5884, 3090, 0), Map.Felucca);
        Assert.False(TownStruggleCombat.AllowsMovement(first, Direction.East));
    }

    [Fact]
    public void NativeMountGate_OnlyBlocksEnrolledParticipants()
    {
        var (_, first, _, _) = ActiveMatch();
        var outsider = Player(5853, 3050);
        Assert.False(BaseMount.CheckMountAllowed(first));
        Assert.True(BaseMount.CheckMountAllowed(outsider));
        Assert.True(((TownStruggleRegion)outsider.Region).MountsAllowed);
    }

    [Fact]
    public void TownOverlay_PreservesOriginalGuardFootprintForNonparticipants()
    {
        var (_, first, _, _) = ActiveMatch();
        var guards = new GuardProbeRegion(new Rectangle2D(5840, 3040, 20, 20));
        guards.Register();
        _regions.Add(guards);
        var outsider = Player(5853, 3050);
        var eventRegion = Assert.IsType<TownStruggleRegion>(first.Region);

        eventRegion.MakeGuard(first);
        Assert.Equal(0, guards.Spawns);
        eventRegion.MakeGuard(outsider);
        Assert.Equal(1, guards.Spawns);
        outsider.MoveToWorld(new Point3D(5875, 3050, 0), Map.Felucca);
        eventRegion.MakeGuard(outsider);
        Assert.Equal(1, guards.Spawns);
    }

    [Fact]
    public void ExistingIdleGuards_CannotAcquireEventParticipants()
    {
        var (_, first, _, _) = ActiveMatch();
        var guard = new GuardProbe();
        _created.Add(guard);
        guard.MoveToWorld(new Point3D(5853, 3050, 0), Map.Felucca);
        BaseGuard.Spawn(first.Region, first);
        Assert.Null(guard.Focus);

        var outsider = Player(5854, 3050);
        BaseGuard.Spawn(first.Region, outsider);
        Assert.Same(outsider, guard.Focus);
    }

    private sealed class GuardProbeRegion : GuardedRegion
    {
        internal int Spawns { get; private set; }
        internal GuardProbeRegion(Rectangle2D area) : base("Guard test", Map.Felucca, 100, area)
        {
        }
        public override void MakeGuard(Mobile focus) => Spawns++;
    }

    private sealed class GuardProbe : BaseGuard
    {
        internal GuardProbe() : base(null)
        {
        }
        public override Mobile Focus { get; set; }
        public override void NonLethalAttack(Mobile target)
        {
        }
        public override void OnAfterDelete()
        {
            Focus = null;
            base.OnAfterDelete();
        }
    }

    [Fact]
    public void EnrolledPlayersCannotAcquireOrReceivePets_ExistingOwnerIsPreserved()
    {
        var (_, first, _, _) = ActiveMatch();
        var owner = Player(5860, 3060);
        var pet = new Horse();
        _created.Add(pet);
        Assert.True(pet.SetControlMaster(owner));

        Assert.False(pet.SetControlMaster(first));
        Assert.Same(owner, pet.ControlMaster);
        Assert.True(pet.Controlled);
        Assert.Equal(0, first.Followers);
        Assert.True(pet.SetControlMaster(null));
        Assert.False(pet.SetControlMaster(first));
        Assert.Null(pet.ControlMaster);
    }

    [Fact]
    public void StableClaimsAreDeniedForParticipantsButUnrelatedPlayersRemainAllowed()
    {
        var (_, first, _, _) = ActiveMatch();
        var outsider = Player(5860, 3060);
        var trainer = new AnimalTrainer();
        var pet = new Horse { IsStabled = true, StabledBy = first };
        _created.Add(trainer);
        _created.Add(pet);

        Assert.False(trainer.CanClaim(first, pet));
        Assert.True(trainer.CanClaim(outsider, pet));
        Assert.True(pet.IsStabled);
        Assert.Same(first, pet.StabledBy);
        Assert.Null(pet.ControlMaster);
    }

    [Fact]
    public void AutoStableLoginRecoveryRetainsStabledPetDuringTownStruggle()
    {
        var (_, first, _, _) = ActiveMatch();
        var pet = new Horse { IsStabled = true, StabledBy = first };
        _created.Add(pet);
        var stabled = new HashSet<Mobile> { pet };
        var automatic = new HashSet<Mobile> { pet };
        typeof(PlayerMobile).GetProperty(nameof(PlayerMobile.Stabled)).SetValue(first, stabled);
        typeof(PlayerMobile).GetProperty(nameof(PlayerMobile.AutoStabled)).SetValue(first, automatic);

        first.ClaimAutoStabledPets();

        Assert.Same(automatic, first.AutoStabled);
        Assert.Contains(pet, first.Stabled);
        Assert.True(pet.IsStabled);
        Assert.Same(first, pet.StabledBy);
        Assert.Null(pet.ControlMaster);
        Assert.Equal(Map.Internal, pet.Map);
    }

    [Fact]
    public void DisguiseValidationRejectsEvenPreopenedGumpRepliesDuringEvent()
    {
        var (_, first, _, _) = ActiveMatch();
        first.NpcGuild = NpcGuild.ThievesGuild;
        var pack = new Backpack();
        var kit = new DisguiseKit();
        _created.Add(pack);
        _created.Add(kit);
        first.AddItem(pack);
        pack.DropItem(kit);

        Assert.False(kit.ValidateUse(first));

        var outsider = Player(5860, 3060);
        outsider.NpcGuild = NpcGuild.ThievesGuild;
        var outsiderPack = new Backpack();
        _created.Add(outsiderPack);
        outsider.AddItem(outsiderPack);
        outsiderPack.DropItem(kit);
        Assert.True(kit.ValidateUse(outsider));
    }

    private (TownStruggleController Controller, PlayerMobile First, PlayerMobile Enemy, PlayerMobile Teammate) ActiveMatch()
    {
        const string venueJson = """
            {"name":"Combat test","mapId":0,"x":5800,"y":3000,"width":100,"height":100,
             "neutralBase":{"x":100,"y":100,"z":0},
             "orderBase":{"x":5810,"y":3010,"z":0},"chaosBase":{"x":5890,"y":3090,"z":0},
             "capturePoints":[{"x":5830,"y":3030,"z":0},{"x":5850,"y":3050,"z":0},{"x":5870,"y":3070,"z":0}],
             "respawnPoints":[{"x":5810,"y":3090,"z":0},{"x":5890,"y":3010,"z":0},{"x":5870,"y":3030,"z":0}]}
            """;
        var controller = new TownStruggleController();
        _created.Add(controller);
        SetController(controller);
        controller.ConfigureVenue(venueJson, new Host(), false);
        Assert.True(controller.TryOpenSignup(out var openError), openError);
        var first = Player(5850, 3050);
        var enemy = Player(5851, 3050);
        var teammate = Player(5852, 3050);
        Assert.True(controller.TryJoin(first, out var firstError), firstError);
        Assert.True(controller.TryJoin(enemy, out var enemyError), enemyError);
        Assert.True(controller.TryJoin(teammate, out var teamError), teamError);
        Core._now = controller.SignupDeadline;
        controller.Advance();
        Assert.Equal(TownStrugglePhase.Active, controller.Phase);
        foreach (var entry in controller.Participants)
        {
            _created.Add(entry);
            entry.Team = entry.Player == enemy ? TownStruggleTeam.Chaos : TownStruggleTeam.Order;
        }
        first.MoveToWorld(new Point3D(5850, 3050, 0), Map.Felucca);
        enemy.MoveToWorld(new Point3D(5851, 3050, 0), Map.Felucca);
        teammate.MoveToWorld(new Point3D(5852, 3050, 0), Map.Felucca);
        TownStruggleCombat.Publish(controller);
        return (controller, first, enemy, teammate);
    }

    private sealed class Host : ITownStruggleHost
    {
        public DateTime Now => Core.Now;
        public bool Eligible(Mobile player) => player is { Deleted: false, Alive: true };
        public bool AtNeutralBase(Mobile player, TownStruggleVenue venue) => true;
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

    private static void SetController(TownStruggleController controller) =>
        typeof(TownStruggleRuntime).GetProperty(nameof(TownStruggleRuntime.Controller), BindingFlags.Public | BindingFlags.Static)
            .SetValue(null, controller);

    private static void Publish(TownStrugglePhase phase, params TownStruggleParticipant[] participants) =>
        TownStruggleCombat.Publish((Serial)0x40001000u, phase, Venue, participants);

    private TownStruggleParticipant Participant(TownStruggleTeam team, int x, int y)
    {
        var player = Player(x, y);
        var participant = new TownStruggleParticipant { Player = player, Team = team };
        _created.Add(participant);
        return participant;
    }

    private PlayerMobile Player(int x, int y)
    {
        var player = new PlayerMobile { Player = true, Body = 0x190, Young = false, RawStr = 100 };
        _created.Add(player);
        player.MoveToWorld(new Point3D(x, y, 0), Map.Felucca);
        return player;
    }
}
