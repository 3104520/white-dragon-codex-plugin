[CmdletBinding(DefaultParameterSetName = 'Install')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Validate')][switch]$ValidateOnly,
    [Parameter(Mandatory, ParameterSetName = 'Uninstall')][switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$marketName = 'white-dragon-local'
$pluginId = "white-dragon@$marketName"
$programs = Join-Path $env:LOCALAPPDATA 'Programs'
$installDir = Join-Path $programs 'WhiteDragonCodexPlugin'
$marketplace = Join-Path $installDir 'marketplace'
$startupPath = Join-Path $installDir 'WhiteDragonStartup.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runName = 'WhiteDragonCodexPlugin'
$marketFiles = @(
    '.agents\plugins\marketplace.json',
    'plugins\white-dragon\.codex-plugin\plugin.json',
    'plugins\white-dragon\assets\dragon.png',
    'plugins\white-dragon\runtime\DragonQuota.Plugin.dll'
)

function Get-CodexExecutable {
    $packages = @(Get-AppxPackage -Name 'OpenAI.Codex' | Sort-Object Version -Descending)
    foreach ($package in $packages) {
        if ($package.Architecture -ne 'X64') { continue }
        $candidate = Join-Path $package.InstallLocation 'app\resources\codex.exe'
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    throw 'Install or update Codex for Windows from the Microsoft Store first.'
}

function Invoke-Codex {
    param([string[]]$Arguments)
    & $codex @Arguments
    if ($LASTEXITCODE -ne 0) { throw ('Codex command failed: ' + ($Arguments -join ' ')) }
}

function Get-StartupProcess {
    Get-Process -Name 'WhiteDragonStartup' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $startupPath }
}

function Stop-Startup {
    foreach ($process in (Get-StartupProcess)) {
        Stop-Process -InputObject $process -ErrorAction Stop
        if (-not $process.WaitForExit(3000)) { throw 'The startup component did not exit.' }
    }
}

function Test-LegacyMonitor {
    $legacyPath = Join-Path $programs 'DragonCodexBoot\DragonCodexBoot.exe'
    $command = Get-ItemPropertyValue -LiteralPath $runKey -Name 'DragonCodexBootMonitor' -ErrorAction SilentlyContinue
    $expected = '"{0}" --monitor' -f $legacyPath
    if ($command -ne $expected -or -not (Test-Path -LiteralPath $legacyPath -PathType Leaf)) { return $false }
    $assemblyText = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($legacyPath))
    return $assemblyText.Contains('WhiteDragonPlugin')
}

function Copy-PayloadFile {
    param([string]$Source, [string]$Target)
    if ($Source -eq $Target) { return }
    $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Target)
    Copy-Item -LiteralPath $Source -Destination $Target -Force
}

if (-not [Environment]::Is64BitProcess) {
    throw 'Run this package with 64-bit PowerShell on Windows x64.'
}
$codex = Get-CodexExecutable

if (-not $Uninstall) {
    $payload = Join-Path $PSScriptRoot 'marketplace'
    $startupSource = Join-Path $PSScriptRoot 'startup\bin\WhiteDragonStartup.exe'
    foreach ($relativePath in $marketFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $payload $relativePath) -PathType Leaf)) {
            throw "Missing package file $relativePath. Extract the release ZIP or run build.ps1 first."
        }
    }
    if (-not (Test-Path -LiteralPath $startupSource -PathType Leaf)) {
        throw 'Missing the startup component. Extract the release ZIP or run build.ps1 first.'
    }
    $manifest = Get-Content -LiteralPath (Join-Path $payload $marketFiles[1]) -Raw -Encoding UTF8 | ConvertFrom-Json
    $catalog = Get-Content -LiteralPath (Join-Path $payload $marketFiles[0]) -Raw -Encoding UTF8 | ConvertFrom-Json
    $pluginAssembly = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $payload $marketFiles[3]))
    if ($catalog.name -ne $marketName -or $manifest.name -ne 'white-dragon' -or
        $pluginAssembly.Name -ne 'DragonQuota.Plugin' -or $pluginAssembly.Version.ToString(3) -ne $manifest.version) {
        throw 'The White Dragon catalog, plugin manifest and DLL do not match.'
    }
    $test = Start-Process -FilePath $startupSource -ArgumentList '--check' -WindowStyle Hidden -Wait -PassThru
    if ($test.ExitCode -ne 0) { throw 'The startup component failed its check.' }
    if ($ValidateOnly) {
        Write-Output "White Dragon $($manifest.version) package validated."
        Invoke-Codex -Arguments @('--version')
        return
    }
}

