param(
    [Parameter(Mandatory=$true)][string]$VMName,
    [Parameter(Mandatory=$true)][string]$PasswordFile,
    [string]$UserName = 'Administrator'
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$publish = Join-Path $root 'out\certificate-key-store-probe'
$files = @('ArzoieMail.CertificateKeyStoreProbe.exe','ArzoieMail.CertificateKeyStoreProbe.dll','ArzoieMail.CertificateKeyStoreProbe.deps.json','ArzoieMail.CertificateKeyStoreProbe.runtimeconfig.json')
foreach ($name in $files) {
    if (-not (Test-Path -LiteralPath (Join-Path $publish $name) -PathType Leaf)) { throw 'Publish the storage probe to out/certificate-key-store-probe first.' }
}
if ((Get-VM -Name $VMName).State -ne 'Running') { throw 'Start the authorized disposable VM before running this probe.' }
$probePassword = (Get-Content -LiteralPath $PasswordFile -Raw).TrimEnd([char]13,[char]10)
$credential = [pscredential]::new($UserName,(ConvertTo-SecureString $probePassword -AsPlainText -Force))
$probePassword = $null
$checkpoint = $null; $session = $null
try {
    $pending = @(Get-VMCheckpoint -VMName $VMName | Where-Object Name -Like 'ArzoieMail-phase1b-*')
    if ($pending.Count) { throw 'Previous Phase 1b test checkpoint exists.' }
    $checkpoint = Checkpoint-VM -VMName $VMName -SnapshotName ('ArzoieMail-phase1b-cng-' + [Guid]::NewGuid().ToString('N')) -Passthru
    Write-Output 'Created an isolated CNG storage feasibility checkpoint.'
    $session = New-PSSession -VMName $VMName -Credential $credential
    $work = Invoke-Command -Session $session -ScriptBlock {
        $work = 'C:\ArzoieCngStorageProbe-' + [Guid]::NewGuid().ToString('N')
        New-Item -ItemType Directory -Path $work | Out-Null
        & icacls.exe $work /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Cannot protect probe directory.' }
        $work
    }
    foreach ($name in $files) {
        Copy-Item -ToSession $session -LiteralPath (Join-Path $publish $name) -Destination ($work + '\' + $name) -Force
    }
    Invoke-Command -Session $session -ArgumentList $work -ScriptBlock {
        param($work)
        $ErrorActionPreference = 'Stop'
        $probeUser = $null
        try {
            $userName = 'CngProbe' + [Guid]::NewGuid().ToString('N').Substring(0,8)
            $randomBytes = New-Object byte[] 32
            $random = [Security.Cryptography.RandomNumberGenerator]::Create()
            try { $random.GetBytes($randomBytes) } finally { $random.Dispose() }
            $password = [Convert]::ToBase64String($randomBytes) + '!aA1'
            [Array]::Clear($randomBytes,0,$randomBytes.Length)
            $probeUser = New-LocalUser -Name $userName -Password (ConvertTo-SecureString $password -AsPlainText -Force) -Description 'ArzoieMail CNG probe'
            $users = Get-LocalGroup -SID ([Security.Principal.SecurityIdentifier]::new('S-1-5-32-545'))
            Add-LocalGroupMember -Group $users.Name -Member $probeUser
            $env:ARZOIEMAIL_CNG_PROBE_USER = $userName
            $env:ARZOIEMAIL_CNG_PROBE_PASSWORD = $password
            $env:ARZOIEMAIL_CNG_PROBE_DISPOSABLE_VM = '1'
            $password = $null
            $start = [Diagnostics.ProcessStartInfo]::new()
            $start.FileName = $work + '\ArzoieMail.CertificateKeyStoreProbe.exe'
            $start.WorkingDirectory = $work
            $start.UseShellExecute = $false
            $start.CreateNoWindow = $true
            $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
            $start.RedirectStandardOutput = $true
            $start.RedirectStandardError = $true
            $probe = [Diagnostics.Process]::new()
            $probe.StartInfo = $start
            $started = $false
            try {
                $started = $probe.Start()
                if (-not $started) { throw 'Could not start CNG probe.' }
                $stdout = $probe.StandardOutput.ReadToEndAsync()
                $stderr = $probe.StandardError.ReadToEndAsync()
                if (-not $probe.WaitForExit(120000)) {
                    $probe.Kill()
                    throw 'CNG probe exceeded two minutes; restore the disposable checkpoint.'
                }
                $stdout.GetAwaiter().GetResult()
                $stderr.GetAwaiter().GetResult()
                Write-Output ('CNG storage probe exit code: ' + $probe.ExitCode)
                if ($probe.ExitCode -ne 0) { throw 'CNG storage feasibility probe failed; review the reported operation.' }
            } finally {
                if ($started -and -not $probe.HasExited) { $probe.Kill() }
                $probe.Dispose()
            }
        } finally {
            $env:ARZOIEMAIL_CNG_PROBE_USER = $null
            $env:ARZOIEMAIL_CNG_PROBE_PASSWORD = $null
            $env:ARZOIEMAIL_CNG_PROBE_DISPOSABLE_VM = $null
            $password = $null
            if ($null -ne $probeUser) { Remove-LocalUser -SID $probeUser.SID }
        }
    }
} finally {
    if ($null -ne $session) { Remove-PSSession $session }
    if ($null -ne $checkpoint) {
        Restore-VMCheckpoint -VMSnapshot $checkpoint -Confirm:$false
        if ((Get-VM -Name $VMName).State -eq 'Off') { Start-VM -Name $VMName }
        Remove-VMCheckpoint -VMSnapshot $checkpoint -Confirm:$false
        Write-Output 'Restored and removed only the temporary CNG storage checkpoint.'
    }
}
