param(
    [switch]$DisposableEnvironment,
    [string]$WorkDirectory = (Join-Path $env:TEMP 'ArzoieMSSQLTest')
)
$ErrorActionPreference = 'Stop'
if (-not $DisposableEnvironment -or (Get-CimInstance Win32_ComputerSystem).Model -ne 'Virtual Machine') {
    throw 'SQL Server provisioning is only for an explicitly selected disposable VM/CI runner.'
}
New-Item -ItemType Directory -Path $WorkDirectory -Force | Out-Null
function Get-MicrosoftInstaller([string]$Url, [string]$Path, [string]$Sha256 = '') {
    if (-not (Test-Path -LiteralPath $Path)) {
        if (-not $Url) { throw "Expected downloaded SQL media: $Path" }
        Invoke-WebRequest -Uri $Url -OutFile $Path -UseBasicParsing
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike '*O=Microsoft Corporation*') {
        throw "Installer does not have a valid Microsoft signature: $Path"
    }
    if ($Sha256 -and (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Sha256) {
        throw "Installer checksum mismatch: $Path"
    }
}
function Install-Checked([string]$Path, [string[]]$Arguments) {
    $process = Start-Process -FilePath $Path -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -notin @(0,3010,1638)) { throw "Installer failed with exit code $($process.ExitCode): $Path" }
}
if (-not [type]::GetTypeFromProgID('MSOLEDBSQL19')) {
    foreach ($arch in @('x86','x64')) {
        $runtime = Join-Path $WorkDirectory "vc_redist.$arch.exe"
        Get-MicrosoftInstaller "https://aka.ms/vs/17/release/vc_redist.$arch.exe" $runtime
        Install-Checked $runtime @('/install','/quiet','/norestart')
    }
    $driver = Join-Path $WorkDirectory 'msoledbsql.msi'
    Get-MicrosoftInstaller 'https://download.microsoft.com/download/bbb0d870-7a04-44d9-af17-af28575a0ed8/amd64/1033/msoledbsql.msi' $driver 'C1D5BD79C316588FB08C852ACF76628AB662426E763B68AEF4CFA25FA1DD7BEB'
    Install-Checked 'msiexec.exe' @('/i',"`"$driver`"",'/qn','/norestart','IACCEPTMSOLEDBSQLLICENSETERMS=YES','/log',"`"$WorkDirectory\driver-install.log`"")
}
if (-not [type]::GetTypeFromProgID('MSOLEDBSQL19')) { throw 'The x64 OLE DB 19 provider is unavailable after installation.' }
$serviceName = 'MSSQL$ARZOIETEST'
if (-not (Get-Service -Name $serviceName -ErrorAction SilentlyContinue)) {
    $bootstrap = Join-Path $WorkDirectory 'SQL-Express.exe'
    Get-MicrosoftInstaller 'https://aka.ms/sql2025express' $bootstrap
    $media = Join-Path $WorkDirectory 'media'
    $package = Join-Path $media 'SQLEXPR_x64_ENU.exe'
    if (-not (Test-Path -LiteralPath $package)) {
        Install-Checked $bootstrap @('/ACTION=Download','/MEDIATYPE=Core',"/MEDIAPATH=`"$media`"",'/QUIET')
    }
    Get-MicrosoftInstaller '' $package
    $setup = Join-Path $WorkDirectory 'setup'
    if (-not (Test-Path "$setup\setup.exe")) { Install-Checked $package @('/q',"/x:`"$setup`"") }
    Install-Checked "$setup\setup.exe" @('/Q','/ACTION=Install','/FEATURES=SQLEngine','/INSTANCENAME=ARZOIETEST',
        '/IACCEPTSQLSERVERLICENSETERMS',"/SQLSYSADMINACCOUNTS=`"$env:USERDOMAIN\$env:USERNAME`"",'/SQLSVCSTARTUPTYPE=Manual',
        '/TCPENABLED=1','/NPENABLED=0','/UPDATEENABLED=False')
}
Start-Service -Name $serviceName
$instance = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL').ARZOIETEST
if (-not $instance) { throw 'Cannot locate the disposable SQL Server instance.' }
$instanceKey = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$instance"
$network = "$instanceKey\MSSQLServer\SuperSocketNetLib"
$cert = New-SelfSignedCertificate -DnsName @($env:COMPUTERNAME,'localhost') -CertStoreLocation 'Cert:\LocalMachine\My' `
    -KeySpec KeyExchange -Provider 'Microsoft RSA SChannel Cryptographic Provider' -Type SSLServerAuthentication `
    -KeyAlgorithm RSA -KeyLength 2048 -KeyExportPolicy NonExportable
$certFile = Join-Path $WorkDirectory 'sql-test.cer'
Export-Certificate -Cert $cert -FilePath $certFile | Out-Null
# Trust is added later, after proving the native adapter rejects an untrusted certificate.
$rsa = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($cert)
if ($rsa -is [Security.Cryptography.RSACryptoServiceProvider]) {
    $keyName = $rsa.CspKeyContainerInfo.UniqueKeyContainerName
} elseif ($rsa -is [Security.Cryptography.RSACng] -and $rsa.Key.Provider.Provider -eq 'Microsoft RSA SChannel Cryptographic Provider') {
    # PowerShell/.NET versions can wrap a legacy CSP key in RSACng.
    $keyName = $rsa.Key.UniqueName
} else { throw 'Unexpected private-key provider for the SQL test certificate.' }
if (-not $keyName) { throw 'The SQL test certificate must use a machine CSP key.' }
$keyFile = Join-Path "$env:ProgramData\Microsoft\Crypto\RSA\MachineKeys" $keyName
if (-not (Test-Path -LiteralPath $keyFile -PathType Leaf)) { throw 'Cannot locate the SQL certificate private-key file.' }
$rsa.Dispose()
$sqlServiceIdentity = (Get-CimInstance Win32_Service | Where-Object Name -eq $serviceName).StartName
& icacls.exe $keyFile /grant "${sqlServiceIdentity}:(R)" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Cannot grant the SQL service access to its test certificate.' }
Set-ItemProperty $network -Name Certificate -Value $cert.Thumbprint.ToLowerInvariant()
Set-ItemProperty $network -Name ForceEncryption -Value 1
Set-ItemProperty "$network\Tcp" -Name Enabled -Value 1
Set-ItemProperty "$network\Tcp\IPAll" -Name TcpDynamicPorts -Value ''
Set-ItemProperty "$network\Tcp\IPAll" -Name TcpPort -Value '14330'
Set-ItemProperty "$instanceKey\MSSQLServer" -Name LoginMode -Value 2
Restart-Service -Name $serviceName
$server = 'tcp:localhost,14330'
$admin = New-Object -ComObject ADODB.Connection
$deadline = (Get-Date).AddSeconds(90)
do {
    try { $admin.Open("Provider=MSOLEDBSQL19;Server=$server;Integrated Security=SSPI;Use Encryption for Data=Mandatory;Trust Server Certificate=True;"); break }
    catch { if ((Get-Date) -ge $deadline) { throw }; Start-Sleep -Seconds 2 }
} while ($true)
try {
    $version = $admin.Execute("SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS int) AS major, CAST(SERVERPROPERTY('Edition') AS nvarchar(128)) AS edition")
    if ([int]$version.Fields.Item('major').Value -ne 17 -or [string]$version.Fields.Item('edition').Value -notlike '*Express*') {
        throw 'This fixture requires SQL Server 2025 Express.'
    }
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($version)
    $mailIdentity = (Get-CimInstance Win32_Service -Filter "Name='hMailServer'").StartName
    if ($mailIdentity -notlike 'NT SERVICE\*') { throw 'The mail service must use its isolated virtual service identity.' }
    $login = $mailIdentity.Replace(']',']]')
    $literal = $mailIdentity.Replace("'","''")
    [void]$admin.Execute("IF SUSER_ID(N'$literal') IS NULL CREATE LOGIN [$login] FROM WINDOWS; ALTER SERVER ROLE dbcreator ADD MEMBER [$login];")
} finally { $admin.Close(); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($admin) }
[pscustomobject]@{ Server=$server; CertificateFile=$certFile; CertificateThumbprint=$cert.Thumbprint; Service=$serviceName } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $WorkDirectory 'fixture.json') -Encoding UTF8
'Provisioned SQL Server 2025 Express with OLE DB 19 and a VM-only TLS certificate.'
