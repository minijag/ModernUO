using ModernUO.Serialization;
using Server.Engines.PvpEvents;

namespace Server.Engines.TownStruggle;

[SerializationGenerator(0)]
public partial class TownStruggleStone : Item
{
    [Constructible]
    public TownStruggleStone() : base(0xED4)
    {
        Movable = false;
        Hue = 0x489;
    }

    public override string DefaultName => "Town Struggle";

    public override void OnDoubleClick(Mobile from)
    {
        if (from.Map != Map || !from.InRange(GetWorldLocation(), 2) || !from.InLOS(this))
        {
            from.SendMessage("Move closer to the Town Struggle stone.");
            return;
        }
        PvpEventsGump.DisplayTo(from);
    }
}
