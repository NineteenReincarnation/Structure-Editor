[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$src = Join-Path $root 'src/StructureEditor'

function RequireFolder([string]$relative) {
    $target = Join-Path $src $relative
    if (-not (Test-Path -LiteralPath $target -PathType Container)) {
        throw "Missing source module: $relative"
    }
}
@(
    'App', 'Core/Models', 'Core/Services',
    'Canvas/Editing', 'Canvas/Rendering',
    'Catalog/Discovery', 'Catalog/Collections', 'Catalog/Textures',
    'Catalog/Caching', 'Catalog/Metadata',
    'Compatibility', 'Formats/Codecs', 'Formats/Export',
    'UI/Workspace', 'UI/Controls', 'UI/Audio', 'UI/Theme',
    'UI/Motion', 'UI/Help', 'UI/Wpf/Navigation', 'UI/Wpf/Libraries',
    'UI/Wpf/Inspector', 'UI/Wpf/Motion', 'UI/Wpf/Primitives',
    'Validation/Core', 'Validation/Interaction', 'Validation/Structures',
    'Validation/Workspace', 'Validation/Performance', 'Validation/Smoke'
) | ForEach-Object { RequireFolder $_ }

foreach ($folder in @('UI', 'UI/Wpf', 'Catalog', 'Canvas', 'Core', 'Formats', 'Validation')) {
    $loose = @(Get-ChildItem -LiteralPath (Join-Path $src $folder) -File -Filter '*.cs')
    if ($loose.Count -ne 0) {
        throw "Loose C# files outside a module ($folder): $($loose.Name -join ', ')"
    }
}
$required = @(
    'App/Program.cs',
    'UI/Workspace/MainForm.cs',
    'UI/Workspace/MP_PaletteCollectionCommands.cs',
    'UI/Audio/MP_UiSound.cs',
    'UI/Audio/MP_GamepadSoundDesign.cs',
    'Validation/Smoke/MP_UiSmokeChecks.cs',
    'Formats/Codecs/FrameworkCodec.cs'
)
foreach ($file in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $src $file) -PathType Leaf)) {
        throw "Missing required source file: $file"
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $root 'structures/README.md') -PathType Leaf)) {
    throw 'The canonical editable structures/ library is missing.'
}
Write-Output 'Module layout OK: UI, domain, validation and structure library have stable owners.'
