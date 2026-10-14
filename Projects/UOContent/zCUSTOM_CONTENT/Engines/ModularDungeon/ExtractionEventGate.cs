using System;
using ModernUO.Serialization;
using Server.Items;

namespace Server.Engines.ModularDungeon;

public enum ExtractionEventGateKind
{
    Entry,
    LobbyReturn,
    Extraction
}

[SerializationGenerator(0)]
public partial class ExtractionEventGate : Item
{
    public const int GreenHue = 0x3F;
    public const int BlueHue = 0x482;

    [CanBeNull]
    [SerializableField(0, setter: "private")]
    private ExtractionEventController _controller;

    [SerializableField(1, setter: "private", fieldChanged: nameof(OnKindChanged))]
    private ExtractionEventGateKind _kind;

    [Constructible]
    public ExtractionEventGate() : base(MoongateAppearance.Blue)
    {
        ApplyAppearance();
    }

    public ExtractionEventGate(ExtractionEventController controller, ExtractionEventGateKind kind)
        : base(ValidateConstruction(controller, kind))
    {
        Controller = controller;
        Kind = kind;
        ApplyAppearance();
    }

    public override string DefaultName => _kind switch
    {
        ExtractionEventGateKind.Entry => "an extraction event moongate",
        ExtractionEventGateKind.LobbyReturn => "a return moongate to Galven",
        ExtractionEventGateKind.Extraction => "an extraction rune",
        _ => "an inactive event gate"
    };

    public bool CanUseGate(Mobile player, out string error)
    {
        if (Deleted || Parent != null || Map == null || Map == Map.Internal ||
            _controller is not { Deleted: false } || !Enum.IsDefined(_kind))
        {
            error = "This event gate is inactive.";
            return false;
        }

        if (player is not { Deleted: false, Player: true, NetState: not null } ||
            !player.Alive && _kind != ExtractionEventGateKind.LobbyReturn)
        {
            error = "Only connected players eligible for this route can use the event gate.";
            return false;
        }

        if (player.Map != Map || !player.InRange(GetWorldLocation(), 1) || Math.Abs(player.Z - Z) >= 16)
        {
            error = "You must stand next to the event gate.";
            return false;
        }

        if (!player.InLOS(this))
        {
            error = "You cannot see the event gate.";
            return false;
        }

        if (player.Spell != null)
        {
            error = "Finish casting before using the event gate.";
            return false;
        }

        error = null;
        return true;
    }

    public override void OnDoubleClick(Mobile from) => TryUseGate(from);

    // Rune travel is immediate; moongate travel waits for a separate confirmation response.
    public override bool OnMoveOver(Mobile from) => !TryUseGate(from);

    private bool TryUseGate(Mobile player)
    {
        if (_kind != ExtractionEventGateKind.Extraction && CanUseGate(player, out _))
        {
            var controller = _controller;
            var kind = _kind;
            MoongateTravelConfirmation.DisplayTo(player, this,
                from => _controller == controller && _kind == kind && CanUseGate(from, out _),
                from => UseConfirmedGate(from));
            return false;
        }

        return UseConfirmedGate(player);
    }

    private bool UseConfirmedGate(Mobile player)
    {
        if (CanUseGate(player, out var error) && ExtractionEventRuntime.TryUseGate(this, player, out error))
        {
            return true;
        }

        if (player is { Deleted: false, NetState: not null } && !string.IsNullOrEmpty(error))
        {
            player.SendMessage(error);
        }

        return false;
    }

    private void OnKindChanged(ExtractionEventGateKind oldValue, ExtractionEventGateKind newValue) => ApplyAppearance();

    private static int ValidateConstruction(ExtractionEventController controller, ExtractionEventGateKind kind)
    {
        ArgumentNullException.ThrowIfNull(controller);
        if (controller.Deleted)
        {
            throw new ArgumentException("The controller has been deleted.", nameof(controller));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        return kind == ExtractionEventGateKind.Extraction ? 0x1822 : MoongateAppearance.Blue;
    }

    private void ApplyAppearance(bool preserveLegacy = false)
    {
        ItemID = _kind == ExtractionEventGateKind.Extraction ? 0x1822 :
            preserveLegacy && ItemID == MoongateAppearance.LegacyBlue ? MoongateAppearance.LegacyBlue : MoongateAppearance.Blue;
        Hue = _kind == ExtractionEventGateKind.LobbyReturn ? BlueHue : GreenHue;
        Movable = false;
        Visible = true;
        Light = LightType.Circle300;
    }

    [AfterDeserialization]
    private void AfterDeserialization() => ApplyAppearance(true);

    public override void OnAfterDelete()
    {
        Controller = null;
        base.OnAfterDelete();
    }
}
