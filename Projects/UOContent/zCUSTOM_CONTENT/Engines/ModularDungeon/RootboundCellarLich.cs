using ModernUO.Serialization;
using Server.Mobiles;

namespace Server.Engines.ModularDungeon;

[SerializationGenerator(0)]
public partial class RootboundCellarLich : Lich
{
    [Constructible]
    public RootboundCellarLich()
    {
        PackReg(40, 60);
    }

    public override void GenerateLoot()
    {
        base.GenerateLoot();
        AddLoot(LootPack.FilthyRich, 2);
        AddLoot(LootPack.MedScrolls, 2);
    }
}
