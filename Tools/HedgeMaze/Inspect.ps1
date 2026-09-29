param([string]$Source='C:\workspace\Ultima_online\Ultima Online Classic 7.0.15.1 relpor')
Add-Type -Path 'C:\workspace\Ultima_online\UOFiddler-4.17.8\Ultima.dll'
[Ultima.Files]::SetMulPath($Source)
$ix=[IO.File]::ReadAllBytes("$Source\staidx0.mul")
$st=[IO.File]::ReadAllBytes("$Source\statics0.mul")
$land=[IO.File]::ReadAllBytes("$Source\map0.mul")
function Tiles($x1,$y1,$x2,$y2) {
 for($bx=[int][math]::Floor($x1/8);$bx -le [math]::Floor($x2/8);$bx++){for($by=[int][math]::Floor($y1/8);$by -le [math]::Floor($y2/8);$by++){
 $i=($bx*512+$by)*12;$o=[BitConverter]::ToInt32($ix,$i);$n=[BitConverter]::ToInt32($ix,$i+4)
 if($o -lt 0){continue};for($p=$o;$p -lt $o+$n;$p+=7){$x=$bx*8+$st[$p+2];$y=$by*8+$st[$p+3];if($x -lt $x1 -or $x -gt $x2 -or $y -lt $y1 -or $y -gt $y2){continue};$id=[BitConverter]::ToUInt16($st,$p);$z=[int]$st[$p+4];if($z -gt 127){$z-=256};$t=[Ultima.TileData]::ItemTable[$id];[pscustomobject]@{X=$x;Y=$y;Z=$z;Id=$id;Hex=('{0:X4}' -f $id);Name=$t.Name;Height=$t.Height;Flags=$t.Flags}
 }}}
}
Tiles 1878 917 1902 942 | Group-Object Hex,Name,Height,Flags | Select-Object Count,Name | Format-Table -AutoSize
'Shore statics'
Tiles 1904 950 1944 980 | Group-Object Hex,Name,Height,Flags | Select-Object Count,Name | Format-Table -AutoSize
'Cliff art'
for($i=0;$i -lt [Ultima.TileData]::ItemTable.Length;$i++){if([Ultima.TileData]::ItemTable[$i].Name -match 'cliff'){[pscustomobject]@{Id=('{0:X4}' -f $i);Name=[Ultima.TileData]::ItemTable[$i].Name;Height=[Ultima.TileData]::ItemTable[$i].Height}}}
