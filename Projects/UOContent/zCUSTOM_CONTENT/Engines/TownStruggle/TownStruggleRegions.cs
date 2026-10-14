using Server.Mobiles;
using Server.Regions;
using Server.Spells;
using Server.Spells.Eighth;
using Server.Spells.Fifth;
using Server.Spells.Mysticism;
using Server.Spells.Necromancy;
using Server.Spells.Ninjitsu;
using Server.Spells.Seventh;
using Server.Spells.Spellweaving;

namespace Server.Engines.TownStruggle;

public sealed class TownStruggleRegion : GuardedRegion
{
    private TownStruggleController _controller;

    public TownStruggleRegion(TownStruggleController controller)
        : base("Town Struggle", controller.Venue.Map, 180, controller.Venue.Area)
    {
        _controller = controller;
        ExcludeFromParentSpawns = true;
    }

    public override bool MountsAllowed => true;
    public override bool AllowHousing(Mobile from, Point3D point) => false;
    public override bool CanUseStuckMenu(Mobile mobile) => !TownStruggleRuntime.IsEnrolled(mobile);

    public override bool AllowHarmful(Mobile from, Mobile target) =>
        TownStruggleCombat.TryAllowHarmful(from, target, out var allowed) ? allowed : base.AllowHarmful(from, target);

    public override bool AllowBeneficial(Mobile from, Mobile target) =>
        TownStruggleCombat.TryAllowBeneficial(from, target, out var allowed) ? allowed : base.AllowBeneficial(from, target);

    public override bool OnCombatantChange(Mobile mobile, Mobile old, Mobile next) =>
        next == null || (TownStruggleCombat.TryAllowHarmful(mobile, next, out var allowed)
            ? allowed
            : base.OnCombatantChange(mobile, old, next));

    public override bool OnDamage(Mobile mobile, ref int damage) =>
        !TownStruggleRuntime.TryGetParticipant(mobile, out var participant) ||
        TownStruggleCombat.CanAct(_controller, participant) && !TownStruggleCombat.IsInBase(_controller.Venue, mobile.Location);

    public override bool OnBeforeDeath(Mobile mobile) =>
        _controller?.TryDefeat(mobile) != true && base.OnBeforeDeath(mobile);

    public override bool OnMoveInto(Mobile mobile, Direction direction, Point3D location, Point3D previous)
    {
        if (mobile is BaseCreature creature && TownStruggleRuntime.IsEnrolled(creature.GetMaster()))
        {
            return false;
        }

        if (!_controller.CanReenter(mobile))
        {
            return false;
        }

        var participant = _controller.GetParticipant(mobile);
        if (participant == null)
        {
            return base.OnMoveInto(mobile, direction, location, previous);
        }

        return TownStruggleCombat.AllowsMovement(_controller, participant, location);
    }

    public override bool OnBeginSpellCast(Mobile mobile, ISpell spell)
    {
        if (!TownStruggleRuntime.TryGetParticipant(mobile, out var participant))
        {
            return FindUnderlyingGuards(mobile)?.OnBeginSpellCast(mobile, spell) ?? true;
        }

        if (!TownStruggleCombat.CanAct(_controller, participant) || IsRestrictedSpell(spell))
        {
            mobile.SendMessage("That spell cannot be used during Town Struggle.");
            return false;
        }

        return true;
    }

    internal static bool IsRestrictedSpell(ISpell spell) =>
        spell is IncognitoSpell or PolymorphSpell or SummonCreatureSpell or BladeSpiritsSpell or
            AirElementalSpell or EarthElementalSpell or FireElementalSpell or WaterElementalSpell or
            EnergyVortexSpell or SummonDaemonSpell or AnimateDeadSpell or SummonFamiliarSpell or
            VengefulSpiritSpell or SummonFeySpell or SummonFiendSpell or NatureFurySpell or
            AnimatedWeaponSpell or MirrorImage;

    public override bool CheckTravel(Mobile mobile, Point3D location, TravelCheckType type, out TextDefinition message)
    {
        message = null;
        if (_controller.Venue.Area.Contains(location) && !_controller.CanReenter(mobile))
        {
            return false;
        }
        if (!TownStruggleRuntime.TryGetParticipant(mobile, out var participant))
        {
            return base.CheckTravel(mobile, location, type, out message);
        }

        return TownStruggleCombat.CanAct(_controller, participant) &&
               type is TravelCheckType.TeleportFrom or TravelCheckType.TeleportTo &&
               _controller.Venue.Area.Contains(location) &&
               !TownStruggleCombat.IsInEnemyBase(_controller.Venue, participant.Team, location);
    }

    public void Release()
    {
        Unregister();
        _controller = null;
    }

    public override void OnEnter(Mobile mobile)
    {
        if (mobile is PlayerMobile player)
        {
            player.ObserveTownStruggleLocation();
            if (mobile.Map != Map || !Contains(mobile.Location))
            {
                return;
            }
        }
        if (!TownStruggleRuntime.IsGuardExempt(mobile))
        {
            FindUnderlyingGuards(mobile)?.OnEnter(mobile);
        }
    }

    public override void OnAggressed(Mobile aggressor, Mobile aggressed, bool criminal)
    {
        if (!TownStruggleRuntime.IsEnrolled(aggressor))
        {
            FindUnderlyingGuards(aggressor)?.OnAggressed(aggressor, aggressed, criminal);
        }
    }

    public override void OnGotBeneficialAction(Mobile helper, Mobile helped)
    {
        if (!TownStruggleRuntime.IsEnrolled(helper))
        {
            FindUnderlyingGuards(helper)?.OnGotBeneficialAction(helper, helped);
        }
    }

    public override void OnCriminalAction(Mobile mobile, bool message)
    {
        if (TownStruggleRuntime.IsEnrolled(mobile))
        {
            return;
        }

        if (FindUnderlyingGuards(mobile) is { } guards)
        {
            guards.OnCriminalAction(mobile, message);
        }
        else if (message)
        {
            mobile.SendLocalizedMessage(1005040);
        }
    }

    public override void OnSpeech(SpeechEventArgs args) => FindUnderlyingGuards(args.Mobile)?.OnSpeech(args);

    public override void MakeGuard(Mobile focus)
    {
        if (!TownStruggleRuntime.IsGuardExempt(focus))
        {
            FindUnderlyingGuards(focus)?.MakeGuard(focus);
        }
    }

    public override bool CheckVendorAccess(BaseVendor vendor, Mobile from) =>
        !TownStruggleRuntime.IsEnrolled(from) && (FindUnderlyingGuards(from)?.CheckVendorAccess(vendor, from) ?? true);

    private GuardedRegion FindUnderlyingGuards(Mobile mobile)
    {
        if (mobile?.Map == null || mobile.Map == Map.Internal)
        {
            return null;
        }

        // Preserve the town's original guard footprint rather than guard the event's entire rectangle.
        foreach (var candidate in mobile.Map.GetSector(mobile.Location).Regions)
        {
            if (candidate == this || candidate is TownStruggleRegion || !candidate.Contains(mobile.Location))
            {
                continue;
            }

            if (candidate.GetRegion<GuardedRegion>() is { } guards)
            {
                return guards;
            }
        }

        return null;
    }
}
