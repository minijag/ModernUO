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

// Explicit V1 -> V2 upgrade. Never run automatically during ordinary startups.
public static class ArborExpansionDeployment
{
    public sealed class Manifest
    {
        public Guid[] RemovedSpawnerGuids { get; set; }
        public Guid[] NewSpawnerGuids { get; set; }
        public ArborMazeDeployment.Marker[] Portals { get; set; }
        public ArborMazeDeployment.Marker[] Books { get; set; }
        public ArborMazeDeployment.Marker[] OldPortals { get; set; }
        public ArborMazeDeployment.Marker[] OldBooks { get; set; }
        public int[][] Scope { get; set; }
    }
    static string Data => Path.Combine(Core.BaseDirectory,"Data","ArborMazeV2");
    public static void Initialize()
    {
        if(!File.Exists(Path.Combine(Data,"audit.pending"))&&!File.Exists(Path.Combine(Data,"install.pending")))return;
        try { Deploy(); } catch(Exception ex) { File.WriteAllText(Path.Combine(Data,"failure.txt"),ex.ToString());Console.WriteLine($"[ArborMazeV2] BLOCKED: {ex.Message}"); }
    }
    static void Deploy()
    {
        var m=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Path.Combine(Data,"manifest.json")));
        if(m?.NewSpawnerGuids?.Length!=26||m.Portals?.Length!=13||m.Books?.Length!=3||m.Scope?.Length<10000)throw new Exception("Invalid V2 manifest");
        var scope=m.Scope.Select(p=>(p[0],p[1])).ToHashSet();
        if(scope.Any(p=>p.Item1<1780||p.Item1>1952||p.Item2<880||p.Item2>992))throw new Exception("Scope outside peninsula");
        bool Inside(int x,int y)=>scope.Contains((x,y));
        bool Protected(Mobile mob)=>mob.Player||mob is BaseCreature {Controlled:true} or BaseCreature {Summoned:true} or BaseCreature {IsStabled:true};
        var mobs=World.Mobiles.Values.Where(mob=>mob.Map==Map.Felucca&&Inside(mob.X,mob.Y)).ToArray();
        if(mobs.Any(Protected))throw new Exception("Player or protected creature in footprint");
        foreach(var p in scope)if(BaseHouse.FindHouseAt(new Point3D(p.Item1,p.Item2,0),Map.Felucca,128)!=null)throw new Exception("House intersects footprint");
        var old=World.Items.Values.OfType<BaseSpawner>().Where(s=>m.RemovedSpawnerGuids.Contains(s.Guid)).ToArray();
        if(old.Any(s=>s.Spawned.Keys.OfType<Mobile>().Any(Protected)))throw new Exception("Protected creature attached to old spawner");
        bool Matches(Item i,ArborMazeDeployment.Marker[] marks)=>marks.Any(p=>p.X==i.X&&p.Y==i.Y&&p.Z==i.Z&&p.Name==(i is BaseBook book?book.Title:i.Name));
        bool Replaced(Item i)=>i is BaseSpawner s&&m.RemovedSpawnerGuids.Contains(s.Guid)
            ||i is Static&&!i.Movable
            ||i is Teleporter&&Matches(i,m.OldPortals)
            ||i is BaseBook&&!i.Movable&&Matches(i,m.OldBooks);
        var items=World.Items.Values.Where(i=>i.Parent==null&&i.Map==Map.Felucca&&Inside(i.X,i.Y)).ToArray();
        var unknown=items.Where(i=>!Replaced(i)).ToArray();
        if(unknown.Length>0)throw new Exception("Review world items: "+string.Join(", ",unknown.Select(i=>$"{i.Serial}:{i.GetType().Name}@{i.Location}")));
        string summary=$"Audit passed: {old.Length} replaced spawners, {mobs.Length} wild NPCs, {items.Length} known world items; no players, pets, houses or unknown items.";
        File.WriteAllText(Path.Combine(Data,"audit.txt"),summary);Console.WriteLine("[ArborMazeV2] "+summary);
        if(!File.Exists(Path.Combine(Data,"install.pending")))return;
        if(File.Exists(Path.Combine(Data,"installed.txt")))throw new Exception("V2 already applied; refusing repeated migration");
        var options=JsonConfig.GetOptions();
        var selected=JsonConfig.Deserialize<List<DynamicJson>>(Path.Combine(Core.BaseDirectory,"zodiac.json")).Where(j=>j.GetProperty("guid",options,out Guid id)&&m.NewSpawnerGuids.Contains(id)).ToArray();
        var tele=JsonConfig.Deserialize<List<TeleporterDefinition>>(Path.Combine(Core.BaseDirectory,"Data","teleporters.json")).Where(t=>t.Source.Map==Map.Felucca&&m.Portals.Any(p=>p.X==t.Source.X&&p.Y==t.Source.Y&&p.Z==t.Source.Z)).ToArray();
        if(selected.Length!=26||tele.Length!=13)throw new Exception("Definition count mismatch");
        var bad=new List<string>();
        foreach(var j in selected){j.GetProperty("location",options,out Point3D p);if(!Map.Felucca.CanFit(p.X,p.Y,p.Z,16,false,false))bad.Add($"spawn {p}");}
        foreach(var t in tele)foreach(var p in new[]{new Point3D(t.Source.X,t.Source.Y,t.Source.Z),new Point3D(t.Destination.X,t.Destination.Y,t.Destination.Z)})if(!Map.Felucca.CanFit(p.X,p.Y,p.Z,16,false,false))bad.Add($"portal {p}");
        foreach(var b in m.Books)if(!Map.Felucca.CanFit(b.X,b.Y,b.Z,16,false,false))bad.Add($"book {b.X},{b.Y},{b.Z}");
        if(bad.Count>0)throw new Exception("Blocked placements: "+string.Join("; ",bad));
        foreach(var s in old)s.Delete();
        foreach(var mob in mobs)if(!mob.Deleted)mob.Delete();
        foreach(var i in items)if(!i.Deleted)i.Delete();
        foreach(var j in selected){j.GetProperty("location",options,out Point3D p);var s=new Spawner(j,options);s.MoveToWorld(p,Map.Felucca);s.Respawn();}
        foreach(var t in tele){var p=new Point3D(t.Source.X,t.Source.Y,t.Source.Z);var mark=m.Portals.Single(b=>b.X==p.X&&b.Y==p.Y&&b.Z==p.Z);new Teleporter(new Point3D(t.Destination.X,t.Destination.Y,t.Destination.Z),Map.Felucca){Name=mark.Name}.MoveToWorld(p,Map.Felucca);new Static(0xF6C){Name=mark.Name,Hue=mark.Hue}.MoveToWorld(p,Map.Felucca);}
        foreach(var b in m.Books){var book=new BaseBook(0xFF1,b.Name,"The Keeper of the Labrynth",1,false){Movable=false};book.Pages[0].Lines=b.Lines;book.MoveToWorld(new Point3D(b.X,b.Y,b.Z),Map.Felucca);}
        File.WriteAllText(Path.Combine(Data,"installed.txt"),"26 spawners, 13 portals, 3 books installed. Pending save.");
        Console.WriteLine("[ArborMazeV2] Installed 26 spawners, 13 portals, 3 books. Saving in ten seconds.");
        Timer.DelayCall(TimeSpan.FromSeconds(10),World.Save);
    }
}
