$ErrorActionPreference='Stop'
$all=@(Get-Content 'C:/workspace/Ultima_online/ModernUO/Distribution/zodiac.json' -Raw|ConvertFrom-Json)
$name='Labrynth - Tormented Minotaur Art Preview'
if($all.name -contains $name){throw 'Already configured'}
$all+=@{type='Spawner';name=$name;guid=[guid]::NewGuid().ToString();location=@{x=5276;y=715;z=0};map='Felucca';count=1;minDelay='00:05:00';maxDelay='00:07:00';team=0;homeRange=0;walkingRange=0;entries=@(@{name='TormentedMinotaur';probability=100;maxCount=1})}
ConvertTo-Json -InputObject $all -Depth 12|Set-Content 'C:/workspace/Ultima_online/ModernUO/Tools/MinotaurArt/staged/zodiac.json'
