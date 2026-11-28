$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('white-dragon-quota-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $fixture
$binary = Join-Path $fixture 'QuotaRefreshTests.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
try {
    $sources = @('src/QuotaRefresh.cs', 'src/CodexUsage.cs', 'src/SessionMonitor.cs', 'tests/QuotaRefreshTests.cs') |
        ForEach-Object { Join-Path $repo $_ }
    & $compiler /nologo /langversion:5 /codepage:65001 /reference:System.Web.Extensions.dll "/out:$binary" @sources
    if ($LASTEXITCODE) { throw 'Quota regression tests did not compile.' }
    & $binary $fixture
    if ($LASTEXITCODE) { throw 'Quota regression tests failed.' }
} finally {
    foreach ($file in @($binary, (Join-Path $fixture 'rollout-quota-test.jsonl'))) {
        if ([IO.File]::Exists($file)) { [IO.File]::Delete($file) }
    }
    [IO.Directory]::Delete($fixture)
}
