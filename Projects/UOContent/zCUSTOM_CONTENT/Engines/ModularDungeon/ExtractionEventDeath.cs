using System;
using ModernUO.CodeGeneratedEvents;
using Server.Items;
using Server.Mobiles;

namespace Server.Engines.ModularDungeon;

public static class ExtractionEventDeath
{
    public static Func<ExtractionEventController> ControllerLookup { private get; set; }

    public static bool MustDropOnDeath(PlayerMobile player, Item item)
    {
        if (item is not { Deleted: false } || !IsEventDeath(player))
        {
            return false;
        }

        var backpack = player.Backpack;
        var bank = player.FindBankNoCreate();
        if (item == backpack || item == bank || item is BankBox || bank != null && item.IsChildOf(bank) ||
            item.Parent == player && item.Layer is Layer.Backpack or Layer.Bank or Layer.Hair or Layer.FacialHair or Layer.Mount)
        {
            return false;
        }

        return item.Parent == player || backpack != null && item.IsChildOf(backpack);
    }

    [OnEvent(nameof(PlayerMobile.PlayerDeathEvent))]
    public static void OnPlayerDeath(PlayerMobile player)
    {
        if (!IsEventDeath(player))
        {
            return;
        }
        if (player.Corpse is Corpse { Deleted: false } corpse)
        {
            corpse.Criminal = true;
        }

        var killer = player.FindMostRecentDamager(true);
        if (killer is BaseCreature creature)
        {
            killer = creature.GetMaster();
        }
        ControllerLookup?.Invoke()?.RecordStatisticsDeath(player, killer);
    }

    private static bool IsEventDeath(PlayerMobile player)
    {
        var controller = ControllerLookup?.Invoke();
        if (player is not { Deleted: false } ||
            controller is not { Deleted: false, Phase: ExtractionEventPhase.Active or ExtractionEventPhase.Finishing or ExtractionEventPhase.Faulted } ||
            !controller.IsParticipant(player))
        {
            return false;
        }

        // Disconnected participants keep their arena position in their logout fields until timeout death completes.
        var offline = player.Map == Map.Internal;
        return (offline ? player.LogoutMap : player.Map) == Map.Felucca &&
               ExtractionDungeonLayout.DungeonArea.Contains(offline ? player.LogoutLocation : player.Location);
    }
}
