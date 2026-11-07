[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$GameRoot, [switch]$IncludeMods)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath (Join-Path $GameRoot 'Data/Core/Defs'))) { throw 'GameRoot must contain Data/Core/Defs.' }
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
dotnet build (Join-Path $projectRoot 'src/StructureEditor/StructureEditor.csproj') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$arguments = @((Join-Path $projectRoot 'src/StructureEditor/bin/Debug/net10.0-windows/StructureEditor.dll'), '--self-test', '--game', $GameRoot)
if (-not $IncludeMods) { $arguments += '--core-only' }
dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Validation failed; see $env:LOCALAPPDATA/StructureEditor/error.log" }
Get-Content -LiteralPath (Join-Path $env:LOCALAPPDATA 'StructureEditor/Validation/checks.txt')
