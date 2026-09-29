using ModernUO.Serialization;
using Server.Items;
using Server.Mobiles;
using Server.Misc;

namespace Server.Engines.MorganCastle;

[SerializationGenerator(0, false)]
public partial class MorganCastleResident : BaseCreature
{
    [Constructible]
    public MorganCastleResident() : this("swordsman") { }

    [Constructible]
    public MorganCastleResident(string role) : base(role == "archer" ? AIType.AI_Archer : AIType.AI_Melee, FightMode.Aggressor)
    {
        Body = 400;
        Hue = 1002;
        Name = role == "king" ? "Morgan" : NameList.RandomName("male");
        Title = role switch
        {
            "king" => "the King",
            "jester" => "the court jester",
            "cook" => "the royal cook",
            "scholar" => "the royal scholar",
            "library-assistant" => "the librarian's assistant",
            "kitchen-assistant" => "the cook's assistant",
            "prisoner" => "the prisoner",
            "archer" => "the castle archer",
            "sentry" => "the gate sentry",
            _ => "the royal swordsman"
        };
        SetStr(150); SetDex(100); SetInt(100); SetHits(250); SetDamage(10, 16);
        SetSkill(SkillName.Tactics, 90); SetSkill(SkillName.MagicResist, 80);
        SetSkill(SkillName.Swords, 90); SetSkill(SkillName.Archery, 90);
        Fame = 1000; Karma = 5000;
        if (role != "prisoner") { AddItem(new Boots(1109)); }
        if (role is "king" or "scholar" or "library-assistant")
        {
            AddItem(new Robe(role == "king" ? 1153 : 1109));
            AddItem(new Cloak(1153));
            AddItem(role == "king" ? new Circlet { Hue = 2213, Name = "Morgan's crown" } : new WizardsHat());
            SetSkill(SkillName.Inscribe, 100);
        }
        else if (role == "jester")
        {
            AddItem(new JesterSuit(33)); AddItem(new JesterHat(53));
            SetSkill(SkillName.Musicianship, 100);
        }
        else if (role is "cook" or "kitchen-assistant")
        {
            AddItem(new Shirt()); AddItem(new LongPants(1109)); AddItem(new HalfApron());
            AddItem(new Cap()); SetSkill(SkillName.Cooking, 100);
        }
        else if (role == "prisoner")
        {
            AddItem(new Shirt(1109)); AddItem(new ShortPants(1109));
            SetStr(40); SetDex(40); SetInt(40); SetHits(50); SetDamage(1, 3);
            Fame = 0; Karma = 0; FightMode = FightMode.None;
        }
        else
        {
            AddItem(new ChainChest()); AddItem(new ChainLegs()); AddItem(new CloseHelm());
            AddItem(new Surcoat(1153));
            if (role == "archer") { AddItem(new Bow()); PackItem(new Arrow(100)); }
            else if (role == "sentry") { AddItem(new Halberd()); }
            else { AddItem(new Broadsword()); AddItem(new HeaterShield()); }
        }
    }
}
