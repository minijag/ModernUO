using System;
using ModernUO.Serialization;
using Server.Items;

namespace Server.Engines.ModularDungeon;

[SerializationGenerator(0)]
public partial class ExtractionShrinePortal : Item
{
    [CanBeNull]
    [SerializableField(0, setter: "private")]
    private ExtractionEventShrine _shrine;

    [SerializableField(1, setter: "private")]
    private int _roundNumber;

    private TimerExecutionToken _validationTimer;
    private bool _armed;

    [Constructible(AccessLevel.Administrator)]
    public ExtractionShrinePortal() : base(MoongateAppearance.Blue)
    {
        Hue = ExtractionEventShrine.RedPortalHue;
        Light = LightType.Circle300;
        Movable = false;
    }

    internal ExtractionShrinePortal(ExtractionEventShrine shrine, int roundNumber)
        : base(ValidateConstruction(shrine))
    {
        _shrine = shrine;
        _roundNumber = roundNumber;
        Hue = ExtractionEventShrine.RedPortalHue;
        Light = LightType.Circle300;
        Movable = false;
    }

    public override string DefaultName => "a shared red extraction moongate";

    private static int ValidateConstruction(ExtractionEventShrine shrine)
    {
        ArgumentNullException.ThrowIfNull(shrine);
        if (shrine.Deleted)
        {
            throw new ArgumentException("The shrine has been deleted.", nameof(shrine));
        }
        return MoongateAppearance.Blue;
    }

    internal bool ExactAppearance => !Deleted && Parent == null && !Movable && Visible &&
        MoongateAppearance.IsBlueBase(ItemID) && Hue == ExtractionEventShrine.RedPortalHue && Light == LightType.Circle300;

    internal void Resume()
    {
        _validationTimer.Cancel();
        if (Deleted)
        {
            return;
        }
        _armed = true;
        var interval = TimeSpan.FromMilliseconds(ExtractionEventShrine.ValidationMilliseconds);
        Timer.StartTimer(interval, interval, Advance, out _validationTimer);
    }

    internal void Suspend()
    {
        _validationTimer.Cancel();
        _armed = false;
    }

    internal void Advance()
    {
        if (ExtractionEventRuntime.IsValidShrinePortal(this))
        {
            return;
        }

        // Deferred world restore can publish the gate before its controller is bound.
        if (!ExtractionEventRuntime.HasBoundController(_shrine?.Controller))
        {
            Suspend();
            return;
        }
        Delete();
    }

    public override void OnDoubleClick(Mobile from) => TryUse(from);

    public override bool OnMoveOver(Mobile from)
    {
        TryUse(from);
        return true;
    }

    private void TryUse(Mobile player)
    {
        if (!ExtractionEventRuntime.CanUseShrinePortal(this, player, out var error))
        {
            SendFailure(player, error);
            return;
        }

        var shrine = _shrine;
        var roundNumber = _roundNumber;
        MoongateTravelConfirmation.DisplayTo(player, this,
            from => _shrine == shrine && _roundNumber == roundNumber &&
                    ExtractionEventRuntime.CanUseShrinePortal(this, from, out _),
            UseConfirmedPortal);
    }

    private void UseConfirmedPortal(Mobile player)
    {
        if (!ExtractionEventRuntime.TryUseShrinePortal(this, player, out var error))
        {
            SendFailure(player, error);
        }
    }

    private static void SendFailure(Mobile player, string error)
    {
        if (player is { Deleted: false, NetState: not null } && !string.IsNullOrEmpty(error))
        {
            player.SendMessage(error);
        }
    }

    [AfterDeserialization(false)]
    private void AfterDeserialization()
    {
        _shrine?.RestorePortal();
    }

    public override void OnLocationChange(Point3D oldLocation)
    {
        base.OnLocationChange(oldLocation);
        if (_armed && !ExtractionEventRuntime.IsValidShrinePortal(this))
        {
            Delete();
        }
    }

    public override void OnMapChange()
    {
        base.OnMapChange();
        if (_armed && !ExtractionEventRuntime.IsValidShrinePortal(this))
        {
            Delete();
        }
    }

    public override void OnAdded(IEntity parent)
    {
        base.OnAdded(parent);
        if (parent != null)
        {
            Delete();
        }
    }

    public override void OnDelete()
    {
        Suspend();
        base.OnDelete();
    }

    public override void OnAfterDelete()
    {
        Suspend();
        var shrine = _shrine;
        Shrine = null;
        shrine?.NotifyPortalDeleted(this);
        base.OnAfterDelete();
    }
}
