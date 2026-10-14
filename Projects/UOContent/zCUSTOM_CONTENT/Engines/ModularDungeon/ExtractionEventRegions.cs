using Server.Mobiles;
using Server.Regions;
using Server.Spells;

namespace Server.Engines.ModularDungeon;

public sealed class ExtractionLobbyRegion : BaseRegion
{
    public ExtractionLobbyRegion() : base("Extraction Lobby", Map.Felucca, 150, ExtractionDungeonLayout.LobbyBounds)
    {
        ExcludeFromParentSpawns = true;
    }

    public override bool AllowHousing(Mobile from, Point3D point) => false;
    public override bool AllowHarmful(Mobile from, Mobile target) => false;
    public override bool AllowBeneficial(Mobile from, Mobile target) => target.Region == this && base.AllowBeneficial(from, target);
    public override bool OnDamage(Mobile mobile, ref int damage) => false;
    public override bool OnCombatantChange(Mobile mobile, Mobile old, Mobile next) => next == null;
    public override bool CanUseStuckMenu(Mobile mobile) => false;

    public override bool CheckTravel(Mobile mobile, Point3D location, TravelCheckType type, out TextDefinition message)
    {
        message = null;
        return mobile?.AccessLevel > AccessLevel.Player;
    }
}

public sealed class ExtractionArenaRegion : DungeonRegion
{
    private readonly ExtractionEventController _controller;
    public bool IsActive => _controller.Phase == ExtractionEventPhase.Active;

    public ExtractionArenaRegion(ExtractionEventController controller)
        : base("Extraction Dungeon", Map.Felucca, 150, ExtractionDungeonLayout.DungeonArea)
    {
        _controller = controller;
        ExcludeFromParentSpawns = true;
    }

    public override bool CanUseStuckMenu(Mobile mobile) => false;

    public override bool OnMoveInto(Mobile mobile, Direction direction, Point3D location, Point3D previous)
    {
        if (mobile.AccessLevel > AccessLevel.Player)
        {
            return base.OnMoveInto(mobile, direction, location, previous);
        }

        if (mobile.Player)
        {
            return _controller.Phase == ExtractionEventPhase.Active && _controller.IsParticipant(mobile);
        }

        if (mobile is BaseCreature { Controlled: true, ControlMaster: { } master })
        {
            return _controller.Phase == ExtractionEventPhase.Active && _controller.IsParticipant(master);
        }

        return base.OnMoveInto(mobile, direction, location, previous);
    }

    public override void OnLocationChanged(Mobile mobile, Point3D oldLocation)
    {
        ExtractionEventRuntime.NotifyShrineMovement(mobile, oldLocation);
        base.OnLocationChanged(mobile, oldLocation);
    }

    public override void OnExit(Mobile mobile)
    {
        ExtractionEventRuntime.NotifyShrineMovement(mobile, mobile.Location);
        base.OnExit(mobile);
    }

    public override bool AllowHarmful(Mobile from, Mobile target) =>
        IsActive && target.Region == this && base.AllowHarmful(from, target);

    public override bool AllowBeneficial(Mobile from, Mobile target) =>
        target.Region == this && base.AllowBeneficial(from, target);

    public override bool OnDamage(Mobile mobile, ref int damage) => IsActive && base.OnDamage(mobile, ref damage);

    public override bool OnCombatantChange(Mobile mobile, Mobile old, Mobile next) =>
        next == null || _controller.Phase == ExtractionEventPhase.Active && base.OnCombatantChange(mobile, old, next);

    public override bool CheckTravel(Mobile mobile, Point3D location, TravelCheckType type, out TextDefinition message)
    {
        message = null;
        if (mobile?.AccessLevel > AccessLevel.Player)
        {
            return true;
        }

        // Short-range teleport stays useful in a chase, but cannot substitute for extraction or admission.
        return mobile != null && _controller.Phase == ExtractionEventPhase.Active && _controller.IsParticipant(mobile) &&
               type is TravelCheckType.TeleportFrom or TravelCheckType.TeleportTo &&
               ExtractionDungeonLayout.DungeonArea.Contains(location);
    }

}
