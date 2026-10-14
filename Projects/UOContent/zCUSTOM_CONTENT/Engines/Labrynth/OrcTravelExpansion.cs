using System;
using System.IO;
using System.Text.Json;
using Server.Items;
using Server.Logging;

namespace Server.Engines.Labrynth;

public static class OrcTravelExpansion
{
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(OrcTravelExpansion));
    internal readonly record struct Route(Serial Serial, Point3D OriginalSource, Point3D OriginalDestination,
        Point3D Source, Point3D Destination, string Name);
    internal readonly record struct Footprint(Serial Serial, Point3D OriginalPosition, Point3D Position);
    internal sealed record Result(int MovedTeleporters, int MovedFootprints, Teleporter[] Teleporters, Static[] FootprintItems);
    internal static readonly Route[] Routes =
    [
        new((Serial)0x4000198Eu, new(1404, 1462, 0), new(5279, 1082, 2),
            new(1404, 1460, 0), new(5279, 1082, 2), "Galven Cave - Orc Fort"),
        new((Serial)0x4000198Fu, new(1405, 1462, 0), new(5280, 1082, 2),
            new(1405, 1460, 0), new(5280, 1082, 2), "Galven Cave - Orc Fort"),
        new((Serial)0x40007517u, new(5279, 1084, 0), new(1404, 1463, 0),
            new(5279, 1084, 0), new(1404, 1461, 0), "Orc Fort - Galven Cave"),
        new((Serial)0x40007518u, new(5280, 1084, 0), new(1405, 1463, 0),
            new(5280, 1084, 0), new(1405, 1461, 0), "Orc Fort - Galven Cave"),
        new((Serial)0x4000750Fu, new(1666, 1247, 0), new(5300, 981, 0),
            new(1666, 1247, 0), new(5284, 985, 20), "Orc Fort - Dungeon Entrance"),
        new((Serial)0x40007510u, new(1667, 1247, 0), new(5300, 981, 0),
            new(1667, 1247, 0), new(5284, 985, 20), "Orc Fort - Dungeon Entrance"),
        new((Serial)0x40007511u, new(5297, 981, 0), new(1666, 1247, 0),
            new(5284, 984, 20), new(1666, 1247, 0), "Dungeon - Return to Orc Fort"),
        new((Serial)0x40007512u, new(5298, 981, 0), new(1666, 1247, 0),
            new(5285, 984, 20), new(1666, 1247, 0), "Dungeon - Return to Orc Fort"),
        new((Serial)0x40007513u, new(5299, 981, 0), new(1666, 1247, 0),
            new(5284, 983, 20), new(1666, 1247, 0), "Dungeon - Return to Orc Fort"),
        new((Serial)0x40007514u, new(5300, 981, 0), new(1666, 1247, 0),
            new(5285, 983, 20), new(1666, 1247, 0), "Dungeon - Return to Orc Fort")
    ];
    internal static readonly Footprint[] Footprints =
    [
        new((Serial)0x40007519u, new(1405, 1469, 1), new(1405, 1469, 1)),
        new((Serial)0x4000751Au, new(1405, 1468, 1), new(1404, 1468, 1)),
        new((Serial)0x4000751Bu, new(1405, 1467, 1), new(1405, 1466, 1)),
        new((Serial)0x4000751Cu, new(1405, 1466, 1), new(1404, 1465, 1)),
        new((Serial)0x4000751Du, new(1405, 1465, 1), new(1405, 1463, 1)),
        new((Serial)0x4000751Eu, new(1405, 1464, 1), new(1404, 1462, 1)),
        new((Serial)0x4000751Fu, new(1405, 1463, 1), new(1405, 1461, 1))
    ];

    public static void Initialize()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data");
        if (!File.Exists(Path.Combine(directory, "orc-travel-expansion.pending")))
        {
            return;
        }
        var resultPath = Path.Combine(directory, "orc-travel-expansion-result.json");
        try
        {
            var result = Ensure();
            var routes = new object[Routes.Length];
            for (var index = 0; index < routes.Length; index++)
            {
                var item = result.Teleporters[index];
                routes[index] = new
                {
                    Serial = item.Serial.Value,
                    Source = new { item.X, item.Y, item.Z, Map = "Felucca" },
                    Destination = new { item.PointDest.X, item.PointDest.Y, item.PointDest.Z, Map = "Felucca" }
                };
            }
            var footprints = new object[Footprints.Length];
            for (var index = 0; index < footprints.Length; index++)
            {
                var item = result.FootprintItems[index];
                footprints[index] = new { Serial = item.Serial.Value, item.X, item.Y, item.Z, item.ItemID, item.Hue };
            }
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new
            {
                Success = true, UpdatedTeleporters = result.MovedTeleporters, result.MovedFootprints,
                Routes = routes, Footprints = footprints, AwaitingWorldSave = true
            }, new JsonSerializerOptions { WriteIndented = true }));
            _logger.Information("Verified orc travel expansion; updated teleporters={Teleporters}, footprints={Footprints}; awaiting world save",
                result.MovedTeleporters, result.MovedFootprints);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Orc travel expansion refused; authorization remains pending");
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new { Success = false, Error = exception.Message }));
        }
    }

    internal static Result Ensure()
    {
        var teleporters = new Teleporter[Routes.Length];
        var footprintItems = new Static[Footprints.Length];
        var original = true;
        var installed = true;
        for (var index = 0; index < Routes.Length; index++)
        {
            var route = Routes[index];
            if (World.FindItem(route.Serial) is not Teleporter item || item.GetType() != typeof(Teleporter) ||
                item.Map != Map.Felucca || item.Parent != null || item.Name != route.Name ||
                item.MapDest != Map.Felucca || item.Flags != TeleporterFlags.Active || item.Visible || item.Movable ||
                item.Delay != TimeSpan.Zero || item.SoundID != 0)
            {
                throw new InvalidOperationException($"Pinned travel teleporter {route.Serial} has unexpected state.");
            }
            original &= item.Location == route.OriginalSource && item.PointDest == route.OriginalDestination;
            installed &= item.Location == route.Source && item.PointDest == route.Destination;
            teleporters[index] = item;
            CheckTeleporterSpot(route.OriginalSource, item);
            CheckTeleporterSpot(route.Source, item);
            if (!Map.Felucca.CanFit(route.Source, 16, true, false) || !Map.Felucca.CanFit(route.Destination, 16, true, false))
            {
                throw new InvalidOperationException($"Travel route {route.Serial} needs supported source and destination tiles.");
            }
        }
        for (var index = 0; index < Footprints.Length; index++)
        {
            var footprint = Footprints[index];
            if (World.FindItem(footprint.Serial) is not Static item || item.GetType() != typeof(Static) ||
                item.Map != Map.Felucca || item.Parent != null || item.Name != OrcCaveFootsteps.TrailName ||
                item.ItemID != OrcCaveFootsteps.ArtId || item.Hue != 0 || item.Movable || !item.Visible ||
                item.ItemData.Surface || item.ItemData.Impassable || item.ItemData.CalcHeight != 0)
            {
                throw new InvalidOperationException($"Pinned cave footprint {footprint.Serial} has unexpected state.");
            }
            original &= item.Location == footprint.OriginalPosition;
            installed &= item.Location == footprint.Position;
            footprintItems[index] = item;
            if (!Map.Felucca.CanFit(footprint.Position.X, footprint.Position.Y, 0, 16, true, false))
            {
                throw new InvalidOperationException($"Cave footprint {footprint.Serial} needs supported walking ground.");
            }
        }
        foreach (var footprint in Footprints)
        {
            CheckFootprintSpot(footprint.OriginalPosition, footprintItems);
            CheckFootprintSpot(footprint.Position, footprintItems);
        }
        if (!original && !installed)
        {
            throw new InvalidOperationException("Travel expansion requires a complete original or complete installed set; mixed state was found.");
        }
        if (installed)
        {
            return new Result(0, 0, teleporters, footprintItems);
        }

        var movedFootprints = 0;
        try
        {
            for (var index = 0; index < Routes.Length; index++)
            {
                teleporters[index].PointDest = Routes[index].Destination;
                teleporters[index].MoveToWorld(Routes[index].Source, Map.Felucca);
            }
            for (var index = 0; index < Footprints.Length; index++)
            {
                if (Footprints[index].OriginalPosition != Footprints[index].Position)
                {
                    footprintItems[index].MoveToWorld(Footprints[index].Position, Map.Felucca);
                    movedFootprints++;
                }
            }
        }
        catch
        {
            for (var index = 0; index < Routes.Length; index++)
            {
                teleporters[index].PointDest = Routes[index].OriginalDestination;
                teleporters[index].MoveToWorld(Routes[index].OriginalSource, Map.Felucca);
            }
            for (var index = 0; index < Footprints.Length; index++)
            {
                footprintItems[index].MoveToWorld(Footprints[index].OriginalPosition, Map.Felucca);
            }
            throw;
        }
        return new Result(Routes.Length, movedFootprints, teleporters, footprintItems);
    }

    private static void CheckTeleporterSpot(Point3D position, Teleporter expected)
    {
        foreach (var item in Map.Felucca.GetItemsAt<Teleporter>(position))
        {
            if (item != expected)
            {
                throw new InvalidOperationException($"Unexpected teleporter at travel endpoint {position}.");
            }
        }
    }

    private static void CheckFootprintSpot(Point3D position, Static[] footprints)
    {
        foreach (var item in Map.Felucca.GetItemsAt<Item>(position))
        {
            if (item.Name != OrcCaveFootsteps.TrailName && item.ItemID != OrcCaveFootsteps.ArtId)
            {
                continue;
            }
            var managed = false;
            foreach (var footprint in footprints)
            {
                if (item == footprint)
                {
                    managed = true;
                    break;
                }
            }
            if (!managed)
            {
                throw new InvalidOperationException($"Unexpected footprint decoration at {position}.");
            }
        }
    }
}
