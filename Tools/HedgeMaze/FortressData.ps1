param([string]$Stage='C:\Users\minij\AppData\Local\Temp\arbor-maze-v4-stage')
$ErrorActionPreference='Stop';$root='C:\workspace\Ultima_online\ModernUO'
if(Test-Path "$Stage\manifest.json"){throw 'Already staged'}
$prior=Get-Content "$root\Distribution\Data\ArborMazeV3\manifest.json" -Raw | ConvertFrom-Json
$geo=Get-Content "$Stage\geometry.json" -Raw | ConvertFrom-Json
$all=@(Get-Content "$root\Distribution\zodiac.json" -Raw | ConvertFrom-Json)
foreach($s in $all){if($s.guid -notin $prior.AllSpawnerGuids){continue}
 if($s.name -like 'Arbor Labrynth - Maze encounter *'){$s.count++;$s.entries[0].maxCount=$s.count}
 switch($s.name){
 'Arbor Labrynth - Fort Gatekeepers' {$s.location=@{x=1910;y=953;z=0}}
 'Arbor Labrynth - Fort Adepts' {$s.location=@{x=1917;y=952;z=20}}
 'Arbor Labrynth - The Thorn Crown' {$s.location=@{x=1916;y=953;z=40}}
 'Arbor Labrynth - Crown Guardians' {$s.location=@{x=1916;y=955;z=40}}
 }
}
$new=@{type='Spawner';name='Arbor Labrynth - Watchtower Sentries';guid=[guid]::NewGuid().ToString();location=@{x=1903;y=940;z=20};map='Felucca';count=2;minDelay='00:12:00';maxDelay='00:15:00';team=0;homeRange=2;walkingRange=2;entries=@(@{name='Daemon';probability=100;maxCount=2})}
$all+= $new
$keys=@($prior.NewPortals | ForEach-Object {"$($_.X),$($_.Y),$($_.Z)"})
$tele=@(Get-Content "$root\Distribution\Data\teleporters.json" -Raw | ConvertFrom-Json)
$tele=@($tele | Where-Object {$_.src.map -ne 'Felucca' -or ($_.src.loc -join ',') -notin $keys})
$guids=@($prior.AllSpawnerGuids)+@($new.guid)
@{OldSpawnerGuids=$prior.AllSpawnerGuids;AllSpawnerGuids=$guids;OldPortals=$prior.NewPortals;AllPortals=@($prior.AllPortals | Where-Object {$_.Name -notlike 'Fort -*'});Scope=$geo.Scope;Checks=$geo.Checks;Platforms=$geo.Platforms} | ConvertTo-Json -Depth 12 | Set-Content "$Stage\manifest.json"
ConvertTo-Json -InputObject $all -Depth 12 | Set-Content "$Stage\zodiac.json"
ConvertTo-Json -InputObject $tele -Depth 12 | Set-Content "$Stage\teleporters.json"
"27 spawners, population $(($all | Where-Object {$_.guid -in $guids} | Measure-Object count -Sum).Sum); two stair portal pairs removed."
