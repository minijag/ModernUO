Add-Type -Path 'C:/workspace/Ultima_online/UOFiddler-4.17.8/Ultima.dll'
[Ultima.Files]::SetMulPath('C:/workspace/Ultima_online/ModernUO/Distribution/Backups/MinotaurPreview-20260928')
$sheet=[Drawing.Bitmap]::new(1000,1320);$g=[Drawing.Graphics]::FromImage($sheet);$g.Clear([Drawing.Color]::FromArgb(40,40,40));$font=[Drawing.Font]::new('Arial',9)
for($a=0;$a -lt 22;$a++){for($d=0;$d -lt 5;$d++){$f=[Ultima.Animations]::GetAnimation(21,$a,$d,5);if(!$f){continue};$b=$f[[int][math]::Floor(($f.Length-1)/2)].Bitmap;$x=$d*200;$y=$a*60;$g.DrawImageUnscaled($b,$x+50,$y+58-$b.Height);$g.DrawString("$a / $d",$font,[Drawing.Brushes]::White,$x,$y+40)}}
$sheet.Save('C:/workspace/Ultima_online/ModernUO/Tools/MinotaurArt/original-actions.png');$g.Dispose();$sheet.Dispose()
