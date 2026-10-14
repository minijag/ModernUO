using System;
using ModernUO.Serialization;
using Server.Factions;
using Server.Gumps;
using Server.Misc;
using Server.Mobiles;
using Server.Network;
using Server.Regions;

namespace Server.Items;

[DispellableField]
[SerializationGenerator(0, false)]
public partial class Moongate : Item
{
    [SerializableField(0)]
    [SerializedCommandProperty(AccessLevel.GameMaster)]
    private Point3D _target;

    [SerializableField(1)]
    [SerializedCommandProperty(AccessLevel.GameMaster)]
    private Map _targetMap;

    [SerializableField(2)]
    [SerializedCommandProperty(AccessLevel.GameMaster)]
    private bool _dispellable;

    [Constructible]
    public Moongate(bool dispellable = true) : this(Point3D.Zero, null, dispellable)
    {
    }

    [Constructible]
    public Moongate(Point3D target, Map targetMap = null, bool dispellable = true) : base(MoongateAppearance.Blue)
    {
        Movable = false;
        Light = LightType.Circle300;

        Target = target;
        TargetMap = targetMap;
        Dispellable = dispellable;
    }

    public override int LabelNumber => MoongateAppearance.IsReplacement(ItemID) ? MoongateAppearance.LegacyLabelNumber(ItemID) : base.LabelNumber;

    public virtual bool ShowFeluccaWarning => false;

    public override void OnDoubleClick(Mobile from)
    {
        if (!from.Player)
        {
            return;
        }

        if (from.InRange(GetWorldLocation(), 1))
        {
            CheckGate(from, 1);
        }
        else
        {
            from.SendLocalizedMessage(500446); // That is too far away.
        }
    }

    public override bool OnMoveOver(Mobile m)
    {
        if (m.Player)
        {
            CheckGate(m, 0);
        }

        return true;
    }

    public virtual void CheckGate(Mobile m, int range)
    {
        if (m.Hidden && m.AccessLevel == AccessLevel.Player && Core.ML)
        {
            m.RevealingAction();
        }

        new DelayTimer(m, this, range).Start();
    }

    public virtual void OnGateUsed(Mobile m)
    {
    }

    public virtual void UseGate(Mobile m)
    {
        var flags = m.NetState?.Flags ?? ClientFlags.None;

        if (Sigil.ExistsOn(m))
        {
            m.SendLocalizedMessage(1061632); // You can't do that while carrying the sigil.
        }
        else if (TargetMap == Map.Felucca && m is PlayerMobile mobile && mobile.Young)
        {
            mobile.SendLocalizedMessage(1049543); // You decide against traveling to Felucca while you are still young.
        }
        else if (m.Murderer && TargetMap != Map.Felucca ||
                 TargetMap == Map.Tokuno && (flags & ClientFlags.Tokuno) == 0 ||
                 TargetMap == Map.Malas && (flags & ClientFlags.Malas) == 0 ||
                 TargetMap == Map.Ilshenar && (flags & ClientFlags.Ilshenar) == 0 ||
                 TargetMap == Map.TerMur && (flags & ClientFlags.TerMur) == 0)
        {
            m.SendLocalizedMessage(1019004); // You are not allowed to travel there.
        }
        else if (m.Spell != null)
        {
            m.SendLocalizedMessage(1049616); // You are too busy to do that at the moment.
        }
        else if (TargetMap != null && TargetMap != Map.Internal)
        {
            BaseCreature.TeleportPets(m, Target, TargetMap);

            m.MoveToWorld(Target, TargetMap);

            if (m.AccessLevel == AccessLevel.Player || !m.Hidden)
            {
                m.PlaySound(0x1FE);
            }

            OnGateUsed(m);
        }
        else
        {
            m.SendMessage("This moongate does not seem to go anywhere.");
        }
    }

    public virtual bool ValidateUse(Mobile from, bool message)
    {
        if (from.Deleted || Deleted)
        {
            return false;
        }

        if (!MoongateTravelConfirmation.IsInRange(from, this))
        {
            if (message)
            {
                from.SendLocalizedMessage(500446); // That is too far away.
            }

            return false;
        }

        return true;
    }

