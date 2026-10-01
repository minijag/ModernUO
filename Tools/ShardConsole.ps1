param([int]$ServerProcessId, [ValidateSet('save','shutdown','y','n')][string]$Command)
$ErrorActionPreference='Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ShardConsolePipe {
 [StructLayout(LayoutKind.Explicit, Size=20)] public struct Input {
  [FieldOffset(0)] public ushort Type;
  [FieldOffset(4)] public int Down;
  [FieldOffset(8)] public ushort Repeat;
  [FieldOffset(10)] public ushort Key;
  [FieldOffset(14)] public char Character;
 }
 [DllImport("kernel32.dll",SetLastError=true)] public static extern bool AttachConsole(uint process);
 [DllImport("kernel32.dll")] public static extern bool FreeConsole();
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] public static extern IntPtr CreateFile(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] public static extern bool WriteConsoleInputW(IntPtr handle,Input[] input,uint count,out uint written);
 [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
 public static void Send(uint process,string command) {
  FreeConsole();
  if(!AttachConsole(process)) throw new Exception("AttachConsole failed: "+Marshal.GetLastWin32Error());
  var h=CreateFile("CONIN$",0x40000000,3,IntPtr.Zero,3,0,IntPtr.Zero);
  try {
   if(h==new IntPtr(-1)) throw new Exception("Open console input failed");
   var text=command+"\r";var inputs=new Input[text.Length*2];
   for(int i=0;i<text.Length;i++) {
    var key=new Input{Type=1,Down=1,Repeat=1,Character=text[i],Key=(ushort)(text[i]=='\r'?13:0)};
    inputs[i*2]=key;key.Down=0;inputs[i*2+1]=key;
   }
   uint written;
   if(!WriteConsoleInputW(h,inputs,(uint)inputs.Length,out written)||written!=inputs.Length) throw new Exception("Console write failed");
  } finally {CloseHandle(h);FreeConsole();}
 }
}
'@
[ShardConsolePipe]::Send([uint32]$ServerProcessId,$Command)
Write-Output "Sent $Command to shard PID $ServerProcessId"
