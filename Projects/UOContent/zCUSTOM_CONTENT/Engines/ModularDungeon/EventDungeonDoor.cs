using System;
using ModernUO.Serialization;
using Server.Items;
using Server.Mobiles;

namespace Server.Engines.ModularDungeon;

[SerializationGenerator(0)]
public partial class EventDungeonDoor : MetalDoor
{
    private TimerExecutionToken _openingTimer;
    private TimerExecutionToken _workingTimer;
    private Mobile _openingMobile;
    private long _openingDeadlineTick;
    private long _nextWorkingTick;

    [Constructible]
    public EventDungeonDoor() : this(DoorFacing.WestCW)
    {
    }

    [Constructible]
    public EventDungeonDoor(DoorFacing facing) : base(facing)
    {
        Locked = true;
    }

    public override bool AutoClose => false;

    public override bool UseLocks() => false;

    public override void OnDoubleClick(Mobile from) => Use(from);

    public override void OnOpenDoorMacro(Mobile from)
    {
        if (!Open)
        {
            Use(from);
        }
    }

    internal void ResetForMatch(Map map, ModularDungeonDeployment.DoorDefinition definition)
    {
        CancelOpening();
        Open = false;
        ClosedId = definition.ClosedId;
        OpenedId = definition.OpenedId;
        Offset = BaseDoor.GetOffset(Enum.Parse<DoorFacing>(definition.Facing));
        ItemID = definition.ClosedId;
        Locked = true;
        KeyValue = 0;
        Movable = false;
        MoveToWorld(definition.Closed.Point, map);
    }

    public override void Use(Mobile from)
    {
        if (_openingTimer.Running || (from is PlayerMobile player && !player.TryDoorUseRequest()) || !CanUse(from))
        {
            return;
        }

        if (Open)
        {
            CancelOpening();
            base.Use(from);
            return;
        }

        if (!ExtractionEventRuntime.CanOpenEventDoor(this, out var error))
        {
            if (from is not PlayerMobile rejectedPlayer || rejectedPlayer.TryDoorErrorMessage())
            {
                from.SendMessage(error);
            }
            return;
        }

        var delay = EventDungeonOpening.GetDelay(from, EventDungeonOpening.DoorBaseSeconds);
        _openingMobile = from;
        _openingDeadlineTick = Core.TickCount + (long)Math.Ceiling(delay.TotalMilliseconds);
        _nextWorkingTick = Core.TickCount + EventDungeonSounds.WorkingCadenceMilliseconds;
        from.SendMessage($"Opening this door will take {delay.TotalSeconds:0.##} seconds");
        Effects.PlaySound(this, EventDungeonSounds.Working);
        Timer.StartTimer(delay, CompleteOpening, out _openingTimer);
        var interval = TimeSpan.FromMilliseconds(EventDungeonOpening.ValidationIntervalMilliseconds);
        Timer.StartTimer(interval, interval, CheckOpening, out _workingTimer);
    }

    private bool CanUse(Mobile from) =>
        !Deleted && Parent == null && Map != null && Map != Map.Internal &&
        from is { Deleted: false, Alive: true, NetState: not null } &&
        from.Map == Map && from.InRange(GetWorldLocation(), 2) && from.InLOS(this);

    private bool CanOpen(Mobile from) =>
        !Open && CanUse(from) && ExtractionEventRuntime.CanOpenEventDoor(this, out _);

    private void CheckOpening()
    {
        if (!CanOpen(_openingMobile))
        {
            CancelOpening();
            return;
        }

        if (EventDungeonOpening.CanReplayWorkingSound(_nextWorkingTick, _openingDeadlineTick))
        {
            Effects.PlaySound(this, EventDungeonSounds.Working);
            _nextWorkingTick = Core.TickCount + EventDungeonSounds.WorkingCadenceMilliseconds;
        }
    }

    private void CompleteOpening()
    {
        var from = _openingMobile;
        CancelOpening();

        if (CanOpen(from))
        {
            var closedLocation = GetWorldLocation();
            var map = Map;
            base.Use(from);

            if (Open)
            {
                Effects.PlaySound(closedLocation, map, EventDungeonSounds.Unlocked);
            }
        }
    }

    private void CancelOpening()
    {
        _openingTimer.Cancel();
        _workingTimer.Cancel();
        _openingMobile = null;
    }

    [AfterDeserialization]
    private void AfterDeserialization() => CancelOpening();

    public override void OnLocationChange(Point3D oldLocation)
    {
        CancelOpening();
        base.OnLocationChange(oldLocation);
    }

    public override void OnMapChange()
    {
        CancelOpening();
        base.OnMapChange();
    }

    public override void OnAdded(IEntity parent)
    {
        CancelOpening();
        base.OnAdded(parent);
    }

    public override void OnDelete()
    {
        CancelOpening();
        base.OnDelete();
    }

    public override void OnAfterDelete()
    {
        CancelOpening();
        base.OnAfterDelete();
    }
}
