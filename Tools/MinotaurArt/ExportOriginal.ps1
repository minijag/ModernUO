$ErrorActionPreference='Stop'
Add-Type -Path 'C:/workspace/Ultima_online/UOFiddler-4.17.8/Ultima.dll'
[Ultima.Files]::SetMulPath('C:/workspace/Ultima_online/ModernUO/Distribution/Backups/MinotaurPreview-20260928')
$out='C:/workspace/Ultima_online/ModernUO/Tools/MinotaurArt/original'
New-Item -ItemType Directory -Path $out -Force|Out-Null
$meta=@()
for($d=0;$d -lt 5;$d++){
 $sheet=[Drawing.Bitmap]::new(1280,1664);$g=[Drawing.Graphics]::FromImage($sheet);$g.Clear([Drawing.Color]::FromArgb(48,48,48));$font=[Drawing.Font]::new('Arial',10)
 $row=0
 for($a=0;$a -lt 22;$a++){
  $frames=[Ultima.Animations]::GetAnimation(21,$a,$d,5)
  if(!$frames -or !$frames[0].Bitmap){continue}
  for($f=0;$f -lt $frames.Length;$f++){
   $frame=$frames[$f];$b=$frame.Bitmap;$name="$a-$d-$f.png";$b.Save("$out/$name")
   $g.DrawImageUnscaled($b,$f*128+64-$frame.Center.X,$row*128+110-$frame.Center.Y-$b.Height)
   $g.DrawString("$a / $f",$font,[Drawing.Brushes]::White,$f*128+2,$row*128+112)
   $meta+=@{Action=$a;Direction=$d;Frame=$f;File=$name;X=$frame.Center.X;Y=$frame.Center.Y;Width=$b.Width;Height=$b.Height;Row=$row}
  };$row++
 }
 $sheet.Save("$out/direction-$d.png");$g.Dispose();$sheet.Dispose();$font.Dispose()
}
$meta|ConvertTo-Json|Set-Content "$out/frames.json"
"Actual nonempty frames: $($meta.Count)"
