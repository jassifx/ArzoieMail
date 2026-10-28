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
if (-not $EvidenceDirectory) { $EvidenceDirectory = Join-Path $repo 'out\management-evidence' }
New-Item -ItemType Directory -Force $EvidenceDirectory | Out-Null
function Invoke-ManagementMode([string]$Mode) {
    $log = Join-Path $EvidenceDirectory ($Mode.TrimStart('-') + '.log')
    if ([IO.Path]::GetExtension($TestExecutable) -eq '.dll') { & dotnet $TestExecutable $Mode | Tee-Object -FilePath $log }
    else { & $TestExecutable $Mode | Tee-Object -FilePath $log }
    if ($LASTEXITCODE -ne 0) { throw "Management regression mode $Mode failed." }
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
    Invoke-ManagementMode '--live'
    Invoke-ManagementMode '--live-http'
    Invoke-ManagementMode '--tls-native'
    if ($PublishedApi) { & "$PSScriptRoot\test-management-browser.ps1" -PublishedApi $PublishedApi -Python $Python }
} finally {
    foreach ($item in @($objects) + @($app)) { if ($null -ne $item -and [Runtime.InteropServices.Marshal]::IsComObject($item)) { try { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($item) } catch { } } }
    Stop-TestService
    [IO.File]::WriteAllBytes($iniPath, $originalIni)
    if ($null -eq $originalEnvironment) { Remove-ItemProperty -LiteralPath $serviceKey -Name Environment -ErrorAction SilentlyContinue }
    else { New-ItemProperty -LiteralPath $serviceKey -Name Environment -Value $originalEnvironment -PropertyType MultiString -Force | Out-Null }
    Start-Service hMailServer
    (Get-Service hMailServer).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
    $cleanup = New-Object -ComObject 'hMailServer.Application'
    if ($null -ne $cleanup.Authenticate($AdministratorCredential.UserName, $adminPassword) -and $null -ne $fixtureDomainId) { $cleanup.Domains.DeleteByDBID($fixtureDomainId) }
    $env:ARZOIEMAIL_TEST_USERNAME = $null; $env:ARZOIEMAIL_TEST_PASSWORD = $null
    $env:ARZOIEMAIL_TEST_DOMAIN_USERNAME = $null; $env:ARZOIEMAIL_TEST_USER_USERNAME = $null
    $env:ARZOIEMAIL_TEST_STORED_ID = $null
}
