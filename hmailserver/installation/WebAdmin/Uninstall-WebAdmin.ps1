<#
.SYNOPSIS
    Removes the ArzoieMail web administration service. Run by setup when uninstalling.

.DESCRIPTION
    Stops and deletes the ArzoieMailAdmin service, removes the panel's identity from
    hMailServer.ini (which closes the mail service's management pipe at its next start), and
    removes the panel's own HTTPS certificate when appsettings.Production.json names one it
    created. Mail listener certificates are not touched.

.PARAMETER InstallDirectory
    The ArzoieMail installation folder.

.PARAMETER IniFile
    The path of hMailServer.ini.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InstallDirectory,
    [Parameter(Mandatory = $true)][string]$IniFile
)
$ErrorActionPreference = 'Stop'
# Started from PowerShell 7, setup's environment can carry PowerShell 7's module path, whose
# core modules Windows PowerShell cannot load (the Cert: drive then goes missing). Use Windows
# PowerShell's own module folders.
$env:PSModulePath = "$env:ProgramFiles\WindowsPowerShell\Modules;$env:SystemRoot\System32\WindowsPowerShell\v1.0\Modules"
Import-Module Microsoft.PowerShell.Security
$serviceName = 'ArzoieMailAdmin'
$settingsFile = Join-Path $InstallDirectory 'WebAdmin\appsettings.Production.json'

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force
        (Get-Service -Name $serviceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }
    & sc.exe delete $serviceName | Out-Null
}

if (Test-Path -LiteralPath $IniFile) {
    if (-not ('ArzoieWebAdminIni' -as [type])) {
        Add-Type 'using System.Runtime.InteropServices; public static class ArzoieWebAdminIni { [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] public static extern bool WritePrivateProfileString(string section, string key, string value, string file); }'
    }
    [void][ArzoieWebAdminIni]::WritePrivateProfileString('Management', 'ApiClientSID', $null, $IniFile)
}

if (Test-Path -LiteralPath $settingsFile) {
    $subject = (Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json).Kestrel.Endpoints.Https.Certificate.Subject
    if ($subject -like 'ArzoieMail Administration *') {
        Get-ChildItem 'Cert:\LocalMachine\My' | Where-Object { $_.Subject -eq "CN=$subject" } | Remove-Item -DeleteKey
    }
    Remove-Item -LiteralPath $settingsFile -Force
}
