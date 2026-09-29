using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
public static class SplashArt
{
    public static void Stage(string source,string live,string output)
    {
        Directory.CreateDirectory(output);
        using var src=new Bitmap(source);
        using var bmp=new Bitmap(640,480);
        using(var g=Graphics.FromImage(bmp)) {
            g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            // The extended 4:3 painting fills the entire login background.
            g.DrawImage(src,new Rectangle(0,0,640,480));
        }
        bmp.Save(Path.Combine(output,"login-background.png"));
        File.Copy(Path.Combine(live,"gumpart.mul"),Path.Combine(output,"gumpart.mul"),false);
        File.Copy(Path.Combine(live,"gumpidx.mul"),Path.Combine(output,"gumpidx.mul"),false);
        using var ms=new MemoryStream();using var bw=new BinaryWriter(ms);
        bw.Write(new byte[480*4]);
        for(int y=0;y<480;y++) {
            long row=ms.Position;ms.Position=y*4;bw.Write((int)(row/4));ms.Position=row;
            for(int x=0;x<640;) {ushort c=Rgb(bmp.GetPixel(x,y));int n=1;while(x+n<640&&Rgb(bmp.GetPixel(x+n,y))==c)n++;bw.Write(c);bw.Write((ushort)n);x+=n;}
        }
        using var art=new BinaryWriter(File.Open(Path.Combine(output,"gumpart.mul"),FileMode.Append));
        using var idx=new BinaryWriter(File.Open(Path.Combine(output,"gumpidx.mul"),FileMode.Open));
        idx.BaseStream.Position=0x2329*12;idx.Write((int)art.BaseStream.Position);idx.Write((int)ms.Length);idx.Write((640<<16)|480);art.Write(ms.ToArray());
    }
    // Zero means transparent in MUL: reserve the darkest near-black for opaque pixels.
    static ushort Rgb(Color c) {int v=((c.R>>3)<<10)|((c.G>>3)<<5)|(c.B>>3);return (ushort)(v==0?1:v);}
}
