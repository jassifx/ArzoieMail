param([switch]$Live)
$ErrorActionPreference = 'Stop'
if ((Get-CimInstance Win32_ComputerSystem).Model -ne 'Virtual Machine') {
    throw 'Run runtime tests inside a disposable VM or CI runner. The development host is for editing and compilation only.'
}
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'hmailserver\source\Management\Tests\ArzoieMail.Management.Tests.csproj'
$env:ASPNETCORE_CONTENTROOT = Join-Path $repo 'hmailserver\source\Management\Api'
try {
    $arguments = @('run', '--project', $project, '--configuration', 'Release')
    if ($Live) { $arguments += @('--', '--live') }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Management regression tests failed.' }
} finally { $env:ASPNETCORE_CONTENTROOT = $null }
