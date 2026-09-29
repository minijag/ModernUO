$ErrorActionPreference='Stop'
$root='C:\workspace\Ultima_online\ModernUO'
$stage='C:\Users\minij\AppData\Local\Temp\arbor-maze-stage'
function InFootprint($x,$y) { ($x -ge 1792 -and $x -le 1868 -and $y -ge 916 -and $y -le 980) -or ($x -ge 1880 -and $x -le 1900 -and $y -ge 942 -and $y -le 962) }
function Cell($x,$y) { @((1794+4*$x),(918+4*$y),0) }
$all=Get-Content "$root\Distribution\zodiac.json" -Raw | ConvertFrom-Json
$removed=@($all | Where-Object { $_.map -eq 'Felucca' -and (InFootprint $_.location.x $_.location.y) })
$remaining=@($all | Where-Object { -not ($_.map -eq 'Felucca' -and (InFootprint $_.location.x $_.location.y)) })
$new=[Collections.Generic.List[object]]::new()
function Spawn($label,$pos,$mob,$count=1,$range=2,$minutes=6) {
    $new.Add([ordered]@{type='Spawner';name="Arbor Labrynth - $label";guid=[guid]::NewGuid().ToString();location=@{x=$pos[0];y=$pos[1];z=$pos[2]};map='Felucca';count=$count;minDelay=([timespan]::FromMinutes($minutes)).ToString();maxDelay=([timespan]::FromMinutes($minutes+3)).ToString();team=0;homeRange=$range;walkingRange=4;entries=@(@{name=$mob;probability=100;maxCount=$count})})
}
Spawn 'Rootward Sentries' (Cell 2 14) 'Reaper'
Spawn 'Whispering Eye' (Cell 7 12) 'Gazer'
Spawn 'Briar Hunter' (Cell 10 13) 'BogThing'
Spawn 'Lost Arcanist' (Cell 4 8) 'EvilMage'
Spawn 'Windbound' (Cell 9 7) 'AirElemental'
Spawn 'Thorn Sentinel' (Cell 12 5) 'Reaper'
Spawn 'Mirror Eye' (Cell 15 7) 'ElderGazer'
Spawn 'North Watcher' (Cell 10 1) 'Gazer'
Spawn 'Archivist Gone Mad' (Cell 8 4) 'EvilMage'
Spawn 'Garden Wisp' (Cell 14 10) 'Wisp'
Spawn 'Tower Gatekeeper' @(1890,951,0) 'Daemon' 1 2 12
Spawn 'Tower Adepts' @(1894,956,0) 'LichLord' 2 2 12
Spawn 'The Thorn Crown' @(1890,950,20) 'AncientLich' 1 0 30
Spawn 'Crown Guardians' @(1895,954,20) 'Daemon' 2 2 20
ConvertTo-Json -InputObject @($remaining + $new.ToArray()) -Depth 12 | Set-Content "$stage\zodiac.json"
$tele=[Collections.Generic.List[object]]::new();$markers=[Collections.Generic.List[object]]::new()
function Portal($name,$p,$d,$hue) {
    $tele.Add(@{src=@{map='Felucca';loc=$p};dst=@{map='Felucca';loc=$d};back=$false})
    $markers.Add(@{Name=$name;X=$p[0];Y=$p[1];Z=$p[2];Hue=$hue})
}
function Landing($c,$r) {$p=Cell $c $r;@(($p[0]+1),$p[1],$p[2])}
Portal 'Labrynth - Violet Mirror' (Cell 1 1) (Landing 15 11) 1153
Portal 'Labrynth - Violet Mirror' (Cell 15 11) (Landing 1 1) 1153
Portal 'Labrynth - Amber Mirror' (Cell 8 1) (Landing 2 13) 2213
Portal 'Labrynth - Amber Mirror' (Cell 2 13) (Landing 8 1) 2213
Portal 'Labrynth - Jade Mirror' (Cell 5 7) (Landing 16 3) 1272
Portal 'Labrynth - Jade Mirror' (Cell 16 3) (Landing 5 7) 1272
Portal 'Exit - Labrynth Dungeon Cave' (Cell 18 1) @(1873,914,0) 1150
Portal 'Exit - Tower of the Thorn Crown' (Cell 18 14) @(1883,958,0) 2117
Portal 'Tower - Ascend to the Thorn Crown' @(1897,945,0) @(1883,958,20) 2117
Portal 'Tower - Descend' @(1883,959,20) @(1896,945,0) 1150
Portal 'Tower - Return to the Hedge Maze' @(1883,959,0) (Landing 18 14) 1272
$oldTele=@(Get-Content "$root\Distribution\Data\teleporters.json" -Raw | ConvertFrom-Json)
if(@($oldTele | Where-Object { $_.src.map -eq 'Felucca' -and (InFootprint $_.src.loc[0] $_.src.loc[1]) }).Count) {throw 'Existing teleporter in footprint needs review'}
ConvertTo-Json -InputObject @($oldTele+$tele.ToArray()) -Depth 10 | Set-Content "$stage\teleporters.json"
$manifest=@{RemovedSpawnerGuids=@($removed.guid);NewSpawnerGuids=@($new.guid);Portals=$markers.ToArray();Books=@(
    @{Name='The Gardener''s Warning';X=1804;Y=927;Z=0;Lines=@('The Labrynth remembers.','Violet, amber and jade','each have a twin. Step','aside after crossing.','The pale gate leads','to the northern cave.','The red gate leads to','the Thorn Crown.')},
    @{Name='The Silent Archive';X=1828;Y=931;Z=0;Lines=@('Not every path is true.','Walls are rooted here,','but distance is a lie.','Seek the white gate','for the dungeon below.','Bring allies before','challenging the tower.','Its crown is death.')},
    @{Name='A Moment of Stillness';X=1848;Y=955;Z=0;Lines=@('Sit with the stones.','Let the wind pass.','A wandering eye sees','only another turning.','The patient traveller','remembers the colors.','Even this garden has','a path back home.')}
)}
$manifest | ConvertTo-Json -Depth 10 | Set-Content "$stage\manifest.json"
"Old spawners replaced: $($removed.Count); new spawners: $($new.Count); teleporters: $($tele.Count)"
