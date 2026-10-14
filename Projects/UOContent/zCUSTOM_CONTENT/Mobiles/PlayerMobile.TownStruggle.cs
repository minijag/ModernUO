using Server.Engines.TownStruggle;

namespace Server.Mobiles;

public partial class PlayerMobile
{
    private bool _observingTownStruggleLocation;

    public override void OnDisconnected()
    {
        base.OnDisconnected();
        ObserveTownStruggleLocation();
    }

    public override void OnConnected()
    {
        base.OnConnected();
        ObserveTownStruggleLocation();
    }

    internal void ObserveTownStruggleLocation()
    {
        var controller = TownStruggleRuntime.Controller;
        if (_observingTownStruggleLocation || controller is not { Deleted: false } ||
            controller.GetParticipant(this) == null || controller.IsMovingParticipant(this))
        {
            return;
        }

        _observingTownStruggleLocation = true;
        try
        {
            if (!controller.TryEnforceBoundary(this) && controller.Venue.Contains(this))
            {
                controller.TryReturnOutside(this, out _);
            }
        }
        finally
        {
            _observingTownStruggleLocation = false;
        }
    }

    public override void OnHeal(ref int amount, Mobile from)
    {
        if (!TownStruggleCombat.AllowsHealing(this, from))
        {
            amount = 0;
            return;
        }

        base.OnHeal(ref amount, from);
        TownStruggleCombat.RecordHealing(this, from, amount);
    }
}
