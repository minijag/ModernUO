param([string]$StagedDir="$PSScriptRoot/redesign-v2/packed")
$ErrorActionPreference='Stop'
$live='C:/workspace/Ultima_online/Ultima Online Classic 7.0.15.1 relpor'
$staged=$StagedDir
if(Get-Process ClassicUO,UOFiddler -ErrorAction SilentlyContinue){throw 'Close ClassicUO and UOFiddler first'}
if((Get-Content "$staged/verification.txt" -Raw) -notmatch '^PASS: 280 decoded frames'){throw 'Missing passing verification'}
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss'
$backup=[IO.Path]::GetFullPath("$PSScriptRoot/../../Distribution/Backups/MinotaurRedesign-$stamp")
New-Item -ItemType Directory -Path $backup|Out-Null
$names=@('anim5.mul','anim5.idx')
$old=@{};$new=@{};$handles=@{}
foreach($name in $names){
 Copy-Item -LiteralPath "$live/$name" -Destination "$backup/$name"
 $old[$name]=[IO.File]::ReadAllBytes("$backup/$name")
 $new[$name]=[IO.File]::ReadAllBytes("$staged/$name")
}
$sha=[Security.Cryptography.SHA256]::Create()
function Hash-Bytes([byte[]]$bytes){[Convert]::ToHexString($sha.ComputeHash($bytes))}
# Recheck staging is still based on the exact backed-up live MUL.
if((Hash-Bytes $old['anim5.mul']) -ne [Convert]::ToHexString($sha.ComputeHash($new['anim5.mul'],0,$old['anim5.mul'].Length))){throw 'Live MUL changed after staging'}
$slots=@((Get-Content "$staged/manifest.json" -Raw|ConvertFrom-Json).Slot)
if($old['anim5.idx'].Length -ne $new['anim5.idx'].Length){throw 'Index length mismatch'}
for($i=0;$i -lt $old['anim5.idx'].Length;$i++){
 if($old['anim5.idx'][$i] -ne $new['anim5.idx'][$i] -and [int][math]::Floor($i/12) -notin $slots){throw 'Unrelated index change'}
}
$wrote=$false
try{
 foreach($name in $names){
  $h=[IO.File]::Open("$live/$name",[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
  $handles[$name]=$h
  if([Convert]::ToHexString($sha.ComputeHash($h)) -ne (Hash-Bytes $old[$name])){throw "Live $name changed after backup"}
 }
 $wrote=$true
 foreach($name in $names){$h=$handles[$name];$h.Position=0;$h.Write($new[$name],0,$new[$name].Length);$h.SetLength($new[$name].Length);$h.Flush($true)}
 foreach($name in $names){$h=$handles[$name];$h.Position=0;if([Convert]::ToHexString($sha.ComputeHash($h)) -ne (Hash-Bytes $new[$name])){throw "Installed hash mismatch $name"}}
}catch{
 if($wrote){foreach($name in $names){$h=$handles[$name];$h.Position=0;$h.Write($old[$name],0,$old[$name].Length);$h.SetLength($old[$name].Length);$h.Flush($true)}}
 throw
}finally{foreach($h in $handles.Values){$h.Dispose()};$sha.Dispose()}
$report=@{InstalledAt=(Get-Date -Format o);LiveFolder=$live;BackupFolder=$backup;Frames=280;Records=$slots.Count;Files=@{}}
foreach($name in $names){$report.Files[$name]=(Get-FileHash -LiteralPath "$live/$name" -Algorithm SHA256).Hash}
$report|ConvertTo-Json -Depth 4|Set-Content "$staged/deployment.json"
"Installed 280 redesigned frames. Backup: $backup. Both live file hashes verified."
