param([switch]$Live, [switch]$Runtime, [switch]$PipeIdentity, [switch]$Tls, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
if ((Get-CimInstance Win32_ComputerSystem).Model -ne 'Virtual Machine') {
    throw 'Run runtime tests inside a disposable VM or CI runner. The development host is for editing and compilation only.'
}
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'hmailserver\source\Management\Tests\ArzoieMail.Management.Tests.csproj'
$env:ASPNETCORE_CONTENTROOT = Join-Path $repo 'hmailserver\source\Management\Api'
try {
    $arguments = @('run', '--project', $project, '--configuration', 'Release')
    if ($NoBuild) { $arguments += '--no-build' }
    if ($Live) { $arguments += @('--', '--live') }
    elseif ($Runtime) { $arguments += @('--', '--runtime') }
    elseif ($PipeIdentity) { $arguments += @('--', '--squatting') }
    elseif ($Tls) { $arguments += @('--', '--tls') }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Management regression tests failed.' }
} finally { $env:ASPNETCORE_CONTENTROOT = $null }
