using ModernUO.Serialization;
using Server.Items;

namespace Server.Engines.ModularDungeon;

[SerializationGenerator(0)]
public partial class RootboundCellarChest : EventDungeonChest
{
    [SerializableField(0, setter: "private")]
    private bool _lootInitialized;

    [Constructible]
    public RootboundCellarChest() : this(0xE41)
    {
    }

    [Constructible]
    public RootboundCellarChest(int itemId) : base(itemId)
    {
        InitializeLoot();
    }

    private void InitializeLoot()
    {
        if (_lootInitialized)
        {
            return;
        }

        // Only normal construction fills the chest; serial-load construction must preserve an emptied reward.
        LootInitialized = true;
        DropItem(new Gold(Utility.RandomMinMax(2000, 2500)));

        foreach (var reagentType in Loot.RegTypes)
        {
            var reagent = reagentType.CreateInstance<Item>();
            reagent.Amount = Utility.RandomMinMax(40, 60);
            DropItem(reagent);
        }

        for (var i = 0; i < 8; ++i)
        {
            DropItem(Loot.RandomScroll(24, 63, SpellbookType.Regular));
        }

        for (var i = 0; i < 4; ++i)
        {
            if (i % 2 == 0)
            {
                var weapon = Loot.RandomWeapon();
                weapon.DamageLevel = (WeaponDamageLevel)Utility.RandomMinMax(1, 3);
                weapon.AccuracyLevel = (WeaponAccuracyLevel)Utility.RandomMinMax(1, 3);
                weapon.DurabilityLevel = (WeaponDurabilityLevel)Utility.RandomMinMax(1, 3);
                DropItem(weapon);
            }
            else
            {
                var armor = Loot.RandomArmorOrShield();
                armor.ProtectionLevel = (ArmorProtectionLevel)Utility.RandomMinMax(1, 3);
                armor.Durability = (ArmorDurabilityLevel)Utility.RandomMinMax(1, 3);
                DropItem(armor);
            }
        }
    }
}
