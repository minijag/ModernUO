param([string]$Stage='C:\Users\minij\AppData\Local\Temp\arbor-maze-v6-stage')
$ErrorActionPreference='Stop'
$root='C:\workspace\Ultima_online\ModernUO\Distribution'
New-Item -ItemType Directory -Path $Stage -Force | Out-Null
$all=@(Get-Content "$root\zodiac.json" -Raw | ConvertFrom-Json)
$new=@()
foreach($entry in @(@('Whispering Shrine',1795,930,'ElderGazer'),@('Eastern Fort Patrol',1921,937,'Daemon'))){
 $name="Arbor Labrynth - $($entry[0])"
 if($all.name -contains $name){throw 'Already configured'}
 $new+=@{type='Spawner';name=$name;guid=[guid]::NewGuid().ToString();location=@{x=$entry[1];y=$entry[2];z=0};map='Felucca';count=1;minDelay='00:06:00';maxDelay='00:09:00';team=0;homeRange=1;walkingRange=2;entries=@(@{name=$entry[3];probability=100;maxCount=1})}
}
$tele=@(Get-Content "$root\Data\teleporters.json" -Raw | ConvertFrom-Json)
if(@($tele | Where-Object {$_.src.map -eq 'Felucca' -and ($_.src.loc -join ',') -eq '5249,784,0'}).Count){throw 'Existing return teleporter definition'}
$tele+=@{src=@{map='Felucca';loc=@(5249,784,0)};dst=@{map='Felucca';loc=@(1873,914,0)};back=$false}
ConvertTo-Json -InputObject @($all+$new) -Depth 12 | Set-Content "$Stage\zodiac.json"
ConvertTo-Json -InputObject $tele -Depth 12 | Set-Content "$Stage\teleporters.json"
$new | ConvertTo-Json -Depth 12 | Set-Content "$Stage\new-spawners.json"
