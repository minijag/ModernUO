using System;
using System.Collections.Generic;
using Server.Items;
using Server.Mobiles;

namespace Server.Engines.ModularDungeon;

public static class ExtractionDungeonReset
{
    public static Func<EventDungeonChest, bool> ManagedChestLookup { private get; set; }

    public static bool IsManagedChest(EventDungeonChest chest) => ManagedChestLookup?.Invoke(chest) == true;

    public sealed record MonsterSpawn(string ModuleId, string CreatureType, Point3D Position, Point3D Home, int RangeHome = 3);

    public sealed record Context(
        ModularDungeonDeployment.Manifest Manifest,
        Map Map,
        EventDungeonDoor[] Doors,
        EventDungeonChest[] Chests,
        RootboundCellarFeature.Features Features,
        IReadOnlyList<BaseCreature> OwnedCreatures,
        IReadOnlyList<Item> OwnedLoot,
        Container RecoveryStorage,
        MonsterSpawn[] Spawns,
        Point3D[] PlayerStarts,
        Action<BaseCreature> AddOwnedCreature,
        Action<BaseCreature> RemoveOwnedCreature,
        Action<Item> AddOwnedLoot,
        Action<Item> RemoveOwnedLoot,
        Action<Item> AddManagedItem,
        Action<RootboundCellarFeature.Features> FeaturesRestored = null
    );

    public static IExtractionEventResetSession CreateSession(Context context)
    {
        ValidateContext(context);
        return new Session(context);
    }

    private static void ValidateContext(Context context)
    {
        Require(context is not null && context.Map == Map.Felucca, "The extraction reset requires Felucca.");
        var manifest = context.Manifest;
        Require(ExtractionDungeonLayout.IsSupportedManifest(manifest) &&
                manifest.Origin?.Point == new Point3D(5256, 808, 0) && manifest.Bounds?.Rectangle == new Rectangle2D(5248, 800, 136, 136),
            "The extraction reset requires a pinned installed dungeon.");
        Require(manifest.Doors?.Length == 71 && context.Doors?.Length == 71 &&
                manifest.Chests?.Length == 100 && context.Chests?.Length == 100 &&
                manifest.Modules?.Length == 25 && context.PlayerStarts?.Length == 25 &&
                context.Spawns is { Length: > 0 and <= 128 }, "The extraction reset ownership or spawn counts are invalid.");
        Require(context.Features is { Hatch.Deleted: false, ReturnRune.Deleted: false } &&
                manifest.HiddenCellar is not null, "The owned Rootbound transports are missing.");
        Require(context.OwnedCreatures is not null && context.OwnedLoot is not null &&
                context.RecoveryStorage is { Deleted: false, Parent: null, Visible: false, Map: not null } &&
                context.RecoveryStorage.Map == Map.Internal, "A private recovery store and persistent ownership lists are required.");
        Require(context.AddOwnedCreature is not null && context.RemoveOwnedCreature is not null &&
                context.AddOwnedLoot is not null && context.RemoveOwnedLoot is not null && context.AddManagedItem is not null,
            "The reset needs immediate persistent ownership callbacks.");
    }

    internal static BaseCreature CreateCreature(string type) => type switch
    {
        "Skeleton" => new Skeleton(),
        "Wraith" => new Wraith(),
        "Mummy" => new Mummy(),
        "GiantSpider" => new GiantSpider(),
        "DireWolf" => new DireWolf(),
        "TimberWolf" => new TimberWolf(),
        "SilverSerpent" => new SilverSerpent(),
        "Gazer" => new Gazer(),
        "RootboundCellarLich" => new RootboundCellarLich(),
        _ => throw new InvalidOperationException("An extraction monster type is not supported.")
    };

