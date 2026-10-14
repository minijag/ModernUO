using System;
using Server.Engines.ModularDungeon;

namespace Server.Engines.TownStruggle;

public static class PvpEventRotationRuntime
{
    private static bool _recoveryRequired;
    public static PvpEventRotationController Current { get; private set; }
    public static bool IsEnabled => Current is { Deleted: false, Enabled: true };

    public static void Configure()
    {
        CommandSystem.Register("PvpRotation", AccessLevel.Administrator, Command);
        EventSink.Shutdown += Shutdown;
    }

    public static bool CanStartExtraction(ExtractionEventController controller, out string error)
    {
        if (ExtractionLobbyDecoration.IsBusy)
        {
            error = "The PvP lobby installation must finish before an event can start.";
            return false;
        }
        if (_recoveryRequired)
        {
            error = "Duplicate saved rotation ownership needs staff recovery before any event can start.";
            return false;
        }
        if (TownStruggleRuntime.Controller is { Deleted: false } town &&
            (town.Phase != TownStrugglePhase.Dormant || town.Participants.Count != 0))
        {
            error = "Town Struggle has not finished; Extraction cannot overlap it.";
            return false;
        }
        if (IsEnabled && (Current.Extraction != controller || !Current.AllowsDispatch(PvpRotatingEvent.Extraction)))
        {
            error = "The PvP rotation owns event starts. Use [PvpRotation status to see the next event.";
            return false;
        }
        error = null;
        return true;
    }

    public static bool CanStartTown(TownStruggleController controller, out string error)
    {
        if (ExtractionLobbyDecoration.IsBusy)
        {
            error = "The PvP lobby installation must finish before an event can start.";
            return false;
        }
        if (_recoveryRequired)
        {
            error = "Duplicate saved rotation ownership needs staff recovery before any event can start.";
            return false;
        }
        if (ExtractionEventRuntime.Controller is { Deleted: false } extraction &&
            (extraction.Phase != ExtractionEventPhase.Waiting || extraction.Participants.Count != 0 || extraction.QueuedPlayers.Count != 0))
        {
            error = "Extraction has not finished or needs recovery; Town Struggle cannot overlap it.";
            return false;
        }
        if (IsEnabled && (Current.Town != controller || !Current.AllowsDispatch(PvpRotatingEvent.TownStruggle)))
        {
            error = "The PvP rotation owns event starts. Use [PvpRotation status to see the next event.";
            return false;
        }
        error = null;
        return true;
    }

    internal static void Restore(PvpEventRotationController controller)
    {
        if (Current is { Deleted: false } other && other != controller)
        {
            _recoveryRequired = true;
            other.Pause("Multiple saved rotation controllers require staff recovery.");
            controller.Pause("Multiple saved rotation controllers require staff recovery.");
            return;
        }
        Current = controller;
        controller.BindIntegration(new Integration(controller.Extraction, controller.Town));
    }

    internal static void Removed(PvpEventRotationController controller)
    {
        if (Current == controller)
        {
            Current = null;
        }
    }

    private static void Shutdown() => Current?.Suspend();

    internal static bool TryEnableRotation(ExtractionEventController extraction, TownStruggleController town,
        TimeSpan interval, out PvpEventRotationController controller, out string error)
    {
        controller = null;
        if (_recoveryRequired || IsEnabled)
        {
            error = "Rotation is already enabled or duplicate saved ownership requires staff recovery.";
            return false;
        }
        var created = Current == null || Current.Deleted;
        var pending = created ? new PvpEventRotationController() : Current;
        if (!pending.TryEnable(extraction, town, new Integration(extraction, town), interval, out error))
        {
            if (created)
            {
                pending.Delete();
            }
            return false;
        }
        Current = controller = pending;
        return true;
    }

