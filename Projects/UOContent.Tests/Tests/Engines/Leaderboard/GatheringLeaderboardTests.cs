using Server;
using Server.Collections;
using Server.Engines.Custom;
using Server.Engines.Harvest;
using Server.Engines.Harvest.Custom;
using Server.Items;
using Server.Mobiles;
using Server.Targets;
using Xunit;

namespace UOContent.Tests;

[Collection("Sequential UOContent Tests")]
public class GatheringLeaderboardTests
{
    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 5)]
    public void Mining_UpdatesOreAndLegacyTotalsByYield(bool colored, int amount)
    {
        var player = CreatePlayer();
        var ore = colored ? (BaseOre)new ValoriteOre(amount) : new IronOre(amount);

        try
        {
            player.TotalOreMined = 20;
            player.SetLeaderboardStat(LeaderboardStat.OreMined, 20);

            MiningTracker.OnOreMined(player, ore, ore.Amount);

            Assert.Equal(20 + amount, player.TotalOreMined);
            Assert.Equal(20L + amount, player.GetLeaderboardStat(LeaderboardStat.OreMined));
        }
        finally
        {
            ore.Delete();
            player.Delete();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Mining_ExcludesSandStoneAndGems(int resource)
    {
        var player = CreatePlayer();
        var item = resource switch
        {
            0 => (Item)new Sand(),
            1 => new Granite(),
            _ => new Amber()
        };

        try
        {
            MiningTracker.OnOreMined(player, item, 4);

            Assert.Equal(0, player.TotalOreMined);
            Assert.Equal(0L, player.GetLeaderboardStat(LeaderboardStat.OreMined));
        }
        finally
        {
            item.Delete();
            player.Delete();
        }
    }

    [Theory]
    [InlineData(Expansion.UOR, HideType.Regular, false, false, true, 12)]
    [InlineData(Expansion.UOR, HideType.Spined, false, false, true, 12)]
    [InlineData(Expansion.UOR, HideType.Horned, false, false, true, 12)]
    [InlineData(Expansion.UOR, HideType.Barbed, false, false, true, 12)]
    [InlineData(Expansion.AOS, HideType.Regular, true, false, false, 6)]
    [InlineData(Expansion.AOS, HideType.Spined, true, false, true, 12)]
    [InlineData(Expansion.AOS, HideType.Horned, true, true, true, 12)]
    [InlineData(Expansion.AOS, HideType.Barbed, true, false, false, 6)]
    [InlineData(Expansion.ML, HideType.Regular, false, false, false, 7)]
    [InlineData(Expansion.ML, HideType.Regular, true, false, true, 14)]
    [InlineData(Expansion.UOR, HideType.Regular, true, false, false, 6)]
    public void Skinning_CreditsActualYieldOnce(
        Expansion expansion, HideType hideType, bool useSkinningKnife, bool packFull, bool felucca, int expected
    )
    {
        var previous = Core.Expansion;
        var player = CreatePlayer();
        var creature = new SkinningTestCreature(hideType);
        var tool = useSkinningKnife ? (Item)new SkinningKnife() : new Hatchet();
        Corpse corpse = null;
        var map = felucca ? Map.Felucca : Map.Trammel;
        var location = new Point3D(5200, 700, 0);

        try
        {
            Core.Expansion = expansion;
            player.MoveToWorld(location, map);
            player.Backpack.MaxItems = packFull ? 1 : 125;
            if (packFull)
            {
                player.Backpack.DropItem(new Gold(1));
            }

            if (useSkinningKnife)
            {
                player.AddItem(tool);
                Assert.Same(tool, player.Weapon);
            }

            creature.RegisterDamage(10, player);
            corpse = new Corpse(creature, []);
            corpse.MoveToWorld(location, map);
            var converted = useSkinningKnife && expansion >= Expansion.AOS;
            var resourceType = converted ? typeof(BaseLeather) : typeof(BaseHides);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (useSkinningKnife)
                {
                    new SkinningTestTarget(tool).Carve(player, corpse);
                }
                else
                {
                    new AxeSkinningTestTarget(tool).Carve(player, corpse);
                }

                Assert.Equal(expected, corpse.GetAmount(resourceType) + player.Backpack.GetAmount(resourceType));
                Assert.Equal(converted && !packFull ? expected : 0, player.Backpack.GetAmount(resourceType));
                Assert.Equal((long)expected, player.GetLeaderboardStat(LeaderboardStat.LeatherGathered));
                Assert.True(corpse.Carved);
            }
        }
        finally
        {
            corpse?.Delete();
            creature.Delete();
            tool.Delete();
            player.Delete();
            using var bloodItems = PooledRefList<Blood>.Create();
            foreach (var blood in map.GetItemsAt<Blood>(location))
            {
                bloodItems.Add(blood);
            }

            foreach (var blood in bloodItems)
            {
                blood.Delete();
            }

            Core.Expansion = previous;
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Skinning_WithoutGeneratedHidesDoesNotCredit(bool summoned, bool noHides)
    {
        var player = CreatePlayer();
        var creature = new SkinningTestCreature(HideType.Regular, noHides) { Summoned = summoned };
        var knife = new SkinningKnife();
        var corpse = new Corpse(creature, []);

        try
        {
            corpse.Carve(player, knife);

            Assert.Equal(0L, player.GetLeaderboardStat(LeaderboardStat.LeatherGathered));
            Assert.Equal(0, corpse.GetAmount(typeof(BaseHides)));
            Assert.False(corpse.Carved);
        }
        finally
        {
            corpse.Delete();
            creature.Delete();
            knife.Delete();
            player.Delete();
        }
    }

    private static PlayerMobile CreatePlayer()
    {
        var player = new PlayerMobile(World.NewMobile);
        player.DefaultMobileInit();
        player.Player = true;
        player.Race = Race.Human;
        player.AddItem(new Backpack());
        return player;
    }

    private class SkinningTestCreature : BaseCreature
    {
        private readonly HideType _hideType;
        private readonly bool _noHides;

        public SkinningTestCreature(HideType hideType, bool noHides = false) : base(World.NewMobile)
        {
            DefaultMobileInit();
            Body = 0xC8;
            _hideType = hideType;
            _noHides = noHides;
        }

        public override int Hides => _noHides ? 0 : 6;
        public override HideType HideType => _hideType;
    }

    private class SkinningTestTarget(Item item) : BladedItemTarget(item)
    {
        public void Carve(Mobile from, Corpse corpse) => OnTarget(from, corpse);
    }

    private class AxeSkinningTestTarget(Item item) : HarvestTarget(item, Lumberjacking.System)
    {
        public void Carve(Mobile from, Corpse corpse) => OnTarget(from, corpse);
    }
}
