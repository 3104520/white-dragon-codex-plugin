$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1')

$manifestPath = Join-Path $PSScriptRoot 'marketplace\plugins\white-dragon\.codex-plugin\plugin.json'
$version = (Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json).version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid plugin release version.' }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$dist = Join-Path $PSScriptRoot 'dist'
$null = New-Item -ItemType Directory -Path $dist -Force
$name = "white-dragon-codex-plugin-$version-windows-x64.zip"
$output = Join-Path $dist $name
$temporary = Join-Path $dist ('.package-' + [Guid]::NewGuid().ToString('N') + '.zip')
$files = @(
    'Install.cmd',
    'install.ps1',
    'README.md',
    'docs/mascot.png',
    'docs/preview.png',
    'licenses/DragonCodexBoot-MIT.txt',
    'startup/bin/WhiteDragonStartup.exe',
    'marketplace/.agents/plugins/marketplace.json',
    'marketplace/plugins/white-dragon/.codex-plugin/plugin.json',
    'marketplace/plugins/white-dragon/assets/dragon.png',
    'marketplace/plugins/white-dragon/runtime/DragonQuota.Plugin.dll'
)

try {
    $zip = [IO.Compression.ZipFile]::Open($temporary, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $files) {
            $source = Join-Path $PSScriptRoot $file
            $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $source, $file, [IO.Compression.CompressionLevel]::Optimal)
        }
    } finally {
        $zip.Dispose()
    }
    if ([IO.File]::Exists($output)) { [IO.File]::Replace($temporary, $output, [NullString]::Value) }
    else { [IO.File]::Move($temporary, $output) }
    $digest = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Value "$digest  $name" -Encoding ASCII
    Write-Output $output
} finally {
    if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
}
