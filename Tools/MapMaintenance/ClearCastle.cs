using System;
using System.IO;
using System.Linq;

// One-off, bounded map edit. Source files are never modified by this utility.
public static class ClearCastle5246
{
    private static bool Inside(int x,int y) => x>=5246 && x<=5353 && y>=858 && y<=979;
    public static string Stage(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        var land=File.ReadAllBytes(Path.Combine(source,"map0.mul"));
        var index=File.ReadAllBytes(Path.Combine(source,"staidx0.mul"));
        var statics=File.ReadAllBytes(Path.Combine(source,"statics0.mul"));
        if(land.Length!=77070336 || index.Length!=4718592) throw new Exception("Unexpected map dimensions");
        var output=(byte[])land.Clone();
        int landCount=0,removed=0;
        for(int x=5246;x<=5353;x++) for(int y=858;y<=979;y++)
        {
            int offset=((x/8)*512+y/8)*196+4+((y%8)*8+x%8)*3;
            output[offset]=0x44; output[offset+1]=0x02; output[offset+2]=0; landCount++;
        }
        using(var data=new MemoryStream())
        using(var writer=new BinaryWriter(data))
        {
            writer.Write(statics);
            for(int bx=5246/8;bx<=5353/8;bx++) for(int by=858/8;by<=979/8;by++)
            {
                int io=(bx*512+by)*12, offset=BitConverter.ToInt32(index,io), length=BitConverter.ToInt32(index,io+4);
                if(offset<0 || length<=0)continue;
                if(length%7!=0 || (long)offset+length>statics.Length)throw new Exception("Invalid static block");
                int start=(int)data.Position;
                for(int p=offset;p<offset+length;p+=7)
                {
                    if(Inside(bx*8+statics[p+2],by*8+statics[p+3]))removed++;
                    else writer.Write(statics,p,7);
                }
                int size=(int)data.Position-start;
                Array.Copy(BitConverter.GetBytes(size==0 ? -1:start),0,index,io,4);
                Array.Copy(BitConverter.GetBytes(size),0,index,io+4,4);
            }
            File.WriteAllBytes(Path.Combine(destination,"statics0.mul"),data.ToArray());
        }
        File.WriteAllBytes(Path.Combine(destination,"map0.mul"),output);
        File.WriteAllBytes(Path.Combine(destination,"staidx0.mul"),index);
        // Confirm every terrain cell outside the rectangle is byte-identical.
        for(int block=0;block<land.Length/196;block++)
        {
            for(int cell=0;cell<64;cell++)
            {
                int x=(block/512)*8+cell%8,y=(block%512)*8+cell/8,o=block*196+4+cell*3;
                if(!Inside(x,y) && (land[o]!=output[o] || land[o+1]!=output[o+1] || land[o+2]!=output[o+2]))
                    throw new Exception("Outside terrain changed");
            }
        }
        // Confirm statics outside the rectangle, including partial edge blocks, are unchanged.
        var newStatics=File.ReadAllBytes(Path.Combine(destination,"statics0.mul"));
        var oldIndex=File.ReadAllBytes(Path.Combine(source,"staidx0.mul"));
        for(int b=0;b<index.Length/12;b++)
        {
            byte[] Records(byte[] idx,byte[] bytes,bool filter)
            {
                int o=BitConverter.ToInt32(idx,b*12),n=BitConverter.ToInt32(idx,b*12+4);
                using(var ms=new MemoryStream())
                {
                    if(o>=0 && n>0)for(int p=o;p<o+n;p+=7)
                    {
                        bool inside=Inside((b/512)*8+bytes[p+2],(b%512)*8+bytes[p+3]);
                        if(filter && inside)continue;
                        ms.Write(bytes,p,7);
                    }
                    return ms.ToArray();
                }
            }
            if(!Records(oldIndex,statics,true).SequenceEqual(Records(index,newStatics,false)))
                throw new Exception("Static verification failed at block "+b);
        }
        return $"Land cells blackened: {landCount}; statics removed: {removed}; all outside cells/statics unchanged.";
    }
}
