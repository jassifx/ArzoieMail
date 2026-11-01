<#
.SYNOPSIS
    Checks the web administration panel as setup installed it. Disposable VM or CI only.

.DESCRIPTION
    The ArzoieMailAdmin service runs as its own virtual account; the mail service's management
    pipe accepts that account; the panel's HTTPS certificate key is readable by it; the panel
    answers over HTTPS only, and its ACME port serves nothing but challenges. A real sign-in
    through the installed panel proves the whole path to the mail service. Then the panel is
    removed and set up again with the scripts setup uses, as an upgrade or repair would.
#>
param(
    [Parameter(Mandatory = $true)][pscredential]$AdministratorCredential,
    [string]$InstallDirectory = (Join-Path $env:ProgramFiles 'ArzoieMail')
)
$ErrorActionPreference = 'Stop'
if ((Get-CimInstance Win32_ComputerSystem).Model -ne 'Virtual Machine') { throw 'This test runs only inside a disposable VM or CI runner.' }
$checks = 0
function Check([bool]$condition, [string]$message) { $script:checks++; if (-not $condition) { throw "FAILED: $message" } }

$setup = Join-Path $InstallDirectory 'WebAdmin\Setup'
$settingsFile = Join-Path $InstallDirectory 'WebAdmin\appsettings.Production.json'
$iniFile = @('C:\ProgramData\ArzoieMail\hMailServer.ini', 'C:\ProgramData\hMailServer\hMailServer.ini') | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not ('ArzoieInstalledIni' -as [type])) {
    Add-Type 'using System.Runtime.InteropServices; using System.Text; public static class ArzoieInstalledIni { [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] public static extern int GetPrivateProfileString(string section, string key, string fallback, StringBuilder value, int size, string file); }'
}
function Get-ClientSid { $value = [Text.StringBuilder]::new(256); [void][ArzoieInstalledIni]::GetPrivateProfileString('Management', 'ApiClientSID', '', $value, 256, $iniFile); $value.ToString() }

# Why the panel is not running: what setup logged, what Windows logged, and the panel's own
# output when started by hand in its folder.
function Show-PanelDiagnostics {
    $log = Join-Path $InstallDirectory 'WebAdmin\Install-WebAdmin.log'
    if (Test-Path -LiteralPath $log) { "--- $log"; Get-Content -LiteralPath $log }
    "--- Application and System events"
    Get-WinEvent -FilterHashtable @{ LogName = 'Application', 'System'; StartTime = (Get-Date).AddHours(-3) } -MaxEvents 400 -ErrorAction SilentlyContinue |
        Where-Object { $_.ProviderName -in 'ArzoieMail Administration', '.NET Runtime', 'Application Error', 'Service Control Manager' -and ($_.ProviderName -ne 'Service Control Manager' -or $_.Message -match 'ArzoieMail') } |
        Select-Object -First 15 | ForEach-Object { "[$($_.TimeCreated)] $($_.ProviderName): $($_.Message)" }
    "--- Started in the foreground"
    $exe = Join-Path $InstallDirectory 'WebAdmin\ArzoieMail.Api.exe'
    $out = Join-Path $env:TEMP 'arzoie-panel-out.txt'; $err = Join-Path $env:TEMP 'arzoie-panel-err.txt'
    $process = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru -RedirectStandardOutput $out -RedirectStandardError $err -WindowStyle Hidden
    if (-not $process.WaitForExit(15000)) { $process.Kill() }
    Get-Content $out, $err -ErrorAction SilentlyContinue | Select-Object -First 60
}

