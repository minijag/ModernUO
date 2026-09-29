$ErrorActionPreference='Stop'
Add-Type -Path 'C:/workspace/Ultima_online/UOFiddler-4.17.8/Ultima.dll'
$live='C:/workspace/Ultima_online/Ultima Online Classic 7.0.15.1 relpor'
[Ultima.Files]::SetMulPath($live)
$outputDir=Join-Path $PSScriptRoot 'install-frames'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$replacement=@(Get-Content "$PSScriptRoot/frames/frames.json" -Raw | ConvertFrom-Json)
if($replacement.Count -ne 55){throw 'Expected 50 walk frames and 5 idle frames'}
$metadata=@()
for($action=0;$action -lt 22;$action++){
 for($direction=0;$direction -lt 5;$direction++){
  $frames=[Ultima.Animations]::GetAnimation(21,$action,$direction,5)
  if(!$frames -or !$frames[0].Bitmap){continue}
  for($i=0;$i -lt $frames.Length;$i++){
   $name="$action-$direction-$i.png"
   $frame=$frames[$i];$bitmap=$frame.Bitmap
   $match=@($replacement | Where-Object { $_.Action -eq $action -and $_.Direction -eq $direction -and $_.Frame -eq $i })
   if($match.Count -eq 1){
    Copy-Item -LiteralPath "$PSScriptRoot/frames/$name" -Destination "$outputDir/$name"
    $metadata+=$match[0]
   }else{
    $bitmap.Save("$outputDir/$name")
    $metadata+=@{Action=$action;Direction=$direction;Frame=$i;File=$name;X=$frame.Center.X;Y=$frame.Center.Y;Width=$bitmap.Width;Height=$bitmap.Height}
   }
  }
 }
}
if($metadata.Count -ne 280){throw "Unexpected live frame count $($metadata.Count)"}
$metadata | ConvertTo-Json -Depth 5 | Set-Content "$outputDir/frames.json"
'Staged current live frames with walking and matching idle replacements only.'
