param([int]$Action=0,[int]$Direction=1)
Add-Type -AssemblyName System.Drawing
$root=$PSScriptRoot
$meta=@(Get-Content "$root/original/frames.json" -Raw|ConvertFrom-Json|Where-Object {$_.Action -eq $Action -and $_.Direction -eq $Direction}|Sort-Object Frame)
$rows=[int][math]::Ceiling($meta.Count/5);$sheet=[Drawing.Bitmap]::new(1280,$rows*512);$g=[Drawing.Graphics]::FromImage($sheet);$g.Clear([Drawing.Color]::FromArgb(44,47,48));$g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::NearestNeighbor;$g.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::Half
$font=[Drawing.Font]::new('Arial',14)
foreach($m in $meta){$column=$m.Frame%5;$row=[int][math]::Floor($m.Frame/5);$b=[Drawing.Bitmap]::FromFile("$root/original/$($m.File)");$x=$column*256+144-$m.X*3;$y=$row*512+430-($m.Y+$m.Height)*3;$g.DrawImage($b,[Drawing.Rectangle]::new($x,$y,$b.Width*3,$b.Height*3),0,0,$b.Width,$b.Height,[Drawing.GraphicsUnit]::Pixel);$g.DrawString("Frame $($m.Frame+1)",$font,[Drawing.Brushes]::White,$column*256+8,$row*512+486);$b.Dispose()}
New-Item -ItemType Directory -Path "$root/redesign" -Force|Out-Null
$sheet.Save("$root/redesign/guide-$Action-$Direction.png");$g.Dispose();$sheet.Dispose();$font.Dispose()
