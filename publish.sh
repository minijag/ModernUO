#!/bin/bash
set -e
SOURCE_ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$SOURCE_ROOT"
exec dotnet run --project "$SOURCE_ROOT/Projects/BuildTool/BuildTool.csproj" -- "$@"
