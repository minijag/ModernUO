using ModernUO.Serialization;
using Server.Items;
using Server.Network;

namespace Server.Engines.TownStruggle;

[SerializationGenerator(0)]
public partial class TownStruggleCorpse : Corpse
{
    [CanBeNull]
    [SerializableField(0, setter: "private")] private TownStruggleController _controller;
    [SerializableField(1, setter: "private")] private int _roundNumber;

    [Constructible(AccessLevel.Administrator)]
    public TownStruggleCorpse(Mobile owner) : this(null, owner)
    {
    }

    public TownStruggleCorpse(TownStruggleController controller, Mobile owner) : base(owner, [])
    {
        Controller = controller;
        RoundNumber = controller?.RoundNumber ?? 0;
        Criminal = true;
        // Event defeat leaves its owner alive; this container must not produce body parts or spirit energy.
        Carved = true;
        Channeled = true;
    }

    public bool CanEventLoot(Mobile from)
    {
        if (Deleted || from is not { Deleted: false })
        {
            return false;
        }
        if (Controller is not { Deleted: false, Phase: TownStrugglePhase.Active } controller ||
            controller.RoundNumber != RoundNumber)
        {
            return true;
        }
        return from.Map == Map && TownStruggleCombat.CanAct(controller, controller.GetParticipant(from));
    }

    public override bool CheckItemUse(Mobile from, Item item) =>
        CanEventLoot(from) && base.CheckItemUse(from, item);

    public override bool CheckLift(Mobile from, Item item, ref LRReason reject)
    {
        if (!CanEventLoot(from))
        {
            reject = LRReason.CannotLift;
            return false;
        }
        return base.CheckLift(from, item, ref reject);
    }

    public override void Open(Mobile from, bool checkSelfLoot)
    {
        if (!CanEventLoot(from))
        {
            from.SendMessage("Only active participants may loot this Town Struggle corpse until the match ends.");
            return;
        }
        base.Open(from, checkSelfLoot);
    }

    public override bool CheckHold(Mobile from, Item item, bool message, bool checkItems, int plusItems, int plusWeight)
    {
        if (message)
        {
            from?.SendMessage("This corpse accepts only equipment dropped by the event.");
        }
        return false;
    }

    public override void OnAfterDelete()
    {
        Controller = null;
        base.OnAfterDelete();
    }
}
