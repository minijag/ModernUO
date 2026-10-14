using System;
using Server.Items;

namespace Server.Engines.ModularDungeon;

public static class RootboundCellarFeature
{
    public static Features Create(
        Map map,
        Point3D hatch,
        Point3D entry,
        Point3D returnRune,
        Point3D surfaceReturn,
        Point3D lichPoint,
        EventDungeonChest treasureChest
    )
    {
        if (map == null || map == Map.Internal || hatch.Z != 0 || entry.Z != -25 ||
            returnRune.Z != entry.Z || lichPoint.Z != entry.Z || surfaceReturn.Z != hatch.Z || surfaceReturn == hatch)
        {
            throw new ArgumentException("The cellar needs Z -25 destinations and a distinct Z 0 surface return.");
        }

        if (treasureChest is not RootboundCellarChest { Deleted: false, Parent: null, LootInitialized: true } ||
            treasureChest.Map != map || treasureChest.Z != entry.Z)
        {
            throw new ArgumentException("Place a newly constructed RootboundCellarChest in the cellar first.", nameof(treasureChest));
        }

        var features = new Features();

        try
        {
            features.Cover = new WoodenBox { Movable = true, Locked = false };
            features.Cover.MoveToWorld(hatch, map);
            features.Hatch = new EventDungeonHiddenHatch(map, hatch, entry, features.Cover);
            features.ReturnRune = new RootboundCellarReturnRune(surfaceReturn, map, hatch);
            features.ReturnRune.MoveToWorld(returnRune, map);
            features.Lich = new RootboundCellarLich { Home = lichPoint, HomeMap = map, RangeHome = 3 };
            features.Lich.MoveToWorld(lichPoint, map);
            return features;
        }
        catch
        {
            features.Delete();
            throw;
        }
    }

    public sealed record Features
    {
        public WoodenBox Cover { get; internal set; }
        public EventDungeonHiddenHatch Hatch { get; internal set; }
        public RootboundCellarReturnRune ReturnRune { get; internal set; }
        public RootboundCellarLich Lich { get; internal set; }

        public void Delete()
        {
            Hatch?.Delete();
            Hatch = null;
            ReturnRune?.Delete();
            ReturnRune = null;
            Lich?.Delete();
            Lich = null;
            Cover?.Delete();
            Cover = null;
        }
    }
}
