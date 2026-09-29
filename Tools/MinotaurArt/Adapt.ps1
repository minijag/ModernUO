$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
. "$PSScriptRoot/Landmarks.ps1"
$root=$PSScriptRoot;$out="$root/adapted";New-Item -ItemType Directory -Path $out -Force|Out-Null
$meta=@(Get-Content "$root/original/frames.json" -Raw|ConvertFrom-Json)
foreach($m in $meta){
 $b=[Drawing.Bitmap]::FromFile("$root/original/$($m.File)")
 $poses=$Landmarks[[int]$m.Direction][[int]$m.Action].Split(';');$pose=$poses[[math]::Min($m.Frame,$poses.Length-1)].Split(',')|ForEach-Object {[double]$_}
 $hx,$hy,$px,$py=$pose;$dx=$px-$hx;$dy=$py-$hy;$length=[math]::Sqrt($dx*$dx+$dy*$dy);$dx/=$length;$dy/=$length
 $original=$b.Clone()
 for($y=0;$y -lt $b.Height;$y++){for($x=0;$x -lt $b.Width;$x++){
  $c=$original.GetPixel($x,$y);if($c.A -eq 0){continue}
  $ax=$x+64-$m.X;$ay=$y+110-$m.Y-$m.Height
  $lum=0.299*$c.R+0.587*$c.G+0.114*$c.B
  $warm=$c.R -gt $c.B*1.22 -and $c.G -gt $c.B*1.04
  $along=($ax-$hx)*$dx+($ay-$hy)*$dy;$across=($ax-$hx)*$dy-($ay-$hy)*$dx
  $head=($across*$across/196)+($along*$along/256) -lt 1
  if($m.Direction -ge 3){$head=($across*$across/100)+(($along-7)*($along-7)/484) -lt 1}
  $legDistance=($ax-$px)*$dx+($ay-$py)*$dy
  $fur=$head -or ($legDistance -gt -2 -and [math]::Abs(($ax-$px)*$dy-($ay-$py)*$dx) -lt 31)
  # Horns and metallic fittings use neutral palette entries; retain their shading.
  if($warm){
   if($fur){$v=[int][math]::Round(12+$lum*0.25);$r=$v;$g=$v;$blue=$v}
   else{$v=[int][math]::Round(22+$lum*0.46);$r=$v+10;$g=$v+2;$blue=$v-2}
  }else{$v=[int][math]::Round(10+$lum*0.68);$r=$v+3;$g=$v+2;$blue=$v}
  $b.SetPixel($x,$y,[Drawing.Color]::FromArgb(255,[math]::Clamp($r,0,255),[math]::Clamp($g,0,255),[math]::Clamp($blue,0,255)))
 }}
 # A single subdued white pixel at each visible eye; rear-facing poses have none.
 if($m.Direction -le 2 -and !(($m.Action -eq 2 -or $m.Action -eq 3) -and $m.Frame -ge 2)){
  $offsets=if($m.Direction -eq 0){@(-3,3)}else{@(-5)}
  foreach($offset in $offsets){$ex=[int]($hx+$offset-64+$m.X);$ey=[int]($hy-110+$m.Y+$m.Height);$best=$null;$score=[double]::MaxValue
   for($yy=$ey-2;$yy -le $ey+2;$yy++){for($xx=$ex-2;$xx -le $ex+2;$xx++){if($xx -lt 0 -or $yy -lt 0 -or $xx -ge $b.Width -or $yy -ge $b.Height){continue};$c=$original.GetPixel($xx,$yy);if($c.A -eq 0){continue};$s=[math]::Abs(($c.R+$c.G+$c.B)/3-55)+12*([math]::Abs($xx-$ex)+[math]::Abs($yy-$ey));if($s -lt $score){$score=$s;$best=@($xx,$yy)}}}
   if($best){$b.SetPixel($best[0],$best[1],[Drawing.Color]::FromArgb(255,192,192,184))}
  }
 }
 $w=[int][math]::Round($b.Width*1.1);$h=[int][math]::Round($b.Height*1.1);$scaled=[Drawing.Bitmap]::new($w,$h);$graphics=[Drawing.Graphics]::FromImage($scaled)
 $graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::NearestNeighbor;$graphics.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::Half
 $graphics.DrawImage($b,[Drawing.Rectangle]::new(0,0,$w,$h),0,0,$b.Width,$b.Height,[Drawing.GraphicsUnit]::Pixel);$graphics.Dispose()
 $scaled.Save("$out/$($m.File)");$original.Dispose();$b.Dispose();$scaled.Dispose()
 $m.X=[int][math]::Round($m.X*1.1);$m.Y=[int][math]::Round($m.Y*1.1);$m.Width=$w;$m.Height=$h
}
$meta|ConvertTo-Json|Set-Content "$out/frames.json"
foreach($d in 0..4){$sheet=[Drawing.Bitmap]::new(1600,1820);$g=[Drawing.Graphics]::FromImage($sheet);$g.Clear([Drawing.Color]::FromArgb(48,48,48));$font=[Drawing.Font]::new('Arial',10)
 foreach($m in ($meta|Where-Object Direction -eq $d)){$b=[Drawing.Bitmap]::FromFile("$out/$($m.File)");$g.DrawImageUnscaled($b,$m.Frame*160+80-$m.X,$m.Row*140+118-$m.Y-$m.Height);$g.DrawString("$($m.Action)/$($m.Frame)",$font,[Drawing.Brushes]::White,$m.Frame*160+2,$m.Row*140+122);$b.Dispose()}
 $sheet.Save("$out/direction-$d.png");$g.Dispose();$sheet.Dispose();$font.Dispose()
}
"Adapted $($meta.Count) real frames."
