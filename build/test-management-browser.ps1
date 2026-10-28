param([string]$Python = 'python', [string]$PublishedApi)
$ErrorActionPreference = 'Stop'
if ((Get-CimInstance Win32_ComputerSystem).Model -ne 'Virtual Machine') {
    throw 'Browser previews and tests run only inside a disposable VM or CI runner.'
}
$repo = Split-Path $PSScriptRoot -Parent
$testDll = Join-Path $repo 'hmailserver\source\Management\Tests\bin\Release\net10.0\ArzoieMail.Management.Tests.dll'
$live = -not [string]::IsNullOrEmpty($PublishedApi)
$results = Join-Path $repo $(if ($live) { 'out\management-browser-live-results' } else { 'out\management-browser-results' })
New-Item -ItemType Directory -Force $results | Out-Null
$env:ASPNETCORE_CONTENTROOT = if ($live) { $PublishedApi } else { Join-Path $repo 'hmailserver\source\Management\Api' }
$server = $null
try {
    $serverArguments = if ($live) { "`"$(Join-Path $PublishedApi 'ArzoieMail.Api.dll')`" --environment Development --urls http://127.0.0.1:18086" } else { "`"$testDll`" --browser-host" }
    $server = Start-Process dotnet -ArgumentList $serverArguments -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $results 'host.log') -RedirectStandardError (Join-Path $results 'host-error.log')
    $ready = $false
    for ($attempt=0; $attempt -lt 30; ++$attempt) {
        if ($server.HasExited) { throw 'Browser test host stopped during startup.' }
        try { Invoke-WebRequest 'http://127.0.0.1:18086/' -UseBasicParsing | Out-Null; $ready = $true; break }
        catch { Start-Sleep -Milliseconds 300 }
    }
    if (-not $ready) { throw 'Browser test host did not become ready.' }
    $browserScript = if ($live) { 'browser_live.py' } else { 'browser.py' }
    & $Python (Join-Path $repo "hmailserver\test\RegressionTests\Management\$browserScript") 'http://127.0.0.1:18086' $results
    if ($LASTEXITCODE -ne 0) { throw 'Browser management tests failed.' }
} finally {
    if ($null -ne $server -and -not $server.HasExited) { Stop-Process -Id $server.Id }
    $env:ASPNETCORE_CONTENTROOT = $null
}
