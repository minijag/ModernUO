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

public static class ArborPolishDeployment
{
    public sealed class Manifest
    {
        public Guid[] NewSpawnerGuids { get; set; }
        public Guid[] AllSpawnerGuids { get; set; }
        public ArborMazeDeployment.Marker[] OldPortals { get; set; }
        public ArborMazeDeployment.Marker[] NewPortals { get; set; }
        public int[][] Scope { get; set; }
    }
    static string Data=>Path.Combine(Core.BaseDirectory,"Data","ArborMazeV5");
    public static void Initialize(){if(!File.Exists(Path.Combine(Data,"audit.pending"))&&!File.Exists(Path.Combine(Data,"install.pending")))return;try{Deploy();}catch(Exception ex){File.WriteAllText(Path.Combine(Data,"failure.txt"),ex.ToString());Console.WriteLine($"[ArborMazeV5] BLOCKED: {ex.Message}");}}
    static void Deploy()
    {
        var m=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Path.Combine(Data,"manifest.json")));
        if(m?.NewSpawnerGuids?.Length!=16||m.AllSpawnerGuids?.Length!=43||m.OldPortals?.Length!=9||m.NewPortals?.Length!=6)throw new Exception("Invalid manifest");
        var scope=m.Scope.Select(p=>(p[0],p[1])).Concat(m.OldPortals.Concat(m.NewPortals).Select(p=>(p.X,p.Y))).ToHashSet();if(scope.Any(p=>p.Item1<1778||p.Item1>1952||p.Item2<880||p.Item2>1010))throw new Exception("Invalid bounds");
        var mobs=World.Mobiles.Values.Where(mob=>mob.Map==Map.Felucca&&scope.Contains((mob.X,mob.Y))).ToArray();
        bool Protected(Mobile mob)=>mob.Player||mob is BaseCreature {Controlled:true} or BaseCreature {Summoned:true} or BaseCreature {IsStabled:true};
        if(mobs.Any(Protected))throw new Exception("Protected occupants: "+string.Join(", ",mobs.Where(Protected).Select(mob=>$"{mob.Name}@{mob.Location}")));
        foreach(var p in scope)if(BaseHouse.FindHouseAt(new Point3D(p.Item1,p.Item2,0),Map.Felucca,128)!=null)throw new Exception("House intersects footprint");
        var unknown=World.Items.Values.Where(i=>i.Parent==null&&i.Map==Map.Felucca&&scope.Contains((i.X,i.Y))&&i is not BaseSpawner and not Teleporter and not BaseBook&&!(i is Static&&!i.Movable)).ToArray();if(unknown.Length>0)throw new Exception("Review items: "+string.Join(", ",unknown.Select(i=>$"{i.Serial}:{i.GetType().Name}@{i.Location}")));
        var old=World.Items.Values.Where(i=>i.Map==Map.Felucca&&i.Parent==null&&m.OldPortals.Any(p=>p.X==i.X&&p.Y==i.Y&&p.Z==i.Z&&p.Name==i.Name)&&(i is Teleporter||i is Static&&!i.Movable)).ToArray();if(old.Count(i=>i is Teleporter)!=9||old.Count(i=>i is Static)!=9)throw new Exception("Old portal inventory mismatch");
        if(World.Items.Values.OfType<BaseSpawner>().Any(s=>m.NewSpawnerGuids.Contains(s.Guid)))throw new Exception("New encounter GUID already exists");
        string audit=$"Audit passed: 9 obsolete portal pairs, 16 new encounter spawners, {mobs.Length} wild occupants; no players, pets, houses or unknown items.";File.WriteAllText(Path.Combine(Data,"audit.txt"),audit);Console.WriteLine("[ArborMazeV5] "+audit);
        if(!File.Exists(Path.Combine(Data,"install.pending")))return;if(File.Exists(Path.Combine(Data,"installed.txt")))throw new Exception("Already installed");
        var options=SpawnerJsonSerializer.Options;var selected=JsonConfig.Deserialize<List<SpawnerDto>>(Path.Combine(Core.BaseDirectory,"zodiac.json"), options).Where(j=>(j.Guid is var id)&&m.AllSpawnerGuids.Contains(id)).ToArray();if(selected.Length!=43)throw new Exception("Expected 43 spawner definitions");
        var targets=selected.Select(j=>{Point3D p = j.Location;return p;}).ToArray();
        var links=JsonConfig.Deserialize<List<TeleporterDefinition>>(Path.Combine(Core.BaseDirectory,"Data","teleporters.json")).Where(t=>t.Source.Map==Map.Felucca&&m.NewPortals.Any(p=>p.X==t.Source.X&&p.Y==t.Source.Y&&p.Z==t.Source.Z)).ToArray();if(links.Length!=6)throw new Exception("Expected six POI gate definitions");
        foreach(var t in links)if(!links.Any(b=>b.Source.X==t.Destination.X&&b.Source.Y==t.Destination.Y&&b.Source.Z==t.Destination.Z&&b.Destination.X==t.Source.X&&b.Destination.Y==t.Source.Y&&b.Destination.Z==t.Source.Z))throw new Exception("Gate is not an exact reciprocal pair");
        var bad=new List<string>();foreach(var p in targets.Concat(m.NewPortals.Select(p=>new Point3D(p.X,p.Y,p.Z))).Concat(new[]{new Point3D(1802,950,0),new Point3D(1811,964,0)}))if(!Map.Felucca.CanFit(p.X,p.Y,p.Z,16,false,false))bad.Add($"blocked {p}");if(bad.Count>0)throw new Exception(string.Join("; ",bad));
        var probe=new Mobile {Body=0x190};var reached=new HashSet<Point3D>();
        try{var q=new Queue<Point3D>();var start=new Point3D(1806,965,0);reached.Add(start);q.Enqueue(start);while(q.Count>0){var p=q.Dequeue();probe.Location=p;foreach(var d in new[]{Direction.North,Direction.East,Direction.South,Direction.West}){int x=p.X,y=p.Y;Server.Movement.Movement.Offset(d,ref x,ref y);if(x<1778||x>1952||y<884||y>1010)continue;if(Server.Movement.Movement.CheckMovement(probe,Map.Felucca,p,d,out int z)){var n=new Point3D(x,y,z);if(z>=-20&&z<=60&&reached.Add(n))q.Enqueue(n);}}}}finally{probe.Delete();}
        foreach(var p in targets.Concat(m.NewPortals.Select(p=>new Point3D(p.X,p.Y,p.Z))).Concat(new[]{new Point3D(1802,950,0),new Point3D(1811,964,0)}))if(!reached.Contains(p))bad.Add($"unreachable {p}");if(bad.Count>0)throw new Exception(string.Join("; ",bad));
        // MoveToWorld does not invoke destination OnMoveOver. Verify actual
        // arrival and reverse travel on standard teleporters before committing.
        var gates=new List<Teleporter>();bool keep=false;
        try{
            foreach(var t in links){var mark=m.NewPortals.Single(p=>p.X==t.Source.X&&p.Y==t.Source.Y&&p.Z==t.Source.Z);var gate=new Teleporter(new Point3D(t.Destination.X,t.Destination.Y,t.Destination.Z),Map.Felucca){Name=mark.Name};gate.MoveToWorld(new Point3D(mark.X,mark.Y,mark.Z),Map.Felucca);gates.Add(gate);}
            var traveller=new Mobile {Body=0x190,Player=true};
            try{foreach(var gate in gates){traveller.MoveToWorld(new Point3D(gate.X+1,gate.Y,gate.Z),Map.Felucca);gate.OnMoveOver(traveller);if(traveller.Map!=Map.Felucca||traveller.Location!=gate.PointDest)throw new Exception("Exact gate arrival test failed");var back=gates.Single(b=>b.Location==gate.PointDest);traveller.MoveToWorld(new Point3D(back.X+1,back.Y,back.Z),Map.Felucca);back.OnMoveOver(traveller);if(traveller.Location!=gate.Location)throw new Exception("Gate return test failed");}}finally{traveller.Delete();}
            foreach(var i in old)i.Delete();
            foreach(var mark in m.NewPortals)new Static(0xF6C){Name=mark.Name,Hue=mark.Hue}.MoveToWorld(new Point3D(mark.X,mark.Y,mark.Z),Map.Felucca);
            foreach(var j in selected){Guid id = j.Guid;if(!m.NewSpawnerGuids.Contains(id))continue;Point3D p = j.Location;var s=j.ToSpawner();s.MoveToWorld(p,Map.Felucca);s.Respawn();}
            foreach(var book in World.Items.Values.OfType<BaseBook>().Where(b=>b.Map==Map.Felucca&&!b.Movable&&(b.X==1836&&b.Y==917||b.X==1852&&b.Y==941||b.X==1872&&b.Y==961))){book.Pages[0].Lines=new[]{"Three colors, three twins.","Seek the six sanctuaries.","Step into a colored gate","to arrive on its twin.","Step off before returning.","The northern trail leads","to the cavern. Bring allies","to the two stone towers."};book.MarkDirty();}
            int rescued=0;foreach(var mob in mobs)if(!mob.Deleted&&!Map.Felucca.CanFit(mob.X,mob.Y,mob.Z,16,false,false)){var p=targets.Where(p=>p.Z==0).OrderBy(p=>Math.Abs(p.X-mob.X)+Math.Abs(p.Y-mob.Y)).First();mob.MoveToWorld(p,Map.Felucca);rescued++;}
            keep=true;Console.WriteLine($"[ArborMazeV5] Movement passed ({reached.Count} positions); all 43 spawns, six POIs and bridge corners reachable. All six exact gate arrivals and return trips passed, without automatic bounce.");
            File.WriteAllText(Path.Combine(Data,"installed.txt"),$"Added 16 spawners (43 total, configured population 85). Six POI gates replace nine corridor/shortcut gates. Exact reciprocal travel tested. Rescued {rescued} wild creatures. Pending save.");Console.WriteLine("[ArborMazeV5] Installed. Saving in ten seconds.");Timer.DelayCall(TimeSpan.FromSeconds(10),World.Save);
        }finally{if(!keep)foreach(var gate in gates)gate.Delete();}
    }
}
