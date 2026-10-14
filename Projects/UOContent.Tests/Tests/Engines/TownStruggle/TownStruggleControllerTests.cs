using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Server;
using Server.Accounting;
using Server.Accounting.Security;
using Server.Engines.ModularDungeon;
using Server.Engines.TownStruggle;
using Server.Items;
using Server.Mobiles;
using Server.Network;
using Server.Tests.Network;
using Xunit;

namespace UOContent.Tests.Engines.TownStruggle;

[Collection("Sequential UOContent Tests")]
public class TownStruggleControllerTests : IDisposable
{
    private const string VenueJson = """
        {"name":"Test Town","mapId":0,"x":100,"y":100,"width":200,"height":200,
         "neutralBase":{"x":90,"y":90,"z":0},"orderBase":{"x":110,"y":110,"z":0},
         "chaosBase":{"x":280,"y":280,"z":0},
         "capturePoints":[{"x":140,"y":140,"z":0},{"x":240,"y":140,"z":0},{"x":200,"y":240,"z":0}],
         "respawnPoints":[{"x":140,"y":220,"z":0},{"x":220,"y":180,"z":0},{"x":160,"y":180,"z":0}]}
        """;
    private readonly List<IEntity> _created = [];
    private readonly List<NetState> _states = [];
    private readonly List<Account> _accounts = [];
    private readonly DateTime _now = Core.Now;
    private readonly long _ticks = Core.TickCount;
    private readonly PasswordProtectionAlgorithm _originalAlgorithm = AccountSecurity.CurrentAlgorithm;

    public TownStruggleControllerTests()
    {
        Timer.Init(Core.TickCount);
        AccountSecurity.CurrentAlgorithm = PasswordProtectionAlgorithm.SHA2;
    }

    public void Dispose()
    {
        foreach (var entity in _created)
        {
            if (entity is TownStruggleController controller)
            {
                var participants = new List<TownStruggleParticipant>(controller.Participants);
                foreach (var participant in participants) { participant.Delete(); }
            }
        }
        for (var i = _created.Count - 1; i >= 0; i--)
        {
            if (_created[i] is Mobile mobile) { mobile.NetState = null; }
            _created[i].Delete();
        }
        foreach (var state in _states) { state.Dispose(); }
        foreach (var account in _accounts) { account.Delete(); }
        AccountSecurity.CurrentAlgorithm = _originalAlgorithm;
        TownStruggleCombat.Clear(null);
        Core._now = _now;
        Core._tickCount = _ticks;
        Timer.Init(Core.TickCount);
    }

