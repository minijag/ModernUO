using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;

// V3 is an incremental patch: preserve the maze, restore native coastline,
// add the reference river's bank transitions, and translate the existing fort.
public static class ArborRefine
{
    public record Tile(int X,int Y,int Z,int Id,int Hue=0);
    static int O(int x,int y)=>((x/8)*512+y/8)*196+4+((y%8)*8+x%8)*3;
    static IEnumerable<Tile> Read(byte[] ix,byte[] st,int b){int o=BitConverter.ToInt32(ix,b*12),n=BitConverter.ToInt32(ix,b*12+4);if(o<0||n<=0)yield break;if(n%7!=0||o+(long)n>st.Length)throw new Exception("Invalid statics");for(int p=o;p<o+n;p+=7)yield return new Tile(b/512*8+st[p+2],b%512*8+st[p+3],unchecked((sbyte)st[p+4]),BitConverter.ToUInt16(st,p),BitConverter.ToUInt16(st,p+5));}
    static bool OldFort(int x,int y)=>x>=1877&&x<=1903&&y>=917&&y<=941;
    static bool NewFort(int x,int y)=>x>=1896&&x<=1924&&y>=934&&y<=960;
    static bool Cave(int x,int y)=>x>=1868&&x<=1879&&y>=902&&y<=916;
    static bool WaterArt(int id)=>id>=0x1796&&id<=0x17B5;
    public static string Stage(string source,string baseline,string output)
    {
        if(Path.GetFullPath(source)==Path.GetFullPath(output))throw new Exception("Separate staging required");
        Directory.CreateDirectory(output);
        var original=File.ReadAllBytes(Path.Combine(source,"map0.mul"));var land=(byte[])original.Clone();var ix=File.ReadAllBytes(Path.Combine(source,"staidx0.mul"));var index=(byte[])ix.Clone();var st=File.ReadAllBytes(Path.Combine(source,"statics0.mul"));
        var coast=File.ReadAllBytes(Path.Combine(baseline,"map0.mul"));var bix=File.ReadAllBytes(Path.Combine(baseline,"staidx0.mul"));var bst=File.ReadAllBytes(Path.Combine(baseline,"statics0.mul"));
        if(land.Length!=77070336||ix.Length!=4718592)throw new Exception("Wrong map dimensions");
        var scope=new HashSet<(int,int)>();var coastal=new HashSet<(int,int)>();var banks=new HashSet<(int,int)>();var replace=new HashSet<(int,int)>();var trails=new HashSet<(int,int)>();var open=new HashSet<(int,int)>();var add=new List<Tile>();
        void Set(int x,int y,int id,int z){int o=O(x,y);land[o]=(byte)id;land[o+1]=(byte)(id>>8);land[o+2]=unchecked((byte)(sbyte)z);scope.Add((x,y));}
        void Add(int x,int y,int z,int id)=>add.Add(new Tile(x,y,z,id));
        bool Wet(int x,int y){int o=O(x,y),id=BitConverter.ToUInt16(coast,o);return id is >=0xA8 and <=0xAB or 0x136 or 0x137 || unchecked((sbyte)coast[o+2])<0;}
        bool NearCoast(int x,int y){for(int a=-3;a<=3;a++)for(int b=-3;b<=3;b++)if(Wet(x+a,y+b))return true;return false;}
        // Restore the actual pre-build coastline, including native dirt transitions
        // and water overlays. Leave all existing hedges/maze scenery in place.
        for(int x=1780;x<=1952;x++)for(int y=880;y<=992;y++)if(y-x+851<-5&&!Cave(x,y)&&NearCoast(x,y)){
            int o=O(x,y);Array.Copy(coast,o,land,o,3);scope.Add((x,y));coastal.Add((x,y));
        }
        // These diagonal bank layers are sampled from the river near (662,947):
        // grass -> 0xA2/0x91 or 0xA1/0x8D dirt -> recessed 0x66 bed,
        // covered by 1797 + direction-specific 17A9/17B2 and 17AB/17B0 water.
        // They replace the jagged raised rock/water seam, not the channel routing.
        for(int x=1780;x<=1850;x++)for(int y=925;y<=992;y++){
            int d=y-x+851;if(d<-4||d>7)continue;
            // Join the ocean without constructing banks in open sea.
            if(Wet(x,y)&&!NearOriginalDry(x,y))continue;
            scope.Add((x,y));banks.Add((x,y));
            switch(d){
                case -4: Set(x,y,0x3+(x+y)%4,0);break;
                case -3: Set(x,y,0xA2,-10);break;
                case -2: Set(x,y,0x91,-10);Add(x,y,-5,0x17B2);break;
                case -1: Set(x,y,0x66,-15);Add(x,y,-5,0x17A9);break;
                case 0: Set(x,y,0x66,-15);Add(x,y,-5,0x1797);break;
                case 1: case 2: Set(x,y,0xA8+(x+y)%4,-5);break;
                case 3: Set(x,y,0x66,-15);Add(x,y,-5,0x1797);break;
                case 4: Set(x,y,0x66,-15);Add(x,y,-5,0x17AB);break;
                case 5: Set(x,y,0x8D,-10);Add(x,y,-5,0x17B0);break;
                case 6: Set(x,y,0xA1,0);break;
                case 7: Set(x,y,0x3+(x+y)%4,0);break;
            }
        }
        bool NearOriginalDry(int x,int y){for(int a=-1;a<=1;a++)for(int b=-1;b<=1;b++)if(!Wet(x+a,y+b))return true;return false;}
        // Preserve the crossing above the recessed banks, including its dry ramps.
        for(int x=1805;x<=1807;x++)for(int y=946;y<=963;y++)if(banks.Contains((x,y))){Add(x,y,0,0x519);}
        // Capture all architecture before changing either footprint.
        var architecture=new List<Tile>();
        for(int bx=1877/8;bx<=1903/8;bx++)for(int by=917/8;by<=941/8;by++)architecture.AddRange(Read(ix,st,bx*512+by).Where(t=>OldFort(t.X,t.Y)).Select(t=>t with{X=t.X+20,Y=t.Y+18}));
        for(int x=1876;x<=1924;x++)for(int y=916;y<=960;y++)if((x<=1904&&y<=944||NewFort(x,y))&&!Cave(x,y)){
            scope.Add((x,y));replace.Add((x,y));Set(x,y,0xC4+(x+y)%4,0);
        }
        // Replace the old fort site with a connected three-wide local labyrinth.
        var cells=new HashSet<(int,int)>();
        for(int x=1878;x<=1902;x+=4)for(int y=918;y<=942;y+=4){bool valid=true;for(int a=-1;a<=1;a++)for(int b=-1;b<=1;b++)if(!replace.Contains((x+a,y+b))||NewFort(x+a,y+b))valid=false;if(valid)cells.Add((x,y));}
        void Rect(int x1,int y1,int x2,int y2){for(int x=x1;x<=x2;x++)for(int y=y1;y<=y2;y++)open.Add((x,y));}
        foreach(var c in cells)Rect(c.Item1-1,c.Item2-1,c.Item1+1,c.Item2+1);
        var dirs=new[]{(4,0),(-4,0),(0,4),(0,-4)};var rng=new Random(3910947);var visited=new HashSet<(int,int)>();var stack=new Stack<(int,int)>();var start=cells.OrderBy(c=>c.Item1).ThenBy(c=>c.Item2).First();stack.Push(start);visited.Add(start);
        while(stack.Count>0){var p=stack.Peek();var opts=dirs.Select(d=>(p.Item1+d.Item1,p.Item2+d.Item2)).Where(c=>cells.Contains(c)&&!visited.Contains(c)).ToArray();if(opts.Length==0){stack.Pop();continue;}var n=opts[rng.Next(opts.Length)];Rect(Math.Min(p.Item1,n.Item1)-1,Math.Min(p.Item2,n.Item2)-1,Math.Max(p.Item1,n.Item1)+1,Math.Max(p.Item2,n.Item2)+1);visited.Add(n);stack.Push(n);}
        if(visited.Count!=cells.Count)throw new Exception("Old-fort replacement maze disconnected");
        void Trail(int x1,int y1,int x2,int y2){for(int x=x1;x<=x2;x++)for(int y=y1;y<=y2;y++){if(Cave(x,y))throw new Exception("Cave altered");scope.Add((x,y));trails.Add((x,y));open.Add((x,y));Set(x,y,0xC4,0);}}
        Trail(1873,925,1879,927);Trail(1901,925,1911,927);Trail(1889,941,1891,951);
        // Two distinct ground-level approaches: west from the central maze,
        // south from the lower maze. No entrance on the cave-facing north side.
        Trail(1889,949,1903,951);Trail(1909,956,1911,967);
        // Keep the eastern maze branch connected around the relocated fort,
        // without opening a third gap in its perimeter hedge.
        Trail(1925,917,1927,943);Trail(1925,941,1935,943);
        foreach(var p in replace){
            if(NewFort(p.Item1,p.Item2)){
                bool edge=p.Item1==1896||p.Item1==1924||p.Item2==934||p.Item2==960;
                if(edge&&!trails.Contains(p))Add(p.Item1,p.Item2,0,0xDB8);
            }
            else Add(p.Item1,p.Item2,0,open.Contains(p)?0x31F4:0xDB8);
        }
        foreach(var t in architecture)if(!(trails.Contains((t.X,t.Y))&&t.Z<16))add.Add(t);
        foreach(var p in trails)Add(p.Item1,p.Item2,0,NewFort(p.Item1,p.Item2)?0x519:0x31F4);
        using(var ms=new MemoryStream())using(var bw=new BinaryWriter(ms)){
            bw.Write(st);var groups=add.GroupBy(t=>t.X/8*512+t.Y/8).ToDictionary(g=>g.Key,g=>g.ToArray());
            for(int bx=1780/8;bx<=1952/8;bx++)for(int by=880/8;by<=992/8;by++){
                int block=bx*512+by;if(!Enumerable.Range(0,64).Any(c=>scope.Contains((bx*8+c%8,by*8+c/8))))continue;
                var keep=Read(ix,st,block).Where(t=>{
                    var p=(t.X,t.Y);if(replace.Contains(p))return false;
                    if(trails.Contains(p)&&t.Z<16)return false;
                    if(banks.Contains(p))return t.Id==0xDB8||t.Id==0xDB9; // keep hedge boundary only
                    return !coastal.Contains(p)||!WaterArt(t.Id);
                }).ToList();
                keep.AddRange(Read(bix,bst,block).Where(t=>coastal.Contains((t.X,t.Y))&&!banks.Contains((t.X,t.Y))&&WaterArt(t.Id)));
                if(groups.TryGetValue(block,out var extra))keep.AddRange(extra);
                int pos=(int)ms.Position;foreach(var t in keep.Distinct()){bw.Write((ushort)t.Id);bw.Write((byte)(t.X%8));bw.Write((byte)(t.Y%8));bw.Write((sbyte)t.Z);bw.Write((ushort)t.Hue);}
                int length=(int)ms.Position-pos;Array.Copy(BitConverter.GetBytes(length==0?-1:pos),0,index,block*12,4);Array.Copy(BitConverter.GetBytes(length),0,index,block*12+4,4);
            }
            File.WriteAllBytes(Path.Combine(output,"statics0.mul"),ms.ToArray());
        }
        for(int b=0;b<land.Length/196;b++)for(int c=0;c<64;c++){int x=b/512*8+c%8,y=b%512*8+c/8,o=b*196+4+c*3;if(!scope.Contains((x,y))&&!land.AsSpan(o,3).SequenceEqual(original.AsSpan(o,3)))throw new Exception("Outside land changed");}
        var next=File.ReadAllBytes(Path.Combine(output,"statics0.mul"));
        for(int b=0;b<index.Length/12;b++)if(!Read(ix,st,b).Where(t=>!scope.Contains((t.X,t.Y))).SequenceEqual(Read(index,next,b).Where(t=>!scope.Contains((t.X,t.Y)))))throw new Exception("Outside statics changed");
        File.WriteAllBytes(Path.Combine(output,"map0.mul"),land);File.WriteAllBytes(Path.Combine(output,"staidx0.mul"),index);
        // Only actually changed coordinates belong in the live migration audit;
        // scanning open ocean for coastline restoration does not edit that ocean.
        var oldByTile=new Dictionary<(int,int),List<Tile>>();var newByTile=new Dictionary<(int,int),List<Tile>>();
        foreach(var b in scope.Select(p=>p.Item1/8*512+p.Item2/8).Distinct()){
            foreach(var t in Read(ix,st,b)){var p=(t.X,t.Y);if(!oldByTile.TryGetValue(p,out var list))oldByTile[p]=list=new List<Tile>();list.Add(t);}
            foreach(var t in Read(index,next,b)){var p=(t.X,t.Y);if(!newByTile.TryGetValue(p,out var list))newByTile[p]=list=new List<Tile>();list.Add(t);}
        }
        var actual=scope.Where(p=>!land.AsSpan(O(p.Item1,p.Item2),3).SequenceEqual(original.AsSpan(O(p.Item1,p.Item2),3)) || !(oldByTile.TryGetValue(p,out var a)?a:Enumerable.Empty<Tile>()).SequenceEqual(newByTile.TryGetValue(p,out var b)?b:Enumerable.Empty<Tile>())).ToArray();
        File.WriteAllText(Path.Combine(output,"geometry.json"),JsonSerializer.Serialize(new{Scope=actual.Select(p=>new[]{p.Item1,p.Item2}).ToArray(),Cells=cells.Select(p=>new[]{p.Item1,p.Item2}).ToArray(),Trails=trails.Select(p=>new[]{p.Item1,p.Item2}).ToArray()}));
        return $"Restored {coastal.Count} native coastal tiles; transitioned {banks.Count} moat tiles; translated {architecture.Count} fort statics by +20,+18. {scope.Count} scoped tiles; outside map unchanged.";
    }
}
