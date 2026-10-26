param(
    [Parameter(Mandatory=$true)][pscredential]$AdministratorCredential,
    [Parameter(Mandatory=$true)][string]$FixtureDirectory,
    [switch]$PolicyProbeOnly,
    [string]$Installer = ''
)
$ErrorActionPreference = 'Stop'
if ((Get-CimInstance Win32_ComputerSystem).Model -ne 'Virtual Machine') { throw 'SQL Server tests run only in the disposable VM/CI.' }
$fixture = Get-Content (Join-Path $FixtureDirectory 'fixture.json') -Raw | ConvertFrom-Json
$service = Get-CimInstance Win32_Service -Filter "Name='hMailServer'"
if ($service.PathName -notmatch '^"([^"]+)"') { throw 'Expected an installed mail service.' }
$serviceExe = $matches[1]
$iniPath = @('C:\ProgramData\ArzoieMail\hMailServer.ini','C:\ProgramData\hMailServer\hMailServer.ini',
    (Join-Path (Split-Path $matches[1]) 'hMailServer.ini')) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $iniPath) { throw 'Cannot locate the installed configuration.' }
Add-Type -TypeDefinition 'using System.Runtime.InteropServices; public static class SqlTestIni { [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] public static extern bool WritePrivateProfileString(string section,string key,string value,string path); }'
$originalIni = [IO.File]::ReadAllBytes($iniPath)
$application = New-Object -ComObject 'hMailServer.Application'
if ($null -eq $application.Authenticate($AdministratorCredential.UserName,$AdministratorCredential.GetNetworkCredential().Password)) { throw 'Cannot authenticate mail-server fixture setup.' }
function Set-SqlSetting([string]$Key,[string]$Value) {
    if (-not [SqlTestIni]::WritePrivateProfileString('Database',$Key,$Value,$iniPath)) { throw "Could not write test setting $Key" }
}
function Reload-TestSettings {
    $errorText = $application.Reinitialize()
    if ($errorText) { throw "Cannot initialize mail service: $errorText" }
}
function Assert-ConnectionRefused([string]$Expected,[string]$Label) {
    $refused = $false
    try { $application.Database.SetDefaultDatabase(2,$fixture.Server,0,'master','','') }
    catch { if ($_.Exception.Message -notmatch $Expected) { throw }; $refused=$true }
    if (-not $refused) { throw "SQL Server regression: $Label was accepted." }
    "Passed: $Label"
}
$completed = $false
try {
    $testRoot = "Cert:\LocalMachine\Root\$($fixture.CertificateThumbprint)"
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Force }
    Set-SqlSetting 'Provider' ''
    Set-SqlSetting 'MSSQLEncryption' 'Mandatory'
    Set-SqlSetting 'MSSQLTrustServerCertificate' '0'
    Set-SqlSetting 'MSSQLHostNameInCertificate' ''
    Reload-TestSettings
    Assert-ConnectionRefused 'certificate chain|not trusted|SSL Provider' 'untrusted SQL certificate'
    if ($PolicyProbeOnly) { $completed=$true; return }

    Import-Certificate -FilePath $fixture.CertificateFile -CertStoreLocation 'Cert:\LocalMachine\Root' | Out-Null
    Set-SqlSetting 'MSSQLHostNameInCertificate' 'wrong-name.example.test'
    Reload-TestSettings
    Assert-ConnectionRefused 'target principal|name mismatch|SSL Provider' 'SQL certificate hostname mismatch'
    Set-SqlSetting 'MSSQLHostNameInCertificate' ''
    Set-SqlSetting 'Provider' 'SQLOLEDB'
    Reload-TestSettings
    Assert-ConnectionRefused 'legacy|MSOLEDBSQL19' 'legacy SQL provider override'
    Set-SqlSetting 'Provider' ''
    Set-SqlSetting 'MSSQLEncryption' 'Optional'
    Reload-TestSettings
    Assert-ConnectionRefused 'MSSQLEncryption' 'unencrypted SQL connection policy'
    Set-SqlSetting 'MSSQLEncryption' 'Mandatory'
    Reload-TestSettings

    $admin = New-Object -ComObject ADODB.Connection
    $admin.Open("Provider=MSOLEDBSQL19;Server=$($fixture.Server);Integrated Security=SSPI;Use Encryption for Data=Mandatory;Trust Server Certificate=False;")
    try {
        # Exercise SQL authentication with connection-string delimiter characters. No credentials
        # are printed. The login exists only in this disposable fixture.
        $sqlUser = 'arzoie;"login_' + [Guid]::NewGuid().ToString('N')
        $sqlPassword = [Guid]::NewGuid().ToString('N') + ';"!aA1'
        $login = $sqlUser.Replace(']',']]')
        $passwordLiteral = $sqlPassword.Replace("'","''")
        [void]$admin.Execute("CREATE LOGIN [$login] WITH PASSWORD=N'$passwordLiteral', CHECK_POLICY=OFF; ALTER SERVER ROLE dbcreator ADD MEMBER [$login];")
        $sqlDatabase = 'arzoie_sql_auth_' + [Guid]::NewGuid().ToString('N')
        $application.Database.CreateExternalDatabase(2,'tcp:localhost',14330,$sqlDatabase,$sqlUser,$sqlPassword)
        "Passed: SQL authentication with quoted credential delimiters"
        # Restore the active internal configuration before testing the Windows-authenticated path.
        [IO.File]::WriteAllBytes($iniPath,$originalIni)
        Set-SqlSetting 'Provider' ''
        Set-SqlSetting 'MSSQLEncryption' 'Strict'
        Set-SqlSetting 'MSSQLTrustServerCertificate' '0'
        Reload-TestSettings
        $databaseName = 'arzoie_mssql_' + [Guid]::NewGuid().ToString('N')
        $application.Database.CreateExternalDatabase(2,'tcp:localhost',14330,$databaseName,'','')
        $application.Database.SetDefaultDatabase(2,'tcp:localhost',14330,$databaseName,'','')
        Reload-TestSettings
        if ($application.Database.DatabaseType -ne 2 -or $application.Database.CurrentVersion -ne $application.Database.RequiredVersion) {
            throw 'The mail service did not initialize the current SQL Server schema.'
        }
        "Passed: SQL Express schema creation and Windows authentication with Strict TLS"
        $mailIdentity = $service.StartName.Replace(']',']]')
        [void]$admin.Execute("ALTER SERVER ROLE dbcreator DROP MEMBER [$mailIdentity];")
        # dbcreator is provisioning-only. The running mail service needs just its own database.
        Set-SqlSetting 'MSSQLEncryption' 'Mandatory'
        Reload-TestSettings
        $rows=$admin.Execute("SELECT COUNT(*) AS total, SUM(CASE WHEN c.encrypt_option='TRUE' THEN 1 ELSE 0 END) AS encrypted FROM sys.dm_exec_sessions s JOIN sys.dm_exec_connections c ON s.session_id=c.session_id WHERE s.program_name=N'ArzoieMail'")
        if ([int]$rows.Fields.Item('total').Value -lt 1 -or [int]$rows.Fields.Item('total').Value -ne [int]$rows.Fields.Item('encrypted').Value) { throw 'Native SQL connections are not encrypted.' }
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($rows)
        'Passed: all native SQL sessions encrypted after dropping provisioning privileges'
        if ($Installer) {
            $savedIni = [IO.File]::ReadAllBytes($iniPath)
            $savedHash = (Get-FileHash -LiteralPath $serviceExe -Algorithm SHA256).Hash
            $savedPid = (Get-CimInstance Win32_Service -Filter "Name='hMailServer'").ProcessId
            $preflightLog = Join-Path $FixtureDirectory 'installer-preflight.log'
            try {
                # Do not reload this deliberately invalid override into the running service.
                Set-SqlSetting 'Provider' 'SQLOLEDB'
                $setup = Start-Process -FilePath $Installer -WindowStyle Hidden -Wait -PassThru `
                    -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/LOG=`"$preflightLog`""
                $guardSeen = $false
                $guardDeadline = (Get-Date).AddSeconds(45)
                do {
                    if (Test-Path -LiteralPath $preflightLog) {
                        $guardSeen = [bool](Select-String -LiteralPath $preflightLog -SimpleMatch 'Provider override' -Quiet)
                    }
                    if (-not $guardSeen) { Start-Sleep -Milliseconds 500 }
                } while (-not $guardSeen -and (Get-Date) -lt $guardDeadline)
                if (-not $guardSeen) {
                    throw 'The installer did not block the legacy SQL provider before upgrade.'
                }
                if ((Get-FileHash -LiteralPath $serviceExe -Algorithm SHA256).Hash -ne $savedHash -or
                    (Get-CimInstance Win32_Service -Filter "Name='hMailServer'").ProcessId -ne $savedPid) {
                    throw 'The failed SQL prerequisite check changed binaries or restarted the service.'
                }
                'Passed: installer rejects legacy SQL configuration before replacing binaries or stopping the service'
            } finally { [IO.File]::WriteAllBytes($iniPath,$savedIni) }
        }
    } finally { $admin.Close(); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($admin) }
    $env:ARZOIEMAIL_TEST_SQLSERVER=$fixture.Server
    if ($env:GITHUB_ENV) { "ARZOIEMAIL_TEST_SQLSERVER=$($fixture.Server)" | Add-Content -LiteralPath $env:GITHUB_ENV }
    $completed=$true
} finally {
    if (-not $completed -or $PolicyProbeOnly) {
        [IO.File]::WriteAllBytes($iniPath,$originalIni)
        [void]$application.Reinitialize()
    }
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($application)
}
