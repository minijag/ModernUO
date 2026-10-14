using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Server.Mobiles;

namespace Server.Engines.TownStruggle;

public static class TownStruggleCombat
{
    private sealed record Membership(Serial Controller, bool Active, Map Map, Rectangle2D Area,
        FrozenDictionary<Serial, TownStruggleTeam> Teams);

    // Notoriety can run on a packet worker; it must not enumerate the live participant roster.
    private static volatile Membership _membership;

    public static void Publish(TownStruggleController controller)
    {
        if (controller == null || controller.Deleted || controller.Venue == null)
        {
            Clear(controller);
            return;
        }

        Publish(controller.Serial, controller.Phase, controller.Venue, controller.Participants);
    }

    internal static void Publish(Serial controller, TownStrugglePhase phase, TownStruggleVenue venue,
        IReadOnlyList<TownStruggleParticipant> participants)
    {
        var teams = new Dictionary<Serial, TownStruggleTeam>(participants.Count);
        foreach (var participant in participants)
        {
            if (participant.Player is { Deleted: false } player && !participant.Disqualified && !participant.Returned)
            {
                teams[player.Serial] = participant.Team;
            }
        }

        _membership = new Membership(controller, phase == TownStrugglePhase.Active,
            venue.Map, venue.Area, teams.ToFrozenDictionary());
    }

    public static void Clear(TownStruggleController controller)
    {
        if (controller == null || _membership?.Controller == controller.Serial)
        {
            _membership = null;
        }
    }

    public static bool TryGetNotoriety(Mobile source, Mobile target, out int notoriety)
    {
        notoriety = 0;
        var membership = _membership;
        if (membership is not { Active: true } ||
            source is not { Deleted: false, Player: true, AccessLevel: AccessLevel.Player } ||
            target is not { Deleted: false, Player: true, AccessLevel: AccessLevel.Player } ||
            source.Map != membership.Map || target.Map != membership.Map ||
            !membership.Area.Contains(source.Location) || !membership.Area.Contains(target.Location) ||
            !membership.Teams.TryGetValue(source.Serial, out var sourceTeam) ||
            !membership.Teams.TryGetValue(target.Serial, out var targetTeam) ||
            sourceTeam == TownStruggleTeam.Neutral || targetTeam == TownStruggleTeam.Neutral)
        {
            return false;
        }

        notoriety = sourceTeam == targetTeam ? Notoriety.Ally : Notoriety.Enemy;
        return true;
    }

    public static bool TryAllowHarmful(Mobile from, Mobile target, out bool allowed) =>
        TryAllowInteraction(from, target, false, out allowed);

    public static bool TryAllowBeneficial(Mobile from, Mobile target, out bool allowed) =>
        TryAllowInteraction(from, target, true, out allowed);

    public static bool AllowsDamage(Mobile target, Mobile from) =>
        !TryAllowHarmful(from, target, out var allowed) || allowed;

    public static bool AllowsHealing(Mobile target, Mobile from) =>
        !TryAllowBeneficial(from, target, out var allowed) || allowed;

    private static bool TryAllowInteraction(Mobile from, Mobile target, bool beneficial, out bool allowed)
    {
        var sourceEnrolled = IsEnrolledOwner(from);
        var targetEnrolled = IsEnrolledOwner(target);
        allowed = false;
        if (!sourceEnrolled && !targetEnrolled)
        {
            return false;
        }

        var controller = TownStruggleRuntime.Controller;
        if (controller is not { Deleted: false, Phase: TownStrugglePhase.Active } ||
            from is not PlayerMobile { Deleted: false, Alive: true } ||
            target is not PlayerMobile { Deleted: false, Alive: true } ||
            !TownStruggleRuntime.TryGetParticipant(from, out var source) ||
            !TownStruggleRuntime.TryGetParticipant(target, out var destination) ||
            !CanAct(controller, source) || !CanAct(controller, destination))
        {
            return true;
        }

        allowed = beneficial
            ? source.Team == destination.Team
            : from != target && source.Team != destination.Team &&
              !IsInBase(controller.Venue, from.Location) && !IsInBase(controller.Venue, target.Location);
        return true;
    }

    private static bool IsEnrolledOwner(Mobile mobile)
    {
        if (mobile == null)
        {
            return false;
        }

        if (TownStruggleRuntime.IsEnrolled(mobile))
        {
            return true;
        }

        return mobile is BaseCreature creature && TownStruggleRuntime.IsEnrolled(creature.GetMaster());
    }

