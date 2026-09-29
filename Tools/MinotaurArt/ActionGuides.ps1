$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$out="$PSScriptRoot/redesign-v2"
New-Item -ItemType Directory -Path $out -Force|Out-Null
$meta=@(Get-Content "$PSScriptRoot/original/frames.json" -Raw|ConvertFrom-Json)
foreach($action in ($meta.Action|Sort-Object -Unique)){
 $frames=@($meta|Where-Object Action -eq $action)
 $count=@($frames|Where-Object Direction -eq 0).Count
 $cols=$count;$rows=5
 if($count -eq 1){$cols=5;$rows=1}
 $sheet=[Drawing.Bitmap]::new($cols*384,$rows*384)
 $g=[Drawing.Graphics]::FromImage($sheet);$g.Clear([Drawing.Color]::FromArgb(45,47,48))
 $g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
 $g.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::Half
 foreach($m in $frames){
  $col=$m.Frame;$row=$m.Direction
  if($count -eq 1){$col=$m.Direction;$row=0}
  $b=[Drawing.Bitmap]::FromFile("$PSScriptRoot/original/$($m.File)")
  $x=$col*384+192-$m.X*2;$y=$row*384+315-($m.Y+$m.Height)*2
  $g.DrawImage($b,[Drawing.Rectangle]::new($x,$y,$b.Width*2,$b.Height*2),0,0,$b.Width,$b.Height,[Drawing.GraphicsUnit]::Pixel)
  $b.Dispose()
 }
 $sheet.Save("$out/guide-$action.png");$g.Dispose();$sheet.Dispose()
}
"Prepared 13 action sheets."
