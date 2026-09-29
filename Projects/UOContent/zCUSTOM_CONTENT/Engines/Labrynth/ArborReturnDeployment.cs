using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Server.Items;
using Server.Json;
using Server.Engines.Spawners;

namespace Server.Engines.Labrynth;

public static class ArborReturnDeployment
{
    static string Data=>Path.Combine(Core.BaseDirectory,"Data","ArborMazeV8");
    public static void Initialize()
    {
        if(!File.Exists(Path.Combine(Data,"install.pending"))||File.Exists(Path.Combine(Data,"installed.txt")))return;
        try { Install(); } catch(Exception ex){File.WriteAllText(Path.Combine(Data,"failure.txt"),ex.ToString());Console.WriteLine($"[ArborMazeV8] BLOCKED: {ex}");}
    }
    static void Install()
    {
        var walls=new[]{new Point3D(1800,925,0),new Point3D(1800,926,0),new Point3D(1800,927,0)};
        var spawnPoint=new Point3D(1831,961,0);var oldPoint=new Point3D(5249,784,0);var newPoint=new Point3D(5248,784,0);var destination=new Point3D(1873,914,0);
        var old=World.Items.Values.OfType<Teleporter>().Where(t=>t.Map==Map.Felucca&&t.Location==oldPoint&&t.Name=="Labrynth - Return to Hedge Maze"&&t.PointDest==destination).ToArray();
        if(old.Length!=1)throw new Exception("Expected the existing return gate");
        foreach(var p in walls.Append(newPoint).Append(spawnPoint))
        {
            if(World.Mobiles.Values.Any(m=>m.Map==Map.Felucca&&m.X==p.X&&m.Y==p.Y))throw new Exception($"Occupied tile {p}");
            if(World.Items.Values.Any(i=>i.Parent==null&&i.Map==Map.Felucca&&i.Location==p))throw new Exception($"Existing item at {p}");
            if(!Map.Felucca.CanFit(p.X,p.Y,p.Z,16,false,false))throw new Exception($"Blocked tile {p}");
        }
        var options=JsonConfig.GetOptions();var defs=JsonConfig.Deserialize<List<DynamicJson>>(Path.Combine(Core.BaseDirectory,"zodiac.json"));
        var selected=defs.Where(j=>j.GetProperty("name",options,out string name)&&name=="Arbor Labrynth - Southern Gazer Patrol").ToArray();
        if(selected.Length!=1||!selected[0].GetProperty("guid",options,out Guid id)||World.Items.Values.OfType<BaseSpawner>().Any(s=>s.Guid==id))throw new Exception("Spawner definition mismatch");
        var created=new List<Item>();bool committed=false;
        try
        {
            foreach(var p in walls){var hedge=new Static(0xDB8){Name="Labrynth hedge"};created.Add(hedge);hedge.MoveToWorld(p,Map.Felucca);}
            var targets=World.Items.Values.OfType<BaseSpawner>().Where(s=>s.Map==Map.Felucca&&s.Name!=null&&s.Name.StartsWith("Arbor Labrynth")).Select(s=>s.Location).Append(spawnPoint).Append(destination).ToArray();
            var reached=new HashSet<Point3D>();var q=new Queue<Point3D>();var start=new Point3D(1806,965,0);reached.Add(start);q.Enqueue(start);var probe=new Mobile{Body=0x190};
            try{while(q.Count>0){var p=q.Dequeue();probe.Location=p;foreach(var d in new[]{Direction.North,Direction.East,Direction.South,Direction.West}){int x=p.X,y=p.Y;Server.Movement.Movement.Offset(d,ref x,ref y);if(x<1778||x>1952||y<884||y>1010)continue;if(Server.Movement.Movement.CheckMovement(probe,Map.Felucca,p,d,out int z)&&z>=-20&&z<=60){var next=new Point3D(x,y,z);if(reached.Add(next))q.Enqueue(next);}}}}finally{probe.Delete();}
            foreach(var p in targets)if(!reached.Contains(p))throw new Exception($"Unreachable target {p}");
            var traveller=new Mobile{Body=0x190,Player=true};
            try
            {
                old[0].MoveToWorld(newPoint,Map.Felucca);
                // The cave entrance lands on this return gate. MoveToWorld must not bounce on arrival.
                traveller.MoveToWorld(newPoint,Map.Felucca);if(traveller.Location!=newPoint)throw new Exception("Arrival bounced");
                traveller.MoveToWorld(oldPoint,Map.Felucca);old[0].OnMoveOver(traveller);
                if(traveller.Location!=destination||traveller.Map!=Map.Felucca)throw new Exception("Return travel failed");
            }
            finally{traveller.Delete();}
            var spawner=new Spawner(selected[0],options);created.Add(spawner);spawner.MoveToWorld(spawnPoint,Map.Felucca);spawner.Respawn();
            committed=true;File.WriteAllText(Path.Combine(Data,"installed.txt"),$"Three walls, new Gazer spawner, return gate moved to 5248,784,0. {targets.Length} movement targets passed. Arrival/return tested. Pending save.");Console.WriteLine("[ArborMazeV8] Installed and validated. Saving in ten seconds.");Timer.DelayCall(TimeSpan.FromSeconds(10),World.Save);
        }
        finally{if(!committed){foreach(var item in created)item.Delete();old[0].MoveToWorld(oldPoint,Map.Felucca);}}
    }
}
