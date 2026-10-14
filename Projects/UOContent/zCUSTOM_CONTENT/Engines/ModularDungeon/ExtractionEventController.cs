using System;
using System.Collections.Generic;
using ModernUO.Serialization;
using Server.Collections;
using Server.Engines.TownStruggle;
using Server.Items;
using Server.Logging;
using Server.Mobiles;

namespace Server.Engines.ModularDungeon;

public enum ExtractionEventPhase
{
    Waiting,
    Starting,
    Active,
    Finishing,
    Faulted
}

public enum ExtractionFinishReason
{
    Extracted,
    Death,
    TimeExpired,
    Stopped
}

public readonly record struct ExtractionModuleSpawn(int ModuleId, Point3D Location);

public interface IExtractionEventResetSession
{
    bool Complete { get; }
    void Process(int operationBudget);
}

public interface IExtractionEventIntegration
{
    DateTime UtcNow { get; }
    bool PolicyReady { get; }
    IReadOnlyList<ExtractionModuleSpawn> ModuleSpawns { get; }
    bool IsInLobby(Mobile player);
    bool IsInArena(Mobile player);
    Mobile[] SnapshotLobbyPlayers();
    bool TryMoveToLobby(Mobile player, out string error);
    bool TryAdmit(
        ExtractionEventController controller,
        Mobile player,
        ExtractionModuleSpawn spawn,
        int moduleCycle,
        out string error
    );
    bool TryFinish(
        ExtractionEventController controller,
        Mobile player,
        ExtractionFinishReason reason,
        out string error
    );
    IExtractionEventResetSession CreateResetSession(ExtractionEventController controller);
    bool ActivatePreparedCreatures(ExtractionEventController controller, int operationBudget) => true;
    void NotifyPhase(ExtractionEventController controller);
}

