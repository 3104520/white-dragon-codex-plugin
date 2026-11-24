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
$uninstallScript = Join-Path $installDir 'install.ps1'
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
}

function Invoke-Codex {
    param([string[]]$Arguments)
    & $codex @Arguments
    if ($LASTEXITCODE -ne 0) { throw ('Codex command failed: ' + ($Arguments -join ' ')) }
}

function Get-RegisteredMarketplace {
    param([switch]$IncludeDisabled)
    $arguments = @('plugin', 'marketplace', 'list', '--json')
    if ($IncludeDisabled) { $arguments += @('--enable', 'plugins') }
    $sourceList = (Invoke-Codex -Arguments $arguments | Out-String) | ConvertFrom-Json
    $sourceList.marketplaces | Where-Object { $_.name -eq $marketName } | Select-Object -First 1
}

function Get-RunEntry {
    param([string]$Name)
    $properties = Get-ItemProperty -LiteralPath $runKey -ErrorAction SilentlyContinue
    if ($null -ne $properties) {
        $entry = $properties.PSObject.Properties[$Name]
        if ($null -ne $entry) { return $entry.Value }
    }
}

function Remove-StartupRunEntry {
    if ($null -ne (Get-RunEntry -Name $runName)) {
        Remove-ItemProperty -LiteralPath $runKey -Name $runName
    }
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
    $command = Get-RunEntry -Name 'DragonCodexBootMonitor'
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

if (-not $Uninstall) {
    $codex = Get-CodexExecutable
    if (-not $codex) { throw 'Install or update Codex for Windows from the Microsoft Store first.' }
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

    if ($Uninstall) {
        Remove-StartupRunEntry
        Stop-Startup
        $registrationCleared = $false
        try {
            $codex = Get-CodexExecutable
            if ($codex) {
                $configured = Get-RegisteredMarketplace -IncludeDisabled
                $ownsSource = $configured -and $configured.root -eq $marketplace
                $localCatalog = Join-Path $marketplace $marketFiles[0]
                if ($ownsSource -or (-not $configured -and (Test-Path -LiteralPath $localCatalog -PathType Leaf))) {
                    Invoke-Codex -Arguments @('plugin', 'remove', $pluginId, '--enable', 'plugins')
                }
                if ($ownsSource) { Invoke-Codex -Arguments @('plugin', 'marketplace', 'remove', $marketName, '--enable', 'plugins') }
                $registrationCleared = $true
            } else {
                Write-Warning 'Codex is unavailable. To remove its marketplace registration, rerun uninstall after restoring Codex.'
            }
        } catch {
            Write-Warning ('Codex registration cleanup failed. Rerun uninstall to retry. ' + $_.Exception.Message)
        }

        $installedFiles = @($startupPath, (Join-Path $installDir 'logs\monitor.log'))
        if ($registrationCleared) {
            $installedFiles += $uninstallScript
            $installedFiles += $marketFiles | ForEach-Object { Join-Path $marketplace $_ }
        } else {
            # Codex needs the manifests intact to list the registered source on a retry.
            $installedFiles += $marketFiles | Select-Object -Skip 2 | ForEach-Object { Join-Path $marketplace $_ }
        }
        foreach ($file in $installedFiles) {
            if (Test-Path -LiteralPath $file -PathType Leaf) { Remove-Item -LiteralPath $file -Force }
        }
        Write-Output 'White Dragon startup component and runtime files were removed.'
        return
    }

    $configured = Get-RegisteredMarketplace
    if ($configured -and $configured.root -ne $marketplace) {
        throw "A White Dragon source is already registered at $($configured.root). See Existing installations in README.md."
    }

    Copy-PayloadFile -Source $PSCommandPath -Target $uninstallScript
    foreach ($relativePath in $marketFiles) {
        Copy-PayloadFile -Source (Join-Path $payload $relativePath) -Target (Join-Path $marketplace $relativePath)
    }
    if (-not $configured) {
        Invoke-Codex -Arguments @('plugin', 'marketplace', 'add', $marketplace)
    }
    Invoke-Codex -Arguments @('plugin', 'add', $pluginId)

    if (Test-LegacyMonitor) {
        Remove-StartupRunEntry
        Stop-Startup
        Write-Output 'Using the existing Dragon Codex Boot startup registration.'
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
        if (-not (Get-StartupProcess)) {
            $started = Start-Process -FilePath $startupPath -WindowStyle Hidden -PassThru
            if ($started.WaitForExit(750) -and -not (Get-StartupProcess)) {
                throw "The startup component exited with code $($started.ExitCode). Run Install.cmd again to retry."
            }
        }
        Write-Output 'White Dragon startup component is running.'
    }
    Write-Output "White Dragon $($manifest.version) installed. Open the Codex desktop app."
} finally {
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
