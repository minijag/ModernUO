using System;
using System.IO;
using System.Text.Json;
using Server.Items;
using Server.Logging;

namespace Server.Engines.Labrynth;

public static class OrcFortNorthRamp
{
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(OrcFortNorthRamp));
    internal readonly record struct Route(Serial Serial, Point3D OldPosition, Point3D Position,
        Point3D OldDestination, Point3D Destination, string Name);
    internal static readonly Route[] Routes =
    [
        new((Serial)0x40007511u, new(5284, 984, 20), new(5284, 985, 20),
            new(1666, 1247, 0), new(1666, 1247, 0), "Dungeon - Return to Orc Fort"),
        new((Serial)0x40007512u, new(5285, 984, 20), new(5285, 985, 20),
            new(1666, 1247, 0), new(1666, 1247, 0), "Dungeon - Return to Orc Fort"),
        new((Serial)0x4000750Fu, new(1666, 1247, 0), new(1666, 1247, 0),
            new(5284, 985, 20), new(5284, 984, 20), "Orc Fort - Dungeon Entrance"),
        new((Serial)0x40007510u, new(1667, 1247, 0), new(1667, 1247, 0),
            new(5284, 985, 20), new(5284, 984, 20), "Orc Fort - Dungeon Entrance")
    ];

    public static void Initialize()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data");
        if (!File.Exists(Path.Combine(directory, "orc-fort-north-ramp.pending")))
        {
            return;
        }
        try
        {
            var moved = Ensure();
            File.WriteAllText(Path.Combine(directory, "orc-fort-north-ramp-result.json"), JsonSerializer.Serialize(new
            {
                Success = true, UpdatedRoutes = moved, RemainingTeleporters = 2,
                Pads = new[] { new[] { 5284, 985, 20 }, new[] { 5285, 985, 20 } },
                IncomingArrival = new[] { 5284, 984, 20 }, AwaitingWorldSave = true
            }));
            _logger.Information("Verified fort north ramp travel; updated={Updated}, retained=2; awaiting world save", moved);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Fort north ramp travel refused; authorization remains pending");
            File.WriteAllText(Path.Combine(directory, "orc-fort-north-ramp-result.json"),
                JsonSerializer.Serialize(new { Success = false, Error = exception.Message }));
        }
    }

    internal static int Ensure()
    {
        var pads = new Teleporter[Routes.Length];
        var original = true;
        var installed = true;
        for (var index = 0; index < Routes.Length; index++)
        {
            var route = Routes[index];
            if (World.FindItem(route.Serial) is not Teleporter item || item.GetType() != typeof(Teleporter) ||
                item.Parent != null || item.Map != Map.Felucca || item.MapDest != Map.Felucca || item.Name != route.Name ||
                item.Flags != TeleporterFlags.Active || item.Visible || item.Movable ||
                item.Delay != TimeSpan.Zero || item.SoundID != 0)
            {
                throw new InvalidOperationException($"Pinned fort travel item {route.Serial} has unexpected state.");
            }
            original &= item.Location == route.OldPosition && item.PointDest == route.OldDestination;
            installed &= item.Location == route.Position && item.PointDest == route.Destination;
            pads[index] = item;
            if (!Map.Felucca.CanFit(route.Position, 16, true, false) || !Map.Felucca.CanFit(route.Destination, 16, true, false))
            {
                throw new InvalidOperationException("Fort travel endpoint has no native landing support.");
            }
        }
        if (!original && !installed)
        {
            throw new InvalidOperationException("Fort travel items have mixed or unexpected positions.");
        }
        foreach (var item in Map.Felucca.GetItemsInBounds<Teleporter>(new Rectangle2D(5282, 977, 5, 10)))
        {
            if (item != pads[0] && item != pads[1])
            {
                throw new InvalidOperationException("Unexpected teleporter in the fort entrance.");
            }
        }
        if (installed)
        {
            return 0;
        }
        try
        {
            for (var index = 0; index < Routes.Length; index++)
            {
                pads[index].PointDest = Routes[index].Destination;
                pads[index].MoveToWorld(Routes[index].Position, Map.Felucca);
            }
        }
        catch
        {
            for (var index = 0; index < Routes.Length; index++)
            {
                pads[index].PointDest = Routes[index].OldDestination;
                pads[index].MoveToWorld(Routes[index].OldPosition, Map.Felucca);
            }
            throw;
        }
        return Routes.Length;
    }
}
