using System;
using System.IO;
using System.Text.Json;
using Server.Items;
using Server.Logging;

namespace Server.Engines.Labrynth;

public static class OrcFortTeleporters
{
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(OrcFortTeleporters));
    internal readonly record struct Route(Point3D Source, Point3D Destination, string Name);
    internal static readonly Route[] Routes =
    [
        new(new(1666, 1247, 0), new(5300, 981, 0), "Orc Fort - Dungeon Entrance"),
        new(new(1667, 1247, 0), new(5300, 981, 0), "Orc Fort - Dungeon Entrance"),
        new(new(5297, 981, 0), new(1666, 1247, 0), "Dungeon - Return to Orc Fort"),
        new(new(5298, 981, 0), new(1666, 1247, 0), "Dungeon - Return to Orc Fort"),
        new(new(5299, 981, 0), new(1666, 1247, 0), "Dungeon - Return to Orc Fort"),
        new(new(5300, 981, 0), new(1666, 1247, 0), "Dungeon - Return to Orc Fort")
    ];

    public static void Initialize()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data");
        if (!File.Exists(Path.Combine(directory, "orc-fort-teleporters.pending")))
        {
            return;
        }

        var resultPath = Path.Combine(directory, "orc-fort-teleporters-result.json");
        try
        {
            var teleporters = Ensure(out var created);
            var routes = new object[Routes.Length];
            for (var index = 0; index < routes.Length; index++)
            {
                var route = Routes[index];
                routes[index] = new
                {
                    Serial = teleporters[index].Serial.Value,
                    Source = new { route.Source.X, route.Source.Y, route.Source.Z, Map = "Felucca" },
                    Destination = new { route.Destination.X, route.Destination.Y, route.Destination.Z, Map = "Felucca" }
                };
            }
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new
            {
                Success = true,
                Created = created,
                Routes = routes,
                AwaitingWorldSave = true
            }, new JsonSerializerOptions { WriteIndented = true }));
            _logger.Information("Verified {Count} orc-fort teleporters; created={Created}; awaiting world save",
                teleporters.Length, created);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Orc-fort teleporter installation refused; authorization remains pending");
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new { Success = false, Error = exception.Message }));
        }
    }

    internal static Teleporter[] Ensure(out int created)
    {
        created = 0;
        var teleporters = new Teleporter[Routes.Length];
        for (var index = 0; index < Routes.Length; index++)
        {
            var route = Routes[index];
            foreach (var teleporter in Map.Felucca.GetItemsAt<Teleporter>(route.Source))
            {
                if (teleporters[index] != null || !IsExactRoute(teleporter, route))
                {
                    throw new InvalidOperationException($"Conflicting or duplicate orc-fort teleporter at {route.Source}.");
                }
                teleporters[index] = teleporter;
            }
            if (!Map.Felucca.CanFit(route.Source, 16, true, false) ||
                !Map.Felucca.CanFit(route.Destination, 16, true, false))
            {
                throw new InvalidOperationException($"Orc-fort route at {route.Source} needs walkable source and destination support.");
            }
        }

        var added = new bool[Routes.Length];
        try
        {
            for (var index = 0; index < Routes.Length; index++)
            {
                if (teleporters[index] != null)
                {
                    continue;
                }
                var route = Routes[index];
                var teleporter = new Teleporter(route.Destination, Map.Felucca) { Name = route.Name };
                teleporters[index] = teleporter;
                added[index] = true;
                teleporter.MoveToWorld(route.Source, Map.Felucca);
                created++;
            }
            return teleporters;
        }
        catch
        {
            for (var index = 0; index < teleporters.Length; index++)
            {
                if (added[index])
                {
                    teleporters[index].Delete();
                }
            }
            created = 0;
            throw;
        }
    }

    private static bool IsExactRoute(Teleporter teleporter, Route route) =>
        teleporter.GetType() == typeof(Teleporter) && teleporter.Location == route.Source &&
        teleporter.Name == route.Name && teleporter.PointDest == route.Destination && teleporter.MapDest == Map.Felucca &&
        teleporter.Flags == TeleporterFlags.Active && !teleporter.Visible && !teleporter.Movable &&
        teleporter.Delay == TimeSpan.Zero && teleporter.SoundID == 0;
}
