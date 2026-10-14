using System;
using System.Collections.Generic;
using System.Globalization;
using ModernUO.Serialization;
using Server.Accounting;
using Server.Engines.ModularDungeon;
using Server.Engines.PvpEvents;
using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.Engines.TownStruggle;

internal interface ITownStruggleHost
{
    DateTime Now { get; }
    bool Eligible(Mobile player);
    bool AtNeutralBase(Mobile player, TownStruggleVenue venue);
    bool Move(Mobile player, Point3D point, Map map);
    bool MoveOutside(Mobile player, TownStruggleVenue venue, Point3D requestedPoint, Map requestedMap);
    void Notify(TownStruggleController controller);
}

[SerializationGenerator(0)]
public partial class TownStruggleController : Item
{
    public const int MaximumParticipants = 128;
    private const string BanTag = "TownStruggle.BlockedUntil";
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(TownStruggleController));
    [SerializableField(0, setter: "private")] private TownStrugglePhase _phase;
    [SerializableField(1, setter: "private")] private string _venueJson;
    [SerializableField(2, setter: "private")] private DateTime _signupDeadline;
    [SerializableField(3, setter: "private")] private DateTime _matchDeadline;
    [SerializableField(4, setter: "private")] private DateTime _resultsDeadline;
    [SerializableField(5, setter: "private")] private DateTime _nextControlCheck;
    [SerializableField(6, setter: "private")] private int _roundNumber;
    [SerializableField(7, setter: "private")] private int _orderScore;
    [SerializableField(8, setter: "private")] private int _chaosScore;
    [SerializableField(9, setter: "private")] private string _faultMessage;
    [SerializableField(10, getter: "private", setter: "private")] private List<TownStruggleParticipant> _roster = [];
    [SerializableField(11, getter: "private", setter: "private")] private int[] _progress = new int[3];
    [SerializableField(12, getter: "private", setter: "private")] private int[] _owners = new int[3];
    [SerializableField(13, getter: "private", setter: "private")] private Dictionary<int, int> _guildPrestigeLedger = [];
    [SerializableField(14, getter: "private", setter: "private")] private Dictionary<int, int> _guildRewardLedger = [];
    [SerializableField(15, setter: "private")] private bool _rewardsCalculated;
    [SerializableField(16, setter: "private")] private TimeSpan _signupDuration = TimeSpan.FromMinutes(15);
    [SerializableField(17, setter: "private")] private TimeSpan _matchDuration = TimeSpan.FromMinutes(15);
    [SerializableField(18, getter: "private", setter: "private")] private List<TownStruggleControlMarker> _markerReferences = [];
    [SerializableField(19, setter: "private")] private int _venueIndex;
    [SerializableField(20, setter: "private")] private bool _selectiveLootEnabled = true;
    [SerializableField(21, getter: "private", setter: "private")] private List<Item> _auxiliaryReferences = [];
    private ITownStruggleHost _host;
    private TimerExecutionToken _timer;
    private TownStruggleRegion _region;
    private TownStruggleVenue _venue;
    private TownStruggleVenue[] _venues;
    private readonly TownStruggleCaptureState[] _captureStates = new TownStruggleCaptureState[3];
    private readonly Dictionary<Mobile, TownStruggleParticipant> _members = [];
    private readonly HashSet<Mobile> _enforcingBoundary = [];
    private readonly HashSet<Mobile> _movingPlayers = [];

    [Constructible(AccessLevel.Administrator)]
    public TownStruggleController() : base(0x1F14)
    {
        Visible = false;
        Movable = false;
    }

    public override string DefaultName => "Town Struggle controller";
    public TownStruggleVenue Venue => _venue;
    public bool ReadyForScheduledStart => _host != null && Venue != null && Phase == TownStrugglePhase.Dormant && _roster.Count == 0;
    public IReadOnlyList<TownStruggleParticipant> Participants => _roster;
    public IReadOnlyList<TownStruggleCaptureState> CaptureStates => _captureStates;
    public IReadOnlyDictionary<int, int> GuildPrestige => _guildPrestigeLedger;
    public IReadOnlyDictionary<int, int> GuildRewardPoints => _guildRewardLedger;
    internal DateTime Now => _host?.Now ?? Core.Now;
    public TownStruggleParticipant GetParticipant(Mobile player) => player != null && _members.TryGetValue(player, out var value) ? value : null;
    public bool IsMovingParticipant(Mobile player) => player != null && _movingPlayers.Contains(player);

    // Pure main-loop query: movement must not clear an expired outside clock before testing it.
    public bool CanReenter(Mobile player)
    {
        var entry = GetParticipant(player);
        return Phase != TownStrugglePhase.Active || entry == null ||
            !entry.Disqualified && !entry.Returned && entry.Team != TownStruggleTeam.Neutral &&
            Now < MatchDeadline && !OutsideExpired(entry);
    }

    public bool TryEnforceBoundary(Mobile player)
    {
        if (IsMovingParticipant(player)) { return true; }
        var entry = GetParticipant(player);
        if (Phase != TownStrugglePhase.Active || entry == null) { return true; }
        if (!_enforcingBoundary.Add(player)) { return false; }
        try
        {
            return ObservePresence(entry) && Now < MatchDeadline;
        }
        finally
        {
            _enforcingBoundary.Remove(player);
        }
    }

    public bool TryReturnOutside(Mobile player, out string error)
    {
        var entry = GetParticipant(player);
        if (_host == null || entry == null || !entry.Disqualified && !entry.Returned)
        {
            error = "Only a departed participant can be returned outside the battlefield.";
            return false;
        }
        if (!Return(entry, true))
        {
            error = "No safe arrival outside the battlefield was available. Staff recovery is required.";
            Fail(error);
            return false;
        }
        error = null;
        return true;
    }

    internal void ConfigureVenue(string json, ITownStruggleHost host, bool validateTerrain = true)
    {
        if (Phase != TownStrugglePhase.Dormant || _roster.Count != 0)
        {
            throw new InvalidOperationException("A venue can only be configured on an empty, dormant controller.");
        }
        var venues = TownStruggleVenue.ParseCatalog(json);
        if (validateTerrain)
        {
            foreach (var venue in venues) { venue.ValidateTerrain(); }
        }
        VenueJson = json;
        _venues = venues;
        VenueIndex = 0;
        _venue = venues[0];
        Bind(host);
    }

    internal void Bind(ITownStruggleHost host)
    {
        _timer.Cancel();
        _host = host;
        _venues ??= TownStruggleVenue.ParseCatalog(VenueJson);
        if (VenueIndex < 0 || VenueIndex >= _venues.Length) { throw new InvalidOperationException("Invalid saved venue index."); }
        _venue = _venues[VenueIndex];
        _members.Clear();
        foreach (var participant in _roster)
        {
            if (participant is { Deleted: false, Player.Deleted: false } && !_members.TryAdd(participant.Player, participant))
            {
                throw new InvalidOperationException("Duplicate saved participant identity.");
            }
        }
        UpdateCaptureView();
        Publish();
        Timer.StartTimer(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), Tick, out _timer);
    }

    internal void RegisterRegion()
    {
        if (_region != null) { return; }
        _region = new TownStruggleRegion(this);
        _region.Register();
    }

    internal void ReleaseRegion()
    {
        _region?.Release();
        _region = null;
    }

    internal void AddMarker(TownStruggleControlMarker marker) => this.Add(_markerReferences, marker);
    internal IReadOnlyList<TownStruggleControlMarker> Markers => _markerReferences;
    internal void AddAuxiliaryItem(Item item) => this.Add(_auxiliaryReferences, item);
    internal IReadOnlyList<Item> AuxiliaryItems => _auxiliaryReferences;

    public bool TrySetSelectiveLoot(bool enabled, out string error)
    {
        if (Phase != TownStrugglePhase.Dormant || _roster.Count != 0)
        {
            error = "Change the loot rule only while Town Struggle is empty and dormant.";
            return false;
        }
        SelectiveLootEnabled = enabled;
        error = null;
        return true;
    }

    public bool TryOpenSignup(out string error)
    {
        if (!PvpEventRotationRuntime.CanStartTown(this, out error)) { return false; }
        if (_host == null || Venue == null || Phase != TownStrugglePhase.Dormant || _roster.Count != 0)
        {
            error = "Town Struggle must be configured, empty and dormant.";
            return false;
        }
        RoundNumber++;
        VenueIndex = Utility.Random(_venues.Length);
        _venue = _venues[VenueIndex];
        ReleaseRegion();
        foreach (var marker in _markerReferences)
        {
            if (marker is { Deleted: false }) { marker.MoveToWorld(marker.GetLocation(Venue), Venue.Map); }
        }
        OrderScore = 0;
        ChaosScore = 0;
        RewardsCalculated = false;
        FaultMessage = null;
        Array.Clear(_progress);
        Array.Clear(_owners);
        this.MarkDirty();
        UpdateCaptureView();
        SignupDeadline = Now + SignupDuration;
        Phase = TownStrugglePhase.Signup;
        Publish();
        error = null;
        return true;
    }

    public bool TryJoin(Mobile player, out string error)
    {
        if (ExtractionLobbyDecoration.IsBusy)
        {
            error = "The PvP lobby installation must finish before joining Town Struggle.";
            return false;
        }
        if (Phase != TownStrugglePhase.Signup || Now >= SignupDeadline || _roster.Count >= MaximumParticipants ||
            !_host.Eligible(player) || GetParticipant(player) != null ||
            ExtractionEventRuntime.Controller?.IsParticipant(player) == true)
        {
            error = "Join during signup while alive, connected, unmounted, out of combat, and without pets or another event.";
            return false;
        }
        if (player.Account is Account account)
        {
            if (DateTime.TryParse(account.GetTag(BanTag), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var until) && until > Now)
            {
                error = "This account is temporarily barred from Town Struggle for inactivity.";
                return false;
            }
            foreach (var entry in _roster)
            {
                if (ReferenceEquals(entry.Player?.Account, account))
                {
                    error = "Only one character per account may join this round.";
                    return false;
                }
            }
        }
        var participant = new TownStruggleParticipant
        {
            Player = player, ReturnMap = player.Map, ReturnLocation = player.Location,
            Team = TownStruggleTeam.Neutral, IdleSince = Now, GuildId = (int)(player.Guild?.Serial.Value ?? 0)
        };
        participant.CaptureIdentity();
        // Returning Town players must not become implicit entrants in the full-loot Extraction queue.
        if (player.Map == Map.Felucca && ExtractionDungeonLayout.LobbyBounds.Contains(player.Location))
        {
            participant.ReturnMap = Map.Felucca;
            participant.ReturnLocation = ExtractionDungeonLayout.TownReturn;
        }
        this.Add(_roster, participant);
        _members.Add(player, participant);
        Publish();
        if (!MovePlayer(player, Venue.NeutralBase, Venue.Map))
        {
            Remove(participant);
            error = "The neutral base has no safe arrival tile.";
            return false;
        }
        Server.Spells.Seventh.PolymorphSpell.EndPolymorph(player);
        Server.Spells.Fifth.IncognitoSpell.EndIncognito(player);
        if (DisguisePersistence.IsDisguised(player))
        {
            DisguisePersistence.RemoveTimer(player);
            player.NameMod = null;
            (player as PlayerMobile)?.SetHairMods(-1, -1);
        }
        error = null;
        return true;
    }

    public bool TryDeploy(Mobile player, int zoneIndex, out string error)
    {
        var participant = GetParticipant(player);
        if (Phase != TownStrugglePhase.Active || participant == null || !TryEnforceBoundary(player) ||
            participant.Disqualified || participant.Returned ||
            !player.Alive || player.NetState == null || player.Map != Venue.Map ||
            !player.InRange(Venue.TeamBase(participant.Team), TownStruggleVenue.BaseRadius) ||
            Now < participant.RespawnAt || zoneIndex is < 0 or >= 6)
        {
            error = "Deploy from your team base after the respawn countdown.";
            return false;
        }
        if (zoneIndex < 3 && _captureStates[zoneIndex].Owner != participant.Team)
        {
            error = "Your team does not control this capture zone.";
            return false;
        }
        var point = zoneIndex < 3 ? Venue.CapturePoints[zoneIndex] : Venue.RespawnPoints[zoneIndex - 3];
        if (!MovePlayer(player, point, Venue.Map))
        {
            error = "That arrival point is blocked; choose another zone.";
            return false;
        }
        participant.IdleSince = Now;
        player.Hidden = false;
        error = null;
        return true;
    }

    public bool TryLeave(Mobile player, out string error)
    {
        var participant = GetParticipant(player);
        if (Phase == TownStrugglePhase.Active && participant is { Disqualified: false, Returned: false } &&
            !TryEnforceBoundary(player))
        {
            error = "You can no longer participate in this match.";
            return false;
        }
        if (participant == null || Phase == TownStrugglePhase.Active && !Venue.IsAtBase(player))
        {
            error = "Return to your team base before leaving the match.";
            return false;
        }
        if (Phase == TownStrugglePhase.Active)
        {
            player.NetState?.CancelAllTrades();
            participant.Disqualified = true;
        }
        if (!Return(participant, Phase == TownStrugglePhase.Active))
        {
            error = "Your return location is blocked; staff can safely recover you.";
            return false;
        }
        if (Phase == TownStrugglePhase.Signup) { Remove(participant); }
        Publish();
        error = null;
        return true;
    }

    public bool TryVote(Mobile voter, Mobile target, out string error)
    {
        var from = GetParticipant(voter);
        var to = GetParticipant(target);
        if (Phase != TownStrugglePhase.Results || Now >= ResultsDeadline || from == null || to == null ||
            from == to || from.Disqualified || to.Disqualified || from.Team != to.Team || from.VoteFor != null)
        {
            error = "Vote once for another eligible teammate during results.";
            return false;
        }
        from.VoteFor = target;
        error = null;
        return true;
    }

    public void RecordDamage(Mobile victim, Mobile from, int actual)
    {
        var target = GetParticipant(victim);
        var source = GetParticipant(from);
        if (actual > 0 && Phase == TownStrugglePhase.Active && source != null && target != null &&
            TryEnforceBoundary(from) && TryEnforceBoundary(victim) &&
            !source.Disqualified && !target.Disqualified && !source.Returned && !target.Returned && source.Team != target.Team)
        {
            target.DamageContributors[from] = Now;
        }
    }

    public void RecordHealing(Mobile target, Mobile from, int actual)
    {
        var healer = GetParticipant(from);
        var healed = GetParticipant(target);
        if (actual > 0 && from != target && Phase == TownStrugglePhase.Active && healer != null && healed != null &&
            TryEnforceBoundary(from) && TryEnforceBoundary(target) &&
            !healer.Disqualified && !healed.Disqualified && !healer.Returned && !healed.Returned && healer.Team == healed.Team &&
            !Venue.IsAtBase(from) && !Venue.IsAtBase(target) && Venue.Contains(from) && Venue.Contains(target))
        {
            healer.Healing = (int)Math.Min(int.MaxValue, (long)healer.Healing + actual);
        }
    }

    public bool TryDefeat(Mobile player)
    {
        var participant = GetParticipant(player);
        if (participant == null || participant.Disqualified || participant.Returned || Phase != TownStrugglePhase.Active) { return false; }
        if (!TryEnforceBoundary(player))
        {
            player.Hits = Math.Max(1, player.HitsMax);
            return true;
        }
        if (Now < participant.RespawnAt || Venue.IsAtBase(player))
        {
            player.Hits = Math.Max(1, player.HitsMax);
            return true;
        }
        if (SelectiveLootEnabled && !TownStruggleLoot.TryDropEligible(this, player, out var lootError))
        {
            player.Hits = Math.Max(1, player.HitsMax);
            Fail(lootError);
            return true;
        }
        var enemy = TownStruggleRules.Opponent(participant.Team);
        var credited = false;
        foreach (var (attacker, when) in participant.DamageContributors)
        {
            var source = GetParticipant(attacker);
            if (Now - when <= TimeSpan.FromSeconds(30) && source != null && !source.Disqualified && !source.Returned &&
                CanReenter(attacker) &&
                source.Team == enemy && Venue.Contains(attacker) && !Venue.IsAtBase(attacker))
            {
                source.KillPoints += TownStruggleRules.KillIndividualPoints;
                credited = true;
            }
        }
        if (credited) { AwardTeam(enemy, TownStruggleRules.KillTeamPoints); }
        participant.DamageContributors.Clear();
        participant.Deaths++;
        participant.RespawnAt = Now + TownStruggleRules.RespawnDelay(Score(participant.Team), Score(enemy));
        participant.IdleSince = Now;
        Refresh(player);
        ClearEventAggression(player);
        if (!MovePlayer(player, Venue.TeamBase(participant.Team), Venue.Map))
        {
            Fail("A team base arrival was blocked. Staff must recover the match.");
        }
        return true;
    }

    internal void Advance()
    {
        if (_host == null || ExtractionLobbyDecoration.IsBusy || World.Saving ||
            World.WorldState is WorldState.PendingSave or WorldState.WritingSave) { return; }
        switch (Phase)
        {
            case TownStrugglePhase.Signup when Now >= SignupDeadline:
                StartMatch();
                break;
            case TownStrugglePhase.Active when Now >= MatchDeadline:
                CheckOutside();
                if (Phase == TownStrugglePhase.Active) { FinishMatch(); }
                break;
            case TownStrugglePhase.Active:
                CheckIdle();
                if (Phase == TownStrugglePhase.Active) { CaptureTick(); }
                break;
            case TownStrugglePhase.Results when Now >= ResultsDeadline:
                PayRewards();
                FinishReturns();
                break;
        }
    }

    private void Tick()
    {
        try { Advance(); }
        catch (Exception exception)
        {
            _logger.Error(exception, "Town Struggle paused safely");
            Fail("A match operation failed; staff recovery is required.");
        }
    }

    private void StartMatch()
    {
        for (var i = _roster.Count - 1; i >= 0; i--)
        {
            var entry = _roster[i];
            if (!_host.Eligible(entry.Player) || !_host.AtNeutralBase(entry.Player, Venue))
            {
                if (!Return(entry)) { Fail("An ineligible signup could not be returned safely."); return; }
                Remove(entry);
            }
        }
        if (_roster.Count < 2)
        {
            foreach (var entry in _roster)
            {
                if (!Return(entry)) { Fail("A cancelled signup could not be returned safely."); return; }
            }
            Reset();
            return;
        }
        for (var i = _roster.Count - 1; i > 0; i--)
        {
            var swap = Utility.Random(i + 1);
            (_roster[i], _roster[swap]) = (_roster[swap], _roster[i]);
        }
        this.MarkDirty();
        for (var i = 0; i < _roster.Count; i++)
        {
            _roster[i].Team = i % 2 == 0 ? TownStruggleTeam.Order : TownStruggleTeam.Chaos;
            _roster[i].IdleSince = Now;
            _roster[i].OutsideSince = DateTime.MinValue;
        }
        MatchDeadline = Now + MatchDuration;
        NextControlCheck = Now + TownStruggleRules.ControlCheckInterval;
        Phase = TownStrugglePhase.Active;
        Publish();
        foreach (var entry in _roster)
        {
            Refresh(entry.Player);
            if (!MovePlayer(entry.Player, Venue.TeamBase(entry.Team), Venue.Map))
            {
                Fail("A team base arrival was blocked. Staff must recover the match.");
                return;
            }
        }
    }

    private void CaptureTick()
    {
        var score = Now >= NextControlCheck;
        for (var zone = 0; zone < 3; zone++)
        {
            var order = 0;
            var chaos = 0;
            foreach (var entry in _roster)
            {
                if (!AtCapture(entry, zone)) { continue; }
                if (entry.Team == TownStruggleTeam.Order) { order++; } else { chaos++; }
            }
            var state = TownStruggleRules.StepCapture(_captureStates[zone], order, chaos);
            _captureStates[zone] = state;
            _progress[zone] = state.Progress;
            _owners[zone] = (int)state.Owner;
            if (score)
            {
                var award = TownStruggleRules.ScoreZone(state, order, chaos);
                OrderScore += award.OrderPoints;
                ChaosScore += award.ChaosPoints;
                foreach (var entry in _roster)
                {
                    if (AtCapture(entry, zone))
                    {
                        entry.ControlPoints += (double)(entry.Team == TownStruggleTeam.Order ? award.OrderIndividualPoints : award.ChaosIndividualPoints);
                    }
                }
            }
        }
        this.MarkDirty();
        if (score) { NextControlCheck = Now + TownStruggleRules.ControlCheckInterval; }
        TownStruggleRuntime.UpdateMarkers(this);
    }

    private bool AtCapture(TownStruggleParticipant entry, int zone) =>
        !entry.Disqualified && !entry.Returned && entry.Player is { Deleted: false, Alive: true, NetState: not null, Hidden: false } player &&
        !OutsideExpired(entry) &&
        player.Map == Venue.Map && player.InRange(Venue.CapturePoints[zone], TownStruggleVenue.CaptureRadius) &&
        Math.Abs(player.Z - Venue.CapturePoints[zone].Z) <= 8 && Now >= entry.RespawnAt;

    private void CheckIdle()
    {
        foreach (var entry in _roster)
        {
            if (!ObservePresence(entry))
            {
                if (Phase != TownStrugglePhase.Active) { return; }
                continue;
            }
            var player = entry.Player;
            if (player is not { Deleted: false, NetState: not null } || !Venue.Contains(player) || !Venue.IsAtBase(player))
            {
                entry.IdleSince = Now;
                continue;
            }
            if (Now - entry.IdleSince < TownStruggleRules.IdleLimit) { continue; }
            DisqualifyForAbsence(entry, "You were removed for base inactivity. Town Struggle entry is blocked for 48 hours.");
            if (Phase != TownStrugglePhase.Active) { return; }
        }
    }

    private bool OutsideExpired(TownStruggleParticipant entry)
    {
        // A delayed final tick must not count time after the scheduled match end as abandonment.
        var until = Now > MatchDeadline ? MatchDeadline : Now;
        return entry.OutsideSince != DateTime.MinValue && until - entry.OutsideSince > TownStruggleRules.OutsideGrace;
    }

    private bool ObservePresence(TownStruggleParticipant entry)
    {
        if (entry.Disqualified || entry.Returned) { return false; }
        if (OutsideExpired(entry))
        {
            DisqualifyForAbsence(entry, "You abandoned Town Struggle by being outside or disconnected for more than 30 seconds. All rewards are forfeited and entry is blocked for 48 hours.");
            return false;
        }
        var player = entry.Player;
        if (player is { Deleted: false, NetState: not null } && Venue.Contains(player))
        {
            if (entry.OutsideSince != DateTime.MinValue)
            {
                entry.OutsideSince = DateTime.MinValue;
                entry.IdleSince = Now;
            }
        }
        else if (entry.OutsideSince == DateTime.MinValue)
        {
            player?.NetState?.CancelAllTrades();
            entry.OutsideSince = Now;
            entry.IdleSince = Now;
            player?.SendMessage("Return to the Town Struggle battlefield within 30 seconds. Staying outside or disconnected longer forfeits this match and all rewards, with a 48-hour entry ban.");
        }
        return true;
    }

    private void CheckOutside()
    {
        foreach (var entry in _roster)
        {
            ObservePresence(entry);
            if (Phase != TownStrugglePhase.Active) { return; }
        }
    }

    private void DisqualifyForAbsence(TownStruggleParticipant entry, string message)
    {
        entry.Player?.NetState?.CancelAllTrades();
        entry.Disqualified = true;
        entry.Rewards = 0;
        entry.DamageContributors.Clear();
        if (entry.Player?.Account is Account account)
        {
            account.SetTag(BanTag, (Now + TownStruggleRules.IdleBanDuration).ToString("O", CultureInfo.InvariantCulture));
        }
        entry.Player?.SendMessage(message);
        Publish();
        if (!Return(entry, true)) { Fail("An abandoned or idle participant could not be returned outside the battlefield safely."); }
    }

    private void FinishMatch()
    {
        foreach (var entry in _roster) { entry.Player?.NetState?.CancelAllTrades(); }
        Phase = TownStrugglePhase.Results;
        ResultsDeadline = Now + TownStruggleRules.ResultsDuration;
        Publish();
        foreach (var entry in _roster)
        {
            if (entry.Returned || entry.Disqualified || entry.Player is not { Deleted: false } player) { continue; }
            ClearEventAggression(player);
            Refresh(player);
            if (!MovePlayer(player, Venue.TeamBase(entry.Team), Venue.Map))
            {
                Fail("A result-stage arrival was blocked. Staff recovery is required.");
                return;
            }
        }
    }

    private void PayRewards()
    {
        if (!RewardsCalculated)
        {
            CalculateRewards();
            RewardsCalculated = true;
        }
        foreach (var entry in _roster)
        {
            if (entry.RewardsDelivered) { continue; }
            if (entry.Rewards > 0 && entry.Player is { Deleted: false } player)
            {
                var token = new BattleCommendation { Amount = entry.Rewards };
                // Bank delivery and the durable flag execute in one loop operation, never across a save.
                player.BankBox.DropItem(token);
            }
            entry.RewardsDelivered = true;
        }
        PvpEventHistory.Record(CaptureCompletedResult());
    }

    internal PvpEventResult CaptureCompletedResult()
    {
        var winner = TownStruggleRules.Winner(OrderScore, ChaosScore);
        var rows = new List<PvpEventScore>(_roster.Count);
        foreach (var entry in _roster)
        {
            entry.CaptureIdentity();
            if (entry.Team is not (TownStruggleTeam.Order or TownStruggleTeam.Chaos) || entry.PlayerSerial == 0)
            {
                continue;
            }
            var won = !entry.Disqualified && winner == entry.Team;
            var outcome = entry.Disqualified ? "Abandoned" : winner == TownStruggleTeam.Neutral ? "Draw" : won ? "Win" : "Loss";
            rows.Add(new PvpEventScore(entry.PlayerSerial, entry.PlayerName ?? "Unknown", entry.GuildAbbreviation ?? "",
                entry.Team.ToString(), entry.Disqualified ? 0 : entry.Score, entry.Disqualified ? 0 : entry.Rewards,
                entry.KillPoints / TownStruggleRules.KillIndividualPoints, entry.Deaths, entry.Disqualified ? 1 : 0,
                1, won ? 1 : 0, entry.Healing, outcome));
        }
        return new PvpEventResult(EventKind.TownStruggle, Serial.Value, RoundNumber, MatchDeadline, Venue.Name,
            OrderScore, ChaosScore, rows);
    }

    private void CalculateRewards()
    {
        var ranked = new List<TownStruggleParticipant>();
        var votes = new Dictionary<Mobile, int>();
        var orderHealers = new List<TownStruggleParticipant>();
        var chaosHealers = new List<TownStruggleParticipant>();
        foreach (var entry in _roster)
        {
            if (entry.Disqualified || entry.Player is not { Deleted: false }) { continue; }
            ranked.Add(entry);
            (entry.Team == TownStruggleTeam.Order ? orderHealers : chaosHealers).Add(entry);
            if (entry.VoteFor != null)
            {
                votes.TryGetValue(entry.VoteFor, out var previous);
                votes[entry.VoteFor] = previous + 1;
            }
        }
        ranked.Sort((a, b) => a.Score != b.Score ? b.Score.CompareTo(a.Score) : a.Serial.Value.CompareTo(b.Serial.Value));
        static int CompareHealing(TownStruggleParticipant a, TownStruggleParticipant b) => a.Healing != b.Healing
            ? b.Healing.CompareTo(a.Healing) : a.Serial.Value.CompareTo(b.Serial.Value);
        orderHealers.Sort(CompareHealing);
        chaosHealers.Sort(CompareHealing);
        TownStruggleParticipant orderMvp = null;
        TownStruggleParticipant chaosMvp = null;
        foreach (var entry in ranked)
        {
            if (!votes.TryGetValue(entry.Player, out var count) || count == 0) { continue; }
            ref var candidate = ref (entry.Team == TownStruggleTeam.Order ? ref orderMvp : ref chaosMvp);
            if (candidate == null || count > votes[candidate.Player]) { candidate = entry; }
        }
        var winner = TownStruggleRules.Winner(OrderScore, ChaosScore);
        var prestigeAwards = new HashSet<(int, TownStruggleTeam)>();
        var rank = 1;
        for (var i = 0; i < ranked.Count; i++)
        {
            var entry = ranked[i];
            if (i > 0 && entry.Score < ranked[i - 1].Score) { rank = i + 1; }
            var healers = entry.Team == TownStruggleTeam.Order ? orderHealers : chaosHealers;
            var healerRank = healers.IndexOf(entry) + 1;
            var basePoints = entry.Score > 0 ? TownStruggleRules.RewardPoints(rank, ranked.Count, winner == entry.Team,
                false, 0, healers.Count) : 0;
            var bonus = entry == orderMvp || entry == chaosMvp ? 2 : 0;
            if (entry.Healing > 0)
            {
                bonus += healerRank == 1 ? 2 : healerRank == 2 && healers.Count >= 6 ? 1 : 0;
            }
            entry.Rewards = Math.Min(TownStruggleRules.MaximumRewardPoints, basePoints + bonus);
            if (entry.GuildId > 0)
            {
                _guildRewardLedger.TryGetValue(entry.GuildId, out var oldReward);
                _guildRewardLedger[entry.GuildId] = oldReward + entry.Rewards;
                if (prestigeAwards.Add((entry.GuildId, entry.Team)))
                {
                    _guildPrestigeLedger.TryGetValue(entry.GuildId, out var oldPrestige);
                    _guildPrestigeLedger[entry.GuildId] = oldPrestige + (winner == entry.Team ? 3 : 1);
                }
            }
        }
        this.MarkDirty();
    }

    private void FinishReturns()
    {
        var budget = 8;
        foreach (var entry in _roster)
        {
            if (entry.Returned) { continue; }
            if (!Return(entry)) { Fail("A return tile is blocked. Staff must recover this match."); return; }
            if (--budget == 0) { return; }
        }
        Reset();
    }

    public bool TryAbort(out string error)
    {
        if (_host == null) { error = "The venue is not bound."; return false; }
        foreach (var entry in _roster)
        {
            if (!Return(entry)) { error = "A return location is blocked; no participant record was deleted."; return false; }
        }
        Reset();
        error = null;
        return true;
    }

    private bool Return(TownStruggleParticipant entry, bool requireOutside = false)
    {
        if (entry.Returned && !requireOutside) { return true; }
        if (entry.Player is not { Deleted: false } player) { entry.Returned = true; return true; }
        player.NetState?.CancelAllTrades();
        ClearEventAggression(player);
        if (requireOutside)
        {
            if (!MovePlayer(player, entry.ReturnLocation, entry.ReturnMap, true)) { return false; }
            var map = player.Map == Map.Internal ? player.LogoutMap : player.Map;
            var point = player.Map == Map.Internal ? player.LogoutLocation : player.Location;
            if (map == null || map == Map.Internal || map == Venue.Map && Venue.Area.Contains(point) ||
                map == Map.Felucca && ExtractionDungeonLayout.LobbyBounds.Contains(point)) { return false; }
        }
        else if (!MovePlayer(player, entry.ReturnLocation, entry.ReturnMap)) { return false; }
        entry.Returned = true;
        return true;
    }

    private bool MovePlayer(Mobile player, Point3D point, Map map, bool outside = false)
    {
        if (!_movingPlayers.Add(player)) { return false; }
        try
        {
            return outside ? _host.MoveOutside(player, Venue, point, map) : _host.Move(player, point, map);
        }
        finally
        {
            _movingPlayers.Remove(player);
        }
    }

    private void Remove(TownStruggleParticipant entry)
    {
        _members.Remove(entry.Player);
        this.Remove(_roster, entry);
        entry.Delete();
        Publish();
    }

    private void Reset()
    {
        foreach (var entry in _roster) { entry.Delete(); }
        this.Clear(_roster);
        _members.Clear();
        _enforcingBoundary.Clear();
        _movingPlayers.Clear();
        Phase = TownStrugglePhase.Dormant;
        FaultMessage = null;
        Publish();
    }

    private void Fail(string message)
    {
        FaultMessage = message;
        Phase = TownStrugglePhase.Faulted;
        Publish();
    }

    private int Score(TownStruggleTeam team) => team == TownStruggleTeam.Order ? OrderScore : ChaosScore;
    private void AwardTeam(TownStruggleTeam team, int points)
    {
        if (team == TownStruggleTeam.Order) { OrderScore += points; } else { ChaosScore += points; }
    }
    private void UpdateCaptureView()
    {
        if (_progress.Length != 3 || _owners.Length != 3) { throw new InvalidOperationException("Invalid saved capture state."); }
        for (var i = 0; i < 3; i++) { _captureStates[i] = new TownStruggleCaptureState(_progress[i], (TownStruggleTeam)_owners[i]); }
    }
    private void Publish()
    {
        TownStruggleCombat.Publish(this);
        _host?.Notify(this);
    }
    private static void Refresh(Mobile player)
    {
        player.Poison = null;
        player.Combatant = null;
        player.Warmode = false;
        player.Hits = player.HitsMax;
        player.Mana = player.ManaMax;
        player.Stam = player.StamMax;
        player.Paralyzed = false;
    }
    private void ClearEventAggression(Mobile player)
    {
        foreach (var entry in _roster)
        {
            if (entry.Player is not { Deleted: false } other || other == player) { continue; }
            player.RemoveAggressed(other);
            player.RemoveAggressor(other);
            other.RemoveAggressed(player);
            other.RemoveAggressor(player);
        }
    }

    [AfterDeserialization(false)]
    private void AfterDeserialization()
    {
        // Damage attribution is transient; never resume a half-played round with missing credits.
        if (Phase == TownStrugglePhase.Active)
        {
            Phase = TownStrugglePhase.Faulted;
            FaultMessage = "The shard restarted during a match. Staff must recover it before rotation resumes.";
        }
        TownStruggleRuntime.Restore(this);
    }

    public override void OnAfterDelete()
    {
        _timer.Cancel();
        _region?.Release();
        _region = null;
        TownStruggleCombat.Clear(this);
        TownStruggleRuntime.Removed(this);
        foreach (var marker in _markerReferences) { marker.Delete(); }
        _markerReferences.Clear();
        foreach (var item in _auxiliaryReferences) { item.Delete(); }
        _auxiliaryReferences.Clear();
        _members.Clear();
        _roster.Clear();
        _enforcingBoundary.Clear();
        _movingPlayers.Clear();
        _host = null;
        base.OnAfterDelete();
    }
}
