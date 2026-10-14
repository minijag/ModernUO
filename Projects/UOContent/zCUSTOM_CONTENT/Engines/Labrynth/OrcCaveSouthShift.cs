using System;
using System.IO;
using System.Text.Json;
using Server.Items;
using Server.Logging;

namespace Server.Engines.Labrynth;

public static class OrcCaveSouthShift
{
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(OrcCaveSouthShift));
    internal const string RouteName = "Orc Fort - Galven Cave";
    internal readonly record struct Route(Serial Serial, Point3D Source, Point3D Destination);
    internal sealed record Result(int Moved, Teleporter[] Teleporters);
    internal static readonly Route[] Routes =
    [
        new((Serial)0x40007517u, new(5279, 1084, 0), new(1404, 1463, 0)),
        new((Serial)0x40007518u, new(5280, 1084, 0), new(1405, 1463, 0))
    ];

    public static void Initialize()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data");
        if (!File.Exists(Path.Combine(directory, "orc-cave-south-shift.pending")))
        {
            return;
        }
        var resultPath = Path.Combine(directory, "orc-cave-south-shift-result.json");
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
                Success = true, result.Moved, Routes = routes, AwaitingWorldSave = true
            }, new JsonSerializerOptions { WriteIndented = true }));
            _logger.Information("Verified orc-cave south-shift pair; moved={Moved}; awaiting world save", result.Moved);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Orc-cave south shift refused; authorization remains pending");
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new { Success = false, Error = exception.Message }));
        }
    }

    internal static Result Ensure()
    {
        var teleporters = new Teleporter[Routes.Length];
        var oldCount = 0;
        for (var index = 0; index < Routes.Length; index++)
        {
            var route = Routes[index];
            var original = new Point3D(route.Source.X, 1083, 0);
            if (World.FindItem(route.Serial) is not Teleporter teleporter ||
                !IsExactRoute(teleporter, route) ||
                teleporter.Location != original && teleporter.Location != route.Source)
            {
                throw new InvalidOperationException($"Pinned orc-cave return teleporter {route.Serial} has unexpected state.");
            }
            teleporters[index] = teleporter;
            if (teleporter.Location == original)
            {
                oldCount++;
            }
            foreach (var candidate in Map.Felucca.GetItemsAt<Teleporter>(original))
            {
                if (candidate != teleporter)
                {
                    throw new InvalidOperationException($"Conflicting teleporter at the original orc-cave pad {original}.");
                }
            }
            foreach (var candidate in Map.Felucca.GetItemsAt<Teleporter>(route.Source))
            {
                if (candidate != teleporter)
                {
                    throw new InvalidOperationException($"Conflicting teleporter at the shifted orc-cave pad {route.Source}.");
                }
            }
            if (!Map.Felucca.CanFit(route.Source, 16, true, false) ||
                !Map.Felucca.CanFit(route.Destination, 16, true, false))
            {
                throw new InvalidOperationException($"Shifted orc-cave pad {route.Source} needs walkable source and destination support.");
            }
        }
        if (oldCount != 0 && oldCount != Routes.Length)
        {
            throw new InvalidOperationException("The orc-cave return pair has mixed original and shifted positions.");
        }
        if (oldCount == 0)
        {
            return new Result(0, teleporters);
        }

        try
        {
            for (var index = 0; index < Routes.Length; index++)
            {
                teleporters[index].MoveToWorld(Routes[index].Source, Map.Felucca);
            }
        }
        catch
        {
            for (var index = 0; index < Routes.Length; index++)
            {
                teleporters[index].MoveToWorld(new Point3D(Routes[index].Source.X, 1083, 0), Map.Felucca);
            }
            throw;
        }
        return new Result(Routes.Length, teleporters);
    }

    private static bool IsExactRoute(Teleporter teleporter, Route route) =>
        teleporter.GetType() == typeof(Teleporter) && teleporter.Map == Map.Felucca && teleporter.Parent == null &&
        teleporter.Name == RouteName && teleporter.PointDest == route.Destination && teleporter.MapDest == Map.Felucca &&
        teleporter.Flags == TeleporterFlags.Active && !teleporter.Visible && !teleporter.Movable &&
        teleporter.Delay == TimeSpan.Zero && teleporter.SoundID == 0;
}
