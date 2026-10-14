using System;
using ModernUO.Serialization;
using Server.Engines.ModularDungeon;
using Server.Logging;

namespace Server.Engines.TownStruggle;

public enum PvpRotatingEvent
{
    None,
    Extraction,
    TownStruggle
}

internal enum PvpEventAvailability
{
    Idle,
    Busy,
    Faulted,
    Unavailable
}

internal interface IPvpEventRotationIntegration
{
    PvpEventAvailability ExtractionAvailability { get; }
    PvpEventAvailability TownAvailability { get; }
    string FailureReason { get; }
    bool TryStartExtraction(out string error);
    bool TryStartTown(out string error);
}

[SerializationGenerator(0)]
public partial class PvpEventRotationController : Item
{
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(PvpEventRotationController));

    [CanBeNull]
    [SerializableField(0, setter: "private")]
    private ExtractionEventController _extraction;

    [CanBeNull]
    [SerializableField(1, setter: "private")]
    private TownStruggleController _town;

    [SerializableField(2, setter: "private")]
    private bool _enabled;

    [SerializableField(3, setter: "private")]
    private PvpRotatingEvent _nextEvent;

    [SerializableField(4, setter: "private")]
    private PvpRotatingEvent _activeEvent;

    [AnchoredDateTime]
    [SerializableField(5, setter: "private")]
    private DateTime _nextDeadline;

    [SerializableField(6, setter: "private")]
    private TimeSpan _interval;

    [SerializableField(7, setter: "private")]
    private bool _previousAutomaticRounds;

    [SerializableField(8, setter: "private")]
    private TimeSpan _previousWaitingInterval;

    [SerializableField(9, setter: "private")]
    private string _faultMessage;

    [SerializableField(10, setter: "private")]
    private int _completedSlots;

    private TimerExecutionToken _timer;
    private IPvpEventRotationIntegration _integration;
    private PvpRotatingEvent _dispatching;

    [Constructible(AccessLevel.Administrator)]
    public PvpEventRotationController() : base(1)
    {
        Visible = false;
        Movable = false;
    }

    public override string DefaultName => "PvP event rotation controller";
    public bool Paused => !string.IsNullOrEmpty(_faultMessage);

    internal bool TryEnable(ExtractionEventController extraction, TownStruggleController town,
        IPvpEventRotationIntegration integration, TimeSpan interval, out string error)
    {
        if (Deleted || _enabled || extraction is not { Deleted: false } || town is not { Deleted: false } ||
            integration == null || interval < TimeSpan.FromSeconds(1) || interval > TimeSpan.FromDays(1))
        {
            error = "Rotation needs two installed events and a waiting interval from one second to one day.";
            return false;
        }
        if (integration.ExtractionAvailability != PvpEventAvailability.Idle ||
            integration.TownAvailability != PvpEventAvailability.Idle)
        {
            error = "Both events must be healthy and idle, with no participants or queued players, before enabling rotation.";
            return false;
        }

        Extraction = extraction;
        Town = town;
        PreviousAutomaticRounds = extraction.AutomaticRounds;
        PreviousWaitingInterval = extraction.WaitingInterval;
        extraction.ConfigureWaiting(extraction.WaitingInterval, false);
        _integration = integration;
        Interval = interval;
        NextEvent = PvpRotatingEvent.Extraction;
        ActiveEvent = PvpRotatingEvent.None;
        NextDeadline = Core.Now + interval;
        FaultMessage = null;
        Enabled = true;
        StartTimer();
        error = null;
        return true;
    }

    public bool TryDisable(out string error)
    {
        if (!_enabled)
        {
            error = null;
            return true;
        }
        if (_integration == null || _integration.ExtractionAvailability != PvpEventAvailability.Idle ||
            _integration.TownAvailability != PvpEventAvailability.Idle)
        {
            error = "Rotation cannot be disabled until both events are healthy, idle, and have no participants. Recover faulted events explicitly first.";
            return false;
        }
        _timer.Cancel();
        Enabled = false;
        ActiveEvent = PvpRotatingEvent.None;
        NextDeadline = DateTime.MinValue;
        FaultMessage = null;
        _extraction.ConfigureWaiting(_previousWaitingInterval, _previousAutomaticRounds);
        error = null;
        return true;
    }

    internal bool AllowsDispatch(PvpRotatingEvent kind) =>
        _enabled && !Paused && _dispatching == kind && _activeEvent == kind;

    internal void BindIntegration(IPvpEventRotationIntegration integration)
    {
        _timer.Cancel();
        _integration = integration;
        if (_enabled)
        {
            if (_extraction is not { Deleted: false } || _town is not { Deleted: false } ||
                !Enum.IsDefined(_activeEvent) || _nextEvent is not (PvpRotatingEvent.Extraction or PvpRotatingEvent.TownStruggle) ||
                _interval < TimeSpan.FromSeconds(1) || _interval > TimeSpan.FromDays(1) ||
                _previousAutomaticRounds && _previousWaitingInterval < TimeSpan.FromSeconds(1))
            {
                Pause("The saved event rotation needs staff recovery.");
                return;
            }
            _extraction.ConfigureWaiting(_extraction.WaitingInterval, false);
            if (!Paused)
            {
                StartTimer();
            }
        }
    }

    private void StartTimer()
    {
        _timer.Cancel();
        Timer.StartTimer(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), Advance, out _timer);
    }

    internal void Advance()
    {
        // Lobby installation temporarily withdraws Extraction readiness without invalidating the saved rotation.
        if (Deleted || !_enabled || Paused || _integration == null || ExtractionLobbyDecoration.IsBusy)
        {
            return;
        }
        try
        {
            var extraction = _integration.ExtractionAvailability;
            var town = _integration.TownAvailability;
            if (extraction is PvpEventAvailability.Faulted or PvpEventAvailability.Unavailable ||
                town is PvpEventAvailability.Faulted or PvpEventAvailability.Unavailable)
            {
                Pause(_integration.FailureReason ?? "An event requires staff recovery; rotation is paused.");
                return;
            }
            if (_extraction.AutomaticRounds)
            {
                Pause("The extraction event's independent scheduler was enabled while rotation owned it.");
                return;
            }

            if (_activeEvent != PvpRotatingEvent.None)
            {
                var active = _activeEvent == PvpRotatingEvent.Extraction ? extraction : town;
                var other = _activeEvent == PvpRotatingEvent.Extraction ? town : extraction;
                if (other != PvpEventAvailability.Idle)
                {
                    Pause("An event started outside the rotation's exclusive slot.");
                }
                else if (active == PvpEventAvailability.Idle)
                {
                    CompleteSlot();
                }
                return;
            }
            if (extraction != PvpEventAvailability.Idle || town != PvpEventAvailability.Idle)
            {
                Pause("An event started outside the rotation's exclusive slot.");
                return;
            }
            if (Core.Now < _nextDeadline)
            {
                return;
            }

            ActiveEvent = _nextEvent;
            NextDeadline = DateTime.MinValue;
            _dispatching = _activeEvent;
            string error;
            var started = _activeEvent == PvpRotatingEvent.Extraction
                ? _integration.TryStartExtraction(out error)
                : _integration.TryStartTown(out error);
            _dispatching = PvpRotatingEvent.None;

            if (!started)
            {
                // Only a healthy Extraction slot can be empty; Town opens signup before gathering entrants.
                if (_activeEvent == PvpRotatingEvent.Extraction &&
                    _integration.ExtractionAvailability == PvpEventAvailability.Idle &&
                    _integration.TownAvailability == PvpEventAvailability.Idle)
                {
                    CompleteSlot();
                }
                else
                {
                    Pause(error ?? "The next event could not start safely.");
                }
            }
        }
        catch (Exception exception)
        {
            _dispatching = PvpRotatingEvent.None;
            Pause("An unexpected event operation paused rotation without recovering or replacing participants.");
            _logger.Error(exception, "PvP rotation {Controller} failed", Serial);
        }
    }

    private void CompleteSlot()
    {
        NextEvent = _activeEvent == PvpRotatingEvent.Extraction ? PvpRotatingEvent.TownStruggle : PvpRotatingEvent.Extraction;
        ActiveEvent = PvpRotatingEvent.None;
        CompletedSlots = checked(_completedSlots + 1);
        // Town's signup already provides its waiting period; only Extraction needs the coordinator's interval.
        NextDeadline = _nextEvent == PvpRotatingEvent.TownStruggle ? Core.Now : Core.Now + _interval;
    }

    internal void Pause(string reason)
    {
        FaultMessage = reason;
        _timer.Cancel();
        _logger.Warning("PvP rotation {Controller} paused: {Reason}", Serial, reason);
    }

    internal void Suspend() => _timer.Cancel();

    [AfterDeserialization(false)]
    private void AfterDeserialization() => PvpEventRotationRuntime.Restore(this);

    public override void OnDelete()
    {
        _timer.Cancel();
        base.OnDelete();
    }

    public override void OnAfterDelete()
    {
        _timer.Cancel();
        PvpEventRotationRuntime.Removed(this);
        _integration = null;
        Extraction = null;
        Town = null;
        _dispatching = PvpRotatingEvent.None;
        base.OnAfterDelete();
    }
}
