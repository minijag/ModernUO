$ErrorActionPreference = 'Stop'
# Use this checkout's build tool so its output paths match the workspace layout.
Push-Location $PSScriptRoot
try {
    & dotnet run --project (Join-Path $PSScriptRoot 'Projects/BuildTool/BuildTool.csproj') -- @args
    $result = $LASTEXITCODE
}
finally {
    Pop-Location
}
exit $result
