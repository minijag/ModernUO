using System;
using ModernUO.Serialization;
using Server.Items;
using Server.Network;
using Server.Systems.FeatureFlags;

namespace Server.Engines.ModularDungeon;

[SerializationGenerator(0)]
public partial class EventDungeonChest : BaseContainer, ITelekinesisable
{
    private TimerExecutionToken _openingTimer;
    private TimerExecutionToken _workingTimer;
    private Mobile _openingMobile;
    private long _openingDeadlineTick;
    private long _nextWorkingTick;

    [Constructible]
    public EventDungeonChest() : this(0xE41)
    {
    }

    [Constructible]
    public EventDungeonChest(int itemId) : base(itemId)
    {
        Movable = false;
    }

    public override string DefaultName => "an event dungeon chest";

    // An immovable event chest is a usable container, not decorative furniture.
    public override bool IsDecoContainer => false;

    public override bool VerifyMove(Mobile from) => false;

    internal void ResetForMatch(Map map, Point3D position, int itemId)
    {
        CancelOpening();
        Openers = null;
        ItemID = itemId;
        Movable = false;
        MoveToWorld(position, map);
    }

    public override bool CheckHold(Mobile from, Item item, bool message, bool checkItems, int plusItems, int plusWeight)
    {
        if (ExtractionDungeonReset.IsManagedChest(this))
        {
            if (message)
            {
                from?.SendMessage("Event chests are for taking loot, not storing your items.");
            }

            return false;
        }

        return base.CheckHold(from, item, message, checkItems, plusItems, plusWeight);
    }

    public override void OnDoubleClick(Mobile from) => BeginOpening(from);

    public override void Open(Mobile from) => BeginOpening(from);

    public override void DisplayTo(Mobile to) => BeginOpening(to);

    public override void OnDoubleClickSecureTrade(Mobile from) => BeginOpening(from);

    public override void OnSnoop(Mobile from) => BeginOpening(from);

    public void OnTelekinesis(Mobile from) => BeginOpening(from);

    public override bool CheckLift(Mobile from, Item item, ref LRReason reject)
    {
        if (item != this && !CanUseContents(from))
        {
            reject = LRReason.CannotLift;
            return false;
        }

        return base.CheckLift(from, item, ref reject);
    }

    public override bool CheckItemUse(Mobile from, Item item) =>
        (item == this || CanUseContents(from)) && base.CheckItemUse(from, item);

    public override bool TryDropItem(Mobile from, Item dropped, bool sendFullMessage) =>
        CanUseContents(from) && base.TryDropItem(from, dropped, sendFullMessage);

    public override bool OnDragDropInto(Mobile from, Item item, Point3D p) =>
        CanUseContents(from) && base.OnDragDropInto(from, item, p);

    private void BeginOpening(Mobile from)
    {
        if (!CanOpen(from))
        {
            return;
        }

        if (_openingTimer.Running)
        {
            from.SendMessage("This chest is already being opened.");
            return;
        }

        var delay = EventDungeonOpening.GetDelay(from, EventDungeonOpening.ChestBaseSeconds);
        _openingMobile = from;
        _openingDeadlineTick = Core.TickCount + (long)Math.Ceiling(delay.TotalMilliseconds);
        _nextWorkingTick = Core.TickCount + EventDungeonSounds.WorkingCadenceMilliseconds;
        from.SendMessage($"Opening this chest will take {delay.TotalSeconds:0.##} seconds");
        Effects.PlaySound(this, EventDungeonSounds.Working);
        Timer.StartTimer(delay, CompleteOpening, out _openingTimer);
        var interval = TimeSpan.FromMilliseconds(EventDungeonOpening.ValidationIntervalMilliseconds);
        Timer.StartTimer(interval, interval, CheckOpening, out _workingTimer);
    }

    private bool CanOpen(Mobile from) =>
        !Deleted && Parent == null && Map != null && Map != Map.Internal &&
        from is { Deleted: false, Alive: true, NetState: not null } &&
        from.Map == Map && from.InRange(GetWorldLocation(), 2) && from.InLOS(this) && IsAccessibleTo(from);

    private bool CanUseContents(Mobile from) => CanOpen(from) && Openers?.Contains(from) == true;

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
            var canDisplay = from.AccessLevel >= FeatureFlagSettings.RequiredAccessLevel ||
                !FeatureFlagManager.IsItemUseBlocked(GetType(), out _);
            base.DisplayTo(from);

            if (canDisplay && from.NetState != null && Openers?.Contains(from) == true)
            {
                Effects.PlaySound(this, EventDungeonSounds.Unlocked);
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
    private void AfterDeserialization()
    {
        CancelOpening();
        Movable = false;
    }

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
