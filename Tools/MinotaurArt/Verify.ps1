param([string]$InputDir="$PSScriptRoot/adapted",[string]$OutputDir="$PSScriptRoot/full-set")
$ErrorActionPreference='Stop'
Add-Type -Path 'C:/workspace/Ultima_online/UOFiddler-4.17.8/Ultima.dll'
$out=$OutputDir
[Ultima.Files]::SetMulPath($out)
$meta=@(Get-Content "$InputDir/frames.json" -Raw|ConvertFrom-Json)
$manifest=@(Get-Content "$out/manifest.json" -Raw|ConvertFrom-Json)
$decoded="$out/decoded";New-Item -ItemType Directory -Path $decoded -Force|Out-Null
$count=0;$maxError=0
foreach($group in ($meta|Group-Object Action,Direction)){
 $records=@($group.Group|Sort-Object Frame);$frames=[Ultima.Animations]::GetAnimation(21,$records[0].Action,$records[0].Direction,5)
 if($frames.Length -ne $records.Count){throw 'Frame count mismatch'}
 for($i=0;$i -lt $frames.Length;$i++){
  $m=$records[$i];$f=$frames[$i];$expected=[Drawing.Bitmap]::FromFile("$InputDir/$($m.File)")
  if($f.Bitmap.Width -ne $m.Width -or $f.Bitmap.Height -ne $m.Height -or $f.Center.X -ne $m.X -or $f.Center.Y -ne $m.Y){throw "Geometry mismatch $($m.File)"}
  for($y=0;$y -lt $m.Height;$y++){for($x=0;$x -lt $m.Width;$x++){$a=$expected.GetPixel($x,$y);$b=$f.Bitmap.GetPixel($x,$y);if(($a.A -ge 128) -ne ($b.A -gt 0)){throw "Transparency mismatch $($m.File) at $x,$y"};if($a.A -ge 128){$err=[math]::Max([math]::Abs($a.R-$b.R),[math]::Max([math]::Abs($a.G-$b.G),[math]::Abs($a.B-$b.B)));$maxError=[math]::Max($maxError,$err);if($err -gt 9){throw "Color mismatch $($m.File): $err"}}}}
  $f.Bitmap.Save("$decoded/$($m.File)");$expected.Dispose();$count++
 }
}
$live='C:/workspace/Ultima_online/Ultima Online Classic 7.0.15.1 relpor'
$old=[IO.File]::ReadAllBytes("$live/anim5.idx");$new=[IO.File]::ReadAllBytes("$out/anim5.idx");$allowed=@($manifest.Slot);$changed=[Collections.Generic.HashSet[int]]::new()
if($old.Length -ne $new.Length){throw 'Index length changed'}
for($i=0;$i -lt $old.Length;$i++){if($old[$i] -ne $new[$i]){$slot=[int][math]::Floor($i/12);if($slot -notin $allowed){throw "Unrelated slot changed: $slot"};[void]$changed.Add($slot)}}
$a=[IO.File]::ReadAllBytes("$live/anim5.mul");$b=[IO.File]::ReadAllBytes("$out/anim5.mul");$sha=[Security.Cryptography.SHA256]::Create()
if([Convert]::ToHexString($sha.ComputeHash($a)) -ne [Convert]::ToHexString($sha.ComputeHash($b,0,$a.Length))){throw 'Original MUL bytes changed'}
if($count -ne 280 -or $changed.Count -ne $manifest.Count){throw 'Incomplete patch'}
$report="PASS: $count decoded frames; frame counts, dimensions, anchors and alpha verified. Max RGB555 color error $maxError. Exactly $($manifest.Count) intended records changed; original MUL prefix intact; unrelated and empty slots untouched."
$report|Set-Content "$out/verification.txt";$report
