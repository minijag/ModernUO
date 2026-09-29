using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

public static class ArborShrine
{
    record Tile(int X,int Y,int Z,int Id,int Hue=0);
    static IEnumerable<Tile> Read(byte[] ix,byte[] st,int b)
    {
        int o=BitConverter.ToInt32(ix,b*12),n=BitConverter.ToInt32(ix,b*12+4);
        if(o<0||n<=0)yield break;
        if(n%7!=0||o+(long)n>st.Length)throw new Exception("Invalid statics");
        for(int p=o;p<o+n;p+=7)yield return new Tile(b/512*8+st[p+2],b%512*8+st[p+3],unchecked((sbyte)st[p+4]),BitConverter.ToUInt16(st,p),BitConverter.ToUInt16(st,p+5));
    }
    public static string Stage(string source,string output)
    {
        if(Path.GetFullPath(source)==Path.GetFullPath(output)||Directory.Exists(output))throw new Exception("Need unused staging directory");
        Directory.CreateDirectory(output);
        var land=File.ReadAllBytes(Path.Combine(source,"map0.mul"));var original=(byte[])land.Clone();
        var ix=File.ReadAllBytes(Path.Combine(source,"staidx0.mul"));var index=(byte[])ix.Clone();var st=File.ReadAllBytes(Path.Combine(source,"statics0.mul"));
        var walls=new[]{(1876,926),(1876,925),(1844,913),(1844,915),(1893,940),(1894,940),(1895,940),(1828,965),(1828,966),(1828,967)};
        var scope=new HashSet<(int,int)>(walls);var room=new HashSet<(int,int)>();var add=new List<Tile>();
        foreach(var p in walls)add.Add(new Tile(p.Item1,p.Item2,0,0xDB8));
        for(int x=1792;x<=1798;x++)for(int y=927;y<=933;y++)
        {
            scope.Add((x,y));room.Add((x,y));int o=((x/8)*512+y/8)*196+4+((y%8)*8+x%8)*3;
            if(unchecked((sbyte)land[o+2])!=0)throw new Exception("Shrine terrain not flat");
            land[o]=0xC4;land[o+1]=0;land[o+2]=0;add.Add(new Tile(x,y,0,0x519));
        }
        // Decorations at the clearing corners leave its center and approach lanes open.
        add.AddRange(new[]{new Tile(1792,927,0,0xE2E),new Tile(1798,933,0,0xE2E),new Tile(1798,927,0,0xB1A),new Tile(1792,933,0,0xB1A)});
        using(var ms=new MemoryStream())using(var bw=new BinaryWriter(ms))
        {
            bw.Write(st);
            foreach(int b in scope.Select(p=>p.Item1/8*512+p.Item2/8).Distinct())
            {
                var keep=Read(ix,st,b).Where(t=>!room.Contains((t.X,t.Y))).ToList();
                keep.AddRange(add.Where(t=>t.X/8*512+t.Y/8==b));int pos=(int)ms.Position;
                foreach(var t in keep.Distinct()){bw.Write((ushort)t.Id);bw.Write((byte)(t.X%8));bw.Write((byte)(t.Y%8));bw.Write((sbyte)t.Z);bw.Write((ushort)t.Hue);}
                Array.Copy(BitConverter.GetBytes(pos),0,index,b*12,4);Array.Copy(BitConverter.GetBytes((int)ms.Position-pos),0,index,b*12+4,4);
            }
            File.WriteAllBytes(Path.Combine(output,"statics0.mul"),ms.ToArray());
        }
        var next=File.ReadAllBytes(Path.Combine(output,"statics0.mul"));
        for(int b=0;b<index.Length/12;b++)if(!Read(ix,st,b).Where(t=>!scope.Contains((t.X,t.Y))).SequenceEqual(Read(index,next,b).Where(t=>!scope.Contains((t.X,t.Y)))))throw new Exception("Outside statics changed");
        for(int b=0;b<land.Length/196;b++)for(int c=0;c<64;c++){int x=b/512*8+c%8,y=b%512*8+c/8,o=b*196+4+c*3;if(!room.Contains((x,y))&&!land.AsSpan(o,3).SequenceEqual(original.AsSpan(o,3)))throw new Exception("Outside land changed");}
        File.WriteAllBytes(Path.Combine(output,"map0.mul"),land);File.WriteAllBytes(Path.Combine(output,"staidx0.mul"),index);
        return "Ten hedge walls and a 7x7 shrine clearing staged. Outside terrain/statics unchanged.";
    }
}
