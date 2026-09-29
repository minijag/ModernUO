using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Server.Items;
using Server.Json;
using Server.Engines.Spawners;
using Server.Mobiles;
using Server.Multis;

namespace Server.Engines.Labrynth;

// Opt-in, one-time installation of ordinary world-saved items. No map patches.
public static class ArborAdditionsDeployment
{
    static string Data => Path.Combine(Core.BaseDirectory, "Data", "ArborMazeV6");
    public static void Initialize()
    {
        if (!File.Exists(Path.Combine(Data, "install.pending")) || File.Exists(Path.Combine(Data, "installed.txt"))) return;
        try { Install(); }
        catch (Exception ex) { File.WriteAllText(Path.Combine(Data, "failure.txt"), ex.ToString()); Console.WriteLine($"[ArborMazeV6] BLOCKED: {ex}"); }
    }

    static void Install()
    {
        var walls = new[] { new Point3D(1876,927,0), new Point3D(1844,914,0), new Point3D(1818,932,0) };
        var source = new Point3D(5249,784,0);
        var destination = new Point3D(1873,914,0);
        var decorations = new[] { (1793,928,0xE2E), (1795,928,0xB1A), (1793,932,0xE2E), (1795,932,0xB1A) };
        var footprint = walls.Concat(decorations.Select(d => new Point3D(d.Item1,d.Item2,0))).Append(source).ToArray();
        foreach (var p in footprint)
        {
            if (BaseHouse.FindHouseAt(p,Map.Felucca,128) != null) throw new Exception($"House at {p}");
            if (World.Mobiles.Values.Any(m => m.Map == Map.Felucca && m.X == p.X && m.Y == p.Y)) throw new Exception($"Occupied tile {p}; nothing changed");
            if (World.Items.Values.Any(i => i.Parent == null && i.Map == Map.Felucca && i.Location == p)) throw new Exception($"Existing world item at {p}; nothing changed");
            if (!Map.Felucca.CanFit(p.X,p.Y,p.Z,16,false,false)) throw new Exception($"Blocked tile {p}");
        }
        var options = JsonConfig.GetOptions();
        var definitions = JsonConfig.Deserialize<List<DynamicJson>>(Path.Combine(Core.BaseDirectory,"zodiac.json"));
        var selected = definitions.Where(j => j.GetProperty("name",options,out string name) && (name == "Arbor Labrynth - Whispering Shrine" || name == "Arbor Labrynth - Eastern Fort Patrol")).ToArray();
        if (selected.Length != 2) throw new Exception("Expected two new spawner definitions");
        foreach (var j in selected)
        {
            if (!j.GetProperty("guid",options,out Guid id) || World.Items.Values.OfType<BaseSpawner>().Any(s => s.Guid == id)) throw new Exception("Duplicate spawner");
        }
        var created = new List<Item>();
        bool committed = false;
        try
        {
            void Place(Item item, Point3D p) { created.Add(item); item.MoveToWorld(p,Map.Felucca); }
            foreach (var p in walls) Place(new Static(0xDB8) { Name="Labrynth hedge" },p);
            foreach (var d in decorations) Place(new Static(d.Item3) { Name="Whispering Shrine" },new Point3D(d.Item1,d.Item2,0));
            // Check all existing maze encounters remain accessible with the new hedges in place.
            var targets = World.Items.Values.OfType<BaseSpawner>().Where(s => s.Map == Map.Felucca && s.Name != null && s.Name.StartsWith("Arbor Labrynth")).Select(s => s.Location).Concat(new[] { new Point3D(1795,930,0),new Point3D(1921,937,0),destination }).ToArray();
            var reached = new HashSet<Point3D>(); var queue = new Queue<Point3D>();
            var start = new Point3D(1806,965,0); reached.Add(start); queue.Enqueue(start);
            var probe = new Mobile { Body=0x190 };
            try
            {
                while (queue.Count > 0)
                {
                    var p = queue.Dequeue(); probe.Location=p;
                    foreach (var d in new[] { Direction.North,Direction.East,Direction.South,Direction.West })
                    {
                        int x=p.X,y=p.Y; Server.Movement.Movement.Offset(d,ref x,ref y);
                        if (x<1778 || x>1952 || y<884 || y>1010) continue;
                        if (Server.Movement.Movement.CheckMovement(probe,Map.Felucca,p,d,out int z) && z>=-20 && z<=60)
                        { var next=new Point3D(x,y,z); if(reached.Add(next)) queue.Enqueue(next); }
                    }
                }
            }
            finally { probe.Delete(); }
            foreach(var p in targets) if(!reached.Contains(p)) throw new Exception($"Unreachable encounter or landing: {p}");
            var gate = new Teleporter(destination,Map.Felucca) { Name="Labrynth - Return to Hedge Maze" }; Place(gate,source);
            var traveller=new Mobile { Body=0x190,Player=true };
            try { traveller.MoveToWorld(source,Map.Felucca); gate.OnMoveOver(traveller); if(traveller.Location!=destination || traveller.Map!=Map.Felucca) throw new Exception("Return teleport test failed"); }
            finally { traveller.Delete(); }
            foreach(var j in selected)
            { j.GetProperty("location",options,out Point3D p); var spawner=new Spawner(j,options); Place(spawner,p); spawner.Respawn(); }
            committed=true;
            File.WriteAllText(Path.Combine(Data,"installed.txt"),$"Installed three hedge tiles, Whispering Shrine, two spawners and return teleporter. Movement passed ({reached.Count} positions, {targets.Length} targets); teleport tested. Pending save.");
            Console.WriteLine("[ArborMazeV6] Installed and validated. Saving in ten seconds.");
            Timer.DelayCall(TimeSpan.FromSeconds(10),World.Save);
        }
        finally { if(!committed) foreach(var item in created) item.Delete(); }
    }
}