$mutex = [Threading.Mutex]::new($false, 'Local\WhiteDragonCodexPlugin.Install')
$locked = $false
try {
    try { $locked = $mutex.WaitOne(0) }
    catch [Threading.AbandonedMutexException] { $locked = $true }
    if (-not $locked) { throw 'Another White Dragon installation is in progress.' }

    $sourceList = (Invoke-Codex -Arguments @('plugin', 'marketplace', 'list', '--json') | Out-String) | ConvertFrom-Json
    $configured = @($sourceList.marketplaces | Where-Object { $_.name -eq $marketName })
    if ($configured.Count -gt 0 -and $configured[0].root -ne $marketplace) {
        throw "A White Dragon source is already registered at $($configured[0].root). See Existing installations in README.md."
    }

    if ($Uninstall) {
        if ($configured.Count -gt 0) {
            Invoke-Codex -Arguments @('plugin', 'remove', $pluginId)
            Invoke-Codex -Arguments @('plugin', 'marketplace', 'remove', $marketName)
        }
        if (Get-ItemPropertyValue -LiteralPath $runKey -Name $runName -ErrorAction SilentlyContinue) {
            Remove-ItemProperty -LiteralPath $runKey -Name $runName
        }
        Stop-Startup
        $installedFiles = @($startupPath, (Join-Path $installDir 'logs\monitor.log'))
        $installedFiles += $marketFiles | ForEach-Object { Join-Path $marketplace $_ }
        foreach ($file in $installedFiles) {
            if (Test-Path -LiteralPath $file -PathType Leaf) { Remove-Item -LiteralPath $file -Force }
        }
        Write-Output 'White Dragon removed.'
        return
    }

    foreach ($relativePath in $marketFiles) {
        Copy-PayloadFile -Source (Join-Path $payload $relativePath) -Target (Join-Path $marketplace $relativePath)
    }
    if ($configured.Count -eq 0) {
        Invoke-Codex -Arguments @('plugin', 'marketplace', 'add', $marketplace)
    }
    Invoke-Codex -Arguments @('plugin', 'add', $pluginId)

    if (Test-LegacyMonitor) {
        if (Get-ItemPropertyValue -LiteralPath $runKey -Name $runName -ErrorAction SilentlyContinue) {
            Remove-ItemProperty -LiteralPath $runKey -Name $runName
        }
        Stop-Startup
        Write-Output 'Using the existing Dragon Codex Boot startup component.'
    } else {
        $startupNeedsCopy = -not (Test-Path -LiteralPath $startupPath -PathType Leaf)
        if (-not $startupNeedsCopy) {
            $startupNeedsCopy = (Get-FileHash -LiteralPath $startupPath).Hash -ne (Get-FileHash -LiteralPath $startupSource).Hash
        }
        if ($startupNeedsCopy) {
            Stop-Startup
            Copy-PayloadFile -Source $startupSource -Target $startupPath
        }
        if (-not (Test-Path -LiteralPath $runKey)) { $null = New-Item -Path $runKey }
        $null = New-ItemProperty -LiteralPath $runKey -Name $runName -Value ('"{0}"' -f $startupPath) -PropertyType String -Force
        if (-not (Get-StartupProcess)) { Start-Process -FilePath $startupPath -WindowStyle Hidden }
    }
    Write-Output "White Dragon $($manifest.version) installed. Open the Codex desktop app."
} finally {
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
