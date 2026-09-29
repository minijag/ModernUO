param([string]$InputDir="$PSScriptRoot/adapted",[string]$OutputDir="$PSScriptRoot/full-set",[int[]]$PreserveActions=@())
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$live='C:/workspace/Ultima_online/Ultima Online Classic 7.0.15.1 relpor'
$out=$OutputDir;New-Item -ItemType Directory -Path $out -Force|Out-Null
if([IO.Path]::GetFullPath($out).TrimEnd('/','\') -eq [IO.Path]::GetFullPath($live).TrimEnd('/','\')){throw 'Staging output must not be the live folder'}
$meta=@(Get-Content "$InputDir/frames.json" -Raw|ConvertFrom-Json)
if($meta.Count -ne 280){throw 'Incomplete frame set'}
Copy-Item "$live/anim5.mul","$live/anim5.idx" $out
$index=[IO.File]::ReadAllBytes("$out/anim5.idx")
$stream=[IO.File]::Open("$out/anim5.mul",[IO.FileMode]::Append);$bw=[IO.BinaryWriter]::new($stream)
$report=@()
try{
foreach($group in ($meta|Group-Object Action,Direction)){
 if([int]$group.Group[0].Action -in $PreserveActions){continue}
 $frames=@($group.Group|Sort-Object Frame);$colors=@{};$images=@()
 foreach($m in $frames){$b=[Drawing.Bitmap]::FromFile("$InputDir/$($m.File)");$images+=,$b;for($y=0;$y -lt $b.Height;$y++){for($x=0;$x -lt $b.Width;$x++){$c=$b.GetPixel($x,$y);if($c.A -lt 128){continue};$v=(([int]$c.R-shr 3)-shl 10)-bor (([int]$c.G-shr 3)-shl 5)-bor ([int]$c.B-shr 3);if($v -eq 0){$v=1057};$colors[$v]++}}}
 $palette=@($colors.GetEnumerator()|Sort-Object Value -Descending|Select-Object -First 256|ForEach-Object {[int]$_.Key})
 $lookup=@{};foreach($v in $colors.Keys){$best=0;$score=[double]::MaxValue;for($i=0;$i -lt $palette.Count;$i++){$p=$palette[$i];$dist=[math]::Pow(($v-shr 10)-($p-shr 10),2)+[math]::Pow((($v-shr 5)-band 31)-(($p-shr 5)-band 31),2)+[math]::Pow(($v-band 31)-($p-band 31),2);if($dist -lt $score){$score=$dist;$best=$i}};$lookup[$v]=[byte]$best}
 $offset=[int]$stream.Position
 for($i=0;$i -lt 256;$i++){$bw.Write([uint16]$(if($i -lt $palette.Count){$palette[$i]}else{0}))}
 $start=$stream.Position;$bw.Write([int]$frames.Count);for($i=0;$i -lt $frames.Count;$i++){$bw.Write([int]0)}
 for($f=0;$f -lt $frames.Count;$f++){
  $m=$frames[$f];$b=$images[$f];$frameOffset=$stream.Position;$stream.Position=$start+4+$f*4;$bw.Write([int]($frameOffset-$start));$stream.Position=$frameOffset
  $bw.Write([int16]$m.X);$bw.Write([int16]$m.Y);$bw.Write([uint16]$b.Width);$bw.Write([uint16]$b.Height)
  for($y=0;$y -lt $b.Height;$y++){$x=0;while($x -lt $b.Width){if($b.GetPixel($x,$y).A -lt 128){$x++;continue};$first=$x;while($x -lt $b.Width -and $b.GetPixel($x,$y).A -ge 128){$x++};$n=$x-$first;$header=((($first-$m.X)-band 1023)-shl 22)-bor ((($y-$m.Y-$b.Height)-band 1023)-shl 12)-bor $n;$bw.Write([uint32]($header-band 4294967295L));for($px=$first;$px -lt $x;$px++){$c=$b.GetPixel($px,$y);$v=(([int]$c.R-shr 3)-shl 10)-bor (([int]$c.G-shr 3)-shl 5)-bor ([int]$c.B-shr 3);if($v -eq 0){$v=1057};$bw.Write([byte]$lookup[$v])}}}
  $bw.Write([int]0x7FFF7FFF);$b.Dispose()
 }
 $slot=21*110+[int]$frames[0].Action*5+[int]$frames[0].Direction;$length=[int]($stream.Position-$offset)
 [BitConverter]::GetBytes($offset).CopyTo($index,$slot*12);[BitConverter]::GetBytes($length).CopyTo($index,$slot*12+4)
 $report+=@{Action=$frames[0].Action;Direction=$frames[0].Direction;Slot=$slot;Frames=$frames.Count;Offset=$offset;Length=$length;Colors=$palette.Count}
}
}finally{$bw.Dispose()}
[IO.File]::WriteAllBytes("$out/anim5.idx",$index)
$report|ConvertTo-Json|Set-Content "$out/manifest.json"
"Packed $($meta.Count) frames in $($report.Count) animation records."
