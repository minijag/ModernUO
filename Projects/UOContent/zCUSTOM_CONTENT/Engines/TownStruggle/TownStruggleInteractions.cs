using Server.Items;

namespace Server.Engines.TownStruggle;

public static class TownStruggleInteractions
{
    public static bool CanExchange(Mobile from, Mobile to)
    {
        if (from == to) { return true; }
        var sourceEnrolled = TownStruggleRuntime.TryGetParticipant(from, out var source);
        var targetEnrolled = TownStruggleRuntime.TryGetParticipant(to, out var target);
        if (!sourceEnrolled && !targetEnrolled) { return true; }
        var controller = TownStruggleRuntime.Controller;
        return sourceEnrolled && targetEnrolled && source.Team == target.Team &&
               TownStruggleCombat.CanAct(controller, source) && TownStruggleCombat.CanAct(controller, target);
    }

    public static bool CanLift(Mobile player, Item item)
    {
        if (!TownStruggleRuntime.IsEnrolled(player)) { return true; }
        if (item == null || item.Deleted) { return false; }
        // Carry-in supplies and this round's gear drops are the only inventory sources during enrollment.
        if (item.RootParent == player)
        {
            return player.FindBankNoCreate() is not { } bank || item != bank && !item.IsChildOf(bank);
        }
        return item.RootParent is TownStruggleCorpse corpse &&
               corpse.Controller == TownStruggleRuntime.Controller && corpse.RoundNumber == corpse.Controller.RoundNumber &&
               corpse.CanEventLoot(player);
    }

    public static bool CanDropToItem(Mobile player, Item target)
    {
        var owner = target?.RootParent as Mobile;
        if (!CanExchange(player, owner)) { return false; }
        if (!TownStruggleRuntime.IsEnrolled(player)) { return true; }
        return target != null && owner == player &&
               (player.FindBankNoCreate() is not { } bank || target != bank && !target.IsChildOf(bank));
    }
}