    public virtual void BeginConfirmation(Mobile from)
    {
        if (IsInTown(from.Location, from.Map) && !IsInTown(Target, TargetMap) ||
            from.Map != Map.Felucca && TargetMap == Map.Felucca && ShowFeluccaWarning)
        {
            if (from.AccessLevel == AccessLevel.Player || !from.Hidden)
            {
                from.SendSound(0x20E, from);
            }

            MoongateConfirmGump.DisplayTo(from, this);
        }
        else
        {
            MoongateTravelConfirmation.DisplayTo(from, this, CaptureConfirmationValidation(), EndConfirmation);
        }
    }

    protected virtual Func<Mobile, bool> CaptureConfirmationValidation()
    {
        var target = Target;
        var map = TargetMap;
        return from => Target == target && TargetMap == map && ValidateUse(from, true);
    }

    internal Func<Mobile, bool> GetConfirmationValidation() => CaptureConfirmationValidation();

    public virtual void EndConfirmation(Mobile from)
    {
        if (!ValidateUse(from, true))
        {
            return;
        }

        UseGate(from);
    }

    public virtual void DelayCallback(Mobile from, int range)
    {
        if (!ValidateUse(from, false) || !from.InRange(this, range))
        {
            return;
        }

        if (TargetMap != null)
        {
            BeginConfirmation(from);
        }
        else
        {
            from.SendMessage("This moongate does not seem to go anywhere.");
        }
    }

    public static bool IsInTown(Point3D p, Map map) =>
        map != null && Region.Find(p, map).GetRegion<GuardedRegion>()?.IsDisabled() == false;

    private class DelayTimer : Timer
    {
        private Mobile _from;
        private Moongate _gate;
        private int _range;

        public DelayTimer(Mobile from, Moongate gate, int range) : base(TimeSpan.FromSeconds(1.0))
        {
            _from = from;
            _gate = gate;
            _range = range;
        }

        protected override void OnTick()
        {
            _gate.DelayCallback(_from, _range);
        }
    }
}

[SerializationGenerator(1, false)]
public partial class ConfirmationMoongate : Moongate
{
    [SerializableField(0)]
    [SerializedCommandProperty(AccessLevel.GameMaster)]
    private TextDefinition _message;

    [SerializableField(1)]
    [SerializedCommandProperty(AccessLevel.GameMaster)]
    private int _gumpWidth;

    [SerializableField(2)]
    [SerializedCommandProperty(AccessLevel.GameMaster)]
    private int _gumpHeight;

    [SerializableField(3)]
    [SerializedCommandProperty(AccessLevel.GameMaster)]
    private int _titleColor;

    [SerializableField(4)]
    [SerializedCommandProperty(AccessLevel.GameMaster)]
    private int _messageColor;

    [SerializableField(5)]
    [SerializedCommandProperty(AccessLevel.GameMaster)]
    private int _titleNumber;

    [Constructible]
    public ConfirmationMoongate() : this(Point3D.Zero)
    {
    }

    [Constructible]
    public ConfirmationMoongate(Point3D target, Map targetMap = null) : base(target, targetMap)
    {
    }

    public virtual void Warning_Callback(Mobile from, bool okay)
    {
        if (okay)
        {
            EndConfirmation(from);
        }
    }

    public override void BeginConfirmation(Mobile from)
    {
        if (GumpWidth > 0 && GumpHeight > 0 && TitleNumber > 0 && Message?.IsEmpty == false)
        {
            var request = MoongateTravelConfirmation.CreateRequest(from, this, CaptureConfirmationValidation(), m => Warning_Callback(m, true));
            if (request == null)
            {
                return;
            }
            from.SendGump(
                new WarningGump(
                    TitleNumber,
                    TitleColor,
                    Message,
                    MessageColor,
                    GumpWidth,
                    GumpHeight,
                    okay => request.Respond(from, okay)
                ),
                true
            );
        }
        else
        {
            base.BeginConfirmation(from);
        }
    }

