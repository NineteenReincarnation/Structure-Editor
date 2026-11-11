[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$appOutput = [IO.Path]::GetFullPath((Join-Path $projectRoot 'app'))
$project = Join-Path $projectRoot 'src/StructureEditor/StructureEditor.csproj'

# The publish folder is user-visible. Never delete it wholesale: there may
# be user-managed docs, branding, fonts or other local untracked files.
New-Item -ItemType Directory -Path $appOutput -Force | Out-Null

# Remove only the exact known outputs from previous multi-file distributions.
$legacyFiles = @(
    '.structure-editor-output',
    'StructureEditor.exe', 'StructureEditor.dll',
    'StructureEditor.deps.json', 'StructureEditor.runtimeconfig.json',
    'NAudio.dll', 'NAudio.Asio.dll', 'NAudio.Core.dll', 'NAudio.Midi.dll',
    'NAudio.Wasapi.dll', 'NAudio.WinForms.dll', 'NAudio.WinMM.dll'
)
foreach ($filename in $legacyFiles) {
    $file = Join-Path $appOutput $filename
    if (Test-Path -LiteralPath $file -PathType Leaf) {
        Remove-Item -LiteralPath $file -Force
    }
}

# Framework-dependent single-file executable: .NET Desktop Runtime is still
# required, but all managed assemblies, deps and runtimeconfig are bundled.
# The end-user sees only StructureEditor.exe, not a pile of NAudio DLL files.
# No ZIP, distribution folder or second launcher is produced.
dotnet publish $project -c $Configuration -r $Runtime --self-contained false `
    -o $appOutput -p:PublishSingleFile=true `
    -p:DebugType=None -p:DebugSymbols=false --nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$exe = Join-Path $appOutput 'StructureEditor.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw 'Single-file executable is missing.'
}
$unexpected = @(
    Get-ChildItem -LiteralPath $appOutput -File |
        Where-Object { $_.Name -like '*.dll' -or $_.Name -like '*.deps.json' -or $_.Name -like '*.runtimeconfig.json' }
)
if ($unexpected.Count -gt 0) {
    throw "Loose runtime dependencies after single-file publish: $($unexpected.Name -join ', ')"
}
Write-Output "Application: $exe (single-file, framework dependent)"
