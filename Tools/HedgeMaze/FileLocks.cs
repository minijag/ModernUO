using System;
using System.Runtime.InteropServices;
public static class MazeFileLocks
{
    [StructLayout(LayoutKind.Sequential)] public struct Unique { public int Pid; public System.Runtime.InteropServices.ComTypes.FILETIME Start; }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct Info {
        public Unique Process;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)] public string Service;
        public int Type;public uint Status;public uint Session;[MarshalAs(UnmanagedType.Bool)] public bool Restart;
    }
    [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)] static extern int RmStartSession(out uint handle,int flags,string key);
    [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)] static extern int RmRegisterResources(uint handle,uint count,string[] files,uint apps,Unique[] procs,uint services,string[] names);
    [DllImport("rstrtmgr.dll")] static extern int RmGetList(uint handle,out uint needed,ref uint count,[In,Out] Info[] results,ref uint reasons);
    [DllImport("rstrtmgr.dll")] static extern int RmEndSession(uint handle);
    public static Info[] Find(string path){uint h;int e=RmStartSession(out h,0,Guid.NewGuid().ToString("N"));if(e!=0)throw new Exception("Start "+e);try{e=RmRegisterResources(h,1,new[]{path},0,null,0,null);if(e!=0)throw new Exception("Register "+e);uint n=0,c=0,r=0;e=RmGetList(h,out n,ref c,null,ref r);if(e==0)return Array.Empty<Info>();if(e!=234)throw new Exception("List "+e);var a=new Info[n];c=n;e=RmGetList(h,out n,ref c,a,ref r);if(e!=0)throw new Exception("List2 "+e);return a;}finally{RmEndSession(h);}}
}
