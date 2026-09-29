using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Server.Items;
using Server.Engines.Spawners;
using Server.Mobiles;

namespace Server.Engines.Labrynth;

public static class ArborShrineDeployment
{
    static string Data => Path.Combine(Core.BaseDirectory,"Data","ArborMazeV7");
    public static void Initialize()
    {
        if(!File.Exists(Path.Combine(Data,"install.pending")) || File.Exists(Path.Combine(Data,"installed.txt")))return;
        try { Install(); } catch(Exception ex) { File.WriteAllText(Path.Combine(Data,"failure.txt"),ex.ToString());Console.WriteLine($"[ArborMazeV7] BLOCKED: {ex}"); }
    }
    static void Install()
    {
        var walls=new[]{new Point3D(1876,926,0),new Point3D(1876,925,0),new Point3D(1844,913,0),new Point3D(1844,915,0),new Point3D(1893,940,0),new Point3D(1894,940,0),new Point3D(1895,940,0),new Point3D(1828,965,0),new Point3D(1828,966,0),new Point3D(1828,967,0)};
        bool Scope(int x,int y)=>walls.Any(p=>p.X==x&&p.Y==y)||(x>=1792&&x<=1798&&y>=927&&y<=933);
        var occupants=World.Mobiles.Values.Where(m=>m.Map==Map.Felucca&&Scope(m.X,m.Y)).ToArray();
        if(occupants.Any(m=>m.Player||m is BaseCreature {Controlled:true} or BaseCreature {Summoned:true} or BaseCreature {IsStabled:true}))throw new Exception("Player or pet in edited footprint; installation paused");
        var old=World.Items.Values.OfType<Static>().Where(i=>i.Map==Map.Felucca&&i.Name=="Whispering Shrine"&&i.Z==0&&((i.X==1793||i.X==1795)&&(i.Y==928||i.Y==932))).ToArray();
        if(old.Length!=4)throw new Exception("Expected exactly four previous shrine decorations");
        var unknown=World.Items.Values.Where(i=>i.Parent==null&&i.Map==Map.Felucca&&Scope(i.X,i.Y)&&i is not BaseSpawner&&!old.Contains(i)).ToArray();
        if(unknown.Length>0)throw new Exception("Unexpected items: "+string.Join(",",unknown.Select(i=>$"{i.Serial}:{i.Location}")));
        var spawns=World.Items.Values.OfType<BaseSpawner>().Where(s=>s.Map==Map.Felucca&&s.Name!=null&&s.Name.StartsWith("Arbor Labrynth")).Select(s=>s.Location).ToArray();
        var gates=World.Items.Values.OfType<Teleporter>().Where(t=>t.Map==Map.Felucca&&t.X>=1780&&t.X<=1950&&t.Y>=890&&t.Y<=990&&t.Y!=908).Select(t=>t.Location);
        var targets=spawns.Concat(gates).Append(new Point3D(1873,914,0)).ToArray();
        var reached=new HashSet<Point3D>();var q=new Queue<Point3D>();var start=new Point3D(1806,965,0);reached.Add(start);q.Enqueue(start);
        var probe=new Mobile {Body=0x190};
        try
        {
            while(q.Count>0)
            {
                var p=q.Dequeue();probe.Location=p;
                foreach(var d in new[]{Direction.North,Direction.East,Direction.South,Direction.West})
                {
                    int x=p.X,y=p.Y;Server.Movement.Movement.Offset(d,ref x,ref y);
                    if(x<1778||x>1952||y<884||y>1010)continue;
                    if(Server.Movement.Movement.CheckMovement(probe,Map.Felucca,p,d,out int z)&&z>=-20&&z<=60)
                    {var next=new Point3D(x,y,z);if(reached.Add(next))q.Enqueue(next);}
                }
            }
        }
        finally {probe.Delete();}
        var missing=targets.Where(p=>!reached.Contains(p)).ToArray();
        if(missing.Length>0)throw new Exception("Unreachable targets: "+string.Join(", ",missing.Select(p=>p.ToString())));
        foreach(var wall in walls)if(Map.Felucca.CanFit(wall.X,wall.Y,wall.Z,16,false,false))throw new Exception($"Missing hedge {wall}");
        foreach(var i in old)i.Delete();
        foreach(var m in occupants)if(!Map.Felucca.CanFit(m.X,m.Y,m.Z,16,false,false))m.MoveToWorld(spawns.Where(p=>p.Z==0).OrderBy(p=>Math.Abs(p.X-m.X)+Math.Abs(p.Y-m.Y)).First(),Map.Felucca);
        File.WriteAllText(Path.Combine(Data,"installed.txt"),$"Ten hedge tiles, 7x7 shrine, four old decorations removed. Movement passed: {reached.Count} positions, {targets.Length} encounter/gate targets. Pending save.");
        Console.WriteLine("[ArborMazeV7] Installed and validated. Saving in ten seconds.");Timer.DelayCall(TimeSpan.FromSeconds(10),World.Save);
    }
}
