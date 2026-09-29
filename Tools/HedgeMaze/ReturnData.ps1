param([string]$Stage='C:\Users\minij\AppData\Local\Temp\arbor-maze-v8-stage')
$ErrorActionPreference='Stop'
$root='C:\workspace\Ultima_online\ModernUO\Distribution'
New-Item -ItemType Directory -Path $Stage -Force | Out-Null
$all=@(Get-Content "$root\zodiac.json" -Raw | ConvertFrom-Json)
$name='Arbor Labrynth - Southern Gazer Patrol'
if($all.name -contains $name){throw 'Already configured'}
$all+=@{type='Spawner';name=$name;guid=[guid]::NewGuid().ToString();location=@{x=1831;y=961;z=0};map='Felucca';count=1;minDelay='00:06:00';maxDelay='00:09:00';team=0;homeRange=1;walkingRange=2;entries=@(@{name='Gazer';probability=100;maxCount=1})}
$tele=@(Get-Content "$root\Data\teleporters.json" -Raw | ConvertFrom-Json)
$old=@($tele|Where-Object {$_.src.map -eq 'Felucca' -and ($_.src.loc -join ',') -eq '5249,784,0'})
if($old.Count -ne 1 -or ($old[0].dst.loc -join ',') -ne '1873,914,0'){throw 'Return gate mismatch'}
if(@($tele|Where-Object {$_.src.map -eq 'Felucca' -and ($_.src.loc -join ',') -eq '5248,784,0'}).Count){throw 'Destination source already exists'}
$old[0].src.loc=@(5248,784,0)
ConvertTo-Json -InputObject $all -Depth 12 | Set-Content "$Stage\zodiac.json"
ConvertTo-Json -InputObject $tele -Depth 12 | Set-Content "$Stage\teleporters.json"
