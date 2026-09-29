using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;

public static class ArborMazeBuild
{
    public const int X=1792,Y=916,W=77,H=65;
    public static bool Inside(int x,int y) => (x>=X&&x<X+W&&y>=Y&&y<Y+H) || (x>=1880&&x<=1900&&y>=942&&y<=962);
    static readonly List<(int x,int y,int z,int id)> added=new();
    public static bool[,] Open;
    static void Add(int x,int y,int z,int id) => added.Add((x,y,z,id));
    static void Rect(int x,int y,int w,int h) {for(int a=x;a<x+w;a++)for(int b=y;b<y+h;b++)Open[a,b]=true;}
    public static string Stage(string source,string output)
    {
        if(Path.GetFullPath(source)==Path.GetFullPath(output))throw new Exception("Separate staging required");
        Directory.CreateDirectory(output);added.Clear();Open=new bool[W,H];
        var rng=new Random(1873908);var visited=new bool[19,16];var stack=new Stack<(int x,int y)>();stack.Push((1,15));visited[1,15]=true;
        for(int x=0;x<19;x++)for(int y=0;y<16;y++)Rect(x*4+1,y*4+1,3,3);
        while(stack.Count>0){var c=stack.Peek();var opts=new List<(int x,int y)>();foreach(var d in new[]{(1,0),(-1,0),(0,1),(0,-1)}){int a=c.x+d.Item1,b=c.y+d.Item2;if(a>=0&&a<19&&b>=0&&b<16&&!visited[a,b])opts.Add((a,b));}if(opts.Count==0){stack.Pop();continue;}var n=opts[rng.Next(opts.Count)];visited[n.x,n.y]=true;Rect(Math.Min(c.x,n.x)*4+1,Math.Min(c.y,n.y)*4+1,c.x==n.x?3:7,c.y==n.y?3:7);stack.Push(n);}
        // Sparse loops prevent the entire maze being solvable by following one wall.
        for(int k=0;k<22;k++){int a=rng.Next(18),b=rng.Next(15);if(k%2==0)Rect(a*4+1,b*4+1,7,3);else Rect(a*4+1,b*4+1,3,7);}
        foreach(var room in new[]{(2,2),(8,3),(13,9),(4,11),(16,4),(15,13)})Rect(room.Item1*4+1,room.Item2*4+1,7,7);
        Rect(5,64,3,1); // only perimeter opening, south toward Arbor
        // Validate full walkable connectivity and exactly one three-tile boundary opening.
        var seen=new HashSet<(int,int)>();var q=new Queue<(int,int)>();q.Enqueue((6,64));seen.Add((6,64));
        while(q.Count>0){var p=q.Dequeue();foreach(var d in new[]{(1,0),(-1,0),(0,1),(0,-1)}){var n=(p.Item1+d.Item1,p.Item2+d.Item2);if(n.Item1>=0&&n.Item1<W&&n.Item2>=0&&n.Item2<H&&Open[n.Item1,n.Item2]&&seen.Add(n))q.Enqueue(n);}}
        int walk=0,boundary=0;for(int x=0;x<W;x++)for(int y=0;y<H;y++)if(Open[x,y]){walk++;if(x==0||y==0||x==W-1||y==H-1)boundary++;}
        if(walk!=seen.Count||boundary!=3)throw new Exception("Maze topology invalid");
        var originalLand=File.ReadAllBytes(Path.Combine(source,"map0.mul"));var land=(byte[])originalLand.Clone();
        var oldIndex=File.ReadAllBytes(Path.Combine(source,"staidx0.mul"));var index=(byte[])oldIndex.Clone();var oldStatics=File.ReadAllBytes(Path.Combine(source,"statics0.mul"));
        if(land.Length!=77070336||index.Length!=4718592)throw new Exception("Unexpected map size");
        void SetLand(int x,int y,int id){int o=((x/8)*512+y/8)*196+4+((y%8)*8+x%8)*3;land[o]=(byte)id;land[o+1]=(byte)(id>>8);land[o+2]=0;}
        for(int x=0;x<W;x++)for(int y=0;y<H;y++){SetLand(X+x,Y+y,0xC4+(x+y)%4);if(!Open[x,y])Add(X+x,Y+y,0,(x+y)%2==0?0xDB8:0xDB9);else Add(X+x,Y+y,0,0x31F4);}
        // Clearings: keep their central three-wide cross free of blocking decoration.
        void Room(int cx,int cy,int theme){int a=X+cx*4+1,b=Y+cy*4+1;for(int u=0;u<7;u++)for(int v=0;v<7;v++)Add(a+u,b+v,0,theme==1?0x519:0x31F4);
            if(theme==0){Add(a,b,0,0xE2E);Add(a+6,b+6,0,0xE2E);Add(a+6,b,0,0xB1A);Add(a,b+6,0,0xB1A);for(int u=0;u<3;u++)for(int v=0;v<3;v++)Add(a+2+u,b+2+v,0,0xFE6+u*3+v);}
            if(theme==1){Add(a,b,0,0xA97);Add(a+1,b,0,0xA98);Add(a+5,b,0,0xA97);Add(a+6,b,0,0xA98);Add(a,b+6,0,0x1207);Add(a+6,b+6,0,0x1207);}
            if(theme==2){added.RemoveAll(t=>t.x>=a&&t.x<a+7&&t.y>=b&&t.y<b+7&&t.id==0x31F4);for(int u=0;u<7;u++)for(int v=0;v<7;v++)SetLand(a+u,b+v,0x16+(u+v)%3);Add(a,b,0,0x1207);Add(a+6,b+6,0,0x1207);Add(a+6,b,0,0x1363);Add(a,b+6,0,0x1364);}}
        Room(2,2,0);Room(8,3,1);Room(13,9,2);Room(4,11,1);Room(16,4,0);Room(15,13,2);
        // Two-floor stone tower: solid outer shell, ground floor and upper boss floor.
        for(int x=1880;x<=1900;x++)for(int y=942;y<=962;y++){
            SetLand(x,y,0xC4);bool edge=x==1880||x==1900||y==942||y==962;
            if(edge){for(int z=0;z<=40;z+=20)Add(x,y,z,x==1880||x==1900?0x80:0x81);}
            else {Add(x,y,0,0x519);Add(x,y,20,0x519);}
        }
        foreach(var p in new[]{(1882,944),(1898,944),(1882,960),(1898,960)}){Add(p.Item1,p.Item2,0,0xB1A);Add(p.Item1,p.Item2,20,0xB1A);}
        Add(1884,944,20,0xA97);Add(1885,944,20,0xA98);Add(1895,944,20,0xE2E);
        var grouped=added.GroupBy(t=>(t.x/8)*512+t.y/8).ToDictionary(g=>g.Key,g=>g.ToArray());int removed=0;
        using(var ms=new MemoryStream())using(var bw=new BinaryWriter(ms)){
            bw.Write(oldStatics);
            for(int bx=1792/8;bx<=1900/8;bx++)for(int by=916/8;by<=980/8;by++){
                int block=bx*512+by,io=block*12,o=BitConverter.ToInt32(oldIndex,io),len=BitConverter.ToInt32(oldIndex,io+4);var keep=new List<byte>();bool touched=false;
                if(o>=0&&len>0){if(len%7!=0||o+(long)len>oldStatics.Length)throw new Exception("Bad statics");for(int p=o;p<o+len;p+=7){if(Inside(bx*8+oldStatics[p+2],by*8+oldStatics[p+3])){removed++;touched=true;}else for(int j=0;j<7;j++)keep.Add(oldStatics[p+j]);}}
                if(!touched&&!grouped.ContainsKey(block))continue;int start=(int)ms.Position;bw.Write(keep.ToArray());
                if(grouped.TryGetValue(block,out var entries))foreach(var t in entries){bw.Write((ushort)t.id);bw.Write((byte)(t.x%8));bw.Write((byte)(t.y%8));bw.Write((sbyte)t.z);bw.Write((ushort)0);}
                int size=(int)ms.Position-start;Array.Copy(BitConverter.GetBytes(size==0?-1:start),0,index,io,4);Array.Copy(BitConverter.GetBytes(size),0,index,io+4,4);
            }
            File.WriteAllBytes(Path.Combine(output,"statics0.mul"),ms.ToArray());
        }
        // Exhaustive outside-footprint checks, including partially edited 8x8 blocks.
        for(int b=0;b<land.Length/196;b++)for(int c=0;c<64;c++){int x=b/512*8+c%8,y=b%512*8+c/8,o=b*196+4+c*3;if(!Inside(x,y)&&(land[o]!=originalLand[o]||land[o+1]!=originalLand[o+1]||land[o+2]!=originalLand[o+2]))throw new Exception("Outside terrain changed");}
        var ns=File.ReadAllBytes(Path.Combine(output,"statics0.mul"));
        byte[] Outside(byte[] ix,byte[] data,int b){int o=BitConverter.ToInt32(ix,b*12),n=BitConverter.ToInt32(ix,b*12+4);using var ms=new MemoryStream();if(o>=0&&n>0)for(int p=o;p<o+n;p+=7)if(!Inside(b/512*8+data[p+2],b%512*8+data[p+3]))ms.Write(data,p,7);return ms.ToArray();}
        for(int b=0;b<index.Length/12;b++)if(!Outside(oldIndex,oldStatics,b).SequenceEqual(Outside(index,ns,b)))throw new Exception("Outside statics changed");
        File.WriteAllBytes(Path.Combine(output,"map0.mul"),land);File.WriteAllBytes(Path.Combine(output,"staidx0.mul"),index);
        using var preview=new Bitmap(W*8,H*8);using var g=Graphics.FromImage(preview);g.Clear(Color.DarkOliveGreen);for(int x=0;x<W;x++)for(int y=0;y<H;y++)if(Open[x,y])g.FillRectangle(Brushes.Tan,x*8,y*8,8,8);preview.Save(Path.Combine(output,"layout.png"));
        return $"Connected walkable tiles {walk}; boundary opening 3 tiles; {removed} old statics removed; {added.Count} added; all terrain/statics outside two footprints unchanged.";
    }
}
