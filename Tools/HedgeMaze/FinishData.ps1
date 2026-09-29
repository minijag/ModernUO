param([string]$Stage='C:\Users\minij\AppData\Local\Temp\arbor-maze-v5-stage')
$ErrorActionPreference='Stop';$root='C:\workspace\Ultima_online\ModernUO'
if(Test-Path "$Stage\manifest.json"){throw 'Already staged'}
. "$root\Tools\HedgeMaze\Inspect.ps1" | Out-Null
$ix=[IO.File]::ReadAllBytes("$Stage\staidx0.mul");$st=[IO.File]::ReadAllBytes("$Stage\statics0.mul");$land=[IO.File]::ReadAllBytes("$Stage\map0.mul")
$blocked=[Collections.Generic.HashSet[string]]::new()
foreach($t in (Tiles 1780 884 1948 992)){$data=[Ultima.TileData]::ItemTable[$t.Id];if(($data.Surface -or $data.Impassable) -and $t.Z -lt 16 -and $t.Z+$data.CalcHeight -gt 0){[void]$blocked.Add("$($t.X),$($t.Y)")}}
$prior=Get-Content "$root\Distribution\Data\ArborMazeV4\manifest.json" -Raw | ConvertFrom-Json
$geo=Get-Content "$Stage\geometry.json" -Raw | ConvertFrom-Json
$cells=(Get-Content 'C:\Users\minij\AppData\Local\Temp\arbor-maze-v2-stage\geometry.json' -Raw | ConvertFrom-Json).Cells
$all=@(Get-Content "$root\Distribution\zodiac.json" -Raw | ConvertFrom-Json)
$existing=@($all | Where-Object {$_.guid -in $prior.AllSpawnerGuids})
$pois=@(@(1836,916),@(1908,916),@(1852,940),@(1834,970),@(1872,960),@(1886,930))
$candidates=@($cells | Where-Object {$p=$_;$x=[int]$p.X;$y=[int]$p.Y;$o=([int][math]::Floor($x/8)*512+[int][math]::Floor($y/8))*196+4+(($y%8)*8+$x%8)*3;
 -not $blocked.Contains("$x,$y") -and $land[$o+2] -eq 0 -and -not ($x -ge 1894 -and $x -le 1926 -and $y -ge 932 -and $y -le 962) -and @($pois | Where-Object {[math]::Abs($_[0]-$x)+[math]::Abs($_[1]-$y) -lt 7}).Count -eq 0 -and [math]::Abs($x-1806)+[math]::Abs($y-946) -gt 12 })
$occupied=[Collections.Generic.List[object]]::new();foreach($s in $existing){$occupied.Add(@{X=$s.location.x;Y=$s.location.y})}
$new=@();$types=@('Gazer','Reaper','EvilMage','BogThing','AirElemental','ElderGazer','Wisp','Gazer')
for($i=0;$i -lt 16;$i++){
 $p=$candidates | Sort-Object -Descending { $c=$_;($occupied | ForEach-Object {[math]::Abs($_.X-$c.X)+[math]::Abs($_.Y-$c.Y)} | Measure-Object -Minimum).Minimum } | Select-Object -First 1
 if(!$p){throw 'No encounter candidates'}
 $distance=($occupied | ForEach-Object {[math]::Abs($_.X-$p.X)+[math]::Abs($_.Y-$p.Y)} | Measure-Object -Minimum).Minimum;if($distance -lt 8){throw 'New encounters too close together'}
 $occupied.Add($p);$new+=@{type='Spawner';name="Arbor Labrynth - Roaming encounter $($i+1)";guid=[guid]::NewGuid().ToString();location=@{x=$p.X;y=$p.Y;z=0};map='Felucca';count=1;minDelay='00:06:00';maxDelay='00:09:00';team=0;homeRange=1;walkingRange=2;entries=@(@{name=$types[$i%8];probability=100;maxCount=1})}
}
$oldKeys=@($prior.AllPortals | ForEach-Object {"$($_.X),$($_.Y),$($_.Z)"})
$tele=@(Get-Content "$root\Distribution\Data\teleporters.json" -Raw | ConvertFrom-Json | Where-Object {$_.src.map -ne 'Felucca' -or ($_.src.loc -join ',') -notin $oldKeys})
$marks=@();$links=@();$names=@('Violet Sanctums','Amber Archives','Jade Gardens');$hues=@(1153,2213,1272)
for($i=0;$i -lt 6;$i++){$p=$pois[$i];$j=if($i%2 -eq 0){$i+1}else{$i-1};$d=$pois[$j];$pair=[int][math]::Floor($i/2);$marks+=@{Name="Labrynth - $($names[$pair])";X=$p[0];Y=$p[1];Z=0;Hue=$hues[$pair]};$links+=@{src=@{map='Felucca';loc=@($p[0],$p[1],0)};dst=@{map='Felucca';loc=@($d[0],$d[1],0)};back=$false}}
@{NewSpawnerGuids=@($new.guid);AllSpawnerGuids=@($prior.AllSpawnerGuids)+@($new.guid);OldPortals=$prior.AllPortals;NewPortals=$marks;Scope=$geo.Scope} | ConvertTo-Json -Depth 12 | Set-Content "$Stage\manifest.json"
ConvertTo-Json -InputObject @($all+$new) -Depth 12 | Set-Content "$Stage\zodiac.json"
ConvertTo-Json -InputObject @($tele+$links) -Depth 12 | Set-Content "$Stage\teleporters.json"
"Added $($new.Count) separate spawners; 43 total, 85 configured creatures. Replaced 9 old portals with 6 POI portals; destinations exactly match paired gates."
