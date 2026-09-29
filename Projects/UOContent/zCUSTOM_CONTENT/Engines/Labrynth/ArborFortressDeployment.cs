using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using Server.Commands;
using Server.Engines.Spawners;
using Server.Items;
using Server.Json;
using Server.Mobiles;
using Server.Multis;

namespace Server.Engines.Labrynth;

public static class ArborFortressDeployment
{
    public sealed class Manifest
    {
        public Guid[] OldSpawnerGuids { get; set; }
        public Guid[] AllSpawnerGuids { get; set; }
        public ArborMazeDeployment.Marker[] OldPortals { get; set; }
        public ArborMazeDeployment.Marker[] AllPortals { get; set; }
        public int[][] Scope { get; set; }
        public int[][] Checks { get; set; }
        public int[][] Platforms { get; set; }
    }
    static string Data=>Path.Combine(Core.BaseDirectory,"Data","ArborMazeV4");
    public static void Initialize()
    {
        if(!File.Exists(Path.Combine(Data,"audit.pending"))&&!File.Exists(Path.Combine(Data,"install.pending")))return;
        try{Deploy();}catch(Exception ex){File.WriteAllText(Path.Combine(Data,"failure.txt"),ex.ToString());Console.WriteLine($"[ArborMazeV4] BLOCKED: {ex.Message}");}
    }
    static void Deploy()
    {
        var m=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Path.Combine(Data,"manifest.json")));
        if(m?.OldSpawnerGuids?.Length!=26||m.AllSpawnerGuids?.Length!=27||m.OldPortals?.Length!=2||m.AllPortals?.Length!=9)throw new Exception("Invalid manifest");
        var scope=m.Scope.Select(p=>(p[0],p[1])).ToHashSet();if(scope.Any(p=>p.Item1<1778||p.Item1>1952||p.Item2<880||p.Item2>992))throw new Exception("Invalid bounds");
        bool BossSummon(Mobile mob)=>mob is BaseCreature {Summoned:true,Controlled:false,IsStabled:false,SummonMaster:BaseCreature {Player:false,Controlled:false,Summoned:false,IsStabled:false} master}&&master.Spawner is BaseSpawner owner&&m.OldSpawnerGuids.Contains(owner.Guid);
        bool Protected(Mobile mob)=>mob.Player||mob is BaseCreature {Controlled:true} or BaseCreature {IsStabled:true}||mob is BaseCreature {Summoned:true}&&!BossSummon(mob);
        var mobs=World.Mobiles.Values.Where(mob=>mob.Map==Map.Felucca&&scope.Contains((mob.X,mob.Y))).ToArray();
        if(mobs.Any(Protected))throw new Exception("Protected occupants: "+string.Join(", ",mobs.Where(Protected).Select(mob=>$"{mob.Name} at {mob.Location}")));
        foreach(var p in scope)if(BaseHouse.FindHouseAt(new Point3D(p.Item1,p.Item2,0),Map.Felucca,128)!=null)throw new Exception("House intersects footprint");
        var old=World.Items.Values.OfType<Spawner>().Where(s=>m.OldSpawnerGuids.Contains(s.Guid)).ToArray();if(old.Length!=26)throw new Exception("Expected 26 existing spawners");
        if(old.Any(s=>s.Spawned.Keys.OfType<Mobile>().Any(Protected)))throw new Exception("Protected creature attached to spawner");
        var items=World.Items.Values.Where(i=>i.Parent==null&&i.Map==Map.Felucca&&scope.Contains((i.X,i.Y))).ToArray();
        var unknown=items.Where(i=>i is not BaseSpawner and not Teleporter and not BaseBook&&!(i is Static&&!i.Movable)).ToArray();
        if(unknown.Length>0)throw new Exception("Review items: "+string.Join(", ",unknown.Select(i=>$"{i.Serial}:{i.GetType().Name}@{i.Location}")));
        bool Obsolete(Item i)=>m.OldPortals.Any(p=>p.X==i.X&&p.Y==i.Y&&p.Z==i.Z&&p.Name==i.Name);
        var portals=World.Items.Values.Where(i=>i.Map==Map.Felucca&&i.Parent==null&&Obsolete(i)&&(i is Teleporter||i is Static&&!i.Movable)).ToArray();if(portals.Count(i=>i is Teleporter)!=2||portals.Count(i=>i is Static)!=2)throw new Exception("Old deck portal inventory mismatch");
        string audit=$"Audit passed: 26 existing spawners, 2 obsolete deck portal pairs, {mobs.Length} wild occupants; no players, pets, houses or unknown items.";File.WriteAllText(Path.Combine(Data,"audit.txt"),audit);Console.WriteLine("[ArborMazeV4] "+audit);
        if(!File.Exists(Path.Combine(Data,"install.pending")))return;if(File.Exists(Path.Combine(Data,"installed.txt")))throw new Exception("Already installed");
        var options=JsonConfig.GetOptions();var selected=JsonConfig.Deserialize<List<DynamicJson>>(Path.Combine(Core.BaseDirectory,"zodiac.json")).Where(j=>j.GetProperty("guid",options,out Guid id)&&m.AllSpawnerGuids.Contains(id)).ToArray();
        if(selected.Length!=27)throw new Exception("Spawner definition mismatch");
        var targets=selected.Select(j=>{j.GetProperty("location",options,out Point3D p);return p;}).ToArray();
        var tele=JsonConfig.Deserialize<List<TeleporterDefinition>>(Path.Combine(Core.BaseDirectory,"Data","teleporters.json")).Where(t=>t.Source.Map==Map.Felucca&&m.AllPortals.Any(p=>p.X==t.Source.X&&p.Y==t.Source.Y&&p.Z==t.Source.Z)).ToArray();if(tele.Length!=9)throw new Exception("Teleporter definition mismatch");
        var bad=new List<string>();foreach(var p in targets)if(!Map.Felucca.CanFit(p.X,p.Y,p.Z,16,false,false))bad.Add($"spawn {p}");
        foreach(var t in tele)foreach(var p in new[]{new Point3D(t.Source.X,t.Source.Y,t.Source.Z),new Point3D(t.Destination.X,t.Destination.Y,t.Destination.Z)})if(!Map.Felucca.CanFit(p.X,p.Y,p.Z,16,false,false))bad.Add($"portal {p}");
        if(bad.Count>0)throw new Exception("Placements: "+string.Join("; ",bad));
        // Real cardinal movement, not fixed-height occupancy. The temporary probe
        // remains on Internal and is always deleted before saving the world.
        var probe=new Mobile {Body=0x190};var reached=new HashSet<Point3D>();
        try{
            var start=new Point3D(1806,965,0);var q=new Queue<Point3D>();q.Enqueue(start);reached.Add(start);
            while(q.Count>0){var p=q.Dequeue();probe.Location=p;foreach(var d in new[]{Direction.North,Direction.East,Direction.South,Direction.West}){
                int x=p.X,y=p.Y;Server.Movement.Movement.Offset(d,ref x,ref y);if(x<1778||x>1952||y<884||y>992)continue;
                if(Server.Movement.Movement.CheckMovement(probe,Map.Felucca,p,d,out int z)){var n=new Point3D(x,y,z);if(z>=-20&&z<=60&&reached.Add(n))q.Enqueue(n);}
            }}
        }finally{probe.Delete();}
        foreach(var p in targets)if(!reached.Contains(p))bad.Add($"unreachable spawn {p}");
        foreach(var a in m.Checks){var p=new Point3D(a[0],a[1],a[2]);if(!reached.Contains(p))bad.Add($"unreachable route {p}");}
        var platforms=m.Platforms.Select(a=>new Point3D(a[0],a[1],a[2])).Distinct().Where(p=>Map.Felucca.CanFit(p.X,p.Y,p.Z,16,false,false)).ToArray();
        foreach(var p in platforms)if(!reached.Contains(p))bad.Add($"unreachable platform {p}");
        if(bad.Count>0)throw new Exception("Movement checks: "+string.Join("; ",bad.Take(40)));
        Console.WriteLine($"[ArborMazeV4] Real movement passed: {reached.Count} reachable positions, all {platforms.Length} standable elevated floor tiles, bridge, both stairs and all 27 spawn points.");
        foreach(var summon in World.Mobiles.Values.Where(BossSummon).ToArray())summon.Delete();foreach(var i in portals)i.Delete();
        foreach(var j in selected){j.GetProperty("guid",options,out Guid id);j.GetProperty("location",options,out Point3D p);j.GetProperty("count",options,out int count);var s=old.SingleOrDefault(s=>s.Guid==id);if(s==null)s=new Spawner(j,options);else{s.Count=count;foreach(var entry in s.Entries)entry.SpawnedMaxCount=count;}s.MoveToWorld(p,Map.Felucca);s.Respawn();}
        int rescued=0;foreach(var mob in mobs)if(!mob.Deleted&&!Map.Felucca.CanFit(mob.X,mob.Y,mob.Z,16,false,false)){var p=targets.Where(p=>p.Z==0).OrderBy(p=>Math.Abs(p.X-mob.X)+Math.Abs(p.Y-mob.Y)).First();mob.MoveToWorld(p,Map.Felucca);rescued++;}
        File.WriteAllText(Path.Combine(Data,"installed.txt"),$"27 spawners, target population 69. Physical stairs replace 2 deck portal pairs. Rescued {rescued} stranded creatures. Pending save.");Console.WriteLine("[ArborMazeV4] Installed. Saving in ten seconds.");Timer.DelayCall(TimeSpan.FromSeconds(10),World.Save);
    }
}
