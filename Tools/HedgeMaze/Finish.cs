using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;

public static class ArborFinish
{
    public record Tile(int X,int Y,int Z,int Id,int Hue=0);
    record Sample(int Mask,int Id,int Z,Tile[] Art);
    static int O(int x,int y)=>((x/8)*512+y/8)*196+4+((y%8)*8+x%8)*3;
    static bool Water(int id)=>id is >=0xA8 and <=0xAB or 0x66 or 0x136 or 0x137;
    static bool WaterArt(int id)=>id>=0x1796&&id<=0x17B5;
    static IEnumerable<Tile> Read(byte[] ix,byte[] st,int b){int o=BitConverter.ToInt32(ix,b*12),n=BitConverter.ToInt32(ix,b*12+4);if(o<0||n<=0)yield break;if(n%7!=0||o+(long)n>st.Length)throw new Exception("Invalid statics");for(int p=o;p<o+n;p+=7)yield return new Tile(b/512*8+st[p+2],b%512*8+st[p+3],unchecked((sbyte)st[p+4]),BitConverter.ToUInt16(st,p),BitConverter.ToUInt16(st,p+5));}
    static double Distance(int x,int y,(int,int)[] points){double best=double.MaxValue;for(int i=1;i<points.Length;i++){var a=points[i-1];var b=points[i];double dx=b.Item1-a.Item1,dy=b.Item2-a.Item2,t=Math.Clamp(((x-a.Item1)*dx+(y-a.Item2)*dy)/(dx*dx+dy*dy),0,1);best=Math.Min(best,Math.Sqrt(Math.Pow(x-a.Item1-t*dx,2)+Math.Pow(y-a.Item2-t*dy,2)));}return best;}
    static bool Rounded(int x,int y,int x1,int y1,int x2,int y2,int r){double dx=Math.Max(Math.Max(x1+r-x,0),x-(x2-r)),dy=Math.Max(Math.Max(y1+r-y,0),y-(y2-r));return dx*dx+dy*dy<=r*r;}
    public static string Stage(string source,string output)
    {
        if(Path.GetFullPath(source)==Path.GetFullPath(output))throw new Exception("Separate staging required");Directory.CreateDirectory(output);
        var original=File.ReadAllBytes(Path.Combine(source,"map0.mul"));var land=(byte[])original.Clone();var ix=File.ReadAllBytes(Path.Combine(source,"staidx0.mul"));var index=(byte[])ix.Clone();var st=File.ReadAllBytes(Path.Combine(source,"statics0.mul"));
        var scope=new HashSet<(int,int)>();var shores=new HashSet<(int,int)>();var rooms=new HashSet<(int,int)>();var add=new List<Tile>();
        void Set(int x,int y,int id,int z){int o=O(x,y);land[o]=(byte)id;land[o+1]=(byte)(id>>8);land[o+2]=unchecked((byte)(sbyte)z);scope.Add((x,y));}
        void Add(int x,int y,int z,int id){scope.Add((x,y));add.Add(new Tile(x,y,z,id));}
        bool Core(int x,int y)=>Rounded(x,y,1800,946,1812,952,2)||Rounded(x,y,1800,962,1814,969,3);
        bool Bank(int x,int y)=>Rounded(x,y,1799,945,1813,953,3)||Rounded(x,y,1799,961,1815,970,4);
        var bridgeRiver=new[]{(1795,946),(1798,952),(1801,955),(1806,957),(1811,959),(1817,964),(1819,972)};
        var mouth=new[]{(1836,985),(1841,990),(1847,996),(1855,1003)};
        bool ExistingWet(int x,int y)=>Water(BitConverter.ToUInt16(original,O(x,y)));
        bool DesiredWet(int x,int y){if(Bank(x,y))return false;return ExistingWet(x,y)||(y-x+851>=-2&&(Distance(x,y,bridgeRiver)<=3||Distance(x,y,mouth)<=4));}
        int Mask(Func<int,int,bool> wet,int x,int y){int mask=0,k=0;for(int a=-2;a<=2;a++)for(int b=-2;b<=2;b++,k++)if(wet(x+a,y+b))mask|=1<<k;return mask;}
        var waterArt=new Dictionary<(int,int),List<Tile>>();for(int bx=570/8;bx<=740/8;bx++)for(int by=870/8;by<=1020/8;by++)foreach(var t in Read(ix,st,bx*512+by).Where(t=>WaterArt(t.Id))){var p=(t.X,t.Y);if(!waterArt.TryGetValue(p,out var list))waterArt[p]=list=new List<Tile>();list.Add(t);}
        var samples=new Dictionary<int,Sample>();for(int x=575;x<=735;x++)for(int y=875;y<=1015;y++){int o=O(x,y),mask=Mask(ExistingWet,x,y),id=BitConverter.ToUInt16(original,o),z=unchecked((sbyte)original[o+2]);if(mask==0||mask==0x1FFFFFF||z>0)continue;if(id is >=0x3 and <=0x6 or >=0x70 and <=0xAB or 0x66 or >=0xC4 and <=0xCF)if(!samples.ContainsKey(mask))samples.Add(mask,new Sample(mask,id,z,waterArt.TryGetValue((x,y),out var a)?a.ToArray():Array.Empty<Tile>()));}
        for(int x=1788;x<=1864;x++)for(int y=939;y<=1010;y++){
            bool bridge=x<=1825&&y<=977,exit=x>=1829&&y>=979;if(!bridge&&!exit)continue;
            // Only alter the local shoreline, not the surrounding maze or town.
            if(!Bank(x,y)&&Distance(x,y,bridgeRiver)>6&&Distance(x,y,mouth)>7)continue;
            int desired=Mask(DesiredWet,x,y),old=Mask(ExistingWet,x,y);
            if(desired==old&&!Bank(x,y)&&Distance(x,y,mouth)>4)continue;
            if(y-x+851<-4&&!Bank(x,y)&&!ExistingWet(x,y))continue;
            scope.Add((x,y));shores.Add((x,y));
            if(Core(x,y)||desired==0){Set(x,y,0x3+(x+y)%4,0);continue;}
            if(desired==0x1FFFFFF){Set(x,y,0xA8+(x+y)%4,-5);continue;}
            if(!samples.TryGetValue(desired,out var sample))sample=samples.Values.Where(s=>((s.Mask>>12)&1)==((desired>>12)&1)).OrderBy(s=>System.Numerics.BitOperations.PopCount((uint)(s.Mask^desired))).First();
            Set(x,y,sample.Id,sample.Z);foreach(var t in sample.Art)Add(x,y,t.Z,t.Id);if(sample.Id==0x66&&sample.Art.Length==0)Add(x,y,-5,0x1797);
        }
        // Reinstate six real POIs: the tower rebuild displaced two earlier rooms.
        foreach(var room in new[]{(1886,930,0),(1834,970,1)}){
            int cx=room.Item1,cy=room.Item2;for(int x=cx-3;x<=cx+3;x++)for(int y=cy-3;y<=cy+3;y++){rooms.Add((x,y));Set(x,y,0xC4,0);Add(x,y,0,0x519);}
            if(room.Item3==0){Add(cx-3,cy-3,0,0xE2E);Add(cx+3,cy+3,0,0xE2E);Add(cx+3,cy-3,0,0xB1A);Add(cx-3,cy+3,0,0xB1A);}
            else{Add(cx-3,cy-3,0,0xA97);Add(cx-2,cy-3,0,0xA98);Add(cx+2,cy-3,0,0xA97);Add(cx+3,cy-3,0,0xA98);Add(cx-3,cy+3,0,0x1207);Add(cx+3,cy+3,0,0x1207);}
        }
        using(var ms=new MemoryStream())using(var bw=new BinaryWriter(ms)){
            bw.Write(st);var groups=add.GroupBy(t=>t.X/8*512+t.Y/8).ToDictionary(g=>g.Key,g=>g.ToArray());
            foreach(int b in scope.Select(p=>p.Item1/8*512+p.Item2/8).Distinct().OrderBy(b=>b)){
                var keep=Read(ix,st,b).Where(t=>!rooms.Contains((t.X,t.Y))&&(!shores.Contains((t.X,t.Y))||!WaterArt(t.Id)&&(!DesiredWet(t.X,t.Y)||t.Id is 0x7C9 or 0x39E or 0x13 or 0x85E or 0x85F or 0x861))).ToList();
                if(groups.TryGetValue(b,out var extra))keep.AddRange(extra);int pos=(int)ms.Position;foreach(var t in keep.Distinct()){bw.Write((ushort)t.Id);bw.Write((byte)(t.X%8));bw.Write((byte)(t.Y%8));bw.Write((sbyte)t.Z);bw.Write((ushort)t.Hue);}int n=(int)ms.Position-pos;Array.Copy(BitConverter.GetBytes(n==0?-1:pos),0,index,b*12,4);Array.Copy(BitConverter.GetBytes(n),0,index,b*12+4,4);
            }File.WriteAllBytes(Path.Combine(output,"statics0.mul"),ms.ToArray());
        }
        // Prove the river actually joins the ocean, through the bridge reach.
        bool WetOut(int x,int y)=>Water(BitConverter.ToUInt16(land,O(x,y)));
        var reached=new HashSet<(int,int)>{(1806,957)};var queue=new Queue<(int,int)>();queue.Enqueue((1806,957));while(queue.Count>0){var p=queue.Dequeue();foreach(var d in new[]{(1,0),(-1,0),(0,1),(0,-1)}){var n=(p.Item1+d.Item1,p.Item2+d.Item2);if(n.Item1>=1778&&n.Item1<=1870&&n.Item2>=925&&n.Item2<=1015&&WetOut(n.Item1,n.Item2)&&reached.Add(n))queue.Enqueue(n);}}
        if(!reached.Contains((1860,1005))||!reached.Contains((1780,930)))throw new Exception("River not continuously connected to both ocean ends");
        for(int b=0;b<land.Length/196;b++)for(int c=0;c<64;c++){int x=b/512*8+c%8,y=b%512*8+c/8,o=b*196+4+c*3;if(!scope.Contains((x,y))&&!land.AsSpan(o,3).SequenceEqual(original.AsSpan(o,3)))throw new Exception("Outside terrain changed");}
        var next=File.ReadAllBytes(Path.Combine(output,"statics0.mul"));for(int b=0;b<index.Length/12;b++)if(!Read(ix,st,b).Where(t=>!scope.Contains((t.X,t.Y))).SequenceEqual(Read(index,next,b).Where(t=>!scope.Contains((t.X,t.Y)))))throw new Exception("Outside statics changed");
        File.WriteAllBytes(Path.Combine(output,"map0.mul"),land);File.WriteAllBytes(Path.Combine(output,"staidx0.mul"),index);
        File.WriteAllText(Path.Combine(output,"geometry.json"),JsonSerializer.Serialize(new{Scope=scope.Select(p=>new[]{p.Item1,p.Item2}).ToArray(),WaterReach=reached.Count}));
        return $"{scope.Count} scoped tiles. Built-out rounded bridge landings; river flood reaches both ocean ends ({reached.Count} water tiles); two replacement POIs. Outside map unchanged.";
    }
}
