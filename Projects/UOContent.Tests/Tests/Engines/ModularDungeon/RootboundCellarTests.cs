using System;
using System.Collections.Generic;
using System.Reflection;
using Server;
using Server.Engines.ModularDungeon;
using Server.Items;
using Server.Mobiles;
using Server.Tests;
using Xunit;

namespace UOContent.Tests.Engines.ModularDungeon;

[Collection("Sequential UOContent Tests")]
public class RootboundCellarTests : IDisposable
{
    private readonly List<IEntity> _created = new();
    private readonly Expansion _previousExpansion = Core.Expansion;
    private readonly long _startTickCount = Core.TickCount;
    private readonly DateTime _startTime = Core.Now;

    public RootboundCellarTests()
    {
        Core.Expansion = Expansion.T2A;
        Timer.Init(Core.TickCount);
    }

    public void Dispose()
    {
        for (var i = _created.Count - 1; i >= 0; --i)
        {
            _created[i].Delete();
        }

        Core.Expansion = _previousExpansion;
        Core._tickCount = _startTickCount;
        Core._now = _startTime;
        Timer.Init(Core.TickCount);
    }

    [Fact]
    public void CoverMovedAndRestored_ReevaluatesWithoutTimersOrPolling()
    {
        var origin = new Point3D(101, 100, 0);
        var box = Track(new WoodenBox());
        box.MoveToWorld(origin, Map.Felucca);
        var hatch = Track(new EventDungeonHiddenHatch(Map.Felucca, origin, new Point3D(106, 105, -25), box));
        var player = Actor(new Point3D(100, 100, 0));

        Assert.True(hatch.IsCovered);
        Assert.False(hatch.CanTeleport(player));
        Assert.True(hatch.OnMoveOver(player));

        box.MoveToWorld(new Point3D(102, 100, 0), Map.Felucca);
        Assert.False(hatch.IsCovered);
        Assert.True(hatch.CanTeleport(player));

        box.MoveToWorld(origin, Map.Felucca);
        Assert.True(hatch.IsCovered);
        Assert.False(hatch.CanTeleport(player));

        box.MoveToWorld(new Point3D(origin.X, origin.Y, origin.Z + 1), Map.Felucca);
        Assert.False(hatch.IsCovered);
        Track(new Backpack()).DropItem(box);
        Assert.False(hatch.IsCovered);

        box.MoveToWorld(origin, Map.Trammel);
        Assert.False(hatch.IsCovered);
        box.MoveToWorld(origin, Map.Felucca);
        box.Delete();
        Assert.False(hatch.IsCovered);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreaturesCannotActivateEitherTransport_EvenWhenCreaturesFlagIsSet(bool flag)
    {
        var hatch = Track(new EventDungeonHiddenHatch(Map.Felucca, new Point3D(101, 100, 0), new Point3D(105, 105, -25), null));
        var rune = Track(new RootboundCellarReturnRune(new Point3D(102, 100, 0), Map.Felucca));
        rune.MoveToWorld(new Point3D(110, 110, -25), Map.Felucca);
        hatch.Creatures = flag;
        rune.Creatures = flag;
        var creature = Track(new Mobile { Player = false, Body = 0xD0 });
        creature.MoveToWorld(new Point3D(100, 100, 0), Map.Felucca);
        var location = creature.Location;

        Assert.False(hatch.CanTeleport(creature));
        Assert.True(hatch.OnMoveOver(creature));
        Assert.False(rune.CanTeleport(creature));
        Assert.True(rune.OnMoveOver(creature));
        Assert.Equal(location, creature.Location);
    }

    [Fact]
    public void NativeMoveOver_StopsTheCurrentWalkFromOverwritingTheCellarDestination()
    {
        var origin = new Point3D(101, 100, 0);
        var destination = new Point3D(106, 105, -25);
        var hatch = Track(new EventDungeonHiddenHatch(Map.Felucca, origin, destination, null));
        var player = Track(new Walker { Player = true, Body = 0x190, Direction = Direction.East });
        player.MoveToWorld(new Point3D(100, 100, 0), Map.Felucca);

        Assert.False(player.Move(Direction.East));
        Assert.Equal(destination, player.Location);
        Assert.Equal(Map.Felucca, player.Map);
        Assert.Equal(TimeSpan.Zero, hatch.Delay);
    }

    [Fact]
    public void HatchRoundTrip_PreservesTheCoverReferenceOriginAndDestination()
    {
        var origin = new Point3D(101, 100, 0);
        var destination = new Point3D(106, 105, -25);
        var box = Track(new WoodenBox());
        box.MoveToWorld(origin, Map.Felucca);
        var hatch = Track(new EventDungeonHiddenHatch(Map.Felucca, origin, destination, box));
        var bytes = Snapshot(hatch);
        hatch.Delete();
        Assert.Null(hatch.CoverBox);
        Assert.Null(hatch.CoverMap);
        Assert.False(box.Deleted);
        var restored = Track(new EventDungeonHiddenHatch(World.NewItem));
        var reader = new BufferReader(bytes);
        restored.Deserialize(reader);

        Assert.Equal(bytes.Length, reader.Position);
        Assert.Same(box, restored.CoverBox);
        Assert.Equal(origin, restored.CoverOrigin);
        Assert.Same(Map.Felucca, restored.CoverMap);
        Assert.Equal(destination, restored.PointDest);
        Assert.Same(Map.Felucca, restored.MapDest);
        Assert.True(restored.IsCovered);
        box.MoveToWorld(new Point3D(102, 100, 0), Map.Felucca);
        Assert.True(restored.CanTeleport(Actor(new Point3D(100, 100, 0))));
    }

    [SkippableFact]
    public void ReturnRune_MovesToDistinctSupportedGround_AndAvoidsMovedCoverAndHatch()
    {
        Skip.If(!TestServerInitializer.TileDataLoaded, "Native tile data is required for return-ground checks.");
        var floorId = 1;
        var blockerId = 2;
        var oldFloor = TileData.ItemTable[floorId];
        var oldBlocker = TileData.ItemTable[blockerId];
        var destination = new Point3D(102, 100, 80);
        var hatch = new Point3D(101, 100, 80);

        try
        {
            TileData.ItemTable[floorId] = new ItemData("test floor", TileFlag.Surface, 0, 0, 0, 0, 0, 0);
            TileData.ItemTable[blockerId] = new ItemData("test box", TileFlag.Impassable, 0, 0, 0, 0, 0, 10);
            for (var x = 100; x <= 104; ++x)
            {
                for (var y = 98; y <= 102; ++y)
                {
                    var floor = Track(new Item(floorId) { Movable = false });
                    floor.MoveToWorld(new Point3D(x, y, 80), Map.Felucca);
                }
            }

            var rune = Track(new RootboundCellarReturnRune(destination, Map.Felucca, hatch));
            rune.MoveToWorld(new Point3D(105, 105, -25), Map.Felucca);
            var player = Actor(rune.Location);
            Assert.True(rune.Visible);
            Assert.False(rune.Movable);
            Assert.Equal(0x1822, rune.ItemID);
            Assert.True(rune.TryFindReturnPoint(out var preferred));
            Assert.Equal(destination, preferred);
            Assert.False(rune.OnMoveOver(player));
            Assert.Equal(destination, player.Location);
            player.MoveToWorld(rune.Location, Map.Felucca);

            var cover = Track(new Item(blockerId));
            cover.MoveToWorld(destination, Map.Felucca);
            Assert.True(rune.TryFindReturnPoint(out var fallback));
            Assert.NotEqual(destination, fallback);
            Assert.NotEqual(hatch, fallback);
            Assert.Equal(80, fallback.Z);
            Assert.True(Map.Felucca.CanFit(fallback, 16, true, false));
            Assert.False(rune.OnMoveOver(player));
            Assert.Equal(fallback, player.Location);
            Assert.Equal(destination, rune.PointDest);

            var bytes = Snapshot(rune);
            rune.Delete();
            var restored = Track(new RootboundCellarReturnRune(World.NewItem));
            var reader = new BufferReader(bytes);
            restored.Deserialize(reader);
            Assert.Equal(bytes.Length, reader.Position);
            Assert.Equal(destination, restored.PointDest);
            Assert.Equal(hatch, restored.HatchOrigin);
            Assert.Equal(Map.Felucca, restored.MapDest);
        }
        finally
        {
            TileData.ItemTable[floorId] = oldFloor;
            TileData.ItemTable[blockerId] = oldBlocker;
        }
    }

    [Fact]
    public void ReturnRune_WithNoSupportedSurface_DoesNotTeleportIntoTheVoid()
    {
        var rune = Track(new RootboundCellarReturnRune(new Point3D(-100, -100, 0), Map.Felucca));
        rune.MoveToWorld(new Point3D(105, 105, -25), Map.Felucca);
        var player = Actor(rune.Location);

        Assert.False(rune.TryFindReturnPoint(out _));
        rune.OnMoveOver(player);
        Assert.Equal(rune.Location, player.Location);
    }

    [Fact]
    public void ChestConstruction_CreatesNormalT2ARewards_AndInitializeCannotRefillIt()
    {
        var chest = Track(new RootboundCellarChest(0xE42));
        Assert.True(chest.LootInitialized);
        Assert.IsAssignableFrom<EventDungeonChest>(chest);
        Assert.False(chest.Movable);
        Assert.InRange(chest.GetAmount(typeof(Gold)), 2000, 2500);
        foreach (var reagentType in Loot.RegTypes)
        {
            Assert.InRange(chest.GetAmount(reagentType), 40, 60);
        }

        var scrolls = 0;
        var magicItems = 0;
        foreach (var item in chest.Items)
        {
            if (item is SpellScroll)
            {
                scrolls += item.Amount;
            }

            if (item is BaseWeapon weapon)
            {
                Assert.InRange((int)weapon.DamageLevel, 1, 3);
                Assert.InRange((int)weapon.AccuracyLevel, 1, 3);
                Assert.InRange((int)weapon.DurabilityLevel, 1, 3);
                magicItems++;
            }
            else if (item is BaseArmor armor)
            {
                Assert.InRange((int)armor.ProtectionLevel, 1, 3);
                Assert.InRange((int)armor.Durability, 1, 3);
                magicItems++;
            }
        }

        Assert.Equal(8, scrolls);
        Assert.Equal(4, magicItems);
        Empty(chest);
        InitializeChest(chest);
        Assert.Empty(chest.Items);

        var bytes = Snapshot(chest);
        chest.Delete();
        var restored = Track(new RootboundCellarChest(World.NewItem));
        var reader = new BufferReader(bytes);
        restored.Deserialize(reader);
        Assert.Equal(bytes.Length, reader.Position);
        Assert.True(restored.LootInitialized);
        Assert.Empty(restored.Items);
        InitializeChest(restored);
        Assert.Empty(restored.Items);
    }

    [Fact]
    public void LichKeepsNormalCombat_WithAdditionalEraLoot_AndNoStartupRefill()
    {
        var lich = Track(new RootboundCellarLich());
        var ordinary = Track(new Lich());
        Assert.Equal(ordinary.Body, lich.Body);
        Assert.Equal(ordinary.AI, lich.AI);
        Assert.Equal(ordinary.DamageMin, lich.DamageMin);
        Assert.Equal(ordinary.DamageMax, lich.DamageMax);
        Assert.InRange(lich.Backpack.GetAmount(typeof(Gold)), 1064, 1650);
        Assert.InRange(ordinary.Backpack.GetAmount(typeof(Gold)), 260, 350);
        var bytes = Snapshot(lich);
        var restored = Track(new RootboundCellarLich(World.NewMobile));
        var reader = new BufferReader(bytes);
        restored.Deserialize(reader);
        Assert.Equal(bytes.Length, reader.Position);
        Assert.Same(lich.Backpack, restored.Backpack);
        Assert.Equal(lich.Backpack.Items.Count, restored.Backpack.Items.Count);
        restored.RemoveItem(restored.Backpack);
    }

    [Fact]
    public void LichDeathLoot_IsAwardedOnce_AndItsNativeGuardSurvivesSerialization()
    {
        var lich = Track(new RootboundCellarLich());
        lich.MoveToWorld(new Point3D(110, 110, -25), Map.Felucca);
        Assert.Equal(0, CountScrolls(lich.Backpack));
        Assert.True(lich.OnBeforeDeath());
        Assert.Equal(4, CountScrolls(lich.Backpack));
        var gold = lich.Backpack.GetAmount(typeof(Gold));
        Assert.True(lich.OnBeforeDeath());
        Assert.Equal(4, CountScrolls(lich.Backpack));
        Assert.Equal(gold, lich.Backpack.GetAmount(typeof(Gold)));

        var bytes = Snapshot(lich);
        var restored = Track(new RootboundCellarLich(World.NewMobile));
        var reader = new BufferReader(bytes);
        restored.Deserialize(reader);
        Assert.Equal(bytes.Length, reader.Position);
        Assert.True(restored.OnBeforeDeath());
        Assert.Equal(4, CountScrolls(restored.Backpack));
        Assert.Equal(gold, restored.Backpack.GetAmount(typeof(Gold)));
        restored.RemoveItem(restored.Backpack);
    }

    [Fact]
    public void FeatureConstructionAndRollback_OwnOnlyItsFourEntities_NotTheTreasure()
    {
        var chest = Track(new RootboundCellarChest());
        chest.MoveToWorld(new Point3D(109, 108, -25), Map.Felucca);
        var gold = chest.GetAmount(typeof(Gold));
        var features = RootboundCellarFeature.Create(Map.Felucca, new Point3D(101, 100, 0),
            new Point3D(106, 105, -25), new Point3D(107, 105, -25), new Point3D(102, 100, 0),
            new Point3D(110, 110, -25), chest);
        var cover = Track(features.Cover);
        var hatch = Track(features.Hatch);
        var rune = Track(features.ReturnRune);
        var lich = Track(features.Lich);

        Assert.Equal(0x9AA, cover.ItemID);
        Assert.True(cover.Movable);
        Assert.False(cover.Locked);
        Assert.True(hatch.IsCovered);
        Assert.Equal(new Point3D(102, 100, 0), rune.PointDest);
        Assert.Equal(new Point3D(110, 110, -25), lich.Home);
        Assert.Equal(3, lich.RangeHome);
        Assert.Equal(gold, chest.GetAmount(typeof(Gold)));
        features.Delete();
        features.Delete();
        Assert.True(cover.Deleted);
        Assert.True(hatch.Deleted);
        Assert.True(rune.Deleted);
        Assert.True(lich.Deleted);
        Assert.Null(features.Cover);
        Assert.Null(features.Hatch);
        Assert.Null(features.ReturnRune);
        Assert.Null(features.Lich);
        Assert.False(chest.Deleted);
        Assert.Equal(gold, chest.GetAmount(typeof(Gold)));
    }

    [Fact]
    public void FeatureRejectsGenericChestsAndRecursiveSurfaceReturn()
    {
        var hatch = new Point3D(101, 100, 0);
        var genericChest = Track(new EventDungeonChest());
        genericChest.MoveToWorld(new Point3D(109, 108, -25), Map.Felucca);
        Assert.Throws<ArgumentException>(() => RootboundCellarFeature.Create(Map.Felucca, hatch,
            new Point3D(106, 105, -25), new Point3D(107, 105, -25), new Point3D(102, 100, 0),
            new Point3D(110, 110, -25), genericChest));
        var chest = Track(new RootboundCellarChest());
        chest.MoveToWorld(genericChest.Location, Map.Felucca);
        Assert.Throws<ArgumentException>(() => RootboundCellarFeature.Create(Map.Felucca, hatch,
            new Point3D(106, 105, -25), new Point3D(107, 105, -25), hatch,
            new Point3D(110, 110, -25), chest));
    }

    private T Track<T>(T entity) where T : IEntity
    {
        _created.Add(entity);
        return entity;
    }

    private Mobile Actor(Point3D location)
    {
        var actor = Track(new Mobile { Player = true, Body = 0x190 });
        actor.MoveToWorld(location, Map.Felucca);
        return actor;
    }

    private static void Empty(Container container)
    {
        while (container.Items.Count > 0)
        {
            container.Items[0].Delete();
        }
    }

    private static int CountScrolls(Container container)
    {
        var count = 0;
        foreach (var item in container.Items)
        {
            if (item is SpellScroll)
            {
                count += item.Amount;
            }
        }

        return count;
    }

    private static void InitializeChest(RootboundCellarChest chest) =>
        typeof(RootboundCellarChest).GetMethod("InitializeLoot", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(chest, null);

    private static byte[] Snapshot(ISerializable entity)
    {
        var writer = new BufferWriter(true);
        entity.Serialize(writer);
        return writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();
    }

    private sealed class Walker : Mobile
    {
        public override bool CheckMovement(Direction direction, out int newZ)
        {
            newZ = Z;
            return true;
        }
    }
}
