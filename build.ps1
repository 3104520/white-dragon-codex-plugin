param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpfDir = Join-Path $frameworkDir 'WPF'
$compiler = Join-Path $frameworkDir 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'Building this project requires the 64-bit .NET Framework 4.x compiler on Windows.'
}
$sourceDir = Join-Path $projectDir 'src'
$assetPath = Join-Path $projectDir 'assets\dragon.png'
$iconPath = Join-Path $projectDir 'assets\dragon.ico'
$package = Join-Path $projectDir 'marketplace\plugins\white-dragon'
$module = if ($OutputPath) { [System.IO.Path]::GetFullPath($OutputPath) } else { Join-Path $package 'runtime\DragonQuota.Plugin.dll' }
$diagnostic = [System.IO.Path]::GetExtension($module) -eq '.exe'
$null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $module)

Add-Type -AssemblyName System.Drawing
$source = [System.Drawing.Image]::FromFile($assetPath)
$iconFrames = [System.Collections.Generic.List[object]]::new()
try {
    foreach ($size in 16, 32, 48, 256) {
        $frame = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $canvas = [System.Drawing.Graphics]::FromImage($frame)
        $encoded = [System.IO.MemoryStream]::new()
        try {
            $canvas.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $canvas.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $inset = [int][Math]::Floor($size * 0.04)
            $canvas.DrawImage($source, $inset, $inset, $size - 2 * $inset, $size - 2 * $inset)
            $frame.Save($encoded, [System.Drawing.Imaging.ImageFormat]::Png)
            $iconFrames.Add(@{ Size = $size; Bytes = $encoded.ToArray() })
        } finally {
            $encoded.Dispose()
            $canvas.Dispose()
            $frame.Dispose()
        }
    }
} finally {
    $source.Dispose()
}

$icon = [System.IO.BinaryWriter]::new([System.IO.File]::Create($iconPath))
try {
    $icon.Write([uint16]0)
    $icon.Write([uint16]1)
    $icon.Write([uint16]$iconFrames.Count)
    $offset = 6 + 16 * $iconFrames.Count
    foreach ($frame in $iconFrames) {
        $icon.Write([byte]($frame.Size % 256))
        $icon.Write([byte]($frame.Size % 256))
        $icon.Write([byte]0)
        $icon.Write([byte]0)
        $icon.Write([uint16]1)
        $icon.Write([uint16]32)
        $icon.Write([uint32]$frame.Bytes.Length)
        $icon.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $iconFrames) { $icon.Write([byte[]]$frame.Bytes) }
} finally {
    $icon.Dispose()
}

$parameters = @(
    '/nologo', '/platform:x64', '/optimize+', '/langversion:5', '/codepage:65001', "/out:$module",
    "/resource:$assetPath,DragonQuota.Dragon.png",
    "/resource:$(Join-Path $projectDir 'assets\thinking.png'),DragonQuota.thinking.png",
    "/resource:$(Join-Path $projectDir 'assets\poke.png'),DragonQuota.poke.png",
    "/resource:$(Join-Path $projectDir 'assets\celebrate.png'),DragonQuota.celebrate.png",
    '/reference:System.Drawing.dll', "/reference:$wpfDir\UIAutomationTypes.dll", "/reference:$wpfDir\UIAutomationProvider.dll",
    '/reference:System.Web.Extensions.dll',
    "/reference:$(Join-Path $wpfDir 'WindowsBase.dll')", "/reference:$(Join-Path $wpfDir 'PresentationCore.dll')",
    "/reference:$(Join-Path $wpfDir 'PresentationFramework.dll')", "/reference:$(Join-Path $frameworkDir 'System.Xaml.dll')"
)
if ($diagnostic) {
    $parameters += '/target:winexe', "/win32icon:$iconPath", "/win32manifest:$(Join-Path $sourceDir 'app.manifest')"
} else {
    $parameters += '/target:library'
    $iconDir = Join-Path $package 'assets'
    $null = New-Item -ItemType Directory -Force -Path $iconDir
    Copy-Item -LiteralPath $assetPath -Destination (Join-Path $iconDir 'dragon.png') -Force
}
$sources = @(Get-ChildItem -LiteralPath $sourceDir -Filter '*.cs' -File | Select-Object -ExpandProperty FullName)
& $compiler @parameters @sources
if ($LASTEXITCODE -ne 0) { throw "C# compilation failed (exit $LASTEXITCODE)." }
Write-Output $module

if (-not $OutputPath) {
    $startupDir = Join-Path $projectDir 'startup'
    $startupBin = Join-Path $startupDir 'bin'
    $null = New-Item -ItemType Directory -Force -Path $startupBin
    $startupApp = Join-Path $startupBin 'WhiteDragonStartup.exe'
    $startupParameters = @(
        '/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/langversion:5', '/codepage:65001',
        "/out:$startupApp", "/win32manifest:$(Join-Path $startupDir 'app.manifest')"
    )
    $startupSources = @(Get-ChildItem -LiteralPath $startupDir -Filter '*.cs' -File | Select-Object -ExpandProperty FullName)
    & $compiler @startupParameters @startupSources
    if ($LASTEXITCODE -ne 0) { throw "Startup component compilation failed (exit $LASTEXITCODE)." }
    Write-Output $startupApp
}
