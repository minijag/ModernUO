using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
public static class ShieldArt
{
    public static void Build(string source, string original, string output)
    {
        Directory.CreateDirectory(output);
        using var src = new Bitmap(source);
        int left=src.Width, top=src.Height, right=0, bottom=0;
        for(int y=0;y<src.Height;y++) for(int x=0;x<src.Width;x++)
            if(src.GetPixel(x,y).A>128) { left=Math.Min(left,x); top=Math.Min(top,y); right=Math.Max(right,x); bottom=Math.Max(bottom,y); }
        if(left==0 && top==0 && right==src.Width-1 && bottom==src.Height-1) throw new Exception("Source lacks transparent margins");
        File.Copy(Path.Combine(original,"gumpart.mul"),Path.Combine(output,"gumpart.mul"),true);
        File.Copy(Path.Combine(original,"gumpidx.mul"),Path.Combine(output,"gumpidx.mul"),true);
        using var art = new BinaryWriter(File.Open(Path.Combine(output,"gumpart.mul"),FileMode.Append));
        using var idx = new BinaryWriter(File.Open(Path.Combine(output,"gumpidx.mul"),FileMode.Open));
        using var sheet=new Bitmap(264,104);
        using var sg=Graphics.FromImage(sheet);
        sg.Clear(Color.FromArgb(24,24,24));
        for(int action=0;action<3;action++) for(int state=0;state<2;state++)
        {
            using var bmp=new Bitmap(44,52);
            using(var g=Graphics.FromImage(bmp))
            {
                g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                float scale=Math.Min(42f/(right-left+1),50f/(bottom-top+1));
                float w=(right-left+1)*scale,h=(bottom-top+1)*scale;
                g.DrawImage(src,new RectangleF((44-w)/2,(52-h)/2,w,h),new RectangleF(left,top,right-left+1,bottom-top+1),GraphicsUnit.Pixel);
                g.SmoothingMode=SmoothingMode.AntiAlias;
                using var pen=new Pen(Color.FromArgb(242,220,153),2);
                using var brush=new SolidBrush(Color.FromArgb(242,220,153));
                if(action==0) { g.DrawLines(pen,new[]{new Point(15,24),new Point(22,17),new Point(29,24)}); g.DrawLine(pen,22,18,22,33); g.DrawLine(pen,16,34,28,34); }
                if(action==1) { g.FillRectangle(brush,20,17,4,13); g.FillEllipse(brush,20,33,4,4); }
                if(action==2) { g.FillEllipse(brush,17,22,10,13); g.FillEllipse(brush,19,18,6,6); for(int yy=23;yy<=31;yy+=4) {g.DrawLine(pen,13,yy-2,19,yy);g.DrawLine(pen,25,yy,31,yy-2);} g.DrawLine(Pens.Navy,22,24,22,33); }
            }
            for(int y=0;y<52;y++) for(int x=0;x<44;x++) {var c=bmp.GetPixel(x,y); float f=state==1?0.7f:1f; bmp.SetPixel(x,y,c.A<128?Color.Transparent:Color.FromArgb(255,(int)(c.R*f),(int)(c.G*f),(int)(c.B*f)));}
            int id=64032+action*2+state;
            bmp.Save(Path.Combine(output,id+".png"));
            sg.InterpolationMode=InterpolationMode.NearestNeighbor;
            sg.DrawImage(bmp,new Rectangle((action*2+state)*44,0,44,52));
            sg.DrawImage(bmp,new Rectangle((action*2+state)*44,52,44,52));
            using var ms=new MemoryStream(); using var bw=new BinaryWriter(ms);
            bw.Write(new byte[52*4]);
            for(int y=0;y<52;y++) {
                long row=ms.Position; ms.Position=y*4; bw.Write((int)(row/4)); ms.Position=row;
                for(int x=0;x<44;) {ushort color=Color16(bmp.GetPixel(x,y)); int n=1;while(x+n<44&&Color16(bmp.GetPixel(x+n,y))==color)n++;bw.Write(color);bw.Write((ushort)n);x+=n;}
            }
            idx.BaseStream.Position=id*12; idx.Write((int)art.BaseStream.Position);idx.Write((int)ms.Length);idx.Write((44<<16)|52);art.Write(ms.ToArray());
        }
        sheet.Save(Path.Combine(output,"preview.png"));
    }
    static ushort Color16(Color c) {if(c.A==0)return 0;int v=((c.R>>3)<<10)|((c.G>>3)<<5)|(c.B>>3);return (ushort)(v==0?1:v);}
}
