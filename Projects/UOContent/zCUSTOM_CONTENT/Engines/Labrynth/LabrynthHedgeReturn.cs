using System;
using System.IO;
using System.Text.Json;
using Server.Items;
using Server.Logging;

namespace Server.Engines.Labrynth;

public static class LabrynthHedgeReturn
{
    private static readonly ILogger _logger = LogFactory.GetLogger(typeof(LabrynthHedgeReturn));
    internal static Point3D Source { get; } = new(5249, 784, 0);
    internal static Point3D Destination { get; } = new(1873, 914, 0);
    internal const string RouteName = "Labrynth - Return to Hedge Maze";

    public static void Initialize()
    {
        var directory = Path.Combine(Core.BaseDirectory, "Data");
        if (!File.Exists(Path.Combine(directory, "labrynth-hedge-return.pending")))
        {
            return;
        }

        var resultPath = Path.Combine(directory, "labrynth-hedge-return-result.json");
        try
        {
            var teleporter = Ensure(out var created);
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new
            {
                Success = true,
                Created = created,
                Serial = teleporter.Serial.Value,
                Source = new { Source.X, Source.Y, Source.Z, Map = "Felucca" },
                Destination = new { Destination.X, Destination.Y, Destination.Z, Map = "Felucca" },
                AwaitingWorldSave = true
            }, new JsonSerializerOptions { WriteIndented = true }));
            _logger.Information("Verified Labrynth hedge return teleporter {Serial}; created={Created}; awaiting world save",
                teleporter.Serial, created);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Labrynth hedge return installation refused; authorization remains pending");
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new { Success = false, Error = exception.Message }));
        }
    }

    internal static Teleporter Ensure(out bool created)
    {
        created = false;
        Teleporter existing = null;
        foreach (var teleporter in Map.Felucca.GetItemsAt<Teleporter>(Source))
        {
            if (existing != null || !IsExactRoute(teleporter))
            {
                throw new InvalidOperationException("Conflicting or duplicate teleporter at the Labrynth hedge return source.");
            }
            existing = teleporter;
        }

        foreach (var teleporter in Map.Felucca.GetItemsAt<Teleporter>(Destination))
        {
            if (teleporter.Active && Math.Abs(teleporter.Z - Destination.Z) <= 16)
            {
                throw new InvalidOperationException("The hedge maze arrival tile has an active teleporter.");
            }
        }

        if (!Map.Felucca.CanFit(Source, 16, true, false) || !Map.Felucca.CanFit(Destination, 16, true, false))
        {
            throw new InvalidOperationException("The Labrynth hedge return source and destination must have walkable support.");
        }
        if (existing != null)
        {
            return existing;
        }

        var result = new Teleporter(Destination, Map.Felucca) { Name = RouteName };
        result.MoveToWorld(Source, Map.Felucca);
        created = true;
        return result;
    }

    private static bool IsExactRoute(Teleporter teleporter) =>
        teleporter.GetType() == typeof(Teleporter) && teleporter.Location == Source &&
        teleporter.Name == RouteName && teleporter.PointDest == Destination && teleporter.MapDest == Map.Felucca &&
        teleporter.Flags == TeleporterFlags.Active && !teleporter.Visible && !teleporter.Movable &&
        teleporter.Delay == TimeSpan.Zero && teleporter.SoundID == 0;
}
