$ErrorActionPreference='Stop'
Add-Type -Path 'C:/workspace/Ultima_online/UOFiddler-4.17.8/Ultima.dll'
[Ultima.Files]::SetMulPath('C:/workspace/Ultima_online/Ultima Online Classic 7.0.15.1 relpor')
$bitmap=[Drawing.Bitmap]::new(1300,360)
$g=[Drawing.Graphics]::FromImage($bitmap)
$g.Clear([Drawing.Color]::FromArgb(24,27,30))
$g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::Half
$font=[Drawing.Font]::new('Segoe UI',15,[Drawing.FontStyle]::Bold)
$small=[Drawing.Font]::new('Segoe UI',11)
$entries=@(@('Minotaur',263,0),@('Scout',281,0),@('Captain',280,0),@('Tormented Minotaur',262,0),@('Meraktus',263,0x835))
for($i=0;$i -lt $entries.Count;$i++){
 $e=$entries[$i];$h=0;$frames=[Ultima.Animations]::GetAnimation([int]$e[1],0,1,[ref]$h,$true,$false)
 if(!$frames.Length){throw "No frames for $($e[0])"}
 $sprite=$frames[0].Bitmap.Clone()
 if($e[2] -gt 0){[Ultima.Hues]::GetHue($e[2]-1).ApplyTo($sprite,$false)}
 $w=$sprite.Width*2;$height=$sprite.Height*2
 $g.DrawImage($sprite,[Drawing.Rectangle]::new([int]($i*260+(260-$w)/2),[int](280-$height),$w,$height),0,0,$sprite.Width,$sprite.Height,[Drawing.GraphicsUnit]::Pixel)
 $g.DrawString($e[0],$font,[Drawing.Brushes]::Wheat,[single]($i*260+14),[single]295)
 $g.DrawString("Body $($e[1])",$small,[Drawing.Brushes]::LightGray,[single]($i*260+14),[single]325)
 $sprite.Dispose()
}
$bitmap.Save('C:/workspace/Ultima_online/ModernUO/Tools/HedgeMaze/minotaurs-preview.png',[Drawing.Imaging.ImageFormat]::Png)
$g.Dispose();$bitmap.Dispose();$font.Dispose();$small.Dispose()
