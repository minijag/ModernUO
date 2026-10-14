using System;
using System.IO;
using System.Text.Json;
using Server.Items;
using Server.Logging;

namespace Server.Engines.Labrynth;

public static class OrcCaveRelocation
{
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(OrcCaveRelocation));
    internal readonly record struct Route(Point3D Source, Point3D Destination, string Name);
    internal sealed record Result(bool Changed, Teleporter[] Teleporters, uint[] RemovedSerials);
    internal static readonly Route[] OriginalEntrances =
    [
        new(new(1485, 1508, 1), new(5278, 1079, 2), null),
        new(new(1486, 1508, 1), new(5279, 1079, 2), null)
    ];
    internal static readonly Route[] OriginalReturns =
    [
        new(new(5278, 1080, 5), new(1485, 1509, 0), null),
        new(new(5279, 1080, 5), new(1486, 1509, 0), null),
        new(new(5278, 1081, 5), new(1485, 1509, 0), null),
        new(new(5279, 1081, 5), new(1486, 1509, 0), null)
    ];
    internal static readonly Route[] Routes =
    [
        new(new(1404, 1462, 0), new(5279, 1082, 2), "Galven Cave - Orc Fort"),
        new(new(1405, 1462, 0), new(5280, 1082, 2), "Galven Cave - Orc Fort"),
        new(new(5279, 1084, 0), new(1404, 1463, 0), "Orc Fort - Galven Cave"),
        new(new(5280, 1084, 0), new(1405, 1463, 0), "Orc Fort - Galven Cave")
    ];

    public static void Initialize()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data");
        if (!File.Exists(Path.Combine(directory, "orc-cave-relocation.pending")))
        {
            return;
        }
        var resultPath = Path.Combine(directory, "orc-cave-relocation-result.json");
        try
        {
            var result = Ensure();
            var routes = new object[Routes.Length];
            for (var index = 0; index < routes.Length; index++)
            {
                var route = Routes[index];
                routes[index] = new
                {
                    Serial = result.Teleporters[index].Serial.Value,
                    Source = new { route.Source.X, route.Source.Y, route.Source.Z, Map = "Felucca" },
                    Destination = new { route.Destination.X, route.Destination.Y, route.Destination.Z, Map = "Felucca" }
                };
            }
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new
            {
                Success = true,
                result.Changed,
                Moved = result.Changed ? 2 : 0,
                Created = result.Changed ? 2 : 0,
                result.RemovedSerials,
                Routes = routes,
                AwaitingWorldSave = true
            }, new JsonSerializerOptions { WriteIndented = true }));
            _logger.Information("Verified four relocated orc-cave routes; changed={Changed}; awaiting world save", result.Changed);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Orc-cave relocation refused; authorization remains pending");
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new { Success = false, Error = exception.Message }));
        }
    }

    internal static Result Ensure()
    {
        var entrances = FindRoutes(OriginalEntrances);
        var oldReturns = FindRoutes(OriginalReturns);
        var installed = FindRoutes(Routes);
        var originalComplete = AllPresent(entrances) && AllPresent(oldReturns) && AllAbsent(installed);
        var installedComplete = AllAbsent(entrances) && AllAbsent(oldReturns) && AllPresent(installed);
        if (!originalComplete && !installedComplete)
        {
            throw new InvalidOperationException("Orc-cave relocation requires either the complete original or complete installed routes; mixed or missing routes were found.");
        }
        foreach (var route in Routes)
        {
            var sourceFits = Map.Felucca.CanFit(route.Source, 16, true, false);
            var destinationFits = Map.Felucca.CanFit(route.Destination, 16, true, false);
            if (!sourceFits || !destinationFits)
            {
                throw new InvalidOperationException($"Orc-cave route at {route.Source} needs walkable support (source={sourceFits}, destination={destinationFits}).");
            }
            foreach (var teleporter in Map.Felucca.GetItemsAt<Teleporter>(route.Destination))
            {
                if (teleporter.Active && Math.Abs(teleporter.Z - route.Destination.Z) <= 16)
                {
                    throw new InvalidOperationException($"Orc-cave arrival at {route.Destination} has an active teleporter.");
                }
            }
        }
        if (installedComplete)
        {
            return new Result(false, installed, []);
        }

        var originalNames = new[] { entrances[0].Name, entrances[1].Name };
        var removedSerials = new uint[oldReturns.Length];
        for (var index = 0; index < oldReturns.Length; index++)
        {
            removedSerials[index] = oldReturns[index].Serial.Value;
        }
        try
        {
            for (var index = 2; index < Routes.Length; index++)
            {
                var route = Routes[index];
                installed[index] = new Teleporter(route.Destination, Map.Felucca) { Name = route.Name };
                installed[index].MoveToWorld(route.Source, Map.Felucca);
            }
            for (var index = 0; index < entrances.Length; index++)
            {
                var route = Routes[index];
                entrances[index].PointDest = route.Destination;
                entrances[index].Name = route.Name;
                entrances[index].MoveToWorld(route.Source, Map.Felucca);
                installed[index] = entrances[index];
            }
        }
        catch
        {
            for (var index = 2; index < installed.Length; index++)
            {
                installed[index]?.Delete();
            }
            for (var index = 0; index < entrances.Length; index++)
            {
                entrances[index].PointDest = OriginalEntrances[index].Destination;
                entrances[index].Name = originalNames[index];
                entrances[index].MoveToWorld(OriginalEntrances[index].Source, Map.Felucca);
            }
            throw;
        }
        foreach (var teleporter in oldReturns)
        {
            teleporter.Delete();
        }
        return new Result(true, installed, removedSerials);
    }

    private static Teleporter[] FindRoutes(Route[] routes)
    {
        var found = new Teleporter[routes.Length];
        for (var index = 0; index < routes.Length; index++)
        {
            var route = routes[index];
            foreach (var teleporter in Map.Felucca.GetItemsAt<Teleporter>(route.Source))
            {
                if (found[index] != null || !IsExactRoute(teleporter, route))
                {
                    throw new InvalidOperationException($"Conflicting or duplicate orc-cave teleporter at {route.Source}.");
                }
                found[index] = teleporter;
            }
        }
        return found;
    }

    private static bool IsExactRoute(Teleporter teleporter, Route route) =>
        teleporter.GetType() == typeof(Teleporter) && teleporter.Location == route.Source &&
        (route.Name == null || teleporter.Name == route.Name) &&
        teleporter.PointDest == route.Destination && teleporter.MapDest == Map.Felucca &&
        teleporter.Flags == TeleporterFlags.Active && !teleporter.Visible && !teleporter.Movable &&
        teleporter.Delay == TimeSpan.Zero && teleporter.SoundID == 0;

    private static bool AllPresent(Teleporter[] teleporters)
    {
        foreach (var teleporter in teleporters)
        {
            if (teleporter == null)
            {
                return false;
            }
        }
        return true;
    }

    private static bool AllAbsent(Teleporter[] teleporters)
    {
        foreach (var teleporter in teleporters)
        {
            if (teleporter != null)
            {
                return false;
            }
        }
        return true;
    }
}