    private static void Command(CommandEventArgs args)
    {
        var action = args.Length == 0 ? "status" : args.GetString(0).ToLowerInvariant();
        if (args.Length > 2 || action != "enable" && args.Length > 1)
        {
            args.Mobile.SendMessage("Usage: [PvpRotation enable [waiting-minutes] | disable | status");
            return;
        }
        switch (action)
        {
            case "enable":
            {
                if (_recoveryRequired)
                {
                    args.Mobile.SendMessage("Duplicate saved rotation controllers require explicit staff recovery and a clean restart before enabling rotation.");
                    return;
                }
                var minutes = args.Length == 2 ? args.GetInt32(1) : 15;
                if (minutes is < 1 or > 1440)
                {
                    args.Mobile.SendMessage("Waiting minutes must be between 1 and 1440.");
                    return;
                }
                if (IsEnabled)
                {
                    args.Mobile.SendMessage("Rotation is already enabled. A paused rotation must be recovered explicitly before disabling or enabling it.");
                    return;
                }
                var extraction = ExtractionEventRuntime.Controller;
                var town = TownStruggleRuntime.Controller;
                if (!TryEnableRotation(extraction, town, TimeSpan.FromMinutes(minutes), out _, out var error))
                {
                    args.Mobile.SendMessage(error);
                    return;
                }
                args.Mobile.SendMessage($"PvP rotation enabled: Extraction, then Town Struggle. First slot starts in {minutes} minutes.");
                break;
            }
            case "disable":
            {
                if (Current != null && !Current.TryDisable(out var error))
                {
                    args.Mobile.SendMessage(error);
                    return;
                }
                args.Mobile.SendMessage("PvP rotation is disabled; Extraction's prior independent schedule is restored.");
                break;
            }
            case "status":
            {
                if (_recoveryRequired)
                {
                    args.Mobile.SendMessage("Duplicate saved rotation ownership has disabled event starts until staff recovery.");
                }
                if (Current is not { Deleted: false } controller)
                {
                    args.Mobile.SendMessage("PvP rotation is not configured.");
                    return;
                }
                args.Mobile.SendMessage($"Rotation enabled: {controller.Enabled}; active slot: {controller.ActiveEvent}; next: {controller.NextEvent}.");
                if (controller.Paused)
                {
                    args.Mobile.SendMessage($"Paused: {controller.FaultMessage}");
                }
                else if (controller.Enabled && controller.ActiveEvent == PvpRotatingEvent.None)
                {
                    args.Mobile.SendMessage($"Next event slot in {Math.Max(0, (int)(controller.NextDeadline - Core.Now).TotalSeconds)} seconds.");
                }
                break;
            }
            default:
            {
                args.Mobile.SendMessage("Usage: [PvpRotation enable [waiting-minutes] | disable | status");
                break;
            }
        }
    }

    private sealed class Integration(ExtractionEventController extraction, TownStruggleController town) : IPvpEventRotationIntegration
    {
        public PvpEventAvailability ExtractionAvailability => extraction switch
        {
            null or { Deleted: true } => PvpEventAvailability.Unavailable,
            { Phase: ExtractionEventPhase.Faulted } => PvpEventAvailability.Faulted,
            _ when ExtractionEventRuntime.Controller != extraction || !extraction.ReadyForScheduledStart => PvpEventAvailability.Unavailable,
            { Phase: ExtractionEventPhase.Waiting, Participants.Count: 0, QueuedPlayers.Count: 0 } => PvpEventAvailability.Idle,
            _ => PvpEventAvailability.Busy
        };

        public PvpEventAvailability TownAvailability => town switch
        {
            null or { Deleted: true } => PvpEventAvailability.Unavailable,
            { Phase: TownStrugglePhase.Faulted } => PvpEventAvailability.Faulted,
            _ when TownStruggleRuntime.Controller != town || town.Phase == TownStrugglePhase.Dormant && !town.ReadyForScheduledStart
                => PvpEventAvailability.Unavailable,
            { Phase: TownStrugglePhase.Dormant, Participants.Count: 0 } => PvpEventAvailability.Idle,
            _ => PvpEventAvailability.Busy
        };

        public string FailureReason => extraction?.Phase == ExtractionEventPhase.Faulted
            ? extraction.FaultMessage
            : town?.Phase == TownStrugglePhase.Faulted ? town.FaultMessage : "An installed event is missing, suspended, or unavailable.";

        public bool TryStartExtraction(out string error) => extraction.TryStart(out error);
        public bool TryStartTown(out string error) => town.TryOpenSignup(out error);
    }
}