function Test-InstalledPanel {
    $service = Get-CimInstance Win32_Service -Filter "Name='ArzoieMailAdmin'"
    Check ($null -ne $service) 'Setup created the ArzoieMailAdmin service.'
    Check ($service.StartName -eq 'NT SERVICE\ArzoieMailAdmin') "The panel runs as its virtual account, not $($service.StartName)."
    if ($service.State -ne 'Running') { Show-PanelDiagnostics }
    Check ($service.State -eq 'Running') "The panel service is $($service.State)."
    $sid = ([Security.Principal.NTAccount]'NT SERVICE\ArzoieMailAdmin').Translate([Security.Principal.SecurityIdentifier]).Value
    Check ((Get-ClientSid) -eq $sid) 'The mail service accepts the panel identity on its management pipe.'

    $settings = Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json
    $url = $settings.Kestrel.Endpoints.Https.Url
    $port = [int]($url -replace '^.*:(\d+)$', '$1')
    $subject = $settings.Kestrel.Endpoints.Https.Certificate.Subject
    $certificate = Get-ChildItem 'Cert:\LocalMachine\My' | Where-Object { $_.Subject -eq "CN=$subject" } | Select-Object -First 1
    Check ($null -ne $certificate) 'The panel has its own HTTPS certificate in the machine store.'
    $key = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($certificate)
    $keyFile = Join-Path $env:ProgramData ('Microsoft\Crypto\Keys\' + $key.Key.UniqueName)
    $rules = (Get-Acl -LiteralPath $keyFile).Access | Where-Object { $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value -eq $sid }
    Check ($null -ne $rules) 'Only the panel account is added to the certificate key.'

    # The certificate the panel serves, from a synchronous handshake so the callback runs here.
    $deadline = (Get-Date).AddSeconds(60)
    while ($true) {
        try {
            $tcp = [Net.Sockets.TcpClient]::new('localhost', $port)
            $tls = [Net.Security.SslStream]::new($tcp.GetStream(), $false, [Net.Security.RemoteCertificateValidationCallback]{ $true })
            $tls.AuthenticateAsClient('localhost')
            $served = [Security.Cryptography.X509Certificates.X509Certificate2]::new($tls.RemoteCertificate)
            $tls.Dispose(); $tcp.Dispose()
            break
        } catch { if ((Get-Date) -gt $deadline) { throw } Start-Sleep -Seconds 2 }
    }
    Check ($served.Thumbprint -eq $certificate.Thumbprint) 'The panel serves its own certificate over HTTPS.'

    # This runs in PowerShell 7, as the workflow does; the certificate was checked above.
    $base = "https://localhost:$port"
    $page = Invoke-WebRequest -Uri "$base/" -SkipCertificateCheck -SessionVariable web
    Check ($page.Content.Contains('assets/panel.js')) 'The installed panel serves its page.'
    $session = (Invoke-WebRequest -Uri "$base/api/v1/session" -SkipCertificateCheck -WebSession $web).Content | ConvertFrom-Json
    $body = @{ username = $AdministratorCredential.UserName; password = $AdministratorCredential.GetNetworkCredential().Password } | ConvertTo-Json
    $login = (Invoke-WebRequest -Uri "$base/api/v1/session" -Method Post -Body $body -ContentType 'application/json' -Headers @{ 'X-CSRF-Token' = $session.data.csrf } `
        -SkipCertificateCheck -WebSession $web).Content | ConvertFrom-Json
    Check ($login.data.user.level -eq 2) 'A server administrator signs in through the installed panel.'
    $status = (Invoke-WebRequest -Uri "$base/api/v1/status" -SkipCertificateCheck -WebSession $web).Content | ConvertFrom-Json
    Check ($status.data.version.StartsWith('6.')) 'The installed panel reaches the mail service through its management pipe.'

    $plain = [Net.Http.HttpClient]::new()
    $plainServed = $false
    try { $plainServed = $plain.GetAsync("http://localhost:$port/").GetAwaiter().GetResult().IsSuccessStatusCode } catch { }
    Check (-not $plainServed) 'The panel port does not serve plain HTTP.'
    if ($settings.Acme.HttpPort -gt 0) {
        $acme = $plain.GetAsync("http://localhost:$($settings.Acme.HttpPort)/api/v1/session").GetAwaiter().GetResult()
        Check ($acme.StatusCode -eq [Net.HttpStatusCode]::NotFound) 'The ACME challenge port serves no panel.'
    }
    $plain.Dispose()
}

try { Test-InstalledPanel } catch { Show-PanelDiagnostics; throw }

# Remove and set up again, as an upgrade or repair runs the same scripts.
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $setup 'Uninstall-WebAdmin.ps1') -InstallDirectory $InstallDirectory -IniFile $iniFile
if ($LASTEXITCODE -ne 0) { throw 'Uninstall-WebAdmin.ps1 failed.' }
Check ($null -eq (Get-Service -Name 'ArzoieMailAdmin' -ErrorAction SilentlyContinue)) 'Removing the panel deletes its service.'
Check ((Get-ClientSid) -eq '') 'Removing the panel withdraws its management pipe access.'
Check (-not (Test-Path -LiteralPath $settingsFile)) 'Removing the panel removes its settings and own certificate.'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $setup 'Install-WebAdmin.ps1') -InstallDirectory $InstallDirectory -IniFile $iniFile
if ($LASTEXITCODE -ne 0) { Show-PanelDiagnostics; throw 'Install-WebAdmin.ps1 failed.' }
try { Test-InstalledPanel } catch { Show-PanelDiagnostics; throw }

"Passed $checks installed web administration checks."
