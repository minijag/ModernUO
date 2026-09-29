using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;

// Stages V2 from the current map; the pre-V1 backup supplies only the original
// coastline and the land/statics restored where the old rectangular build ends.
public static class ArborExpansion
{
    const int L=1780,T=880,R=1952,B=992;
    public record Tile(int X,int Y,int Z,int Id,int Hue=0);
    static int Offset(int x,int y)=>((x/8)*512+y/8)*196+4+((y%8)*8+x%8)*3;
    static bool Old(int x,int y)=>(x>=1792&&x<=1868&&y>=916&&y<=980)||(x>=1880&&x<=1900&&y>=942&&y<=962);
    static bool Fort(int x,int y)=>x>=1877&&x<=1903&&y>=917&&y<=941;
    static bool Cave(int x,int y)=>x>=1868&&x<=1879&&y>=902&&y<=916;
    static IEnumerable<Tile> Read(byte[] ix,byte[] st,int block)
    {
        int o=BitConverter.ToInt32(ix,block*12),n=BitConverter.ToInt32(ix,block*12+4);
        if(o<0||n<=0)yield break;
        if(n%7!=0||o+(long)n>st.Length)throw new Exception("Invalid static index");
        for(int p=o;p<o+n;p+=7)yield return new Tile(block/512*8+st[p+2],block%512*8+st[p+3],unchecked((sbyte)st[p+4]),BitConverter.ToUInt16(st,p),BitConverter.ToUInt16(st,p+5));
    }
    public static string Stage(string source,string baseline,string output)
    {
        if(Path.GetFullPath(source)==Path.GetFullPath(output))throw new Exception("Must stage separately");
        Directory.CreateDirectory(output);
        var original=File.ReadAllBytes(Path.Combine(source,"map0.mul"));var land=(byte[])original.Clone();
        var coast=File.ReadAllBytes(Path.Combine(baseline,"map0.mul"));
        var ix=File.ReadAllBytes(Path.Combine(source,"staidx0.mul"));var index=(byte[])ix.Clone();var st=File.ReadAllBytes(Path.Combine(source,"statics0.mul"));
        var bix=File.ReadAllBytes(Path.Combine(baseline,"staidx0.mul"));var bst=File.ReadAllBytes(Path.Combine(baseline,"statics0.mul"));
        if(land.Length!=77070336||ix.Length!=4718592)throw new Exception("Wrong map dimensions");
        bool Dry(int x,int y){int o=Offset(x,y),id=BitConverter.ToUInt16(coast,o);return id is not (>=0xA8 and <=0xAB) and not 0x136 and not 0x137 && unchecked((sbyte)coast[o+2])>=-2;}
        int Depth(int x,int y){for(int d=0;d<=5;d++)for(int u=-d;u<=d;u++)for(int v=-d;v<=d;v++)if(!Dry(x+u,y+v))return d;return 6;}
        var scope=new HashSet<(int,int)>();var ground=new HashSet<(int,int)>();var paths=new HashSet<(int,int)>();var additions=new List<Tile>();
        void Set(int x,int y,int id,int z){int o=Offset(x,y);land[o]=(byte)id;land[o+1]=(byte)(id>>8);land[o+2]=unchecked((byte)(sbyte)z);scope.Add((x,y));}
        void Add(int x,int y,int z,int id)=>additions.Add(new Tile(x,y,z,id));
        // Natural outline, no rectangular land extension. A diagonal moat cuts the neck.
        for(int x=L;x<=R;x++)for(int y=T;y<=B;y++){
            int d=y-x+851;
            if(Old(x,y)){int o=Offset(x,y);Array.Copy(coast,o,land,o,3);scope.Add((x,y));}
            if(Cave(x,y))continue;
            if(Fort(x,y)){scope.Add((x,y));continue;}
            if(d<=-4&&Dry(x,y)){
                scope.Add((x,y));int depth=Depth(x,y);
                // Raised rock rim with a steep seaward face, rather than grass
                // ending directly in water. Walkable maze remains at Z0 inland.
                if(depth<=2){Set(x,y,0xE4+(x+y)%4,depth==1?-5:8);}
                else {Set(x,y,0xC4+(x+y)%4,0);ground.Add((x,y));}
            }
            if(d>=-3&&d<=7&&Dry(x,y)){
                if(d==-3||d==7)Set(x,y,0xE4+(x+y)%4,0);
                else Set(x,y,0xA8+(x+y)%4,-5);
            }
        }
        // Entire cells must fit dry inland ground, preserving three-wide corridors.
        var cells=new HashSet<(int,int)>();
        for(int cx=0;cx<42;cx++)for(int cy=0;cy<26;cy++){
            int x=1780+cx*4,y=884+cy*4;bool valid=true;
            for(int a=0;a<=4;a++)for(int b=0;b<=4;b++)if(!ground.Contains((x+a,y+b)))valid=false;
            if(valid)cells.Add((cx,cy));
        }
        var dirs=new[]{(1,0),(-1,0),(0,1),(0,-1)};
        var start=(6,15);if(!cells.Contains(start))throw new Exception("Bridge cell absent");
        var connected=new HashSet<(int,int)>{start};var queue=new Queue<(int,int)>();queue.Enqueue(start);
        while(queue.Count>0){var p=queue.Dequeue();foreach(var d in dirs){var n=(p.Item1+d.Item1,p.Item2+d.Item2);if(cells.Contains(n)&&connected.Add(n))queue.Enqueue(n);}}
        cells=connected;
        void Rect(int x,int y,int w,int h){for(int a=x;a<x+w;a++)for(int b=y;b<y+h;b++){if(!ground.Contains((a,b)))throw new Exception($"Path off land {a},{b}");paths.Add((a,b));}}
        foreach(var c in cells)Rect(1781+c.Item1*4,885+c.Item2*4,3,3);
        void Link((int,int) a,(int,int)b)=>Rect(1781+Math.Min(a.Item1,b.Item1)*4,885+Math.Min(a.Item2,b.Item2)*4,a.Item1==b.Item1?3:7,a.Item2==b.Item2?3:7);
        var rng=new Random(20260928);var visited=new HashSet<(int,int)>{start};var stack=new Stack<(int,int)>();stack.Push(start);
        while(stack.Count>0){var p=stack.Peek();var opts=dirs.Select(d=>(p.Item1+d.Item1,p.Item2+d.Item2)).Where(n=>cells.Contains(n)&&!visited.Contains(n)).ToArray();if(opts.Length==0){stack.Pop();continue;}var n=opts[rng.Next(opts.Length)];Link(p,n);visited.Add(n);stack.Push(n);}
        for(int k=0;k<45;k++){var c=cells.OrderBy(p=>p.Item1).ThenBy(p=>p.Item2).ElementAt(rng.Next(cells.Count));var d=dirs[rng.Next(4)];var n=(c.Item1+d.Item1,c.Item2+d.Item2);if(cells.Contains(n))Link(c,n);}
        var rooms=new List<(int,int)>();
        foreach(var target in new[]{(13,7),(17,13),(22,18),(29,18),(34,12),(30,7)}){
            var c=cells.Where(c=>cells.Contains((c.Item1+1,c.Item2))&&cells.Contains((c.Item1,c.Item2+1))&&cells.Contains((c.Item1+1,c.Item2+1))).OrderBy(c=>Math.Abs(c.Item1-target.Item1)+Math.Abs(c.Item2-target.Item2)).First();
            Rect(1781+c.Item1*4,885+c.Item2*4,7,7);rooms.Add(c);
        }
        // Single bridge entrance, northern cave trail and fort doorway approach.
        for(int x=1805;x<=1807;x++)for(int y=946;y<=963;y++){scope.Add((x,y));paths.Add((x,y));if(y-x+851>=-2&&y-x+851<=6)Add(x,y,0,0x519);else Set(x,y,0xC4,0);}
        void Trail(int x1,int y1,int x2,int y2){for(int x=x1;x<=x2;x++)for(int y=y1;y<=y2;y++){if(Cave(x,y))continue;scope.Add((x,y));ground.Add((x,y));paths.Add((x,y));Set(x,y,0xC4,0);}}
        // Connect via nearest maze cell; corridors end immediately below the original cave slope.
        var caveCell=cells.OrderBy(c=>Math.Abs(1782+c.Item1*4-1874)+Math.Abs(886+c.Item2*4-922)).First();
        int caveX=1782+caveCell.Item1*4,caveY=886+caveCell.Item2*4;
        Trail(Math.Min(caveX,1874)-1,caveY-1,Math.Max(caveX,1874)+1,caveY+1);Trail(1873,917,1875,caveY+1);
        foreach(var p in ground)if(!paths.Contains(p))Add(p.Item1,p.Item2,0,(p.Item1+p.Item2)%2==0?0xDB8:0xDB9);
        foreach(var p in paths)if(ground.Contains(p))Add(p.Item1,p.Item2,0,0x31F4);
        var roomData=new List<object>();int theme=0;
        foreach(var c in rooms){int x=1781+c.Item1*4,y=885+c.Item2*4;int t=theme++%3;
            if(t==0){Add(x,y,0,0xE2E);Add(x+6,y+6,0,0xE2E);Add(x+6,y,0,0xB1A);Add(x,y+6,0,0xB1A);}
            if(t==1){Add(x,y,0,0xA97);Add(x+1,y,0,0xA98);Add(x+5,y,0,0xA97);Add(x+6,y,0,0xA98);Add(x,y+6,0,0x1207);Add(x+6,y+6,0,0x1207);}
            if(t==2){additions.RemoveAll(a=>a.X>=x&&a.X<=x+6&&a.Y>=y&&a.Y<=y+6&&a.Id==0x31F4);for(int a=x;a<=x+6;a++)for(int b=y;b<=y+6;b++)Set(a,b,0x16+(a+b)%3,0);Add(x,y,0,0x1363);Add(x+6,y+6,0,0x1364);}
            roomData.Add(new {X=x+3,Y=y+3,Theme=t});
        }
        var reach=new HashSet<(int,int)>{(1806,963)};queue.Clear();queue.Enqueue((1806,963));
        while(queue.Count>0){var p=queue.Dequeue();foreach(var d in dirs){var n=(p.Item1+d.Item1,p.Item2+d.Item2);if(paths.Contains(n)&&reach.Add(n))queue.Enqueue(n);}}
        if(reach.Count!=paths.Count||paths.Count(p=>p.Item2-p.Item1+851==0)!=3)throw new Exception("Disconnected paths or extra moat crossing");
        int Convert(int id)=>id switch {
            0x226=>0x59,0x227=>0x57,0x228=>0x58,0x229=>0x5A,
            0x22A=>0x63,0x22C=>0x6D,0x22D=>0x5B,0x22E=>0x5C,0x22F=>0x6E,
            0x21D=>0x67,0x423=>0x63,0x424=>0x64,
            >=0x4A9 and <=0x4AC=>0x519,
            >=0x7C9 and <=0x7D0=>0x519,
            0x5A4=>0x59D,0x5A6=>0x59F,0x738=>0x71E,0x87B=>0x71F,
            0x8A2=>0x77,0x8A6=>0x77,
            _=>id};
        // Keep the actual fort architecture and elevation; strip the orc clutter.
        bool Clutter(int id)=>id is >=0x420 and <=0x434 or >=0x1B09 and <=0x1B1C or >=0x1CEF and <=0x1CF2 or >=0x122A and <=0x122D or 0xECE or 0xE23 or 0xC19 or 0x1DB4 or 0x1DB6 or 0x4985 or 0x4988;
        using(var ms=new MemoryStream())using(var bw=new BinaryWriter(ms)){
            bw.Write(st);var groups=additions.GroupBy(a=>(a.X/8)*512+a.Y/8).ToDictionary(g=>g.Key,g=>g.ToList());
            for(int bx=L/8;bx<=R/8;bx++)for(int by=T/8;by<=B/8;by++){
                int block=bx*512+by;if(!Enumerable.Range(0,64).Any(c=>scope.Contains((bx*8+c%8,by*8+c/8))))continue;
                var keep=Read(ix,st,block).Where(a=>!scope.Contains((a.X,a.Y))).ToList();
                foreach(var a in Read(bix,bst,block))if(scope.Contains((a.X,a.Y))){
                    if(Fort(a.X,a.Y)){if(Clutter(a.Id))continue;int id=Convert(a.Id);keep.Add(a with {Id=id});}
                    else if(!ground.Contains((a.X,a.Y))&&!paths.Contains((a.X,a.Y)) && (a.Y-a.X+851>7 || Cave(a.X,a.Y)))keep.Add(a);
                }
                if(groups.TryGetValue(block,out var extra))keep.AddRange(extra);
                int pos=(int)ms.Position;foreach(var a in keep){bw.Write((ushort)a.Id);bw.Write((byte)(a.X%8));bw.Write((byte)(a.Y%8));bw.Write((sbyte)a.Z);bw.Write((ushort)a.Hue);}
                int length=(int)ms.Position-pos;Array.Copy(BitConverter.GetBytes(length==0?-1:pos),0,index,block*12,4);Array.Copy(BitConverter.GetBytes(length),0,index,block*12+4,4);
            }
            File.WriteAllBytes(Path.Combine(output,"statics0.mul"),ms.ToArray());
        }
        for(int b=0;b<land.Length/196;b++)for(int c=0;c<64;c++){int x=b/512*8+c%8,y=b%512*8+c/8,o=b*196+4+c*3;if(!scope.Contains((x,y))&&!land.AsSpan(o,3).SequenceEqual(original.AsSpan(o,3)))throw new Exception("Outside land changed");}
        var next=File.ReadAllBytes(Path.Combine(output,"statics0.mul"));
        for(int b=0;b<index.Length/12;b++)if(!Read(ix,st,b).Where(a=>!scope.Contains((a.X,a.Y))).SequenceEqual(Read(index,next,b).Where(a=>!scope.Contains((a.X,a.Y)))))throw new Exception("Outside statics changed");
        File.WriteAllBytes(Path.Combine(output,"map0.mul"),land);File.WriteAllBytes(Path.Combine(output,"staidx0.mul"),index);
        var json=new {Cells=cells.OrderBy(c=>c.Item1).ThenBy(c=>c.Item2).Select(c=>new {X=1782+c.Item1*4,Y=886+c.Item2*4}).ToArray(),Rooms=roomData,Scope=scope.OrderBy(p=>p.Item1).ThenBy(p=>p.Item2).Select(p=>new[]{p.Item1,p.Item2}).ToArray(),Paths=paths.Select(p=>new[]{p.Item1,p.Item2}).ToArray()};
        File.WriteAllText(Path.Combine(output,"geometry.json"),JsonSerializer.Serialize(json));
        return $"{cells.Count} connected maze cells; {paths.Count} path tiles; {scope.Count} edited tiles. Outside terrain/statics unchanged.";
    }
}