    internal static void PrepareCreature(BaseCreature creature, Map map, Point3D home, int rangeHome)
    {
        // SetHits applies the pre-AOS scaling again; the already resolved maximum is the baseline.
        creature.HitsMaxSeed = checked(creature.HitsMax * 2);
        creature.Hits = creature.HitsMax;
        // Admission anchors must remain clear while the main-loop reset and player placement finish.
        creature.Frozen = true;
        creature.Tamable = false;
        creature.Home = home;
        creature.HomeMap = map;
        creature.RangeHome = Math.Clamp(rangeHome, 1, 3);
        creature.Spawner = null;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class Session : IExtractionEventResetSession
    {
        private readonly Context _context;
        private readonly HashSet<Item> _managedItems = new();
        private readonly HashSet<BaseCreature> _ownedCreatures = new();
        private readonly HashSet<Item> _ownedLoot = new();
        private readonly BaseCreature[] _previousCreatures;
        private readonly Item[] _previousLoot;
        private readonly HashSet<Point3D> _spawnPositions = new();
        private int _phase;
        private int _cursor;

        public Session(Context context)
        {
            _context = context;
            _previousCreatures = new BaseCreature[context.OwnedCreatures.Count];
            for (var i = 0; i < _previousCreatures.Length; i++)
            {
                _previousCreatures[i] = context.OwnedCreatures[i];
                if (_previousCreatures[i] is { Deleted: false } creature)
                {
                    Require(_ownedCreatures.Add(creature), "An owned extraction creature is duplicated.");
                }
            }

            if (context.Features.Lich is { Deleted: false } lich)
            {
                _ownedCreatures.Add(lich);
            }

            _previousLoot = new Item[context.OwnedLoot.Count];
            for (var i = 0; i < _previousLoot.Length; i++)
            {
                _previousLoot[i] = context.OwnedLoot[i];
                if (_previousLoot[i] is { Deleted: false } item)
                {
                    Require(_ownedLoot.Add(item), "An owned extraction loot item is duplicated.");
                }
            }

            foreach (var door in context.Doors)
            {
                Require(door is { Deleted: false, Parent: null } && door.GetType() == typeof(EventDungeonDoor) &&
                        _managedItems.Add(door), "An owned extraction door is missing, nested or duplicated.");
            }

            for (var i = 0; i < context.Chests.Length; i++)
            {
                var chest = context.Chests[i];
                var expectedType = context.Manifest.Chests[i].Type == "RootboundCellarChest" ? typeof(RootboundCellarChest) : typeof(EventDungeonChest);
                Require(chest is { Deleted: false, Parent: null } && chest.GetType() == expectedType &&
                        _managedItems.Add(chest), "An owned extraction chest is missing, nested or duplicated.");
            }

            _managedItems.Add(context.Features.Hatch);
            _managedItems.Add(context.Features.ReturnRune);
            if (context.Features.Cover is { Deleted: false } cover)
            {
                _managedItems.Add(cover);
            }
        }

        public bool Complete => _phase == 11;

        public void Process(int operationBudget)
        {
            var budget = Math.Clamp(operationBudget, 0, 8);
            while (budget-- > 0 && !Complete)
            {
                ProcessOne();
            }
        }

        private void ProcessOne()
        {
            switch (_phase)
            {
                case 0:
                {
                    ValidateDoor(_cursor);
                    Advance(_context.Doors.Length);
                    break;
                }
                case 1:
                {
                    ValidateChest(_cursor);
                    Advance(_context.Chests.Length);
                    break;
                }
                case 2:
                {
                    ValidateSpawn(_context.Spawns[_cursor]);
                    Advance(_context.Spawns.Length);
                    break;
                }
                case 3:
                {
                    var position = _context.PlayerStarts[_cursor];
                    Require(position == _context.Manifest.Modules[_cursor].Start.Point, "An admitted player start differs from the pinned module.");
                    ValidateWalkingPoint(position);
                    Advance(_context.PlayerStarts.Length);
                    break;
                }
                case 4:
                {
                    ValidateFeatures();
                    _phase++;
                    _cursor = 0;
                    break;
                }
                case 5:
                {
                    if (_cursor < _previousCreatures.Length)
                    {
                        var creature = _previousCreatures[_cursor];
                        creature?.Delete();
                        _context.RemoveOwnedCreature(creature);
                        _cursor++;
                    }
                    else
                    {
                        if (_context.Features.Lich is { Deleted: false } lich)
                        {
                            lich.Delete();
                            _context.RemoveOwnedCreature(lich);
                        }

                        _context.Features.Lich = null;
                        _phase++;
                        _cursor = 0;
                    }

                    break;
                }
                case 6:
                {
                    _context.Doors[_cursor].ResetForMatch(_context.Map, _context.Manifest.Doors[_cursor]);
                    Advance(_context.Doors.Length);
                    break;
                }
                case 7:
                {
                    ResetChest(_cursor);
                    Advance(_context.Chests.Length);
                    break;
                }
                case 8:
                {
                    if (_cursor < _previousLoot.Length)
                    {
                        _context.RemoveOwnedLoot(_previousLoot[_cursor++]);
                    }
                    else
                    {
                        _phase++;
                        _cursor = 0;
                    }

                    break;
                }
                case 9:
                {
                    RestoreFeatures();

                    _phase++;
                    _cursor = 0;
                    break;
                }
                case 10:
                {
                    Spawn(_context.Spawns[_cursor]);
                    Advance(_context.Spawns.Length);
                    break;
                }
            }
        }

        private void Advance(int count)
        {
            if (++_cursor == count)
            {
                _cursor = 0;
                _phase++;
            }
        }

        private void ValidateDoor(int index)
        {
            var door = _context.Doors[index];
            var definition = _context.Manifest.Doors[index];
            Require(door.Map == _context.Map && door.ClosedId == definition.ClosedId && door.OpenedId == definition.OpenedId &&
                    door.Offset == BaseDoor.GetOffset(Enum.Parse<DoorFacing>(definition.Facing)) &&
                    door.Location == (door.Open ? definition.Opened.Point : definition.Closed.Point),
                "An extraction door differs from its owned original placement.");
            ValidateCellOccupancy(definition.Closed.Point);
            ValidateCellOccupancy(definition.Opened.Point);
        }

        private void ValidateChest(int index)
        {
            var chest = _context.Chests[index];
            var definition = _context.Manifest.Chests[index];
            Require(chest.Map == _context.Map && chest.ItemID == definition.ItemId && chest.Location == definition.Position.Point,
                "An extraction chest differs from its owned original placement.");
            Require(chest.Items.Count <= 256, "An extraction chest has an unsafe reset workload.");
            ValidateCellOccupancy(definition.Position.Point);
            ValidateWalkingPoint(definition.Approach.Point);
        }

        private void ValidateSpawn(MonsterSpawn spawn)
        {
            Require(spawn is not null && SupportedType(spawn.CreatureType) && spawn.CreatureType != "RootboundCellarLich" &&
                    spawn.Home == spawn.Position && spawn.RangeHome is >= 1 and <= 3 &&
                    spawn.Position.Z is -25 or 0 or 25 && _context.Manifest.Bounds.Rectangle.Contains(spawn.Position) &&
                    _spawnPositions.Add(spawn.Position), "A monster spawn definition is invalid or duplicated.");
            var matchedModule = false;
            foreach (var module in _context.Manifest.Modules)
            {
                if (module.Id == spawn.ModuleId)
                {
                    matchedModule = true;
                    break;
                }
            }

            Require(matchedModule, "A monster spawn names an unknown module.");
            foreach (var start in _context.PlayerStarts)
            {
                Require(start.Z != spawn.Position.Z || Math.Max(Math.Abs(start.X - spawn.Position.X), Math.Abs(start.Y - spawn.Position.Y)) >= 5,
                    "A monster spawn is too close to an admitted player start.");
            }

            ValidateWalkingPoint(spawn.Position);
            Require(HasWalkingNeighbor(spawn.Position), "A monster spawn has no safe line-of-sight walking neighbor.");
        }

        private void ValidateFeatures()
        {
            var cellar = _context.Manifest.HiddenCellar;
            var features = _context.Features;
            Require(features.Hatch.Map == _context.Map && features.ReturnRune.Map == _context.Map &&
                    features.Hatch.Location == cellar.Hatch.Point && features.Hatch.PointDest == cellar.Entry.Point &&
                    features.ReturnRune.Location == cellar.ReturnRune.Point && features.ReturnRune.PointDest == cellar.SurfaceReturn.Point &&
                    features.Hatch.CoverOrigin == cellar.Hatch.Point && features.Hatch.CoverMap == _context.Map &&
                    (features.Hatch.CoverBox is null || features.Hatch.CoverBox.Deleted || features.Hatch.CoverBox == features.Cover),
                "The owned Rootbound transports differ from the original placement.");
            ValidateCellOccupancy(cellar.Hatch.Point);
            ValidateWalkingPoint(cellar.Entry.Point);
            ValidateWalkingPoint(cellar.ReturnRune.Point);
            ValidateWalkingPoint(cellar.SurfaceReturn.Point);
            ValidateWalkingPoint(cellar.CreaturePosition.Point);
            foreach (var creature in _ownedCreatures)
            {
                Require(!creature.Controlled && !creature.Summoned && creature.Spawner == null,
                    "A registered event creature is controlled, summoned or belongs to a spawner.");
            }
        }

        private void ValidateWalkingPoint(Point3D position)
        {
            Require(_context.Map.CanFit(position, 16, true, false), "An extraction walking point has no native clearance or support.");
            Require(!HasWaterAtFeet(position), "An extraction walking point is on exposed water.");
            ValidateCellOccupancy(position);
        }

        private void ValidateCellOccupancy(Point3D position)
        {
            foreach (var mobile in _context.Map.GetMobilesInRange<Mobile>(position, 0))
            {
                if (mobile.Z + 16 > position.Z && position.Z + 16 > mobile.Z)
                {
                    Require(mobile is BaseCreature creature && _ownedCreatures.Contains(creature),
                        "An unrelated mobile or player occupies an extraction reset point.");
                }
            }

            foreach (var item in _context.Map.GetItemsInRange<Item>(position, 0))
            {
                var data = item.ItemData;
                if ((data.Surface || data.Impassable || item.BlocksFit) &&
                    item.Z + data.CalcHeight > position.Z && position.Z + 16 > item.Z)
                {
                    Require(_managedItems.Contains(item), "An unrelated item occupies an extraction reset point.");
                }
            }
        }

        private bool HasWalkingNeighbor(Point3D point)
        {
            ReadOnlySpan<Point2D> offsets = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];
            foreach (var offset in offsets)
            {
                var neighbor = new Point3D(point.X + offset.X, point.Y + offset.Y, point.Z);
                if (_context.Map.CanFit(neighbor, 16, true, false) && !HasWaterAtFeet(neighbor) &&
                    _context.Map.LineOfSight(new Point3D(point.X, point.Y, point.Z + 14), new Point3D(neighbor.X, neighbor.Y, neighbor.Z + 14)))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasWaterAtFeet(Point3D point)
        {
            var land = _context.Map.Tiles.GetLandTile(point.X, point.Y);
            if (land.Z == point.Z && (TileData.LandTable[land.ID & TileData.MaxLandValue].Flags & TileFlag.Wet) != 0)
            {
                return true;
            }

            foreach (var tile in _context.Map.Tiles.GetStaticAndMultiTiles(point.X, point.Y))
            {
                if (tile.Z == point.Z && (TileData.ItemTable[tile.ID & TileData.MaxItemValue].Flags & TileFlag.Wet) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void ResetChest(int index)
        {
            var chest = _context.Chests[index];
            var definition = _context.Manifest.Chests[index];
            ValidateCellOccupancy(definition.Position.Point);
            chest.ResetForMatch(_context.Map, definition.Position.Point, definition.ItemId);
            for (var i = chest.Items.Count - 1; i >= 0; i--)
            {
                var item = chest.Items[i];
                if (_ownedLoot.Contains(item))
                {
                    item.Delete();
                }
                else
                {
                    // Existing contents have no safe provenance; preserve them outside the public event.
                    _context.RecoveryStorage.DropItem(item);
                }
            }

            FillChest(chest);
            foreach (var item in chest.Items)
            {
                _context.AddOwnedLoot(item);
            }
        }

        private static void FillChest(EventDungeonChest chest)
        {
            var packs = chest is RootboundCellarChest ? 3 : 1;
            // LootPack resolves geographic equipment pools through a Mobile even for pre-AOS packs.
            var source = new Mobile();
            try
            {
                for (var i = 0; i < packs; i++)
                {
                    LootPack.FilthyRich.Generate(source, chest, true, 0);
                    LootPack.FilthyRich.Generate(source, chest, false, 0);
                    LootPack.MedScrolls.Generate(source, chest, false, 0);
                    LootPack.Potions.Generate(source, chest, false, 0);
                    LootPack.Gems.Generate(source, chest, false, 0);
                }
            }
            finally
            {
                source.Delete();
            }

            foreach (var type in Loot.RegTypes)
            {
                var reagent = type.CreateInstance<Item>();
                reagent.Amount = Utility.RandomMinMax(10 * packs, 20 * packs);
                chest.DropItem(reagent);
            }
        }

        private void RestoreFeatures()
        {
            var definition = _context.Manifest.HiddenCellar;
            var features = _context.Features;
            ValidateCellOccupancy(definition.Hatch.Point);
            Require(_context.Map.CanFit(definition.CreaturePosition.Point, 16, true, true),
                "The hidden lich spawn became obstructed during extraction reset.");
            var cover = features.Cover;
            if (cover is null || cover.Deleted)
            {
                cover = new WoodenBox { Movable = true, Locked = false };
                _context.AddManagedItem(cover);
                features.Cover = cover;
            }

            cover.Movable = true;
            cover.Locked = false;
            cover.MoveToWorld(definition.Hatch.Point, _context.Map);
            features.Hatch.CoverBox = cover;
            features.Hatch.CoverOrigin = definition.Hatch.Point;
            features.Hatch.CoverMap = _context.Map;
            features.Hatch.PointDest = definition.Entry.Point;
            features.Hatch.MapDest = _context.Map;
            features.Hatch.Active = true;
            features.Hatch.MoveToWorld(definition.Hatch.Point, _context.Map);
            features.ReturnRune.PointDest = definition.SurfaceReturn.Point;
            features.ReturnRune.MapDest = _context.Map;
            features.ReturnRune.Active = true;
            features.ReturnRune.MoveToWorld(definition.ReturnRune.Point, _context.Map);
            var lich = (RootboundCellarLich)CreateCreature("RootboundCellarLich");
            PrepareCreature(lich, _context.Map, definition.CreaturePosition.Point, 3);
            _context.AddOwnedCreature(lich);
            features.Lich = lich;
            lich.MoveToWorld(definition.CreaturePosition.Point, _context.Map);
            _context.FeaturesRestored?.Invoke(features);
        }

        private void Spawn(MonsterSpawn spawn)
        {
            Require(_context.Map.CanFit(spawn.Position, 16, true, true), "A monster spawn became obstructed during extraction reset.");
            var creature = CreateCreature(spawn.CreatureType);
            try
            {
                PrepareCreature(creature, _context.Map, spawn.Home, spawn.RangeHome);
                _context.AddOwnedCreature(creature);
                creature.MoveToWorld(spawn.Position, _context.Map);
            }
            catch
            {
                creature.Delete();
                _context.RemoveOwnedCreature(creature);
                throw;
            }
        }

        private static bool SupportedType(string type) => type is
            "Skeleton" or "Wraith" or "Mummy" or "GiantSpider" or "DireWolf" or "TimberWolf" or "SilverSerpent" or "Gazer";
    }
}
