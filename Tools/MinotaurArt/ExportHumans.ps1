$ErrorActionPreference = 'Stop'
Add-Type -Path 'C:/workspace/Ultima_online/UOFiddler-4.17.8/Ultima.dll'
[Ultima.Files]::SetMulPath('C:/workspace/Ultima_online/Ultima Online Classic 7.0.15.1 relpor')
foreach ($body in @(400,401)) {
    $outputDir = Join-Path $PSScriptRoot "human-$body"
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    $records = @()
    foreach ($action in @(0,4)) {
        for ($direction=0; $direction -lt 5; $direction++) {
            $frames = [Ultima.Animations]::GetAnimation($body,$action,$direction,1)
            if (!$frames -or !$frames[0].Bitmap) { throw "Missing human animation $body/$action/$direction" }
            for ($index=0; $index -lt $frames.Length; $index++) {
                $frame=$frames[$index]; $bitmap=$frame.Bitmap
                $filename="$action-$direction-$index.png"
                $bitmap.Save((Join-Path $outputDir $filename))
                $records += @{Action=$action;Direction=$direction;Frame=$index;File=$filename;X=$frame.Center.X;Y=$frame.Center.Y;Width=$bitmap.Width;Height=$bitmap.Height}
            }
        }
    }
    $records | ConvertTo-Json | Set-Content (Join-Path $outputDir 'frames.json')
    Write-Output "Human body ${body}: exported $($records.Count) frames (walk and idle)."
}
