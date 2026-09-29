param([string]$Stage='C:\Users\minij\AppData\Local\Temp\arbor-maze-v3-stage')
$ErrorActionPreference='Stop'
$root='C:\workspace\Ultima_online\ModernUO'
if(Test-Path "$Stage\manifest.json"){throw 'Configuration already staged'}
$prior=Get-Content "$root\Distribution\Data\ArborMazeV2\manifest.json" -Raw | ConvertFrom-Json
$geo=Get-Content "$Stage\geometry.json" -Raw | ConvertFrom-Json
$all=@(Get-Content "$root\Distribution\zodiac.json" -Raw | ConvertFrom-Json)
$moves=@()
foreach($s in $all){if($s.guid -notin $prior.NewSpawnerGuids){continue};$old=@([int]$s.location.x,[int]$s.location.y,[int]$s.location.z)
 if($s.name -in @('Arbor Labrynth - Fort Gatekeepers','Arbor Labrynth - Fort Adepts','Arbor Labrynth - The Thorn Crown','Arbor Labrynth - Crown Guardians')){$s.location.x+=20;$s.location.y+=18}
 elseif($s.name -eq 'Arbor Labrynth - Maze encounter 9'){$s.location.x=1890;$s.location.y=930}
 else{continue}
 $moves+=@{Guid=$s.guid;Old=$old;New=@([int]$s.location.x,[int]$s.location.y,[int]$s.location.z)}
}
if($moves.Count -ne 5){throw 'Expected five spawner relocations'}
$oldMarks=@($prior.Portals | Where-Object {$_.Name -like 'Fort -*' -or $_.Name -eq 'Exit - Fort of the Thorn Crown'})
$oldKeys=@($oldMarks | ForEach-Object {"$($_.X),$($_.Y),$($_.Z)"})
$tele=@(Get-Content "$root\Distribution\Data\teleporters.json" -Raw | ConvertFrom-Json)
$newMarks=@();$newTele=@()
foreach($t in $tele){if($t.src.map -ne 'Felucca' -or ($t.src.loc -join ',') -notin $oldKeys){continue};$mark=$oldMarks | Where-Object {"$($_.X),$($_.Y),$($_.Z)" -eq ($t.src.loc -join ',')}
 if($mark.Name -in @('Fort - Ascend to the Thorn Crown','Fort - Descend')){
  $t.src.loc[0]+=20;$t.src.loc[1]+=18;$t.dst.loc[0]+=20;$t.dst.loc[1]+=18
  $newTele+= $t;$newMarks+=@{Name=$mark.Name;X=$t.src.loc[0];Y=$t.src.loc[1];Z=$t.src.loc[2];Hue=$mark.Hue}
 }
}
# Use original source coordinates to filter, since two objects above were translated.
$keep=@($tele | Where-Object {$_.src.map -ne 'Felucca' -or (($_.src.loc -join ',') -notin $oldKeys -and $_ -notin $newTele)})
$finalTele=@($keep+$newTele)
$validationMarks=@($prior.Portals | Where-Object { $_.Name -notlike 'Fort -*' -and $_.Name -ne 'Exit - Fort of the Thorn Crown' })+$newMarks
@{Moves=$moves;OldPortals=$oldMarks;NewPortals=$newMarks;AllPortals=$validationMarks;AllSpawnerGuids=$prior.NewSpawnerGuids;Scope=$geo.Scope} | ConvertTo-Json -Depth 12 | Set-Content "$Stage\manifest.json"
ConvertTo-Json -InputObject $all -Depth 12 | Set-Content "$Stage\zodiac.json"
ConvertTo-Json -InputObject $finalTele -Depth 12 | Set-Content "$Stage\teleporters.json"
"Moved $($moves.Count) spawners without changing GUIDs; removed four old fort portals, added two relocated deck portals. Maze portal total: $($validationMarks.Count)."