    internal static bool CanAct(TownStruggleController controller, TownStruggleParticipant participant) =>
        controller is { Deleted: false, Phase: TownStrugglePhase.Active } &&
        participant is { Disqualified: false, Returned: false, Player: { Deleted: false, Alive: true } player } &&
        participant.Team is TownStruggleTeam.Order or TownStruggleTeam.Chaos &&
        participant.RespawnAt <= controller.Now && player.Map == controller.Venue.Map &&
        controller.Venue.Area.Contains(player.Location) && controller.CanReenter(player);

    public static bool IgnoreCollision(Mobile first, Mobile second)
    {
        var controller = TownStruggleRuntime.Controller;
        if (controller is not { Deleted: false, Phase: TownStrugglePhase.Active } || first == null || second == null ||
            first.Map != controller.Venue.Map || second.Map != controller.Venue.Map)
        {
            return false;
        }

        var firstEnrolled = IsEnrolledOwner(first);
        var secondEnrolled = IsEnrolledOwner(second);
        return firstEnrolled != secondEnrolled &&
               controller.Venue.Contains(firstEnrolled ? first : second);
    }

    public static bool AllowsBlockingField(Mobile caster, Map map, Point3D center, bool eastToWest, int radius)
    {
        var controller = TownStruggleRuntime.Controller;
        if (controller is not { Deleted: false, Phase: TownStrugglePhase.Signup or TownStrugglePhase.Active })
        {
            return true;
        }

        var enrolled = TownStruggleRuntime.TryGetParticipant(caster, out var participant);
        var canAct = enrolled && CanAct(controller, participant);
        for (var offset = -radius; offset <= radius; offset++)
        {
            var point = new Point3D(center.X + (eastToWest ? offset : 0), center.Y + (eastToWest ? 0 : offset), center.Z);
            var inside = map == controller.Venue.Map && controller.Venue.Area.Contains(point);
            if (inside ? controller.Phase == TownStrugglePhase.Signup || !canAct :
                controller.Phase == TownStrugglePhase.Active && enrolled)
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsInBase(TownStruggleVenue venue, Point3D location) =>
        IsInBase(location, venue.OrderBase) || IsInBase(location, venue.ChaosBase);

    public static bool IsInEnemyBase(TownStruggleVenue venue, TownStruggleTeam team, Point3D location) =>
        team switch
        {
            TownStruggleTeam.Order => IsInBase(location, venue.ChaosBase),
            TownStruggleTeam.Chaos => IsInBase(location, venue.OrderBase),
            _ => false
        };

    private static bool IsInBase(Point3D location, Point3D center) =>
        Math.Abs(location.X - center.X) <= TownStruggleVenue.BaseRadius &&
        Math.Abs(location.Y - center.Y) <= TownStruggleVenue.BaseRadius;

    public static bool AllowsMovement(Mobile mobile, Direction direction)
    {
        var controller = TownStruggleRuntime.Controller;
        var participant = controller?.GetParticipant(mobile);
        if (participant == null)
        {
            return true;
        }

        var location = mobile.Location;
        Movement.Movement.Offset(direction, ref location);
        return AllowsMovement(controller, participant, location);
    }

    internal static bool AllowsMovement(TownStruggleController controller, TownStruggleParticipant participant,
        Point3D location)
    {
        if (controller?.Phase != TownStrugglePhase.Active)
        {
            return true;
        }

        var venue = controller.Venue;
        var player = participant.Player;
        if (player.Map != venue.Map)
        {
            return true;
        }

        var destinationInside = venue.Area.Contains(location);
        if (destinationInside && !controller.CanReenter(player))
        {
            return false;
        }
        if (participant.Disqualified || participant.Returned || !destinationInside && !venue.Contains(player))
        {
            return true;
        }

        if (player.Mounted || IsInEnemyBase(venue, participant.Team, location))
        {
            return false;
        }

        return participant.RespawnAt <= controller.Now || IsInBase(location, venue.TeamBase(participant.Team));
    }

    public static void RecordDamage(Mobile target, Mobile from, int amount)
    {
        if (amount > 0 && TownStruggleRuntime.IsEnrolled(target) && AllowsDamage(target, from))
        {
            TownStruggleRuntime.Controller?.RecordDamage(target, from, Math.Min(amount, Math.Max(0, target.Hits)));
        }
    }

    public static void RecordHealing(Mobile target, Mobile from, int amount)
    {
        if (amount > 0 && TownStruggleRuntime.IsEnrolled(target) && AllowsHealing(target, from))
        {
            TownStruggleRuntime.Controller?.RecordHealing(target, from,
                Math.Min(amount, Math.Max(0, target.HitsMax - target.Hits)));
        }
    }
}
