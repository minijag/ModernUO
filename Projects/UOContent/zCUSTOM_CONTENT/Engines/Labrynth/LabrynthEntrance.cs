using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Server.Commands;
using Server.Items;
using Server.Json;

namespace Server.Engines.Labrynth;

// Opt-in deployment migration. Normal world saves own these ordinary teleporters.
public static class LabrynthEntrance
{
    public static void Initialize()
    {
        if (!File.Exists(Path.Combine(Core.BaseDirectory, "Data", "labrynth-entrance.pending")))
        {
            return;
        }

        var definitions = JsonConfig.Deserialize<List<TeleporterDefinition>>(
            Path.Combine(Core.BaseDirectory, "Data", "teleporters.json"))
            .Where(d => d.Source.Map == Map.Felucca && d.Source.Y == 908 &&
                d.Source.X >= 1872 && d.Source.X <= 1874).ToArray();
        var expected = new[] { new Point3D(1872, 908, -18), new Point3D(1873, 908, -19), new Point3D(1874, 908, -17) };
        var destination = new Point3D(5248, 784, 0);
        if (definitions.Length != 3 || expected.Any(p => definitions.Count(d =>
                d.Source.X == p.X && d.Source.Y == p.Y && d.Source.Z == p.Z) != 1) ||
            definitions.Any(d => d.Back || d.Destination.Map != Map.Felucca ||
                d.Destination.X != destination.X || d.Destination.Y != destination.Y || d.Destination.Z != destination.Z))
        {
            throw new InvalidOperationException("Invalid Labrynth entrance definitions.");
        }

        var existing = World.Items.Values.OfType<Teleporter>().Where(t => t.Map == Map.Felucca &&
            t.Y == 908 && t.X >= 1872 && t.X <= 1874).ToArray();
        // Preflight all three before touching anything. Never replace unrelated teleporters.
        if (existing.Any(t => t.GetType() != typeof(Teleporter) || !expected.Contains(t.Location) ||
                t.PointDest != destination || t.MapDest != Map.Felucca || !t.Active) ||
            existing.GroupBy(t => t.Location).Any(g => g.Count() > 1))
        {
            throw new InvalidOperationException("Conflicting teleporters at Labrynth entrance; no changes made.");
        }

        var created = 0;
        foreach (var p in expected)
        {
            if (existing.Any(t => t.Location == p)) continue;
            var teleporter = new Teleporter(destination, Map.Felucca) { Name = "Labrynth Dungeon" };
            teleporter.MoveToWorld(p, Map.Felucca);
            created++;
        }
        Console.WriteLine($"[Labrynth] Verified 3 entrance teleporters to Felucca {destination}; created {created}. Awaiting world save.");
        Timer.DelayCall(TimeSpan.FromSeconds(10), World.Save);
    }
}
