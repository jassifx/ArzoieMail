<#
.SYNOPSIS
    Sets up the ArzoieMail web administration panel as a Windows service. Run by setup.

.DESCRIPTION
    - Creates (or updates) the ArzoieMailAdmin service, running ArzoieMail.Api.exe as its own
      virtual account, NT SERVICE\ArzoieMailAdmin, which has no access to the mail store.
    - Lets that account, and only it, use the mail service's management pipe, by writing its
      SID as [Management] ApiClientSID in hMailServer.ini, then restarts the mail service.
    - On a first installation, gives the panel its own self-signed HTTPS certificate in the
      machine store, readable by the panel's account, and writes appsettings.Production.json:
      HTTPS on the given port for this computer's name and localhost, and ACME HTTP-01 on
      port 80 when nothing else uses it. An existing appsettings.Production.json is kept, so
      upgrades keep the administrator's choices.
    - Registers the event log source the panel writes to.

    It opens no firewall port. Mail listener certificates are separate: the panel never gets
    access to their keys.

.PARAMETER InstallDirectory
    The ArzoieMail installation folder (the panel is in its WebAdmin folder).

.PARAMETER IniFile
    The path of hMailServer.ini.

.PARAMETER HttpsPort
    The HTTPS port of the panel on a first installation. Default 8443.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InstallDirectory,
    [Parameter(Mandatory = $true)][string]$IniFile,
    [ValidateRange(1, 65535)][int]$HttpsPort = 8443
)
$ErrorActionPreference = 'Stop'
# Started by setup with -NoProfile, PowerShell has not loaded the Cert: drive's module yet.
Import-Module Microsoft.PowerShell.Security
Import-Module PKI
$serviceName = 'ArzoieMailAdmin'
$account = "NT SERVICE\$serviceName"
$webAdmin = Join-Path $InstallDirectory 'WebAdmin'
$executable = Join-Path $webAdmin 'ArzoieMail.Api.exe'
$settingsFile = Join-Path $webAdmin 'appsettings.Production.json'
$logFile = Join-Path $webAdmin 'Install-WebAdmin.log'
function Write-Log([string]$message) { Add-Content -LiteralPath $logFile -Value ("{0:yyyy-MM-dd HH:mm:ss} {1}" -f (Get-Date), $message); Write-Output $message }

if (-not (Test-Path -LiteralPath $executable)) { throw "The panel is not installed: $executable is missing." }
# Setup runs this silently; whatever stops it is recorded where the administrator can find it.
trap { Write-Log "Setup of the panel failed: $($_.Exception.Message) (line $($_.InvocationInfo.ScriptLineNumber))"; break }

if (-not ('ArzoieWebAdminIni' -as [type])) {
    Add-Type 'using System.Runtime.InteropServices; public static class ArzoieWebAdminIni { [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] public static extern bool WritePrivateProfileString(string section, string key, string value, string file); }'
}

# The service, as its virtual account. sc.exe is used for what New-Service cannot set.
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service -and $service.Status -ne 'Stopped') {
    Stop-Service -Name $serviceName -Force
    (Get-Service -Name $serviceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}
if (-not $service) {
    New-Service -Name $serviceName -BinaryPathName "`"$executable`"" -DisplayName 'ArzoieMail Administration' -StartupType Automatic `
        -Description 'The ArzoieMail web administration panel and its certificate renewal.' | Out-Null
    Write-Log "Created the $serviceName service."
} else {
    & sc.exe config $serviceName binPath= "`"$executable`"" | Out-Null
}
& sc.exe config $serviceName obj= $account | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Could not set the $serviceName service account." }
& sc.exe sidtype $serviceName unrestricted | Out-Null
& sc.exe failure $serviceName reset= 86400 actions= restart/60000/restart/60000/none/0 | Out-Null
$sid = ([Security.Principal.NTAccount]$account).Translate([Security.Principal.SecurityIdentifier]).Value
Write-Log "The panel runs as $account ($sid)."

# SourceExists throws when it cannot search the Security log, so register and accept
# "already registered" instead.
try { New-EventLog -LogName Application -Source 'ArzoieMail Administration' -ErrorAction Stop }
catch { if ($_.Exception.Message -notmatch 'already registered') { Write-Log "Could not register the event log source: $($_.Exception.Message)" } }

# The service only reads its folder; it writes nothing there.
& icacls.exe $webAdmin /grant "*$($sid):(OI)(CI)RX" | Out-Null

if (-not (Test-Path -LiteralPath $settingsFile)) {
    $hostName = try { [Net.Dns]::GetHostEntry('').HostName } catch { $env:COMPUTERNAME }
    $token = [Guid]::NewGuid().ToString('N').Substring(0, 12)
    $subject = "ArzoieMail Administration $token"
    $certificate = New-SelfSignedCertificate -Subject "CN=$subject" -DnsName @($hostName, 'localhost') -CertStoreLocation 'Cert:\LocalMachine\My' `
        -KeyAlgorithm RSA -KeyLength 2048 -KeyExportPolicy NonExportable -Provider 'Microsoft Software Key Storage Provider' `
        -NotAfter (Get-Date).AddYears(2) -FriendlyName 'ArzoieMail Administration'
    $key = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($certificate)
    $keyFile = Join-Path $env:ProgramData ('Microsoft\Crypto\Keys\' + $key.Key.UniqueName)
    & icacls.exe $keyFile /grant "*$($sid):R" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not let the panel read its certificate key.' }
    Write-Log "Created the panel's HTTPS certificate for $hostName ($($certificate.Thumbprint))."

    # ACME HTTP-01 needs port 80. Leave it off if something else listens there.
    $acmePort = 80
    try { $probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Any, 80); $probe.Start(); $probe.Stop() }
    catch { $acmePort = 0; Write-Log 'Port 80 is in use; ACME HTTP-01 is off. Set Acme:HttpPort in appsettings.Production.json to turn it on.' }

    $settings = [ordered]@{
        AllowedHosts = "$hostName;localhost"
        Kestrel = @{ Endpoints = @{ Https = @{ Url = "https://*:$HttpsPort"; Certificate = @{ Subject = $subject; Store = 'My'; Location = 'LocalMachine'; AllowInvalid = $true } } } }
        Acme = @{ HttpPort = $acmePort }
    }
    $settings | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $settingsFile -Encoding UTF8
    Write-Log "Wrote $($settingsFile): https://$($hostName):$HttpsPort/."
} else {
    Write-Log "Kept the existing $settingsFile."
}

# The mail service reads the panel's identity when it starts.
if (-not [ArzoieWebAdminIni]::WritePrivateProfileString('Management', 'ApiClientSID', $sid, $IniFile)) { throw "Could not update $IniFile." }
$mail = Get-Service -Name 'hMailServer' -ErrorAction SilentlyContinue
if ($mail -and $mail.Status -eq 'Running') {
    Restart-Service -Name 'hMailServer' -Force
    (Get-Service -Name 'hMailServer').WaitForStatus('Running', [TimeSpan]::FromSeconds(60))
    Write-Log 'Restarted the mail service so it accepts the panel.'
}

Start-Service -Name $serviceName
(Get-Service -Name $serviceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(60))
Write-Log "The $serviceName service is running."
