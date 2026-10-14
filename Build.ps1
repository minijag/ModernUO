$ErrorActionPreference = 'Stop'
dotnet build (Join-Path $PSScriptRoot 'Projects/Application/Application.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Server build failed.' }
Write-Host 'Built into Server/Distribution/Build. The live server was not modified.'
