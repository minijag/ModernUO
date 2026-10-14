using Server.Engines.TownStruggle;

namespace Server.Mobiles;

public partial class PlayerMobile
{
    public override bool OnDragLift(Item item) =>
        TownStruggleInteractions.CanLift(this, item) && base.OnDragLift(item);

    public override bool OnDroppedItemToMobile(Item item, Mobile target) =>
        TownStruggleInteractions.CanExchange(this, target) && base.OnDroppedItemToMobile(item, target);

    public override bool OnDroppedItemToItem(Item item, Item target, Point3D location) =>
        TownStruggleInteractions.CanDropToItem(this, target) && base.OnDroppedItemToItem(item, target, location);
}
