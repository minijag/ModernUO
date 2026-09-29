param([string]$Stage='C:\Users\minij\AppData\Local\Temp\arbor-maze-v2-stage')
$ErrorActionPreference='Stop'
$root='C:\workspace\Ultima_online\ModernUO'
if(Test-Path "$Stage\manifest.json"){throw 'Already generated; preserve GUIDs instead of regenerating.'}
$geo=Get-Content "$Stage\geometry.json" -Raw | ConvertFrom-Json
$prior=Get-Content "$root\Distribution\Data\ArborMaze\manifest.json" -Raw | ConvertFrom-Json
$scope=[Collections.Generic.HashSet[string]]::new();foreach($p in $geo.Scope){[void]$scope.Add("$($p[0]),$($p[1])")}
$all=@(Get-Content "$root\Distribution\zodiac.json" -Raw | ConvertFrom-Json)
$removed=@($all | Where-Object {$_.map -eq 'Felucca' -and ($scope.Contains("$($_.location.x),$($_.location.y)") -or $_.guid -in $prior.NewSpawnerGuids)})
$keep=@($all | Where-Object { $_.guid -notin $removed.guid })
$new=[Collections.Generic.List[object]]::new()
function Spawn($label,$x,$y,$z,$mob,$count,$range,$minutes){$new.Add(@{type='Spawner';name="Arbor Labrynth - $label";guid=[guid]::NewGuid().ToString();location=@{x=$x;y=$y;z=$z};map='Felucca';count=$count;minDelay=([timespan]::FromMinutes($minutes)).ToString();maxDelay=([timespan]::FromMinutes($minutes+3)).ToString();team=0;homeRange=$range;walkingRange=$range;entries=@(@{name=$mob;probability=100;maxCount=$count})})}
# Spread encounters throughout the maze using farthest-point sampling.
$chosen=[Collections.Generic.List[object]]::new();$chosen.Add(($geo.Cells | Sort-Object { [math]::Abs($_.X-1810)+[math]::Abs($_.Y-942) } | Select-Object -First 1))
while($chosen.Count -lt 22){$candidate=$geo.Cells | Where-Object {[math]::Abs($_.X-1806)+[math]::Abs($_.Y-946) -gt 12} | Sort-Object -Descending { $p=$_; ($chosen | ForEach-Object {[math]::Abs($_.X-$p.X)+[math]::Abs($_.Y-$p.Y)} | Measure-Object -Minimum).Minimum } | Select-Object -First 1;$chosen.Add($candidate)}
$types=@('Reaper','Gazer','BogThing','EvilMage','AirElemental','ElderGazer','Wisp','Gazer','Reaper','EvilMage','BogThing')
for($i=0;$i -lt $chosen.Count;$i++){ $p=$chosen[$i];$count=if($types[$i%11] -in @('Reaper','Wisp')){1}else{2};Spawn "Maze encounter $($i+1)" $p.X $p.Y 0 $types[$i%11] $count 2 6 }
# Reuse the orc fort's ground courtyard and upper fighting deck, not the deleted tower.
Spawn 'Fort Gatekeepers' 1900 936 0 'Daemon' 2 2 12
Spawn 'Fort Adepts' 1889 933 0 'LichLord' 2 2 12
Spawn 'The Thorn Crown' 1882 928 19 'AncientLich' 1 0 30
Spawn 'Crown Guardians' 1882 926 19 'Daemon' 2 2 20
$newTele=[Collections.Generic.List[object]]::new();$marks=[Collections.Generic.List[object]]::new()
function Portal($name,$p,$d,$hue){$newTele.Add(@{src=@{map='Felucca';loc=$p};dst=@{map='Felucca';loc=$d};back=$false});$marks.Add(@{Name=$name;X=$p[0];Y=$p[1];Z=$p[2];Hue=$hue})}
function Near($x,$y){$p=$geo.Cells | Sort-Object {[math]::Abs($_.X-$x)+[math]::Abs($_.Y-$y)} | Select-Object -First 1;@([int]$p.X,[int]$p.Y,0)}
$pairs=@(@(1810,930,1926,942),@(1838,906,1870,970),@(1826,946,1910,918),@(1850,958,1862,894))
$names=@('Violet','Amber','Jade','Azure');$hues=@(1153,2213,1272,1150)
for($i=0;$i -lt 4;$i++){$v=$pairs[$i];$a=Near $v[0] $v[1];$b=Near $v[2] $v[3];Portal "Labrynth - $($names[$i]) Mirror" $a @(($b[0]+1),$b[1],0) $hues[$i];Portal "Labrynth - $($names[$i]) Mirror" $b @(($a[0]+1),$a[1],0) $hues[$i]}
$fortExit=Near 1910 934
Portal 'Exit - Labrynth Dungeon Cave' (Near 1858 910) @(1873,914,0) 1150
Portal 'Exit - Fort of the Thorn Crown' $fortExit @(1900,936,0) 2117
Portal 'Fort - Return to the Hedge Maze' @(1901,936,0) @(($fortExit[0]+1),$fortExit[1],0) 1272
Portal 'Fort - Ascend to the Thorn Crown' @(1899,934,0) @(1882,930,19) 2117
Portal 'Fort - Descend' @(1881,930,19) @(1899,935,0) 1150
$oldTele=@(Get-Content "$root\Distribution\Data\teleporters.json" -Raw | ConvertFrom-Json)
$oldKeys=@($prior.Portals | ForEach-Object {"$($_.X),$($_.Y),$($_.Z)"})
$keepTele=@($oldTele | Where-Object {$_.src.map -ne 'Felucca' -or ($_.src.loc -join ',') -notin $oldKeys})
$books=@();$bookNames=@('The Gardener''s Warning','The Silent Archive','A Moment of Stillness')
for($i=0;$i -lt 3;$i++){$r=$geo.Rooms[$i];$books+=@{Name=$bookNames[$i];X=$r.X;Y=($r.Y+1);Z=0;Lines=@('Four colors, four twins.','Each mirror bends space.','The bridge is the way','back to Arbor. Follow','the north trail to the','cavern. The stone fort','holds the Thorn Crown.','Bring trusted allies.')}}
@{RemovedSpawnerGuids=@($removed.guid);NewSpawnerGuids=@($new.guid);Portals=$marks.ToArray();Books=$books;OldPortals=$prior.Portals;OldBooks=$prior.Books;Scope=$geo.Scope} | ConvertTo-Json -Depth 12 | Set-Content "$Stage\manifest.json"
ConvertTo-Json -InputObject @($keep+$new.ToArray()) -Depth 12 | Set-Content "$Stage\zodiac.json"
ConvertTo-Json -InputObject @($keepTele+$newTele.ToArray()) -Depth 12 | Set-Content "$Stage\teleporters.json"
"Replacing $($removed.Count) spawners with $($new.Count), $($newTele.Count) teleporters, population $(($new | Measure-Object count -Sum).Sum)."
