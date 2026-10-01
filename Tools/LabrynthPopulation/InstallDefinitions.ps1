$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath("$PSScriptRoot/../..")
$path="$root/Distribution/zodiac.json"
$all=@(Get-Content -LiteralPath $path -Raw|ConvertFrom-Json)
$new=@(Get-Content "$PSScriptRoot/encounters.json" -Raw|ConvertFrom-Json)
if(@($all|Where-Object {$_.name -like 'Labrynth Dungeon - *'}).Count -gt 0){throw 'Definitions already exist'}
if($new.Count -ne 129){throw 'Unexpected encounter count'}
$ids=@($all.guid)
foreach($s in $new){if($s.guid -in $ids){throw 'Duplicate GUID'}}
# Preserve all existing records and append the generated population definitions.
$text=[IO.File]::ReadAllText($path).TrimEnd()
if(!$text.EndsWith(']')){throw 'Expected JSON array'}
$addition=($new|ConvertTo-Json -Depth 12).Trim()
$merged=$text.Substring(0,$text.Length-1).TrimEnd()+",`r`n"+$addition.Substring(1,$addition.Length-2)+"`r`n]`r`n"
$check=@($merged|ConvertFrom-Json)
if($check.Count -ne $all.Count+$new.Count){throw 'Record count mismatch'}
[IO.File]::WriteAllText($path,$merged)
"Added $($new.Count) encounters; all $($all.Count) existing definitions preserved."
