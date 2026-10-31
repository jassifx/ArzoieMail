param(
    [Parameter(Mandatory=$true)][pscredential]$AdministratorCredential,
    [string]$TestExecutable = (Join-Path $PSScriptRoot '..\hmailserver\source\Management\Tests\bin\Release\net10.0\ArzoieMail.Management.Tests.dll'),
    [string]$PublishedApi,
    [string]$Python = 'python',
    [string]$EvidenceDirectory
)
$ErrorActionPreference = 'Stop'
if ((Get-CimInstance Win32_ComputerSystem).Model -ne 'Virtual Machine') {
    throw 'Live management tests run only inside a disposable VM or CI runner.'
}
$repo = Split-Path $PSScriptRoot -Parent
$originalContentRoot = $env:ASPNETCORE_CONTENTROOT
if (-not $EvidenceDirectory) { $EvidenceDirectory = Join-Path $repo 'out\management-evidence' }
New-Item -ItemType Directory -Force $EvidenceDirectory | Out-Null
function Invoke-ManagementMode([string]$Mode) {
    $log = Join-Path $EvidenceDirectory ($Mode.TrimStart('-') + '.log')
    $errors = Join-Path $EvidenceDirectory ($Mode.TrimStart('-') + '-error.log')
    $program = if ([IO.Path]::GetExtension($TestExecutable) -eq '.dll') { 'dotnet' } else { $TestExecutable }
    $modeArguments = if ($program -eq 'dotnet') { @("`"$TestExecutable`"", $Mode) } else { @($Mode) }
    # PowerShell 5 can throw on the first stderr line and hide the rest of a .NET
    # exception. Retain the complete process output before propagating failure.
    $runner = Start-Process -FilePath $program -ArgumentList $modeArguments -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput $log -RedirectStandardError $errors
    Get-Content -LiteralPath $log | Write-Output
    if ($runner.ExitCode -ne 0) {
        Get-Content -LiteralPath $errors | Write-Output
        throw "Management regression mode $Mode failed. Full evidence is retained in $EvidenceDirectory."
    }
}
$serviceKey = 'HKLM:\SYSTEM\CurrentControlSet\Services\hMailServer'
$service = Get-CimInstance Win32_Service -Filter "Name='hMailServer'"
if ($service.PathName -notmatch '^"([^"]+)"') { throw 'Expected a quoted installed service executable.' }
$serviceExe = $matches[1]
$iniPath = @('C:\ProgramData\ArzoieMail\hMailServer.ini','C:\ProgramData\hMailServer\hMailServer.ini', (Join-Path (Split-Path $serviceExe) 'hMailServer.ini')) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $iniPath) { throw 'Cannot find the installed configuration.' }
$originalIni = [IO.File]::ReadAllBytes($iniPath)
$originalEnvironment = (Get-ItemProperty -LiteralPath $serviceKey -Name Environment -ErrorAction SilentlyContinue).Environment
$adminPassword = $AdministratorCredential.GetNetworkCredential().Password
$app = New-Object -ComObject 'hMailServer.Application'
if ($null -eq $app.Authenticate($AdministratorCredential.UserName, $adminPassword)) { throw 'Cannot authenticate integration-test setup.' }
if ($app.Database.CurrentVersion -ne $app.Database.RequiredVersion) { throw 'Install a matching server/database before running native tests.' }
$fixtureDomainId = $null
$fixtureCertificateId = $null
$pfxFixtureDirectory = $null
$pfxFixturePath = $null
$objects = @()
function Stop-TestService {
    if ((Get-Service hMailServer).Status -ne 'Stopped') {
        Stop-Service hMailServer
        (Get-Service hMailServer).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }
    # COM fixture references can outlive service shutdown. Restrict cleanup to this VM's exact installed image.
    Get-CimInstance Win32_Process -Filter "Name='hMailServer.exe'" | Where-Object { $_.ExecutablePath -eq $serviceExe } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
}
try {
    $domain = $app.Domains.Add()
    $objects += $domain
    $domain.Name = 'native-api-' + [Guid]::NewGuid().ToString('N') + '.example'
    $domain.Active = $true
    $domain.Save()
    $fixtureDomainId = $domain.ID
    # Registration metadata only: deliberately nonexistent paths must never be
    # read, returned, logged by the inventory endpoint, or assigned to a listener.
    $certificate = $app.Settings.SSLCertificates.Add(); $objects += $certificate
    $certificate.Name = 'Inventory fixture <img src=x onerror=window.transportInventoryExecuted=1> ' + [Guid]::NewGuid().ToString('N')
    $certificate.CertificateFile = 'C:\ArzoieInventoryFixture\not-used-certificate.pem'
    $certificate.PrivateKeyFile = 'C:\ArzoieInventoryFixture\not-used-private-key.pem'
    $certificate.Save()
    $fixtureCertificateId = $certificate.ID
    if ($fixtureCertificateId -le 0) { throw 'Certificate inventory fixture was not persisted.' }
    $env:ARZOIEMAIL_TEST_CERTIFICATE_ID = [string]$fixtureCertificateId
    $env:ARZOIEMAIL_TEST_CERTIFICATE_NAME = $certificate.Name
    $password = [Convert]::ToBase64String([Guid]::NewGuid().ToByteArray()) + '!aA1'
    if ($env:GITHUB_ACTIONS -eq 'true') { Write-Output "::add-mask::$password" }
    foreach ($role in @(@('server',2),@('domain',1),@('user',0))) {
        $account = $domain.Accounts.Add()
        $objects += $account
        $account.Address = "$($role[0])@$($domain.Name)"
        $account.Password = $password
        $account.Active = $true
        $account.AdminLevel = [int]$role[1]
        $account.Save()
        switch ($role[0]) {
            'server' {
                $env:ARZOIEMAIL_TEST_USERNAME = $account.Address
                # Fixture only: prove queue operations cannot reach stored mailbox data.
                $folder = $account.IMAPFolders.ItemByName('INBOX'); $objects += $folder
                $stored = $folder.Messages.Add(); $objects += $stored
                $stored.Subject = 'Stored queue-boundary fixture'; $stored.Body = 'Preserve this mailbox message.'; $stored.Save()
                $env:ARZOIEMAIL_TEST_STORED_ID = [string]$stored.ID
            }
            'domain' { $env:ARZOIEMAIL_TEST_DOMAIN_USERNAME = $account.Address }
            'user' { $env:ARZOIEMAIL_TEST_USER_USERNAME = $account.Address }
        }
    }
    $env:ARZOIEMAIL_TEST_PASSWORD = $password
    foreach ($item in @($objects) + @($app)) { if ($null -ne $item) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($item) } }
    $objects = @(); $app = $null
    Stop-TestService
    if (-not ('ArzoieTestIni' -as [type])) {
        Add-Type 'using System.Runtime.InteropServices; public static class ArzoieTestIni { [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] public static extern bool WritePrivateProfileString(string section, string key, string value, string file); }'
    }
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    if (-not [ArzoieTestIni]::WritePrivateProfileString('Management', 'ApiClientSID', $sid, $iniPath)) { throw 'Cannot opt in the native test identity.' }
    $testEnvironment = @($originalEnvironment | Where-Object { $_ -and $_ -notlike 'ARZOIEMAIL_MANAGEMENT_CLIENT_SID=*' }) + "ARZOIEMAIL_MANAGEMENT_CLIENT_SID=$sid"
    New-ItemProperty -LiteralPath $serviceKey -Name Environment -Value $testEnvironment -PropertyType MultiString -Force | Out-Null
    Start-Service hMailServer
    (Get-Service hMailServer).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
    $ready = $false
    for ($attempt=0; $attempt -lt 60; ++$attempt) {
        if ([IO.Directory]::GetFiles('\\.\pipe\') -contains '\\.\pipe\ArzoieMail.Management.v1') { $ready = $true; break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw 'The native management endpoint did not become ready.' }
    $env:ASPNETCORE_CONTENTROOT = if ($PublishedApi) { $PublishedApi } else { Join-Path $repo 'hmailserver\source\Management\Api' }
    Invoke-ManagementMode '--live'
    Invoke-ManagementMode '--live-http'
    Invoke-ManagementMode '--tls-native'
    # ACME end to end, when the workflow has fetched Pebble (Let's Encrypt's test server).
    if ($env:ARZOIEMAIL_TEST_PEBBLE) { Invoke-ManagementMode '--acme-live' }
    if ($PublishedApi) {
        $pfxFixtureDirectory = Join-Path ([IO.Path]::GetTempPath()) ('ArzoieMail-certificate-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $pfxFixtureDirectory | Out-Null
        & icacls.exe $pfxFixtureDirectory /inheritance:r /grant:r "*$($sid):(OI)(CI)F" '*S-1-5-18:(OI)(CI)F' | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not secure the temporary browser PFX directory.' }
        $pfxFixturePath = Join-Path $pfxFixtureDirectory 'inspection-fixture.pfx'
        $env:ARZOIEMAIL_TEST_PFX_PATH = $pfxFixturePath
        $env:ARZOIEMAIL_TEST_PFX_PASSWORD = $password
        Invoke-ManagementMode '--prepare-certificate-browser'
        & "$PSScriptRoot\test-management-browser.ps1" -PublishedApi $PublishedApi -Python $Python
    }
} finally {
    $env:ARZOIEMAIL_TEST_PFX_PATH = $null; $env:ARZOIEMAIL_TEST_PFX_PASSWORD = $null
    if ($null -ne $pfxFixturePath -and (Test-Path -LiteralPath $pfxFixturePath)) { Remove-Item -LiteralPath $pfxFixturePath -Force }
    if ($null -ne $pfxFixtureDirectory -and (Test-Path -LiteralPath $pfxFixtureDirectory)) { [IO.Directory]::Delete($pfxFixtureDirectory, $false) }
    $env:ASPNETCORE_CONTENTROOT = $originalContentRoot
    foreach ($item in @($objects) + @($app)) { if ($null -ne $item -and [Runtime.InteropServices.Marshal]::IsComObject($item)) { try { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($item) } catch { } } }
    Stop-TestService
    [IO.File]::WriteAllBytes($iniPath, $originalIni)
    if ($null -eq $originalEnvironment) { Remove-ItemProperty -LiteralPath $serviceKey -Name Environment -ErrorAction SilentlyContinue }
    else { New-ItemProperty -LiteralPath $serviceKey -Name Environment -Value $originalEnvironment -PropertyType MultiString -Force | Out-Null }
    Start-Service hMailServer
    (Get-Service hMailServer).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
    $cleanup = New-Object -ComObject 'hMailServer.Application'
    if ($null -eq $cleanup.Authenticate($AdministratorCredential.UserName, $adminPassword)) { throw 'Cannot authenticate integration fixture cleanup.' }
    if ($null -ne $fixtureDomainId) { $cleanup.Domains.DeleteByDBID($fixtureDomainId) }
    if ($null -ne $fixtureCertificateId) { $cleanup.Settings.SSLCertificates.DeleteByDBID($fixtureCertificateId) }
    $env:ARZOIEMAIL_TEST_USERNAME = $null; $env:ARZOIEMAIL_TEST_PASSWORD = $null
    $env:ARZOIEMAIL_TEST_DOMAIN_USERNAME = $null; $env:ARZOIEMAIL_TEST_USER_USERNAME = $null
    $env:ARZOIEMAIL_TEST_STORED_ID = $null
    $env:ARZOIEMAIL_TEST_CERTIFICATE_ID = $null; $env:ARZOIEMAIL_TEST_CERTIFICATE_NAME = $null
}