[SerializationGenerator(0)]
public partial class ExtractionEventController : Item
{
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(ExtractionEventController));
    private static readonly int[] _moduleOrder =
    [
        0, 1, 2, 3, 4, 5, 9, 10, 14, 15, 19, 20, 21, 22, 23, 24,
        6, 7, 8, 11, 12, 13, 16, 17, 18
    ];

    public const int OperationsPerTick = 8;
    public static readonly TimeSpan MatchDuration = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan ExtractionDelay = TimeSpan.FromMinutes(7);

    [SerializableField(0, setter: "private")]
    private ExtractionEventPhase _phase;

    [SerializableField(1, setter: "private")]
    private DateTime _matchDeadline;

    [SerializableField(2, setter: "private")]
    private DateTime _waitingDeadline;

    [SerializableField(3, setter: "private")]
    private int _roundNumber;

    [SerializableField(4, setter: "private")]
    private TimeSpan _waitingInterval;

    [SerializableField(5, setter: "private")]
    private bool _automaticRounds;

    [Tidy]
    [SerializableField(6, getter: "private", setter: "private")]
    private List<Mobile> _participantReferences = [];

    [Tidy]
    [SerializableField(7, getter: "private", setter: "private")]
    private List<Mobile> _queuedReferences = [];

    [Tidy]
    [SerializableField(8, getter: "private", setter: "private")]
    private List<BaseCreature> _creatureReferences = [];

    [Tidy]
    [SerializableField(9, getter: "private", setter: "private")]
    private List<Item> _managedReferences = [];

    [Tidy]
    [SerializableField(10, getter: "private", setter: "private")]
    private List<Item> _lootReferences = [];

    [SerializableField(11, setter: "private")]
    private ExtractionFinishReason _finishReason;

    [SerializableField(12, setter: "private")]
    private int _admissionCursor;

    [SerializableField(13, setter: "private")]
    private int _admittedCount;

    [SerializableField(14, setter: "private")]
    private string _faultMessage;

    [CanBeNull]
    [SerializableField(15, setter: "private")]
    private Container _recoveryStorage;

    private readonly HashSet<Mobile> _participantSet = [];
    private TimerExecutionToken _tickTimer;
    private IExtractionEventIntegration _integration;
    private IExtractionEventResetSession _resetSession;
    private ExtractionModuleSpawn[] _orderedSpawns;
    private Action _tickCallback;
    private int _participantCursor;
    private bool _publishedExtractionUnlocked;

    public static event Action<ExtractionEventController> Loaded;
    public static event Action<ExtractionEventController> Removed;

    [Constructible(AccessLevel.Administrator)]
    public ExtractionEventController() : base(0x1F14)
    {
        Visible = false;
        Movable = false;
    }

    public override string DefaultName => "extraction event controller";
    public IReadOnlyList<Mobile> Participants => _participantReferences;
    public IReadOnlyList<Mobile> QueuedPlayers => _queuedReferences;
    public IReadOnlyList<BaseCreature> OwnedCreatures => _creatureReferences;
    public IReadOnlyList<Item> ManagedItems => _managedReferences;
    public IReadOnlyList<Item> OwnedLoot => _lootReferences;
    public bool IsBound => _integration != null;
    internal bool ReadyForScheduledStart => _integration?.PolicyReady == true && _orderedSpawns != null;
    public bool IsParticipant(Mobile player) => player != null && _participantSet.Contains(player);
    public bool ExtractionUnlocked
    {
        get
        {
            if (_phase != ExtractionEventPhase.Active || _integration?.PolicyReady != true)
            {
                return false;
            }
            var now = _integration.UtcNow;
            return now < _matchDeadline && now >= _matchDeadline - MatchDuration + ExtractionDelay;
        }
    }

    public static int GetSpawnModuleId(int ordinal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        return _moduleOrder[ordinal % _moduleOrder.Length];
    }

    public void BindIntegration(IExtractionEventIntegration integration)
    {
        ArgumentNullException.ThrowIfNull(integration);
        _tickTimer.Cancel();
        _integration = integration;
        RebuildParticipantSet();

        try
        {
            _orderedSpawns = ValidateSpawns(integration.ModuleSpawns);

            // Reset and admission can change loot and world objects; replay is never a recovery strategy.
            if (_phase == ExtractionEventPhase.Starting && _resetSession == null)
            {
                Fail("The event restarted during preparation. Staff must recover it before starting another round.");
            }

            ScheduleTick();
            PublishPhase();
        }
        catch (Exception exception)
        {
            Fail("The event could not bind its installed layout.", exception);
        }
    }

    public void ConfigureWaiting(TimeSpan interval, bool automaticRounds)
    {
        if (interval < TimeSpan.Zero || interval > TimeSpan.FromDays(1) ||
            automaticRounds && interval < TimeSpan.FromSeconds(1))
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        WaitingInterval = interval;
        AutomaticRounds = automaticRounds;

        if (_phase == ExtractionEventPhase.Waiting)
        {
            ScheduleWaiting();
        }
    }

    public bool TryJoinLobby(Mobile player, out string error)
    {
        if (!IsOnlineLivingPlayer(player))
        {
            error = "Only a living, connected player can join the lobby.";
            return false;
        }

        if (_integration == null || _phase == ExtractionEventPhase.Faulted)
        {
            error = "The extraction event is unavailable.";
            return false;
        }

        if (IsParticipant(player))
        {
            error = "You are already participating in this match.";
            return false;
        }

        try
        {
            return _integration.TryMoveToLobby(player, out error);
        }
        catch (Exception exception)
        {
            Fail("Lobby entry failed safely.", exception);
            error = _faultMessage;
            return false;
        }
    }

    public bool TryStart(out string error)
    {
        if (!PvpEventRotationRuntime.CanStartExtraction(this, out error))
        {
            return false;
        }

        if (_integration == null || _orderedSpawns == null || !_integration.PolicyReady)
        {
            error = "The event rules have not been configured.";
            return false;
        }

        if (_phase != ExtractionEventPhase.Waiting || _participantReferences.Count != 0)
        {
            error = "The event is not ready to begin another match.";
            return false;
        }

        try
        {
            using var eligible = PooledRefList<Mobile>.Create();
            var snapshot = _integration.SnapshotLobbyPlayers();

            foreach (var player in snapshot)
            {
                if (IsOnlineLivingPlayer(player) && _integration.IsInLobby(player) && !TownStruggleRuntime.IsEnrolled(player))
                {
                    eligible.Add(player);
                }
            }

            if (eligible.Count == 0)
            {
                error = "There are no living players waiting in the lobby.";
                AdvanceEmptyWaitingBoundary();
                return false;
            }

            _queuedReferences.Clear();
            var seen = new HashSet<Mobile>();

            for (var index = 0; index < eligible.Count; index++)
            {
                if (seen.Add(eligible[index]))
                {
                    _queuedReferences.Add(eligible[index]);
                }
            }

            _queuedReferences.Sort(static (left, right) => left.Serial.Value.CompareTo(right.Serial.Value));
            _resetSession = _integration.CreateResetSession(this);

            if (_resetSession == null)
            {
                throw new InvalidOperationException("The event reset did not return a preparation session.");
            }

            RoundNumber = checked(_roundNumber + 1);
            AdmissionCursor = 0;
            AdmittedCount = 0;
            MatchDeadline = DateTime.MinValue;
            WaitingDeadline = DateTime.MinValue;
            FaultMessage = null;
            Phase = ExtractionEventPhase.Starting;
            this.MarkDirty();
            PublishPhase();
            error = _phase == ExtractionEventPhase.Faulted ? _faultMessage : null;
            return error == null;
        }
        catch (Exception exception)
        {
            Fail("Match preparation failed. No additional players will be admitted.", exception);
            error = _faultMessage;
            return false;
        }
    }

    public bool TryExtract(Mobile player, out string error)
    {
        if (_integration == null || !_integration.PolicyReady || _phase != ExtractionEventPhase.Active ||
            !IsOnlineLivingPlayer(player) || !IsParticipant(player) || !_integration.IsInArena(player))
        {
            error = "You are not an active participant able to extract here.";
            return false;
        }

        if (_integration.UtcNow >= _matchDeadline)
        {
            BeginFinishing(ExtractionFinishReason.TimeExpired);
            error = "The match has ended.";
            return false;
        }

        if (!ExtractionUnlocked)
        {
            error = "Extraction opens seven minutes after the match begins.";
            return false;
        }

        return FinishParticipant(player, ExtractionFinishReason.Extracted, out error);
    }

    public void NotifyDeath(Mobile player)
    {
        if (_phase == ExtractionEventPhase.Active && _integration?.PolicyReady == true &&
            player is { Deleted: false, Alive: false } && IsParticipant(player))
        {
            FinishParticipant(player, ExtractionFinishReason.Death, out _);
        }
    }

    internal void RecordStatisticsDeath(Mobile victim, Mobile killer)
    {
        if (_phase != ExtractionEventPhase.Active || _integration?.PolicyReady != true ||
            _integration.UtcNow >= _matchDeadline || victim is not { Deleted: false, Alive: false } ||
            !IsParticipant(victim) || !_integration.IsInArena(victim))
        {
            return;
        }
        var creditedKiller = killer is { Deleted: false } && killer != victim && IsParticipant(killer) &&
                            _integration.IsInArena(killer) ? killer : null;
        ExtractionEventStatistics.RecordDeath(this, victim, creditedKiller);
    }

    public bool TryStop(out string error)
    {
        AutomaticRounds = false;
        WaitingDeadline = DateTime.MinValue;

        if (_participantReferences.Count != 0 && (_integration == null || !_integration.PolicyReady))
        {
            error = "Participant recovery needs configured event rules.";
            return false;
        }

        ExtractionEventStatistics.Discard(this);
        _resetSession = null;
        _queuedReferences.Clear();
        this.MarkDirty();

        if (_participantReferences.Count == 0)
        {
            EnterWaiting();
        }
        else
        {
            BeginFinishing(ExtractionFinishReason.Stopped);
        }

        error = null;
        return true;
    }

    public void PauseForRecovery(string reason) =>
        Fail(string.IsNullOrWhiteSpace(reason) ? "The event requires staff recovery." : reason);

    public void SetRecoveryStorage(Container storage)
    {
        if (storage is { Deleted: true })
        {
            throw new ArgumentException("Recovery storage cannot be deleted.", nameof(storage));
        }

        RecoveryStorage = storage;
    }

    public void AddOwnedCreature(BaseCreature creature) => AddReference(_creatureReferences, creature);
    public void RemoveOwnedCreature(BaseCreature creature) => RemoveReference(_creatureReferences, creature);
    public void AddOwnedLoot(Item item) => AddReference(_lootReferences, item);
    public void RemoveOwnedLoot(Item item) => RemoveReference(_lootReferences, item);
    public void AddManagedItem(Item item) => AddReference(_managedReferences, item);
    public void RemoveManagedItem(Item item) => RemoveReference(_managedReferences, item);

    private void AddReference<T>(List<T> references, T entity) where T : class, IEntity
    {
        if (entity == null || entity.Deleted)
        {
            throw new ArgumentException("Managed references must identify an existing entity.", nameof(entity));
        }

        if (!references.Contains(entity))
        {
            references.Add(entity);
            this.MarkDirty();
        }
    }

    private void RemoveReference<T>(List<T> references, T entity) where T : class, IEntity
    {
        if (references.Remove(entity))
        {
            this.MarkDirty();
        }
    }

    internal void Advance()
    {
        if (Deleted || _integration == null)
        {
            return;
        }

        try
        {
            if (!_integration.PolicyReady)
            {
                return;
            }

            switch (_phase)
            {
                case ExtractionEventPhase.Waiting when _automaticRounds && _waitingDeadline != DateTime.MinValue &&
                    _integration.UtcNow >= _waitingDeadline:
                {
                    TryStart(out _);
                    break;
                }
                case ExtractionEventPhase.Starting:
                {
                    PrepareTick();
                    break;
                }
                case ExtractionEventPhase.Active when _integration.UtcNow >= _matchDeadline:
                {
                    BeginFinishing(ExtractionFinishReason.TimeExpired);
                    break;
                }
                case ExtractionEventPhase.Active:
                {
                    if (!_publishedExtractionUnlocked && ExtractionUnlocked)
                    {
                        PublishPhase();
                        if (_phase != ExtractionEventPhase.Active)
                        {
                            break;
                        }
                    }
                    InspectParticipants();
                    break;
                }
                case ExtractionEventPhase.Finishing:
                {
                    FinishTick();
                    break;
                }
            }
        }
        catch (Exception exception)
        {
            Fail("The event paused after an unexpected operation failure.", exception);
        }
    }

    private void Tick()
    {
        Advance();

        if (!Deleted && _integration != null)
        {
            ScheduleTick();
        }
    }

    private void ScheduleTick()
    {
        _tickTimer.Cancel();
        var delay = _phase is ExtractionEventPhase.Starting or ExtractionEventPhase.Finishing
            ? TimeSpan.FromMilliseconds(50)
            : TimeSpan.FromMilliseconds(250);
        Timer.StartTimer(delay, _tickCallback ??= Tick, out _tickTimer);
    }

    private void PrepareTick()
    {
        if (_resetSession == null)
        {
            Fail("Match preparation has no reset session.");
            return;
        }

        if (!_resetSession.Complete)
        {
            _resetSession.Process(OperationsPerTick);
            return;
        }

        var budget = OperationsPerTick;

        while (_admissionCursor < _queuedReferences.Count && budget-- > 0)
        {
            var player = _queuedReferences[_admissionCursor];
            AdmissionCursor++;

            if (!IsOnlineLivingPlayer(player) || !_integration.IsInLobby(player) || TownStruggleRuntime.IsEnrolled(player))
            {
                continue;
            }

            var spawn = _orderedSpawns[_admittedCount % _orderedSpawns.Length];

            // Keep a recovery reference even if transport fails after changing the player's location.
            _participantReferences.Add(player);
            _participantSet.Add(player);
            this.MarkDirty();

            if (!_integration.TryAdmit(this, player, spawn, _admittedCount / _orderedSpawns.Length, out var error))
            {
                Fail(error ?? "An event spawn could not admit its player safely.");
                return;
            }

            AdmittedCount++;
            this.MarkDirty();
        }

        if (_admissionCursor < _queuedReferences.Count)
        {
            return;
        }

        if (_participantReferences.Count == 0)
        {
            EnterWaiting();
            return;
        }

        if (!_integration.ActivatePreparedCreatures(this, Math.Max(0, budget)))
        {
            return;
        }

        _queuedReferences.Clear();
        _resetSession = null;
        this.MarkDirty();
        MatchDeadline = _integration.UtcNow + MatchDuration;
        Phase = ExtractionEventPhase.Active;
        ExtractionEventStatistics.Begin(this);
        PublishPhase();
    }

    private void InspectParticipants()
    {
        var checks = Math.Min(OperationsPerTick, _participantReferences.Count);

        while (checks-- > 0 && _participantReferences.Count != 0)
        {
            _participantCursor %= _participantReferences.Count;
            var player = _participantReferences[_participantCursor];

            if (player == null || player.Deleted)
            {
                ExtractionEventStatistics.RecordUnavailable(this, player);
                RemoveParticipant(player);
            }
            else if (!player.Alive)
            {
                if (!FinishParticipant(player, ExtractionFinishReason.Death, out _))
                {
                    return;
                }
            }
            else
            {
                _participantCursor++;
            }
        }
    }

    private void BeginFinishing(ExtractionFinishReason reason)
    {
        FinishReason = reason;
        Phase = ExtractionEventPhase.Finishing;
        _resetSession = null;
        _queuedReferences.Clear();
        this.MarkDirty();
        PublishPhase();
    }

    private void FinishTick()
    {
        var budget = OperationsPerTick;

        while (_participantReferences.Count != 0 && budget-- > 0)
        {
            var player = _participantReferences[0];

            if (player == null || player.Deleted)
            {
                ExtractionEventStatistics.RecordUnavailable(this, player);
                RemoveParticipant(player);
            }
            else if (!FinishParticipant(player, _finishReason, out _))
            {
                return;
            }
        }

        if (_participantReferences.Count == 0)
        {
            ExtractionEventStatistics.Complete(this, _integration.UtcNow);
            EnterWaiting();
        }
    }

    private bool FinishParticipant(Mobile player, ExtractionFinishReason reason, out string error)
    {
        try
        {
            if (!_integration.TryFinish(this, player, reason, out error))
            {
                Fail(error ?? "An event participant could not be recovered safely.");
                return false;
            }

            ExtractionEventStatistics.RecordFinish(this, player, reason);
            RemoveParticipant(player);
            return true;
        }
        catch (Exception exception)
        {
            Fail("An event participant could not be recovered safely.", exception);
            error = _faultMessage;
            return false;
        }
    }

    private void RemoveParticipant(Mobile player)
    {
        _participantReferences.Remove(player);
        _participantSet.Remove(player);
        this.MarkDirty();
    }

    private void EnterWaiting()
    {
        var nextMatchBoundary = _matchDeadline;
        Phase = ExtractionEventPhase.Waiting;
        MatchDeadline = DateTime.MinValue;
        FaultMessage = null;
        _resetSession = null;
        _queuedReferences.Clear();
        this.MarkDirty();
        if (_automaticRounds && _integration != null && nextMatchBoundary != DateTime.MinValue)
        {
            // A finished match's deadline is the next start, not the beginning of another waiting period.
            WaitingDeadline = nextMatchBoundary;
        }
        else
        {
            ScheduleWaiting();
        }

        PublishPhase();
    }

    private void ScheduleWaiting() => WaitingDeadline = _automaticRounds && _integration != null
        ? _integration.UtcNow + _waitingInterval
        : DateTime.MinValue;

    private void AdvanceEmptyWaitingBoundary()
    {
        if (!_automaticRounds || _integration == null || _waitingDeadline == DateTime.MinValue)
        {
            ScheduleWaiting();
            return;
        }

        var now = _integration.UtcNow;

        if (_waitingDeadline <= now)
        {
            var cycles = (now - _waitingDeadline).Ticks / _waitingInterval.Ticks + 1;
            WaitingDeadline += TimeSpan.FromTicks(checked(cycles * _waitingInterval.Ticks));
        }
    }

    private void PublishPhase()
    {
        try
        {
            _publishedExtractionUnlocked = ExtractionUnlocked;
            _integration?.NotifyPhase(this);
        }
        catch (Exception exception)
        {
            Fail("The event could not publish its region phase safely.", exception);
        }
    }

    private void Fail(string message, Exception exception = null)
    {
        ExtractionEventStatistics.Discard(this);
        Phase = ExtractionEventPhase.Faulted;
        FaultMessage = message;
        _resetSession = null;

        if (exception == null)
        {
            _logger.Warning("Extraction event {Controller} faulted: {Reason}", Serial, message);
        }
        else
        {
            _logger.Error(exception, "Extraction event {Controller} faulted: {Reason}", Serial, message);
        }
    }

    private void RebuildParticipantSet()
    {
        _participantSet.Clear();

        foreach (var player in _participantReferences)
        {
            if (player is { Deleted: false })
            {
                _participantSet.Add(player);
            }
        }
    }

    private static bool IsOnlineLivingPlayer(Mobile player) =>
        player is { Deleted: false, Player: true, Alive: true, NetState: not null };

    private static ExtractionModuleSpawn[] ValidateSpawns(IReadOnlyList<ExtractionModuleSpawn> spawns)
    {
        if (spawns?.Count != _moduleOrder.Length)
        {
            throw new ArgumentException("The event needs exactly 25 module spawn anchors.", nameof(spawns));
        }

        var byId = new ExtractionModuleSpawn[_moduleOrder.Length];
        Span<bool> seen = stackalloc bool[_moduleOrder.Length];
        seen.Clear();

        foreach (var spawn in spawns)
        {
            if ((uint)spawn.ModuleId >= (uint)seen.Length || seen[spawn.ModuleId])
            {
                throw new ArgumentException("Module spawn IDs must be unique M00 through M24.", nameof(spawns));
            }

            seen[spawn.ModuleId] = true;
            byId[spawn.ModuleId] = spawn;
        }

        var ordered = new ExtractionModuleSpawn[_moduleOrder.Length];

        for (var index = 0; index < ordered.Length; index++)
        {
            ordered[index] = byId[_moduleOrder[index]];
        }

        return ordered;
    }

    [AfterDeserialization(false)]
    private void AfterDeserialization()
    {
        _tickTimer.Cancel();
        _resetSession = null;
        RebuildParticipantSet();

        try
        {
            Loaded?.Invoke(this);
        }
        catch (Exception exception)
        {
            Fail("The installed event could not be restored safely.", exception);
        }
    }

    public override void OnDelete()
    {
        _tickTimer.Cancel();
        base.OnDelete();
    }

    public override void OnAfterDelete()
    {
        ExtractionEventStatistics.Discard(this);
        _tickTimer.Cancel();
        try
        {
            Removed?.Invoke(this);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Extraction event {Controller} could not unregister after deletion", Serial);
        }
        _integration = null;
        _resetSession = null;
        _orderedSpawns = null;
        _tickCallback = null;
        _recoveryStorage = null;
        _participantReferences.Clear();
        _queuedReferences.Clear();
        _creatureReferences.Clear();
        _managedReferences.Clear();
        _lootReferences.Clear();
        _participantSet.Clear();
        base.OnAfterDelete();
    }
}
