[CmdletBinding(SupportsShouldProcess = $true)]
param()

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$app = Join-Path $root 'app'
if (-not (Test-Path -LiteralPath $app -PathType Container)) { throw 'app/ does not exist.' }
$exe = Join-Path $app 'StructureEditor.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw 'StructureEditor.exe is missing. Pull main and build the program first.'
}

# Migrate only the known legacy *user* files. Never delete unknown files.
$move = @(
    @{ From = 'docs'; To = 'docs/app-legacy'; Folder = $true },
    @{ From = 'ICON_SOURCE.txt'; To = 'branding/ICON_SOURCE.txt'; Folder = $false },
    @{ From = 'StructureEditor.ico'; To = 'branding/StructureEditor.ico'; Folder = $false }
)
foreach ($entry in $move) {
    $from = Join-Path $app $entry.From
    $to = Join-Path $root $entry.To
    if (-not (Test-Path -LiteralPath $from)) { continue }
    if (Test-Path -LiteralPath $to) {
        Write-Warning "Kept $from because destination already exists: $to"
        continue
    }
    if ($PSCmdlet.ShouldProcess($from, "Move to $to")) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $to) -Force | Out-Null
        Move-Item -LiteralPath $from -Destination $to
    }
}

# Remove only obsolete *known* generated files. Their absence does not
# affect the new single-file exe. Require it to be from the latest CI:
# inspect this run only AFTER pulling the updated main branch.
$staleFiles = @(
    '.structure-editor-output',
    'StructureEditor.dll', 'StructureEditor.deps.json',
    'StructureEditor.runtimeconfig.json',
    'NAudio.dll', 'NAudio.Asio.dll', 'NAudio.Core.dll',
    'NAudio.Midi.dll', 'NAudio.Wasapi.dll',
    'NAudio.WinForms.dll', 'NAudio.WinMM.dll'
)
foreach ($name in $staleFiles) {
    $file = Join-Path $app $name
    if (Test-Path -LiteralPath $file -PathType Leaf) {
        if ($PSCmdlet.ShouldProcess($file, 'Remove obsolete generated file')) {
            Remove-Item -LiteralPath $file -Force
        }
    }
}
Write-Output 'app/ cleanup complete. Other unrecognized personal files were preserved.'
