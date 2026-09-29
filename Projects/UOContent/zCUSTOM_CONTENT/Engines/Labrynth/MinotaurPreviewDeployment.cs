using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Server.Engines.Spawners;
using Server.Json;

namespace Server.Engines.Labrynth;

public static class MinotaurPreviewDeployment
{
    public static void Initialize()
    {
        var folder=Path.Combine(Core.BaseDirectory,"Data","MinotaurPreview");
        if(!File.Exists(Path.Combine(folder,"install.pending"))||File.Exists(Path.Combine(folder,"installed.txt")))return;
        try
        {
            var p=new Point3D(5276,715,0);
            if(!Map.Felucca.CanFit(p.X,p.Y,p.Z,16,false,true))throw new Exception("Test spawn tile blocked");
            var options=JsonConfig.GetOptions();
            var j=JsonConfig.Deserialize<List<DynamicJson>>(Path.Combine(Core.BaseDirectory,"zodiac.json")).Single(j=>j.GetProperty("name",options,out string n)&&n=="Labrynth - Tormented Minotaur Art Preview");
            j.GetProperty("guid",options,out Guid id);
            if(World.Items.Values.OfType<BaseSpawner>().Any(s=>s.Guid==id))throw new Exception("Already spawned");
            var spawner=new Spawner(j,options);spawner.MoveToWorld(p,Map.Felucca);spawner.Respawn();
            File.WriteAllText(Path.Combine(folder,"installed.txt"),"TormentedMinotaur spawner installed at Felucca 5276,715,0. Pending save.");
            Console.WriteLine("[MinotaurPreview] Installed. Saving in ten seconds.");Timer.DelayCall(TimeSpan.FromSeconds(10),World.Save);
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(folder,"failure.txt"),ex.ToString());Console.WriteLine($"[MinotaurPreview] BLOCKED: {ex}");}
    }
}
