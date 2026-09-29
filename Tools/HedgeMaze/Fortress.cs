using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;

public static class ArborFortress
{
    public record Tile(int X,int Y,int Z,int Id,int Hue=0);
    record Sample(int Mask,int Id,int Z,Tile[] Water);
    static int O(int x,int y)=>((x/8)*512+y/8)*196+4+((y%8)*8+x%8)*3;
    static IEnumerable<Tile> Read(byte[] ix,byte[] st,int b){int o=BitConverter.ToInt32(ix,b*12),n=BitConverter.ToInt32(ix,b*12+4);if(o<0||n<=0)yield break;if(n%7!=0||o+(long)n>st.Length)throw new Exception("Invalid statics");for(int p=o;p<o+n;p+=7)yield return new Tile(b/512*8+st[p+2],b%512*8+st[p+3],unchecked((sbyte)st[p+4]),BitConverter.ToUInt16(st,p),BitConverter.ToUInt16(st,p+5));}
    static bool Water(int id)=>id is >=0xA8 and <=0xAB or 0x66 or 0x136 or 0x137;
    static bool WaterArt(int id)=>id>=0x1796&&id<=0x17B5;
    public static string Stage(string source,string baseline,string output)
    {
        if(Path.GetFullPath(source)==Path.GetFullPath(output))throw new Exception("Separate staging required");Directory.CreateDirectory(output);
        var original=File.ReadAllBytes(Path.Combine(source,"map0.mul"));var land=(byte[])original.Clone();var ix=File.ReadAllBytes(Path.Combine(source,"staidx0.mul"));var index=(byte[])ix.Clone();var st=File.ReadAllBytes(Path.Combine(source,"statics0.mul"));var coast=File.ReadAllBytes(Path.Combine(baseline,"map0.mul"));
        if(land.Length!=77070336||ix.Length!=4718592)throw new Exception("Wrong map dimensions");
        var scope=new HashSet<(int,int)>();var clear=new HashSet<(int,int)>();var add=new List<Tile>();
        void Set(int x,int y,int id,int z){int o=O(x,y);land[o]=(byte)id;land[o+1]=(byte)(id>>8);land[o+2]=unchecked((byte)(sbyte)z);scope.Add((x,y));}
        void Add(int x,int y,int z,int id){scope.Add((x,y));add.Add(new Tile(x,y,z,id));}
        bool NativeWet(int x,int y)=>Water(BitConverter.ToUInt16(coast,O(x,y)));
        int Bend(int y){double t=Math.Clamp((y-929)/61.0,0,1);return (int)Math.Round(7*Math.Pow(Math.Sin(Math.PI*t),2)*(1-Math.Cos(2*Math.PI*(y-956)/40.0))/2);}
        bool NewWet(int x,int y){int q=y-x+851-Bend(y);return NativeWet(x,y)||(y>=928&&y<=991&&q>=-1&&q<=4);}
        int Mask(Func<int,int,bool> wet,int x,int y){int bits=0,k=0;for(int a=-2;a<=2;a++)for(int b=-2;b<=2;b++,k++)if(wet(x+a,y+b))bits|=1<<k;return bits;}
        // Learn full 5x5 shoreline neighborhoods from the requested native river.
        // This picks directional corner/straight artwork for the actual bends,
        // instead of applying one diagonal edge tile to every bend.
        var refWater=new Dictionary<(int,int),List<Tile>>();
        for(int bx=570/8;bx<=740/8;bx++)for(int by=870/8;by<=1020/8;by++)foreach(var t in Read(ix,st,bx*512+by).Where(t=>WaterArt(t.Id))){var p=(t.X,t.Y);if(!refWater.TryGetValue(p,out var list))refWater[p]=list=new List<Tile>();list.Add(t);}
        var samples=new Dictionary<int,Sample>();
        for(int x=575;x<=735;x++)for(int y=875;y<=1015;y++){int mask=Mask(NativeWet,x,y),o=O(x,y),id=BitConverter.ToUInt16(coast,o),z=unchecked((sbyte)coast[o+2]);if(mask==0||mask==0x1FFFFFF||z>0)continue;if(id is >=0x3 and <=0x6 or >=0x70 and <=0xA7 or 0x66 or >=0xA8 and <=0xAB or >=0xC4 and <=0xCF){if(!samples.ContainsKey(mask))samples.Add(mask,new Sample(mask,id,z,refWater.TryGetValue((x,y),out var w)?w.ToArray():Array.Empty<Tile>()));}}
        int unmatched=0;
        for(int x=1778;x<=1856;x++)for(int y=925;y<=992;y++){
            int d=y-x+851,q=d-Bend(y);if(d<-5||d>19||q<-4&&d>8)continue;
            bool oldChannel=d>=-4&&d<=7,newChannel=q>=-4&&q<=7;
            if(!oldChannel&&!newChannel)continue;
            // The original maze hedge remains the northern boundary. Meanders
            // bow into the mainland side, avoiding the maze's working corridors.
            if(d<=-4)continue;
            scope.Add((x,y));clear.Add((x,y));int o=O(x,y);
            if(!newChannel){Array.Copy(coast,o,land,o,3);continue;}
            int mask=Mask(NewWet,x,y);
            if(mask==0){Set(x,y,0x3+(x+y)%4,0);continue;}
            if(mask==0x1FFFFFF){Set(x,y,0xA8+(x+y)%4,-5);continue;}
            if(!samples.TryGetValue(mask,out var sample)){
                unmatched++;sample=samples.Values.Where(s=>((s.Mask>>12)&1)==((mask>>12)&1)).OrderBy(s=>System.Numerics.BitOperations.PopCount((uint)(s.Mask^mask))).First();
            }
            Set(x,y,sample.Id,sample.Z);foreach(var t in sample.Water)Add(x,y,t.Z,t.Id);
        }
        // Copy the requested real bridge: same planks, beams, posts and rails.
        for(int x=1803;x<=1809;x++)for(int y=947;y<=965;y++){scope.Add((x,y));clear.Add((x,y));if(y<=950||y>=964)Set(x,y,0x3,0);}
        int bridgeTiles=0;
        for(int bx=580/8;bx<=586/8;bx++)for(int by=1405/8;by<=1417/8;by++)foreach(var t in Read(ix,st,bx*512+by))if(t.X>=580&&t.X<=586&&t.Y>=1405&&t.Y<=1417&&t.Id is 0x7C9 or 0x39E or 0x13 or 0x85E or 0x85F or 0x861){Add(t.X+1223,t.Y-454,t.Z,t.Id);bridgeTiles++;}
        // Keep river overlays under the copied bridge, but remove the old stone deck.
        // Fort perimeter and its two existing entrances remain in the same place.
        for(int x=1896;x<=1924;x++)for(int y=934;y<=960;y++){scope.Add((x,y));clear.Add((x,y));Set(x,y,0xC4,0);bool edge=x==1896||x==1924||y==934||y==960;bool gate=x==1896&&y>=949&&y<=951||y==960&&x>=1909&&x<=1911;if(edge&&!gate)Add(x,y,0,0xDB8);}
        bool Short(int x,int y)=>x>=1898&&x<=1906&&y>=936&&y<=944;
        bool Tall(int x,int y)=>x>=1914&&x<=1922&&y>=949&&y<=957;
        void Tower(int x1,int y1,int top){
            for(int x=x1;x<=x1+8;x++)for(int y=y1;y<=y1+8;y++){
                bool edge=x==x1||x==x1+8||y==y1||y==y1+8;
                for(int z=0;z<=top;z+=20){
                    bool shaft=top==40&&z==40&&x>=1919&&x<=1921&&y>=951&&y<=955;
                    if(!shaft)Add(x,y,z,0x519);
                    if(!edge)continue;
                    bool opening=top==20&&(z==0&&y==944&&x>=1901&&x<=1903||z==20&&y==944&&x>=1900&&x<=1902||z==20&&x==1906&&y>=939&&y<=941)
                        ||top==40&&(z==0&&x==1914&&y>=952&&y<=954||z==20&&y==949&&x>=1916&&x<=1918);
                    if(!opening)Add(x,y,z,z==top?(x==x1||x==x1+8?0x63:0x64):(x==x1||x==x1+8?0x57:0x58));
                }
            }
        }
        Tower(1898,936,20);Tower(1914,949,40);
        // Five-wide skywalk with three unobstructed tiles between its parapets.
        for(int x=1907;x<=1920;x++)for(int y=938;y<=942;y++){Add(x,y,20,0x519);if(y==938||y==942&&(x<1915||x>1919)||x==1920)Add(x,y,20,y==938||y==942?0x64:0x63);}
        for(int x=1915;x<=1919;x++)for(int y=943;y<=948;y++){Add(x,y,20,0x519);if(x==1915||x==1919)Add(x,y,20,0x63);}
        // Four five-high risers, with full head clearance and no ceiling over
        // the stairwell. Surface Zs are tested using the actual movement engine.
        for(int x=1900;x<=1902;x++)for(int step=0;step<4;step++){for(int z=0;z<step*5;z+=5)Add(x,948-step,z,0x71E);Add(x,948-step,step*5,0x71F);}
        for(int x=1919;x<=1921;x++)for(int step=0;step<4;step++){for(int z=20;z<20+step*5;z+=5)Add(x,954-step,z,0x71E);Add(x,954-step,20+step*5,0x71F);}
        foreach(var p in new[]{(1908,938),(1915,942),(1919,946)})Add(p.Item1,p.Item2,0,0x77);
        // Open courtyard paths between the two distinct entrances and stairs.
        for(int x=1897;x<=1913;x++)for(int y=949;y<=951;y++)if(!Short(x,y)&&!Tall(x,y))Add(x,y,0,0x519);
        for(int x=1909;x<=1911;x++)for(int y=952;y<=960;y++)Add(x,y,0,0x519);
        using(var ms=new MemoryStream())using(var bw=new BinaryWriter(ms)){
            bw.Write(st);var groups=add.GroupBy(t=>t.X/8*512+t.Y/8).ToDictionary(g=>g.Key,g=>g.ToArray());
            foreach(int b in scope.Select(p=>p.Item1/8*512+p.Item2/8).Distinct().OrderBy(b=>b)){
                var keep=Read(ix,st,b).Where(t=>!clear.Contains((t.X,t.Y))).ToList();if(groups.TryGetValue(b,out var extra))keep.AddRange(extra);
                int pos=(int)ms.Position;foreach(var t in keep.Distinct()){bw.Write((ushort)t.Id);bw.Write((byte)(t.X%8));bw.Write((byte)(t.Y%8));bw.Write((sbyte)t.Z);bw.Write((ushort)t.Hue);}int n=(int)ms.Position-pos;Array.Copy(BitConverter.GetBytes(n==0?-1:pos),0,index,b*12,4);Array.Copy(BitConverter.GetBytes(n),0,index,b*12+4,4);
            }File.WriteAllBytes(Path.Combine(output,"statics0.mul"),ms.ToArray());
        }
        for(int b=0;b<land.Length/196;b++)for(int c=0;c<64;c++){int x=b/512*8+c%8,y=b%512*8+c/8,o=b*196+4+c*3;if(!scope.Contains((x,y))&&!land.AsSpan(o,3).SequenceEqual(original.AsSpan(o,3)))throw new Exception("Outside terrain changed");}
        var next=File.ReadAllBytes(Path.Combine(output,"statics0.mul"));for(int b=0;b<index.Length/12;b++)if(!Read(ix,st,b).Where(t=>!scope.Contains((t.X,t.Y))).SequenceEqual(Read(index,next,b).Where(t=>!scope.Contains((t.X,t.Y)))))throw new Exception("Outside statics changed");
        File.WriteAllBytes(Path.Combine(output,"map0.mul"),land);File.WriteAllBytes(Path.Combine(output,"staidx0.mul"),index);
        File.WriteAllText(Path.Combine(output,"geometry.json"),JsonSerializer.Serialize(new{Scope=scope.Select(p=>new[]{p.Item1,p.Item2}).ToArray(),Platforms=add.Where(t=>t.Id==0x519&&t.Z>=20).Select(t=>new[]{t.X,t.Y,t.Z}).Distinct().ToArray(),Checks=new[]{new[]{1806,965,0},new[]{1806,949,0},new[]{1901,949,0},new[]{1901,943,20},new[]{1917,940,20},new[]{1917,952,20},new[]{1920,950,40},new[]{1916,953,40}}}));
        return $"Copied {bridgeTiles} bridge components; {samples.Count} native river patterns ({unmatched} nearest-pattern matches); two towers at Z20/Z40 with real stairs. {scope.Count} scoped tiles; outside map unchanged.";
    }
}
