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

// Explicit one-shot deployment. Static scenery lives in MUL; spawners in zodiac.json;
// ordinary teleporters and books persist in world saves. No recurring regeneration.
public static class ArborMazeDeployment
{
    public sealed class Marker { public string Name { get; set; } public int X { get; set; } public int Y { get; set; } public int Z { get; set; } public int Hue { get; set; } public string[] Lines { get; set; } }
    public sealed class Manifest { public Guid[] RemovedSpawnerGuids { get; set; } public Guid[] NewSpawnerGuids { get; set; } public Marker[] Portals { get; set; } public Marker[] Books { get; set; } }
    static bool Inside(int x,int y) => (x>=1792&&x<=1868&&y>=916&&y<=980)||(x>=1880&&x<=1900&&y>=942&&y<=962);
    static string Data => Path.Combine(Core.BaseDirectory,"Data","ArborMaze");
    public static void Initialize()
    {
        if(!File.Exists(Path.Combine(Data,"audit.pending"))&&!File.Exists(Path.Combine(Data,"install.pending")))return;
        try { Deploy(); } catch(Exception ex) { File.WriteAllText(Path.Combine(Data,"failure.txt"),ex.ToString()); Console.WriteLine($"[ArborMaze] BLOCKED: {ex.Message}"); }
    }
    static void Deploy()
    {
        var manifest=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Path.Combine(Data,"manifest.json")));
        if(manifest?.NewSpawnerGuids?.Length!=14||manifest.Portals?.Length!=11||manifest.Books?.Length!=3)throw new Exception("Invalid manifest");
        var mobs=World.Mobiles.Values.Where(m=>m.Map==Map.Felucca&&Inside(m.X,m.Y)).ToArray();
        if(mobs.Any(m=>m.Player||m is BaseCreature {Controlled:true} or BaseCreature {Summoned:true} or BaseCreature {IsStabled:true}))throw new Exception("Player or protected creature in construction footprint");
        for(int x=1792;x<=1900;x++)for(int y=916;y<=980;y++)if(Inside(x,y)&&BaseHouse.FindHouseAt(new Point3D(x,y,0),Map.Felucca,128)!=null)throw new Exception("Player house intersects construction footprint");
        var oldSpawners=World.Items.Values.OfType<BaseSpawner>().Where(s=>manifest.RemovedSpawnerGuids.Contains(s.Guid)).ToArray();
        if(oldSpawners.Any(s=>s.Spawned.Keys.OfType<Mobile>().Any(m=>m.Player||m is BaseCreature {Controlled:true} or BaseCreature {Summoned:true} or BaseCreature {IsStabled:true})))throw new Exception("Protected creature attached to replaced spawner");
        var items=World.Items.Values.Where(i=>i.Parent==null&&i.Map==Map.Felucca&&Inside(i.X,i.Y)).ToArray();
        // Do not discard dropped equipment, corpses, containers, doors, or player construction.
        var unknown=items.Where(i=>i is not BaseSpawner && !(i is Static && !i.Movable)).ToArray();
        if(unknown.Length>0)throw new Exception("Review existing dynamic items before construction: "+string.Join(", ",unknown.Select(i=>$"{i.Serial}:{i.GetType().Name}@{i.Location}")));
        var summary=$"Audit passed: {mobs.Length} wild NPCs inside; {oldSpawners.Length} old spawners; {items.Count(i=>i is Static)} decorative world items. No players, pets, houses or unknown items.";
        File.WriteAllText(Path.Combine(Data,"audit.txt"),summary);Console.WriteLine("[ArborMaze] "+summary);
        if(!File.Exists(Path.Combine(Data,"install.pending")))return;
        var options=JsonConfig.GetOptions();
        var spawnJson=JsonConfig.Deserialize<List<DynamicJson>>(Path.Combine(Core.BaseDirectory,"zodiac.json"));
        var selected=spawnJson.Where(j=>j.GetProperty("guid",options,out Guid id)&&manifest.NewSpawnerGuids.Contains(id)).ToArray();
        if(selected.Length!=14)throw new Exception("Spawner manifest mismatch");
        var tele=JsonConfig.Deserialize<List<TeleporterDefinition>>(Path.Combine(Core.BaseDirectory,"Data","teleporters.json"))
            .Where(d=>d.Source.Map==Map.Felucca&&Inside(d.Source.X,d.Source.Y)).ToArray();
        if(tele.Length!=11)throw new Exception("Teleporter count mismatch");
        // Validate all spawn/portal endpoints against the newly installed map before deleting anything.
        foreach(var j in selected){j.GetProperty("location",options,out Point3D p);if(!Map.Felucca.CanFit(p.X,p.Y,p.Z,16,false,false))throw new Exception($"Blocked spawn at {p}");}
        foreach(var t in tele)foreach(var p in new[]{new Point3D(t.Source.X,t.Source.Y,t.Source.Z),new Point3D(t.Destination.X,t.Destination.Y,t.Destination.Z)})if(!Map.Felucca.CanFit(p.X,p.Y,p.Z,16,false,false))throw new Exception($"Blocked portal at {p}");
        foreach(var s in oldSpawners)s.Delete();
        foreach(var m in mobs)if(!m.Deleted)m.Delete();
        foreach(var i in items)if(i is Static&&!i.Deleted)i.Delete();
        foreach(var j in selected){j.GetProperty("location",options,out Point3D p);var s=new Spawner(j,options);s.MoveToWorld(p,Map.Felucca);s.Respawn();}
        foreach(var t in tele){var p=new Point3D(t.Source.X,t.Source.Y,t.Source.Z);var mark=manifest.Portals.Single(m=>m.X==p.X&&m.Y==p.Y&&m.Z==p.Z);new Teleporter(new Point3D(t.Destination.X,t.Destination.Y,t.Destination.Z),Map.Felucca){Name=mark.Name}.MoveToWorld(p,Map.Felucca);new Static(0xF6C){Name=mark.Name,Hue=mark.Hue}.MoveToWorld(p,Map.Felucca);}
        foreach(var b in manifest.Books){var book=new BaseBook(0xFF1,b.Name,"The Keeper of the Labrynth",1,false){Movable=false};book.Pages[0].Lines=b.Lines;book.MoveToWorld(new Point3D(b.X,b.Y,b.Z),Map.Felucca);}
        File.WriteAllText(Path.Combine(Data,"installed.txt"),"14 spawners, 11 teleporters, 3 lore books installed. Pending save.");
        Console.WriteLine("[ArborMaze] Installed 14 spawners, 11 teleporters, 3 lore books. Saving in ten seconds.");
        Timer.DelayCall(TimeSpan.FromSeconds(10),World.Save);
    }
}
