using Server.Mobiles;

namespace Server.Engines.ModularDungeon;

public static class ExtractionEventCombat
{
    public static bool IsEventCombat(Mobile from, Mobile target)
    {
        if (from is not { Deleted: false } || target is not { Deleted: false } ||
            from.Region is not ExtractionArenaRegion { IsActive: true } arena || target.Region != arena)
        {
            return false;
        }

        // Arena admission enforces membership; notoriety also runs off-loop and cannot read participant collections.
        return IsCombatant(from, arena) && IsCombatant(target, arena);
    }

    private static bool IsCombatant(Mobile mobile, ExtractionArenaRegion arena) => mobile switch
    {
        { Player: true, AccessLevel: AccessLevel.Player } => true,
        BaseCreature { Controlled: true, ControlMaster: { Deleted: false, Player: true, AccessLevel: AccessLevel.Player } master }
            => master.Region == arena,
        _ => false
    };
}
