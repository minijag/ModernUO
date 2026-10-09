using System;
using Server;
using Server.Engines.Custom;
using Server.Guilds;
using Server.Items;
using Server.Mobiles;
using Server.Tests;
using Xunit;

namespace UOContent.Tests;

[Collection("Sequential UOContent Tests")]
public class GoldFarmedTests : IDisposable
{
    private static readonly Point3D Location = new(5500, 700, 0);
    private readonly Expansion _previousExpansion = Core.Expansion;
    private readonly CreateCorpseHandler _previousCorpseHandler = Mobile.CreateCorpseHandler;

    public GoldFarmedTests()
    {
        Core.Expansion = Expansion.T2A;
        Mobile.CreateCorpseHandler = Corpse.Mobile_CreateCorpseHandler;
    }

    public void Dispose()
    {
        Core.Expansion = _previousExpansion;
        Mobile.CreateCorpseHandler = _previousCorpseHandler;
    }

    [Fact]
    public void KillingMonster_CreditsConfiguredAverageImmediatelyWithoutLooting()
    {
        using var rng = new PredictableRandom(0);
        var player = CreatePlayer();
        var creature = new GoldTestCreature();

        try
        {
            creature.PackGold(100, 300);
            Assert.Equal(100, FindGold(creature.Backpack).Amount);
            creature.RegisterDamage(100, player);

            creature.Kill();

            Assert.True(creature.Deleted);
            Assert.IsType<Corpse>(creature.Corpse);
            Assert.Equal(0, player.Backpack.GetAmount(typeof(Gold)));
            Assert.Equal(200L, GoldFarmedService.GetTotal(player));
            Assert.Equal(200L, player.GetLeaderboardStat(LeaderboardStat.MonsterGoldGenerated));
            Assert.Equal(1L, player.GetLeaderboardStat(LeaderboardStat.MonstersKilled));
            Assert.Contains(new LeaderboardEntry(player.Name, 200), LeaderboardService.GetTop(LeaderboardStat.GoldFarmed));

            creature.Kill();
            Assert.Equal(200L, GoldFarmedService.GetTotal(player));
        }
        finally
        {
            Cleanup(creature, player);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LootPackGold_UsesDiceAverageForSpawnAndDeathLoot(bool spawnTime)
    {
        using var rng = new PredictableRandom(0);
        var player = CreatePlayer();
        var creature = new GoldTestCreature
        {
            TestLootPack = new LootPack([new LootPackEntry(spawnTime, LootPack.Gold, 100.0, "2d100+50")])
        };

        try
        {
            creature.GenerateLoot(true);
            creature.RegisterDamage(100, player);

            creature.Kill();

            Assert.Equal(52, FindGold(creature.Corpse).Amount);
            Assert.Equal(151L, GoldFarmedService.GetTotal(player));
        }
        finally
        {
            Cleanup(creature, player);
        }
    }

    [Fact]
    public void RemovingPhysicalGoldBeforeDeath_DoesNotReduceKillValue()
    {
        var player = CreatePlayer();
        var creature = new GoldTestCreature();

        try
        {
            creature.PackGold(100, 300);
            FindGold(creature.Backpack).Delete();
            creature.RegisterDamage(100, player);

            creature.Kill();

            Assert.Equal(0, creature.Corpse.GetAmount(typeof(Gold)));
            Assert.Equal(200L, GoldFarmedService.GetTotal(player));
        }
        finally
        {
            Cleanup(creature, player);
        }
    }

    [Fact]
    public void DepositingAndLootingGold_DoesNotAlterOrRepeatKillCredit()
    {
        using var rng = new PredictableRandom(0);
        var player = CreatePlayer();
        var creature = new GoldTestCreature();

        try
        {
            creature.PackGold(100, 300);
            creature.PackItem(new Gold(2000));
            creature.RegisterDamage(100, player);
            creature.Kill();
            Assert.Equal(200L, GoldFarmedService.GetTotal(player));

            var corpse = Assert.IsType<Corpse>(creature.Corpse);
            corpse.DropItem(new Gold(3000));
            while (corpse.GetAmount(typeof(Gold)) > 0)
            {
                var gold = FindGold(corpse);
                Loot(player, gold, gold.Amount);
            }

            Assert.Equal(5100, player.Backpack.GetAmount(typeof(Gold)));
            Assert.Equal(200L, GoldFarmedService.GetTotal(player));

            var looted = FindGold(player.Backpack);
            corpse.DropItem(looted);
            Loot(player, looted, looted.Amount);
            Assert.Equal(200L, GoldFarmedService.GetTotal(player));
        }
        finally
        {
            Cleanup(creature, player);
        }
    }

    [Fact]
    public void HighestQualifyingContributor_GetsKillValueAndCurrentGuildCredit()
    {
        var first = CreatePlayer();
        var highest = CreatePlayer();
        var firstGuild = new Guild(first, "First Gold Test", "FGT");
        var highestGuild = new Guild(highest, "Highest Gold Test", "HGT");
        var creature = new GoldTestCreature();

        try
        {
            creature.PackGold(300);
            creature.RegisterDamage(50, first);
            creature.RegisterDamage(100, highest);

            creature.Kill();

            Assert.Equal(0L, GoldFarmedService.GetTotal(first));
            Assert.Equal(300L, GoldFarmedService.GetTotal(highest));
            Assert.Equal(0L, first.GetLeaderboardStat(LeaderboardStat.MonstersKilled));
            Assert.Equal(1L, highest.GetLeaderboardStat(LeaderboardStat.MonstersKilled));
            Assert.Equal(default, GuildProgressService.GetProgress(firstGuild));
            Assert.Equal(new GuildProgress(300, 0, 0), GuildProgressService.GetProgress(highestGuild));
        }
        finally
        {
            firstGuild.Disband();
            highestGuild.Disband();
            Cleanup(creature, first);
            highest.Delete();
        }
    }

    [Fact]
    public void PetKill_CreditsDamageMasterWithoutRequiringMasterToLoot()
    {
        var master = CreatePlayer();
        var pet = new GoldTestPet(master);
        var creature = new GoldTestCreature();

        try
        {
            creature.PackGold(100, 300);
            creature.RegisterDamage(100, pet);

            creature.Kill();

            Assert.Equal(200L, GoldFarmedService.GetTotal(master));
            Assert.Equal(1L, master.GetLeaderboardStat(LeaderboardStat.MonstersKilled));
            Assert.Equal(0, master.Backpack.GetAmount(typeof(Gold)));
        }
        finally
        {
            Cleanup(creature, master);
            pet.Delete();
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void SummonedAndNoAwardCreatures_DoNotCreditKillValue(bool summoned, bool noKillAwards)
    {
        var player = CreatePlayer();
        var creature = new GoldTestCreature { Summoned = summoned, NoKillAwards = noKillAwards };

        try
        {
            creature.PackGold(300);
            creature.RegisterDamage(100, player);

            creature.Kill();

            Assert.Equal(0L, GoldFarmedService.GetTotal(player));
            Assert.Equal(0L, player.GetLeaderboardStat(LeaderboardStat.MonstersKilled));
        }
        finally
        {
            Cleanup(creature, player);
        }
    }

    [Fact]
    public void TreasureGoldAndOrdinaryTransfers_DoNotEarnMonsterKillValue()
    {
        using var rng = new PredictableRandom(10);
        var player = CreatePlayer();
        var chest = new TreasureMapChest(1);

        try
        {
            chest.MoveToWorld(Location, Map.Felucca);
            chest.Locked = false;
            var gold = FindGold(chest);
            Loot(player, gold, gold.Amount);
            var totalGold = player.Backpack.GetAmount(typeof(Gold));
            Assert.True(totalGold > 0);
            Assert.Equal(0L, GoldFarmedService.GetTotal(player));

            player.BankBox.DropItem(FindGold(player.Backpack));
            player.Backpack.DropItem(FindGold(player.BankBox));
            player.Backpack.DropItem(new Gold(1000));

            Assert.Equal(totalGold + 1000, player.Backpack.GetAmount(typeof(Gold)));
            Assert.Equal(0L, GoldFarmedService.GetTotal(player));
        }
        finally
        {
            chest.Delete();
            player.Delete();
        }
    }

    [Fact]
    public void LegacyKillGold_IsVisibleAndRetainsItsExistingPlayerSaveLayout()
    {
        var donor = CreatePlayer();
        var recipient = CreatePlayer();
        var writer = new BufferWriter(true);

        try
        {
            donor.SetLeaderboardStat(LeaderboardStat.MonsterGoldGenerated, 725);
            Assert.Equal(725L, GoldFarmedService.GetTotal(donor));
            Assert.Equal(725L, donor.GetLeaderboardStat(LeaderboardStat.GoldFarmed));
            donor.SerializeLeaderboardStats(writer);

            Assert.Equal(9L * sizeof(long), writer.Position);
            var reader = new BufferReader(writer.Buffer.AsSpan(0, (int)writer.Position).ToArray());
            recipient.DeserializeLeaderboardStats(reader);

            Assert.Equal(writer.Position, reader.Position);
            Assert.Equal(725L, GoldFarmedService.GetTotal(recipient));
        }
        finally
        {
            writer.Close();
            donor.Delete();
            recipient.Delete();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void LegacyPhysicalGold_LoadsAndConsumesRemovedProvenanceFields(int version)
    {
        var player = CreatePlayer();
        var source = new Backpack();
        var donor = new Item(0xEED) { Stackable = true, Amount = 100 };
        var gold = new Gold(World.NewItem);
        var writer = new BufferWriter(true);

        try
        {
            donor.Serialize(writer);
            writer.Write(version);
            if (version == 1)
            {
                writer.Write(100);
                writer.Write(player);
                writer.Write(source);
            }

            var reader = new BufferReader(writer.Buffer.AsSpan(0, (int)writer.Position).ToArray());
            gold.Deserialize(reader);

            Assert.Equal(writer.Position, reader.Position);
            Assert.Equal(100, gold.Amount);
            Assert.True(gold.Stackable);
            player.Backpack.DropItem(gold);
            Assert.Equal(0L, GoldFarmedService.GetTotal(player));
        }
        finally
        {
            writer.Close();
            donor.Delete();
            gold.Delete();
            source.Delete();
            player.Delete();
        }
    }

    [Fact]
    public void CurrentPhysicalGold_RoundTripsWithoutLootCredit()
    {
        var player = CreatePlayer();
        var donor = new Gold(123);
        var copy = new Gold(World.NewItem);
        var writer = new BufferWriter(true);

        try
        {
            donor.Serialize(writer);
            var reader = new BufferReader(writer.Buffer.AsSpan(0, (int)writer.Position).ToArray());
            copy.Deserialize(reader);

            Assert.Equal(writer.Position, reader.Position);
            Assert.Equal(123, copy.Amount);
            player.Backpack.DropItem(copy);
            Assert.Equal(0L, GoldFarmedService.GetTotal(player));
        }
        finally
        {
            writer.Close();
            donor.Delete();
            copy.Delete();
            player.Delete();
        }
    }

    private static PlayerMobile CreatePlayer()
    {
        var player = new PlayerMobile(World.NewMobile);
        player.DefaultMobileInit();
        World.AddEntity(player);
        player.Name = $"Gold Farmer {player.Serial.Value}";
        player.Player = true;
        player.Str = 100;
        player.Race = Race.Human;
        player.AddItem(new Backpack());
        player.MoveToWorld(Location, Map.Felucca);
        return player;
    }

    private static void Cleanup(GoldTestCreature creature, PlayerMobile player)
    {
        creature.Corpse?.Delete();
        creature.Delete();
        player.Delete();
    }

    private static Gold FindGold(Container container)
    {
        foreach (var item in container.Items)
        {
            if (item is Gold gold)
            {
                return gold;
            }
        }

        throw new InvalidOperationException("Expected a gold stack.");
    }

    private static void Loot(PlayerMobile player, Gold gold, int amount)
    {
        player.NextActionTime = Core.TickCount;
        player.Lift(gold, amount, out var rejected, out _);
        Assert.False(rejected);
        Assert.True(player.Drop(player.Backpack, new Point3D(-1, -1, 0)));
    }

    private class GoldTestCreature : BaseCreature
    {
        public GoldTestCreature() : base(World.NewMobile)
        {
            DefaultMobileInit();
            Body = 0xC8;
            HitsMaxSeed = 200;
            Hits = 200;
            MoveToWorld(GoldFarmedTests.Location, Map.Felucca);
        }

        public LootPack TestLootPack { get; init; }

        public override void GenerateLoot()
        {
            if (TestLootPack != null)
            {
                AddLoot(TestLootPack);
            }
        }
    }

    private class GoldTestPet(Mobile master) : Mobile
    {
        private Mobile _master = master;

        public override Mobile GetDamageMaster(Mobile damagee) => _master;

        public override void OnAfterDelete()
        {
            _master = null;
            base.OnAfterDelete();
        }
    }
}