    private void Deserialize(IGenericReader reader, int version)
    {
        base.Deserialize(reader);

        GumpWidth = reader.ReadEncodedInt();
        GumpHeight = reader.ReadEncodedInt();

        TitleColor = reader.ReadEncodedInt();
        MessageColor = reader.ReadEncodedInt();

        TitleNumber = reader.ReadEncodedInt();

        var number = reader.ReadEncodedInt();
        var message = reader.ReadString();
        Message = number > 0 ? number : message;
    }
}

public class MoongateConfirmGump : DynamicGump
{
    private readonly Mobile _from;
    private readonly Moongate _gate;
    private readonly MoongateTravelConfirmation.Request _request;

    public override bool Singleton => true;

    private MoongateConfirmGump(Mobile from, Moongate gate, MoongateTravelConfirmation.Request request) : base(Core.AOS ? 110 : 50, Core.AOS ? 100 : 50)
    {
        _from = from;
        _gate = gate;
        _request = request;
    }

    public static void DisplayTo(Mobile from, Moongate gate)
    {
        if (from?.NetState == null || gate == null || gate.Deleted)
        {
            return;
        }

        var request = MoongateTravelConfirmation.CreateRequest(from, gate, gate.GetConfirmationValidation(), gate.EndConfirmation);
        if (request != null)
        {
            from.SendGump(new MoongateConfirmGump(from, gate, request));
        }
    }

    protected override void BuildLayout(ref DynamicGumpBuilder builder)
    {
        builder.AddPage();

        if (Core.AOS)
        {
            builder.SetNoClose();

            CustomGumpStyle.AddContentPanel(ref builder, 0, 0, 420, 280, "MOONGATE");

            builder.AddHtmlLocalized(10, 10, 400, 20, 1062051, CustomGumpStyle.LocalizedTextColor); // Gate Warning

            if (_from.Map != Map.Felucca && _gate.TargetMap == Map.Felucca && _gate.ShowFeluccaWarning)
            {
                // This Gate goes to Felucca... Continue to enter the gate, Cancel to stay here
                builder.AddHtmlLocalized(10, 40, 400, 200, 1062050, CustomGumpStyle.LocalizedTextColor, false, true);
            }
            else
            {
                // Dost thou wish to step into the moongate? Continue to enter the gate, Cancel to stay here
                builder.AddHtmlLocalized(10, 40, 400, 200, 1062049, CustomGumpStyle.LocalizedTextColor, false, true);
            }

            builder.AddButton(10, 250, 4005, 4007, 1);
            builder.AddHtmlLocalized(40, 250, 170, 20, 1011036, CustomGumpStyle.LocalizedTextColor); // OKAY

            builder.AddButton(210, 250, 4005, 4007, 0);
            builder.AddHtmlLocalized(240, 250, 170, 20, 1011012, CustomGumpStyle.LocalizedTextColor); // CANCEL
        }
        else
        {
            CustomGumpStyle.AddContentPanel(ref builder, 0, 0, 420, 400, "MOONGATE");

            // Dost thou wish to step into the moongate? Continue to enter the gate, Cancel to stay here
            builder.AddHtmlLocalized(20, 40, 380, 60, 1062049, CustomGumpStyle.LocalizedTextColor);

            builder.AddHtmlLocalized(55, 110, 290, 20, 1011012, CustomGumpStyle.LocalizedTextColor); // CANCEL
            builder.AddButton(20, 110, 4005, 4007, 0);

            builder.AddHtmlLocalized(55, 140, 290, 40, 1011011, CustomGumpStyle.LocalizedTextColor); // CONTINUE
            builder.AddButton(20, 140, 4005, 4007, 1);
        }
    }

    public override void OnResponse(NetState state, in RelayInfo info)
    {
        if (state?.Mobile?.NetState == state)
        {
            _request.Respond(state?.Mobile, info.ButtonID == 1);
        }
    }
}