    [Fact]
    public void SignupIsExplicitRejectsDuplicatesAndBalancesTeamsAtDeadline()
    {
        var (controller, host) = Create();
        var outside = Player();
        Assert.False(controller.TryJoin(outside, out _));
        Assert.True(controller.TryOpenSignup(out var error), error);
        var signupEnd = Core.Now + TimeSpan.FromMinutes(15);
        Assert.Equal(signupEnd, controller.SignupDeadline);
        for (var i = 0; i < 7; i++)
        {
            var player = Player();
            Assert.True(controller.TryJoin(player, out error), error);
            Assert.Equal(controller.Venue.NeutralBase, player.Location);
            Assert.False(controller.TryJoin(player, out _));
        }
        Assert.Null(controller.GetParticipant(outside));
        Assert.Equal(TownStrugglePhase.Signup, controller.Phase);
        Core._now = signupEnd - TimeSpan.FromSeconds(1);
        controller.Advance();
        Assert.Equal(TownStrugglePhase.Signup, controller.Phase);
        Core._now = signupEnd;
        Assert.False(controller.TryJoin(outside, out _));
        controller.Advance();
        Assert.Equal(TownStrugglePhase.Active, controller.Phase);
        Assert.Equal(Core.Now + TimeSpan.FromMinutes(15), controller.MatchDeadline);
        var order = 0;
        var chaos = 0;
        foreach (var participant in controller.Participants)
        {
            if (participant.Team == TownStruggleTeam.Order) { order++; } else { chaos++; }
            Assert.Equal(controller.Venue.TeamBase(participant.Team), participant.Player.Location);
        }
        Assert.Equal(4, order);
        Assert.Equal(3, chaos);
        Assert.True(host.Notifications >= 9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void FewerThanTwoEligibleSignupsReturnsEveryoneWithoutStarting(int count)
    {
        var (controller, _) = Create();
        Assert.True(controller.TryOpenSignup(out _));
        Mobile player = null;
        var original = new Point3D(50, 50, 0);
        if (count == 1)
        {
            player = Player();
            original = player.Location;
            Assert.True(controller.TryJoin(player, out _));
        }
        Core._now = controller.SignupDeadline;
        controller.Advance();
        Assert.Equal(TownStrugglePhase.Dormant, controller.Phase);
        Assert.Empty(controller.Participants);
        if (player != null) { Assert.Equal(original, player.Location); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LobbyInstallationHoldsSignupWithoutChangingDeadlineOrRoster(int count)
    {
        var (controller, _) = Create();
        Assert.True(controller.TryOpenSignup(out _));
        for (var i = 0; i < count; i++)
        {
            Assert.True(controller.TryJoin(Player(), out _));
        }
        var deadline = controller.SignupDeadline;
        var round = controller.RoundNumber;
        var oldBusy = ExtractionLobbyDecoration.IsBusy;
        var property = typeof(ExtractionLobbyDecoration).GetProperty(nameof(ExtractionLobbyDecoration.IsBusy))!;
        try
        {
            property.SetValue(null, true);
            Assert.False(controller.TryJoin(Player(), out var error));
            Assert.Contains("lobby installation", error);
            Core._now = deadline + TimeSpan.FromSeconds(20);
            controller.Advance();
            Assert.Equal(TownStrugglePhase.Signup, controller.Phase);
            Assert.Equal(deadline, controller.SignupDeadline);
            Assert.Equal(round, controller.RoundNumber);
            Assert.Equal(count, controller.Participants.Count);
            foreach (var participant in controller.Participants)
            {
                Assert.Equal(TownStruggleTeam.Neutral, participant.Team);
                Assert.Equal(controller.Venue.NeutralBase, participant.Player.Location);
            }
            property.SetValue(null, false);
            controller.Advance();
            Assert.Equal(count == 0 ? TownStrugglePhase.Dormant : TownStrugglePhase.Active, controller.Phase);
            Assert.Equal(round, controller.RoundNumber);
            Assert.Equal(count, controller.Participants.Count);
        }
        finally
        {
            property.SetValue(null, oldBusy);
        }
    }

    [Fact]
    public void IneligibleOrAbsentSignupIsReturnedBeforeTeamAssignment()
    {
        var (controller, host) = Create();
        Assert.True(controller.TryOpenSignup(out _));
        var first = Player();
        var second = Player();
        var third = Player();
        Assert.True(controller.TryJoin(first, out _));
        Assert.True(controller.TryJoin(second, out _));
        var thirdOrigin = third.Location;
        Assert.True(controller.TryJoin(third, out _));
        host.Ineligible.Add(third);
        Core._now = controller.SignupDeadline;
        controller.Advance();
        Assert.Equal(TownStrugglePhase.Active, controller.Phase);
        Assert.Equal(2, controller.Participants.Count);
        Assert.Null(controller.GetParticipant(third));
        Assert.Equal(thirdOrigin, third.Location);
    }

    [Fact]
    public void SoleOccupancyCapturesScoresAndContestingFreezesOwnership()
    {
        var (controller, _) = Active(4);
        var order = Team(controller, TownStruggleTeam.Order);
        var chaos = Team(controller, TownStruggleTeam.Chaos);
        order[0].Player.MoveToWorld(controller.Venue.CapturePoints[0], Map.Felucca);
        order[1].Player.MoveToWorld(controller.Venue.CapturePoints[0], Map.Felucca);
        Seconds(controller, 19);
        Assert.Equal(new TownStruggleCaptureState(95, TownStruggleTeam.Neutral), controller.CaptureStates[0]);
        Seconds(controller, 1);
        Assert.Equal(new TownStruggleCaptureState(100, TownStruggleTeam.Order), controller.CaptureStates[0]);
        Seconds(controller, 10);
        Assert.Equal(10, controller.OrderScore);
        Assert.Equal(5d, order[0].ControlPoints);
        Assert.Equal(5d, order[1].ControlPoints);
        chaos[0].Player.MoveToWorld(controller.Venue.CapturePoints[0], Map.Felucca);
        Seconds(controller, 30);
        Assert.Equal(new TownStruggleCaptureState(100, TownStruggleTeam.Order), controller.CaptureStates[0]);
        Assert.Equal(15, controller.OrderScore);
        Assert.Equal(5, controller.ChaosScore);
        Assert.Equal(7.5d, order[0].ControlPoints);
        Assert.Equal(5d, chaos[0].ControlPoints);
    }

    [Fact]
    public void HiddenDisconnectedAndRespawningPlayersDoNotCapture()
    {
        var (controller, _) = Active(4);
        var entries = Team(controller, TownStruggleTeam.Order);
        entries[0].Player.MoveToWorld(controller.Venue.CapturePoints[0], Map.Felucca);
        entries[0].Player.Hidden = true;
        entries[1].Player.MoveToWorld(controller.Venue.CapturePoints[0], Map.Felucca);
        entries[1].RespawnAt = Core.Now + TimeSpan.FromMinutes(1);
        Seconds(controller, 5);
        Assert.Equal(default, controller.CaptureStates[0]);
        entries[0].Player.Hidden = false;
        entries[0].Player.NetState = null;
        Seconds(controller, 5);
        Assert.Equal(default, controller.CaptureStates[0]);
    }

    [Fact]
    public void DefeatCreditsRecentEnemyContributorsOnceAndKeepsInventory()
    {
        var (controller, _) = Active(4);
        var enemies = Team(controller, TownStruggleTeam.Order);
        var victim = Team(controller, TownStruggleTeam.Chaos)[0];
        foreach (var attacker in enemies) { attacker.Player.MoveToWorld(controller.Venue.RespawnPoints[0], Map.Felucca); }
        victim.Player.MoveToWorld(controller.Venue.RespawnPoints[1], Map.Felucca);
        var loot = Track(new Gold(123));
        victim.Player.Backpack.DropItem(loot);
        controller.RecordDamage(victim.Player, enemies[0].Player, 12);
        controller.RecordDamage(victim.Player, enemies[1].Player, 15);
        Assert.True(controller.TryDefeat(victim.Player));
        Assert.Equal(10, controller.OrderScore);
        Assert.All(enemies, attacker => Assert.Equal(10, attacker.KillPoints));
        Assert.Equal(1, victim.Deaths);
        Assert.True(victim.Player.Alive);
        Assert.Same(victim.Player.Backpack, loot.Parent);
        Assert.Equal(123, loot.Amount);
        Assert.Equal(controller.Venue.ChaosBase, victim.Player.Location);
        Assert.Equal(Core.Now + TimeSpan.FromSeconds(90), victim.RespawnAt);
        Assert.False(controller.TryDeploy(victim.Player, 3, out _));
        Assert.True(controller.TryDefeat(victim.Player));
        Assert.Equal(1, victim.Deaths);
        Assert.Equal(10, controller.OrderScore);
        Core._now = victim.RespawnAt;
        Assert.True(controller.TryDeploy(victim.Player, 3, out _));
        Assert.Equal(controller.Venue.RespawnPoints[0], victim.Player.Location);
        Assert.False(controller.TryDeploy(victim.Player, 3, out _));
    }

    [Fact]
    public void ExpiredContributorsAndFriendlyDamageReceiveNoKillCredit()
    {
        var (controller, _) = Active(4);
        var enemy = Team(controller, TownStruggleTeam.Order)[0];
        var allies = Team(controller, TownStruggleTeam.Chaos);
        enemy.Player.MoveToWorld(controller.Venue.RespawnPoints[0], Map.Felucca);
        foreach (var ally in allies) { ally.Player.MoveToWorld(controller.Venue.RespawnPoints[1], Map.Felucca); }
        controller.RecordDamage(allies[0].Player, enemy.Player, 10);
        Core._now += TimeSpan.FromSeconds(31);
        controller.RecordDamage(allies[0].Player, allies[1].Player, 20);
        Assert.True(controller.TryDefeat(allies[0].Player));
        Assert.Equal(0, enemy.KillPoints);
        Assert.Equal(0, allies[1].KillPoints);
        Assert.Equal(0, controller.OrderScore);
        Assert.Equal(1, allies[0].Deaths);
    }

    [Fact]
    public void HealingCreditRequiresActualTeamHealingOutsideBothBases()
    {
        var (controller, _) = Active(4);
        var order = Team(controller, TownStruggleTeam.Order);
        var enemy = Team(controller, TownStruggleTeam.Chaos)[0];
        controller.RecordHealing(order[1].Player, order[0].Player, 50);
        Assert.Equal(0, order[0].Healing);
        foreach (var participant in controller.Participants)
        {
            participant.Player.MoveToWorld(controller.Venue.RespawnPoints[0], Map.Felucca);
        }
        controller.RecordHealing(order[0].Player, order[0].Player, 20);
        controller.RecordHealing(enemy.Player, order[0].Player, 20);
        controller.RecordHealing(order[1].Player, order[0].Player, 0);
        Assert.Equal(0, order[0].Healing);
        controller.RecordHealing(order[1].Player, order[0].Player, 45);
        Assert.Equal(45, order[0].Healing);
    }

    [Fact]
    public void DeploymentRejectsUncontrolledCaptureAndAcceptsOwnedCaptureFromOwnBase()
    {
        var (controller, _) = Active(2);
        var order = Team(controller, TownStruggleTeam.Order)[0];
        Assert.False(controller.TryDeploy(order.Player, 0, out _));
        Assert.False(controller.TryDeploy(order.Player, 6, out _));
        Assert.True(controller.TryDeploy(order.Player, 3, out _));
        order.Player.MoveToWorld(controller.Venue.CapturePoints[0], Map.Felucca);
        Seconds(controller, 20);
        order.Player.MoveToWorld(controller.Venue.OrderBase, Map.Felucca);
        Assert.True(controller.TryDeploy(order.Player, 0, out _));
        Assert.Equal(controller.Venue.CapturePoints[0], order.Player.Location);
    }

    [Fact]
    public void LeavingDuringMatchRequiresBaseAndDisqualifiesWithoutLosingItems()
    {
        var (controller, _) = Active(2);
        var entry = controller.Participants[0];
        entry.Player.MoveToWorld(controller.Venue.RespawnPoints[0], Map.Felucca);
        Assert.False(controller.TryLeave(entry.Player, out _));
        Assert.False(entry.Disqualified);
        entry.Player.MoveToWorld(controller.Venue.TeamBase(entry.Team), Map.Felucca);
        Assert.True(controller.TryLeave(entry.Player, out _));
        Assert.True(entry.Disqualified);
        Assert.True(entry.Returned);
        Assert.Equal(entry.ReturnLocation, entry.Player.Location);
        Assert.False(controller.TryDeploy(entry.Player, 3, out _));
    }

    [Fact]
    public void OneAccountCannotEnrollTwiceAndBaseInactivityCreatesFortyEightHourBan()
    {
        var account = new Account($"town-struggle-test-{Guid.NewGuid():N}", "test-only-password");
        _accounts.Add(account);
        var (controller, _) = Create();
        Assert.True(controller.TryOpenSignup(out _));
        var first = Player(account);
        var alternate = Player(account);
        Assert.True(controller.TryJoin(first, out _));
        Assert.False(controller.TryJoin(alternate, out var duplicateError));
        Assert.Contains("one character", duplicateError);
        Assert.True(controller.TryJoin(Player(), out _));
        Core._now = controller.SignupDeadline;
        controller.Advance();
        Core._now += TownStruggleRules.IdleLimit;
        controller.Advance();
        var entry = controller.GetParticipant(first);
        Assert.True(entry.Disqualified);
        Assert.True(entry.Returned);
        var blockedUntil = DateTime.Parse(account.GetTag("TownStruggle.BlockedUntil"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        Assert.Equal(Core.Now + TimeSpan.FromHours(48), blockedUntil);
        Assert.True(controller.TryAbort(out _));
        Assert.True(controller.TryOpenSignup(out _));
        Assert.False(controller.TryJoin(alternate, out var error));
        Assert.Contains("temporarily barred", error);
    }

    [Fact]
    public void BattlefieldPresenceDoesNotTriggerBaseIdleBan()
    {
        var (controller, _) = Active(2);
        var player = controller.Participants[0];
        player.Player.MoveToWorld(controller.Venue.RespawnPoints[0], Map.Felucca);
        Core._now += TownStruggleRules.IdleLimit;
        controller.Advance();
        Assert.False(player.Disqualified);
        Assert.False(player.Returned);
    }

    [Fact]
    public void BlockedBaseFaultsWithoutLosingParticipantOrLootAndAbortRetainsFailedRecovery()
    {
        var (controller, host) = Active(2);
        var player = controller.Participants[0];
        player.Player.MoveToWorld(controller.Venue.RespawnPoints[0], Map.Felucca);
        host.FailMoves = true;
        Assert.True(controller.TryDefeat(player.Player));
        Assert.Equal(TownStrugglePhase.Faulted, controller.Phase);
        Assert.Equal(2, controller.Participants.Count);
        Assert.False(controller.TryAbort(out _));
        Assert.False(player.Deleted);
        Assert.False(player.Returned);
        host.FailMoves = false;
        var original = player.ReturnLocation;
        var mobile = player.Player;
        Assert.True(controller.TryAbort(out _));
        Assert.Equal(TownStrugglePhase.Dormant, controller.Phase);
        Assert.Empty(controller.Participants);
        Assert.True(player.Deleted);
        Assert.Equal(original, mobile.Location);
    }

    [Fact]
    public void ResultsVotingRewardsOnceEvenWhenFinalReturnNeedsRecovery()
    {
        var (controller, host) = Active(4);
        var order = Team(controller, TownStruggleTeam.Order);
        var chaos = Team(controller, TownStruggleTeam.Chaos);
        var rewardedPlayer = order[0].Player;
        order[0].KillPoints = 40;
        order[1].KillPoints = 30;
        chaos[0].KillPoints = 20;
        chaos[1].KillPoints = 10;
        Core._now = controller.MatchDeadline;
        controller.Advance();
        Assert.Equal(TownStrugglePhase.Results, controller.Phase);
        Assert.Equal(Core.Now + TimeSpan.FromMinutes(15), controller.ResultsDeadline);
        Assert.False(controller.TryVote(order[0].Player, order[0].Player, out _));
        Assert.False(controller.TryVote(order[0].Player, chaos[0].Player, out _));
        Assert.True(controller.TryVote(order[1].Player, order[0].Player, out _));
        Assert.False(controller.TryVote(order[1].Player, order[0].Player, out _));
        host.FailMoves = true;
        Core._now = controller.ResultsDeadline;
        controller.Advance();
        Assert.True(controller.RewardsCalculated);
        Assert.Equal(TownStrugglePhase.Faulted, controller.Phase);
        Assert.Equal(5, order[0].Rewards);
        Assert.Equal(5, BankRewards(rewardedPlayer));
        Assert.All(controller.Participants, participant => Assert.True(participant.RewardsDelivered));
        Invoke(controller, "PayRewards");
        Assert.Equal(5, BankRewards(rewardedPlayer));
        host.FailMoves = false;
        Assert.True(controller.TryAbort(out _));
        Assert.Equal(5, BankRewards(rewardedPlayer));
    }

    [Fact]
    public void PureHealerAndMvpEarnBonusesWithoutCaptureOrKillPoints()
    {
        var (controller, host) = Active(4);
        var order = Team(controller, TownStruggleTeam.Order);
        var chaos = Team(controller, TownStruggleTeam.Chaos);
        order[0].Healing = 100;
        order[1].KillPoints = 10;
        chaos[0].KillPoints = 30;
        chaos[1].KillPoints = 20;
        Core._now = controller.MatchDeadline;
        controller.Advance();
        Assert.True(controller.TryVote(order[1].Player, order[0].Player, out _));
        host.FailMoves = true;
        Core._now = controller.ResultsDeadline;
        controller.Advance();
        Assert.Equal(0d, order[0].Score);
        Assert.Equal(4, order[0].Rewards);
        Assert.Equal(4, BankRewards(order[0].Player));
    }

    [Fact]
    public void SaveLoadRetainsParticipantsScoresAndCaptureOwnership()
    {
        var (controller, host) = Active(2);
        var participant = Team(controller, TownStruggleTeam.Order)[0];
        participant.Player.MoveToWorld(controller.Venue.CapturePoints[0], Map.Felucca);
        Seconds(controller, 30);
        var state = controller.CaptureStates[0];
        var bytes = Snapshot(controller);
        controller.Delete();
        var restored = Track(new TownStruggleController(World.NewItem));
        restored.Deserialize(new BufferReader(bytes));
        restored.Bind(host);
        Assert.Equal(10, restored.OrderScore);
        Assert.Equal(state, restored.CaptureStates[0]);
        Assert.Same(participant, restored.GetParticipant(participant.Player));
        Assert.Equal(10d, participant.ControlPoints);
        Assert.Equal(2, restored.Participants.Count);
        Assert.Equal(controller.MatchDeadline, restored.MatchDeadline);
    }

    [Fact]
    public void ActiveRestartFaultsAndRetainsRecoveryRecordsWithoutReplayingMatch()
    {
        var (controller, host) = Active(2);
        var participant = controller.Participants[0];
        var mobile = participant.Player;
        var location = mobile.Location;
        var deadline = controller.MatchDeadline;
        var round = controller.RoundNumber;
        var bytes = Snapshot(controller);
        controller.Delete();
        var restored = Track(new TownStruggleController(World.NewItem));
        restored.Deserialize(new BufferReader(bytes));
        Invoke(restored, "AfterDeserialization");
        restored.Bind(host);
        Assert.Equal(TownStrugglePhase.Faulted, restored.Phase);
        Assert.Contains("restarted", restored.FaultMessage);
        Assert.Same(participant, restored.GetParticipant(mobile));
        Assert.Equal(deadline, restored.MatchDeadline);
        Assert.Equal(round, restored.RoundNumber);
        Assert.Equal(location, mobile.Location);
        Core._now += TimeSpan.FromHours(1);
        restored.Advance();
        Assert.Equal(TownStrugglePhase.Faulted, restored.Phase);
        Assert.Equal(location, mobile.Location);
        Assert.False(restored.TryOpenSignup(out _));
        Assert.True(restored.TryAbort(out _));
        Assert.Equal(TownStrugglePhase.Dormant, restored.Phase);
        Assert.Empty(restored.Participants);
    }

    [Fact]
    public void LootToggleIsIdleOnlyAndDisabledModeRetainsEvenMagicalEquipment()
    {
        var (controller, _) = Create();
        Assert.True(controller.SelectiveLootEnabled);
        Assert.True(controller.TrySetSelectiveLoot(false, out _));
        Assert.True(controller.TryOpenSignup(out _));
        var player = Player();
        var weapon = Track(new Longsword { DamageLevel = WeaponDamageLevel.Ruin });
        player.Backpack.DropItem(weapon);
        Assert.True(controller.TryJoin(player, out _));
        Assert.True(controller.TryJoin(Player(), out _));
        Assert.False(controller.TrySetSelectiveLoot(true, out _));
        Core._now = controller.SignupDeadline;
        controller.Advance();
        player.MoveToWorld(controller.Venue.RespawnPoints[0], Map.Felucca);
        Assert.True(controller.TryDefeat(player));
        Assert.Same(player.Backpack, weapon.Parent);
        Assert.False(weapon.Deleted);
        Assert.True(player.Alive);
        Assert.False(controller.TrySetSelectiveLoot(true, out _));
        Assert.True(controller.TryAbort(out _));
        Assert.True(controller.TrySetSelectiveLoot(true, out _));
    }

    [Fact]
    public void ReturnedParticipantHasResultsGuardGraceOnlyInsideVenueUntilDeadline()
    {
        var (controller, _) = Active(2);
        var player = controller.Participants[0].Player;
        var participant = controller.GetParticipant(player);
        var previous = TownStruggleRuntime.Controller;
        var runtimeProperty = typeof(TownStruggleRuntime).GetProperty(nameof(TownStruggleRuntime.Controller));
        runtimeProperty.SetValue(null, controller);
        try
        {
            Core._now = controller.MatchDeadline;
            controller.Advance();
            Assert.Equal(TownStrugglePhase.Results, controller.Phase);
            Assert.True(controller.TryLeave(player, out var error), error);
            Assert.True(participant.Returned);
            Assert.False(TownStruggleRuntime.IsEnrolled(player));
            Assert.False(TownStruggleRuntime.IsGuardExempt(player));
            player.MoveToWorld(controller.Venue.RespawnPoints[0], controller.Venue.Map);
            Assert.True(TownStruggleRuntime.IsGuardExempt(player));
            var outsider = Player();
            outsider.MoveToWorld(player.Location, player.Map);
            Assert.False(TownStruggleRuntime.IsGuardExempt(outsider));
            participant.Disqualified = true;
            Assert.False(TownStruggleRuntime.IsGuardExempt(player));
            participant.Disqualified = false;
            Core._now = controller.ResultsDeadline;
            Assert.False(TownStruggleRuntime.IsGuardExempt(player));
        }
        finally
        {
            runtimeProperty.SetValue(null, previous);
        }
    }

    [Fact]
    public void SignupDeadlineRejectsStaleJoinBeforeTimerAndAfterMatchStart()
    {
        var (controller, _) = Create();
        Assert.True(controller.TryOpenSignup(out _));
        Assert.True(controller.TryJoin(Player(), out _));
        Assert.True(controller.TryJoin(Player(), out _));
        var late = Player();
        Core._now = controller.SignupDeadline;
        Assert.False(controller.TryJoin(late, out _));
        Assert.Null(controller.GetParticipant(late));
        controller.Advance();
        Assert.Equal(TownStrugglePhase.Active, controller.Phase);
        Assert.False(controller.TryJoin(late, out _));
        Assert.Equal(2, controller.Participants.Count);
    }

    [Fact]
    public void ExactlyThirtySecondsOutsideCanReenterAndRestartIndependentGrace()
    {
        var (controller, _) = Active(2);
        var entry = controller.Participants[0];
        var player = entry.Player;
        player.MoveToWorld(new Point3D(99, 150, 0), Map.Felucca);
        Assert.True(controller.TryEnforceBoundary(player));
        var firstExit = entry.OutsideSince;
        Assert.Equal(Core.Now, firstExit);
        Core._now += TownStruggleRules.OutsideGrace;
        Assert.True(controller.CanReenter(player));
        player.MoveToWorld(controller.Venue.RespawnPoints[0], Map.Felucca);
        Assert.True(controller.TryEnforceBoundary(player));
        Assert.Equal(DateTime.MinValue, entry.OutsideSince);
        Assert.False(entry.Disqualified);
        Core._now += TimeSpan.FromSeconds(1);
        player.MoveToWorld(new Point3D(99, 150, 0), Map.Felucca);
        Assert.True(controller.TryEnforceBoundary(player));
        Assert.Equal(Core.Now, entry.OutsideSince);
        Assert.NotEqual(firstExit, entry.OutsideSince);
        Core._now += TownStruggleRules.OutsideGrace;
        Assert.True(controller.CanReenter(player));
        Assert.True(controller.TryEnforceBoundary(player));
        Assert.False(entry.Disqualified);
    }

    [Fact]
    public void ExpiredReentryBeforeTimerAbandonsAndBansWithoutLosingInventory()
    {
        var account = new Account($"town-outside-test-{Guid.NewGuid():N}", "test-only-password");
        _accounts.Add(account);
        var (controller, _) = Create();
        Assert.True(controller.TryOpenSignup(out _));
        var player = Player(account);
        Assert.True(controller.TryJoin(player, out _));
        Assert.True(controller.TryJoin(Player(), out _));
        Core._now = controller.SignupDeadline;
        controller.Advance();
        var entry = controller.GetParticipant(player);
        var weapon = Track(new Longsword { DamageLevel = WeaponDamageLevel.Vanq });
        player.Backpack.DropItem(weapon);
        entry.KillPoints = 40;
        entry.Rewards = 5;
        player.MoveToWorld(new Point3D(99, 150, 0), Map.Felucca);
        Assert.True(controller.TryEnforceBoundary(player));
        Core._now += TimeSpan.FromSeconds(30) + TimeSpan.FromTicks(1);
        player.MoveToWorld(controller.Venue.RespawnPoints[0], Map.Felucca);
        Assert.False(controller.CanReenter(player));
        Assert.False(controller.TryEnforceBoundary(player));
        Assert.True(entry.Disqualified);
        Assert.True(entry.Returned);
        Assert.Equal(0, entry.Rewards);
        Assert.Equal(2, controller.Participants.Count);
        Assert.Same(entry, controller.GetParticipant(player));
        Assert.False(controller.Venue.Contains(player));
        Assert.False(controller.TryDeploy(player, 3, out _));
        Assert.True(player.Alive);
        Assert.Same(player.Backpack, weapon.Parent);
        Assert.False(weapon.Deleted);
        var blockedUntil = DateTime.Parse(account.GetTag("TownStruggle.BlockedUntil"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        Assert.Equal(Core.Now + TimeSpan.FromHours(48), blockedUntil);
        Core._now = controller.MatchDeadline;
        controller.Advance();
        Core._now = controller.ResultsDeadline;
        controller.Advance();
        Assert.Equal(0, BankRewards(player));
    }

    [Fact]
    public void DisconnectionUsesSameContinuousGraceAndTimelyReconnectCancelsIt()
    {
        var (controller, _) = Active(2);
        var entry = controller.Participants[0];
        var player = entry.Player;
        player.MoveToWorld(controller.Venue.RespawnPoints[0], Map.Felucca);
        var connection = player.NetState;
        player.NetState = null;
        Assert.True(controller.TryEnforceBoundary(player));
        Core._now += TimeSpan.FromSeconds(20);
        player.NetState = connection;
        Assert.True(controller.TryEnforceBoundary(player));
        Assert.Equal(DateTime.MinValue, entry.OutsideSince);
        player.NetState = null;
        Assert.True(controller.TryEnforceBoundary(player));
        Core._now += TimeSpan.FromSeconds(31);
        Assert.False(controller.TryEnforceBoundary(player));
        Assert.True(entry.Disqualified);
        Assert.True(entry.Returned);
    }

    [Fact]
    public void ExpiredOutsideClockCannotBeResetByDeployingFromOwnBase()
    {
        var (controller, _) = Active(2);
        var entry = controller.Participants[0];
        entry.Player.MoveToWorld(new Point3D(99, 150, 0), Map.Felucca);
        controller.TryEnforceBoundary(entry.Player);
        Core._now += TimeSpan.FromSeconds(31);
        entry.Player.MoveToWorld(controller.Venue.TeamBase(entry.Team), Map.Felucca);
        Assert.False(controller.TryDeploy(entry.Player, 3, out _));
        Assert.True(entry.Disqualified);
        Assert.True(entry.Returned);
    }

    [Fact]
    public void ExpiredOutsideAttackerCannotReceiveDamageCreditBeforeNextTick()
    {
        var (controller, _) = Active(2);
        var attacker = Team(controller, TownStruggleTeam.Order)[0];
        var victim = Team(controller, TownStruggleTeam.Chaos)[0];
        attacker.Player.MoveToWorld(new Point3D(99, 150, 0), Map.Felucca);
        controller.TryEnforceBoundary(attacker.Player);
        Core._now += TimeSpan.FromSeconds(31);
        attacker.Player.MoveToWorld(controller.Venue.RespawnPoints[0], Map.Felucca);
        victim.Player.MoveToWorld(controller.Venue.RespawnPoints[0], Map.Felucca);
        controller.RecordDamage(victim.Player, attacker.Player, 20);
        Assert.True(attacker.Disqualified);
        Assert.Empty(victim.DamageContributors);
        Assert.Equal(0, attacker.KillPoints);
    }

    [Theory]
    [InlineData(31, true)]
    [InlineData(30, false)]
    public void MatchEndChecksAbandonmentBeforeResultsButDoesNotCountDelayedFinalTick(int secondsBeforeEnd, bool abandoned)
    {
        var (controller, _) = Active(2);
        var entry = controller.Participants[0];
        Core._now = controller.MatchDeadline - TimeSpan.FromSeconds(secondsBeforeEnd);
        entry.Player.MoveToWorld(new Point3D(99, 150, 0), Map.Felucca);
        controller.TryEnforceBoundary(entry.Player);
        Core._now = controller.MatchDeadline + TimeSpan.FromSeconds(5);
        controller.Advance();
        Assert.Equal(TownStrugglePhase.Results, controller.Phase);
        Assert.Equal(abandoned, entry.Disqualified);
        Assert.Equal(abandoned, entry.Returned);
    }

    [Fact]
    public void BriefAbsenceResetsSeparateTenMinuteBaseClock()
    {
        var (controller, _) = Active(2);
        var entry = controller.Participants[0];
        Core._now += TimeSpan.FromMinutes(9);
        controller.Advance();
        Assert.False(entry.Disqualified);
        entry.Player.MoveToWorld(new Point3D(99, 150, 0), Map.Felucca);
        controller.TryEnforceBoundary(entry.Player);
        Core._now += TimeSpan.FromSeconds(20);
        entry.Player.MoveToWorld(controller.Venue.TeamBase(entry.Team), Map.Felucca);
        controller.TryEnforceBoundary(entry.Player);
        Core._now += TimeSpan.FromMinutes(1);
        controller.Advance();
        Assert.False(entry.Disqualified);
        Assert.Equal(DateTime.MinValue, entry.OutsideSince);
    }

    [Fact]
    public void AbandonedReturnInsideTownUsesOutsideFallbackAndCannotRedeploy()
    {
        var (controller, _) = Active(2);
        var entry = controller.Participants[0];
        entry.ReturnLocation = controller.Venue.RespawnPoints[0];
        entry.Player.MoveToWorld(new Point3D(99, 150, 0), Map.Felucca);
        controller.TryEnforceBoundary(entry.Player);
        Core._now += TimeSpan.FromSeconds(31);
        controller.TryEnforceBoundary(entry.Player);
        Assert.True(entry.Returned);
        Assert.False(controller.Venue.Contains(entry.Player));
        entry.Player.MoveToWorld(controller.Venue.TeamBase(entry.Team), Map.Felucca);
        Assert.False(controller.CanReenter(entry.Player));
        Assert.False(controller.TryDeploy(entry.Player, 3, out _));
        Assert.True(controller.TryReturnOutside(entry.Player, out var error), error);
        Assert.False(controller.Venue.Contains(entry.Player));
    }

    [Fact]
    public void BlockedOutsideReturnFaultsAndRetainsAbandonedRecoveryRecord()
    {
        var (controller, host) = Active(2);
        var entry = controller.Participants[0];
        entry.Player.MoveToWorld(new Point3D(99, 150, 0), Map.Felucca);
        controller.TryEnforceBoundary(entry.Player);
        Core._now += TimeSpan.FromSeconds(31);
        host.FailMoves = true;
        Assert.False(controller.TryEnforceBoundary(entry.Player));
        Assert.Equal(TownStrugglePhase.Faulted, controller.Phase);
        Assert.True(entry.Disqualified);
        Assert.False(entry.Returned);
        Assert.False(entry.Deleted);
        Assert.Same(entry, controller.GetParticipant(entry.Player));
    }

    [Fact]
    public void OutsideClockSurvivesParticipantSaveLoad()
    {
        var (controller, _) = Active(2);
        var entry = controller.Participants[0];
        entry.Player.MoveToWorld(new Point3D(99, 150, 0), Map.Felucca);
        controller.TryEnforceBoundary(entry.Player);
        var copy = Track(new TownStruggleParticipant(World.NewItem));
        copy.Deserialize(new BufferReader(Snapshot(entry)));
        Assert.Equal(entry.OutsideSince, copy.OutsideSince);
        Assert.Same(entry.Player, copy.Player);
        Assert.Equal(entry.Team, copy.Team);
        Assert.Equal(entry.ReturnLocation, copy.ReturnLocation);
    }

    [Fact]
    public void VersionZeroParticipantMigratesWithoutLosingScoreRewardsOrReferences()
    {
        var player = Player();
        var vote = Player();
        var baseItem = Track(new Item(1));
        var writer = new BufferWriter(true);
        baseItem.Serialize(writer);
        writer.WriteEncodedInt(0);
        writer.Write(player);
        writer.WriteEnum(TownStruggleTeam.Chaos);
        writer.Write(true);
        writer.Write(Core.Now + TimeSpan.FromSeconds(12));
        writer.Write(Core.Now - TimeSpan.FromMinutes(3));
        writer.Write(12.5d);
        writer.Write(30);
        writer.Write(81);
        writer.Write(2);
        writer.Write(4);
        writer.Write(vote);
        writer.Write(new Point3D(51, 52, 3));
        writer.Write(Map.Felucca);
        writer.Write(true);
        writer.Write(77);
        writer.Write(true);
        var restored = Track(new TownStruggleParticipant(World.NewItem));
        restored.Deserialize(new BufferReader(writer.Buffer.AsSpan(0, (int)writer.Position).ToArray()));
        Assert.Same(player, restored.Player);
        Assert.Equal(TownStruggleTeam.Chaos, restored.Team);
        Assert.True(restored.Disqualified);
        Assert.Equal(Core.Now + TimeSpan.FromSeconds(12), restored.RespawnAt);
        Assert.Equal(Core.Now - TimeSpan.FromMinutes(3), restored.IdleSince);
        Assert.Equal(12.5d, restored.ControlPoints);
        Assert.Equal(30, restored.KillPoints);
        Assert.Equal(81, restored.Healing);
        Assert.Equal(2, restored.Deaths);
        Assert.Equal(4, restored.Rewards);
        Assert.Same(vote, restored.VoteFor);
        Assert.Equal(new Point3D(51, 52, 3), restored.ReturnLocation);
        Assert.Same(Map.Felucca, restored.ReturnMap);
        Assert.True(restored.RewardsDelivered);
        Assert.Equal(77, restored.GuildId);
        Assert.True(restored.Returned);
        Assert.Equal(DateTime.MinValue, restored.OutsideSince);
    }

    private (TownStruggleController Controller, FakeHost Host) Create()
    {
        var controller = Track(new TownStruggleController());
        var host = new FakeHost();
        controller.ConfigureVenue(VenueJson, host, false);
        return (controller, host);
    }

    private (TownStruggleController Controller, FakeHost Host) Active(int count)
    {
        var (controller, host) = Create();
        Assert.True(controller.TryOpenSignup(out var error), error);
        for (var i = 0; i < count; i++) { Assert.True(controller.TryJoin(Player(), out error), error); }
        Core._now = controller.SignupDeadline;
        controller.Advance();
        Assert.Equal(TownStrugglePhase.Active, controller.Phase);
        return (controller, host);
    }

    private Mobile Player(IAccount account = null)
    {
        var state = PacketTestUtilities.CreateTestNetState();
        _states.Add(state);
        account ??= new MockAccount();
        state.Account = account;
        var player = Track(new PlayerMobile { Player = true, Body = 0x190, RawStr = 100, RawDex = 100, RawInt = 100, Account = account });
        player.AddItem(Track(new Backpack()));
        player.NetState = state;
        state.Mobile = player;
        player.MoveToWorld(new Point3D(50, 50, 0), Map.Felucca);
        return player;
    }

    private T Track<T>(T entity) where T : IEntity { _created.Add(entity); return entity; }

    private static List<TownStruggleParticipant> Team(TownStruggleController controller, TownStruggleTeam team)
    {
        var result = new List<TownStruggleParticipant>();
        foreach (var entry in controller.Participants) { if (entry.Team == team) { result.Add(entry); } }
        return result;
    }

    private static void Seconds(TownStruggleController controller, int seconds)
    {
        for (var second = 0; second < seconds; second++) { Core._now += TimeSpan.FromSeconds(1); controller.Advance(); }
    }

    private static int BankRewards(Mobile player)
    {
        var total = 0;
        foreach (var item in player.BankBox.Items) { if (item is BattleCommendation) { total += item.Amount; } }
        return total;
    }

    private static void Invoke(TownStruggleController controller, string method) =>
        typeof(TownStruggleController).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);

    private static byte[] Snapshot(Item item)
    {
        var writer = new BufferWriter(true);
        item.Serialize(writer);
        return writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();
    }

    private sealed class FakeHost : ITownStruggleHost
    {
        public DateTime Now => Core.Now;
        public HashSet<Mobile> Ineligible { get; } = [];
        public bool FailMoves { get; set; }
        public int Notifications { get; private set; }
        public bool Eligible(Mobile player) => player is { Deleted: false, Player: true, Alive: true, NetState: not null } && !Ineligible.Contains(player);
        public bool AtNeutralBase(Mobile player, TownStruggleVenue venue) => player.Map == venue.Map && player.Location == venue.NeutralBase;
        public bool Move(Mobile player, Point3D point, Map map)
        {
            if (FailMoves) { return false; }
            player.MoveToWorld(point, map);
            return true;
        }
        public bool MoveOutside(Mobile player, TownStruggleVenue venue, Point3D point, Map map)
        {
            if (map == venue.Map && venue.Area.Contains(point) ||
                map == Map.Felucca && Server.Engines.ModularDungeon.ExtractionDungeonLayout.LobbyBounds.Contains(point))
            {
                point = new Point3D(venue.Area.Start.X - 2, venue.Area.Start.Y, 0);
                map = venue.Map;
            }
            return Move(player, point, map);
        }
        public void Notify(TownStruggleController controller) => Notifications++;
    }
}
