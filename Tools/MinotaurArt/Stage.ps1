param([int]$Height=89,[string]$Output='C:/workspace/Ultima_online/ModernUO/Tools/MinotaurArt/staged')
$ErrorActionPreference='Stop'
Add-Type -Path 'C:/workspace/Ultima_online/UOFiddler-4.17.8/Ultima.dll'
$live='C:/workspace/Ultima_online/Ultima Online Classic 7.0.15.1 relpor'
$out=$Output
New-Item -ItemType Directory -Path $out -Force | Out-Null
[Ultima.Files]::SetMulPath($live)
$source=[Drawing.Bitmap]::FromFile('C:/workspace/Ultima_online/ModernUO/Tools/MinotaurArt/source.png')
$x1=$source.Width;$y1=$source.Height;$x2=0;$y2=0
for($y=0;$y -lt $source.Height;$y++){for($x=0;$x -lt $source.Width;$x++){if($source.GetPixel($x,$y).A -ge 240){$x1=[math]::Min($x1,$x);$y1=[math]::Min($y1,$y);$x2=[math]::Max($x2,$x);$y2=[math]::Max($y2,$y)}}}
$width=[int][math]::Round(($x2-$x1+1)*$height/($y2-$y1+1))
$sprite=[Drawing.Bitmap]::new($width,$height)
$g=[Drawing.Graphics]::FromImage($sprite);$g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.DrawImage($source,[Drawing.Rectangle]::new(0,0,$width,$height),[Drawing.Rectangle]::new($x1,$y1,$x2-$x1+1,$y2-$y1+1),[Drawing.GraphicsUnit]::Pixel);$g.Dispose();$source.Dispose()
$colors=@{};$pixels=@{}
for($y=0;$y -lt $height;$y++){for($x=0;$x -lt $width;$x++){$c=$sprite.GetPixel($x,$y);if($c.A -lt 220){$sprite.SetPixel($x,$y,[Drawing.Color]::Transparent);continue};$v=(([int]$c.R -shr 3) -shl 10) -bor (([int]$c.G -shr 3) -shl 5) -bor ([int]$c.B -shr 3);if($v -eq 0){$v=1057};$pixels["$x,$y"]=$v;$colors[$v]++}}
$palette=@($colors.GetEnumerator()|Sort-Object Value -Descending|Select-Object -First 256|ForEach-Object {[int]$_.Key})
$indices=@{};foreach($v in $colors.Keys){$best=0;$distance=[int]::MaxValue;for($i=0;$i -lt $palette.Count;$i++){$p=$palette[$i];$d=[math]::Pow(($v -shr 10)-($p -shr 10),2)+[math]::Pow((($v -shr 5)-band 31)-(($p -shr 5)-band 31),2)+[math]::Pow(($v-band 31)-($p-band 31),2);if($d -lt $distance){$distance=$d;$best=$i}};$indices[$v]=$best}
$ms=[IO.MemoryStream]::new();$bw=[IO.BinaryWriter]::new($ms)
for($i=0;$i -lt 256;$i++){$bw.Write([uint16]$(if($i -lt $palette.Count){$palette[$i]}else{0}))}
$bw.Write([int]1);$bw.Write([int]8)
$cx=[int][math]::Round($width*0.65);$cy=0
$bw.Write([int16]$cx);$bw.Write([int16]$cy);$bw.Write([uint16]$width);$bw.Write([uint16]$height)
for($y=0;$y -lt $height;$y++){$x=0;while($x -lt $width){if(!$pixels.ContainsKey("$x,$y")){$x++;continue};$start=$x;while($x -lt $width -and $pixels.ContainsKey("$x,$y")){$x++};$n=$x-$start;$header=((($start-$cx)-band 1023)-shl 22)-bor ((($y-$cy-$height)-band 1023)-shl 12)-bor $n;$bw.Write([int]$header);for($a=$start;$a -lt $x;$a++){$i=$indices[$pixels["$a,$y"]];$bw.Write([byte]$i);$v=$palette[$i];$sprite.SetPixel($a,$y,[Drawing.Color]::FromArgb(255,(($v-shr 10)-band 31)*8,(($v-shr 5)-band 31)*8,($v-band 31)*8))}}}
$bw.Write([int]0x7FFF7FFF);$bw.Flush();$blob=$ms.ToArray();$bw.Dispose()
$sprite.Save("$out/frame.png");$sprite.Dispose()
Copy-Item "$live/anim5.mul","$live/anim5.idx" $out
$stream=[IO.File]::Open("$out/anim5.mul",[IO.FileMode]::Append);$offset=[int]$stream.Position;$stream.Write($blob,0,$blob.Length);$stream.Dispose()
$index=[IO.File]::ReadAllBytes("$out/anim5.idx")
# Body 262 maps to anim5 body 21. Monster standing action 1, five stored facings.
for($dir=0;$dir -lt 5;$dir++){$slot=21*110+5+$dir;[BitConverter]::GetBytes($offset).CopyTo($index,$slot*12);[BitConverter]::GetBytes($blob.Length).CopyTo($index,$slot*12+4)}
[IO.File]::WriteAllBytes("$out/anim5.idx",$index)
"Staged single still pose in standing action only, five facing slots: $width x $height, $($palette.Count) colors."
