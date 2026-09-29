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

// Targeted V2 -> V3 move. Unrelated maze spawners/books/portals retain identity.
public static class ArborRefinementDeployment
{
    public sealed class Move { public Guid Guid { get; set; } public int[] Old { get; set; } public int[] New { get; set; } }
    public sealed class Manifest
    {
        public Move[] Moves { get; set; }
        public ArborMazeDeployment.Marker[] OldPortals { get; set; }
        public ArborMazeDeployment.Marker[] NewPortals { get; set; }
        public ArborMazeDeployment.Marker[] AllPortals { get; set; }
        public Guid[] AllSpawnerGuids { get; set; }
        public int[][] Scope { get; set; }
    }
    static string Data=>Path.Combine(Core.BaseDirectory,"Data","ArborMazeV3");
    public static void Initialize()
    {
        if(!File.Exists(Path.Combine(Data,"audit.pending"))&&!File.Exists(Path.Combine(Data,"install.pending")))return;
        try{Deploy();}catch(Exception ex){File.WriteAllText(Path.Combine(Data,"failure.txt"),ex.ToString());Console.WriteLine($"[ArborMazeV3] BLOCKED: {ex.Message}");}
    }
    static void Deploy()
    {
        var m=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Path.Combine(Data,"manifest.json")));
        if(m?.Moves?.Length!=5||m.OldPortals?.Length!=4||m.NewPortals?.Length!=2||m.AllPortals?.Length!=11||m.AllSpawnerGuids?.Length!=26)throw new Exception("Invalid manifest");
        var scope=m.Scope.Select(p=>(p[0],p[1])).ToHashSet();
        if(scope.Any(p=>p.Item1<1780||p.Item1>1952||p.Item2<880||p.Item2>992))throw new Exception("Invalid footprint");
        bool BossSummon(Mobile mob)=>mob is BaseCreature {Summoned:true,Controlled:false,IsStabled:false, SummonMaster:BaseCreature {Player:false,Controlled:false,Summoned:false,IsStabled:false} master}
            && master.Spawner is BaseSpawner owner && m.Moves.Any(v=>v.Guid==owner.Guid);
        bool Protected(Mobile mob)=>mob.Player||mob is BaseCreature {Controlled:true} or BaseCreature {IsStabled:true}||mob is BaseCreature {Summoned:true}&&!BossSummon(mob);
        var mobs=World.Mobiles.Values.Where(mob=>mob.Map==Map.Felucca&&scope.Contains((mob.X,mob.Y))).ToArray();
        if(mobs.Any(Protected))throw new Exception("Player or protected creature in footprint: "+string.Join(", ",mobs.Where(Protected).Select(mob=>$"{mob.Serial} {mob.Name} at {mob.Location}")));
        foreach(var p in scope)if(BaseHouse.FindHouseAt(new Point3D(p.Item1,p.Item2,0),Map.Felucca,128)!=null)throw new Exception("House intersects footprint");
        var spawners=World.Items.Values.OfType<BaseSpawner>().Where(s=>m.Moves.Any(v=>v.Guid==s.Guid)).ToArray();
        if(spawners.Length!=5)throw new Exception("Expected exactly five live spawners");
        foreach(var s in spawners){var v=m.Moves.Single(v=>v.Guid==s.Guid);if(s.X!=v.Old[0]||s.Y!=v.Old[1]||s.Z!=v.Old[2])throw new Exception("Spawner has moved since staging");if(s.Spawned.Keys.OfType<Mobile>().Any(Protected))throw new Exception("Protected creature attached to moving spawner");}
        var items=World.Items.Values.Where(i=>i.Parent==null&&i.Map==Map.Felucca&&scope.Contains((i.X,i.Y))).ToArray();
        // Preserve all objects except the exact four obsolete fort portal pairs.
        var unknown=items.Where(i=>i is not BaseSpawner and not Teleporter and not BaseBook && !(i is Static&&!i.Movable)).ToArray();
        if(unknown.Length>0)throw new Exception("Review world items: "+string.Join(", ",unknown.Select(i=>$"{i.Serial}:{i.GetType().Name}@{i.Location}")));
        bool OldPortal(Item i)=>m.OldPortals.Any(p=>p.X==i.X&&p.Y==i.Y&&p.Z==i.Z&&p.Name==i.Name);
        var portals=World.Items.Values.Where(i=>i.Map==Map.Felucca&&i.Parent==null&&OldPortal(i)&&(i is Teleporter||i is Static&&!i.Movable)).ToArray();
        if(portals.Count(i=>i is Teleporter)!=4||portals.Count(i=>i is Static)!=4)throw new Exception("Old fort portal inventory mismatch");
        string audit=$"Audit passed: 5 spawners to relocate, 4 obsolete fort portal pairs, {mobs.Length} wild creatures in edited area; no players, pets, houses or unknown items.";
        File.WriteAllText(Path.Combine(Data,"audit.txt"),audit);Console.WriteLine("[ArborMazeV3] "+audit);
        if(!File.Exists(Path.Combine(Data,"install.pending")))return;
        if(File.Exists(Path.Combine(Data,"installed.txt")))throw new Exception("Already installed");
        var options=SpawnerJsonSerializer.Options;
        var configured=JsonConfig.Deserialize<List<SpawnerDto>>(Path.Combine(Core.BaseDirectory,"zodiac.json"), options).Where(j=>(j.Guid is var id)&&m.AllSpawnerGuids.Contains(id)).ToArray();
        var targets=configured.Select(j=>{Point3D p = j.Location;return p;}).ToArray();
        var tele=JsonConfig.Deserialize<List<TeleporterDefinition>>(Path.Combine(Core.BaseDirectory,"Data","teleporters.json")).Where(t=>t.Source.Map==Map.Felucca&&m.AllPortals.Any(p=>p.X==t.Source.X&&p.Y==t.Source.Y&&p.Z==t.Source.Z)).ToArray();
        if(targets.Length!=26||tele.Length!=11)throw new Exception("Definition count mismatch");
        var bad=new List<string>();
        foreach(var p in targets)if(!Map.Felucca.CanFit(p.X,p.Y,p.Z,16,false,false))bad.Add($"spawn {p}");
        foreach(var t in tele)foreach(var p in new[]{new Point3D(t.Source.X,t.Source.Y,t.Source.Z),new Point3D(t.Destination.X,t.Destination.Y,t.Destination.Z)})if(!Map.Felucca.CanFit(p.X,p.Y,p.Z,16,false,false))bad.Add($"portal {p}");
        if(bad.Count>0)throw new Exception("Blocked placements: "+string.Join("; ",bad));
        var walk=new Dictionary<(int,int),bool>();
        bool CanWalk(int x,int y){if(!walk.TryGetValue((x,y),out bool fit)){fit=Map.Felucca.CanFit(x,y,0,16,false,false);walk[(x,y)]=fit;}return fit;}
        HashSet<(int,int)> Reach(int sx,int sy,int blockGate){var seen=new HashSet<(int,int)>();var q=new Queue<(int,int)>();if(!CanWalk(sx,sy))throw new Exception("Blocked walking test start");seen.Add((sx,sy));q.Enqueue((sx,sy));while(q.Count>0){var p=q.Dequeue();foreach(var d in new[]{(1,0),(-1,0),(0,1),(0,-1)}){int x=p.Item1+d.Item1,y=p.Item2+d.Item2;if(x<1780||x>1952||y<884||y>992||seen.Contains((x,y)))continue;if(blockGate==1&&x==1896&&y>=949&&y<=951||blockGate==2&&y==960&&x>=1909&&x<=1911)continue;if(CanWalk(x,y)){seen.Add((x,y));q.Enqueue((x,y));}}}return seen;}
        var connected=Reach(1806,963,0);
        foreach(var p in targets.Where(p=>p.Z==0))if(!connected.Contains((p.X,p.Y)))bad.Add($"unreachable spawn {p}");
        foreach(var p in new[]{(1873,914),(1895,950),(1910,961),(1919,952)})if(!connected.Contains(p))bad.Add($"unreachable approach {p}");
        if(!Reach(1895,950,2).Contains((1920,954)))bad.Add("west entrance cannot reach courtyard independently");
        if(!Reach(1910,961,1).Contains((1920,954)))bad.Add("south entrance cannot reach courtyard independently");
        if(bad.Count>0)throw new Exception("Route checks: "+string.Join("; ",bad));
        Console.WriteLine($"[ArborMazeV3] Placement and walking checks passed ({connected.Count} reachable Z0 tiles); both fort entrances reach courtyard independently.");
        // Explicitly verified NPC-owned summons disappear with the replaced bosses;
        // never classify unknown/player-owned summons as disposable.
        foreach(var summon in World.Mobiles.Values.Where(BossSummon).ToArray())summon.Delete();
        foreach(var i in portals)i.Delete();
        foreach(var s in spawners){var v=m.Moves.Single(v=>v.Guid==s.Guid);s.MoveToWorld(new Point3D(v.New[0],v.New[1],v.New[2]),Map.Felucca);s.Respawn();}
        int rescued=0;
        foreach(var mob in mobs)if(!mob.Deleted&&!Map.Felucca.CanFit(mob.X,mob.Y,mob.Z,16,false,false)){var p=targets.Where(p=>p.Z==0).OrderBy(p=>Math.Abs(p.X-mob.X)+Math.Abs(p.Y-mob.Y)).First();mob.MoveToWorld(p,Map.Felucca);rescued++;}
        foreach(var mark in m.NewPortals){var t=tele.Single(t=>t.Source.X==mark.X&&t.Source.Y==mark.Y&&t.Source.Z==mark.Z);var p=new Point3D(mark.X,mark.Y,mark.Z);new Teleporter(new Point3D(t.Destination.X,t.Destination.Y,t.Destination.Z),Map.Felucca){Name=mark.Name}.MoveToWorld(p,Map.Felucca);new Static(0xF6C){Name=mark.Name,Hue=mark.Hue}.MoveToWorld(p,Map.Felucca);}
        File.WriteAllText(Path.Combine(Data,"installed.txt"),$"Moved 5 spawners, removed 4 old portal pairs, created 2 deck portal pairs, rescued {rescued} stranded wild creatures. Pending save.");
        Console.WriteLine("[ArborMazeV3] Installed. Saving in ten seconds.");Timer.DelayCall(TimeSpan.FromSeconds(10),World.Save);
    }
}
