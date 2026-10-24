<#
.SYNOPSIS
    Fetches the .NET Desktop Runtime installer that the ArzoieMail setup bundles.

.DESCRIPTION
    The administration tools (hMailAdmin, DBSetup, DBUpdater, ...) are .NET 10 Windows Forms
    applications and need the .NET Desktop Runtime. The setup program carries Microsoft's
    runtime installer and runs it when the runtime is missing, so that an installation needs
    no internet connection.

    Downloads windowsdesktop-runtime-<Version>-win-x64.exe from Microsoft into
    %hMailServerLibs%\dotnet-desktop-runtime-<Version>, and verifies it against the SHA-512
    pinned below. Like 7-Zip, it is shipped as downloaded, so a substituted file would go
    straight to every installation; an unlisted version is refused.

    Prerequisites:
      - The environment variable hMailServerLibs, pointing at your library folder.

.PARAMETER Version
    The .NET Desktop Runtime version to fetch, e.g. 10.0.13. Must be listed in $RuntimeHashes.

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File libraries\fetch-dotnet-runtime.ps1 -Version 10.0.13
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version
)

$ErrorActionPreference = "Stop"

# SHA-512 of each supported windowsdesktop-runtime-<version>-win-x64.exe, as Microsoft
# publishes it in the release metadata:
#
#     https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json
#     -> releases[].windowsdesktop.files[] where rid == win-x64 and name ends in .exe -> hash
#
# Add an entry here when pinning a new version.
$RuntimeHashes = @{
    "10.0.13" = "ADAF5E06B66B145848F3A994B51EA1590D19A5C02F6E7C5899ED896BBDB020AF6386D2EAEF613034D697388ADAF48AA8C3291343F0BBB5F8E62E37AFF9564A00"
}

# --- Set up a build log ---------------------------------------------------------

. (Join-Path -Path $PSScriptRoot -ChildPath "build-common.ps1")

$logPath = Join-Path -Path $PSScriptRoot -ChildPath "fetch-dotnet-runtime.log"
Start-BuildLog -LogPath $logPath -Title ".NET Desktop Runtime $Version fetch log"

# --- Resolve the library folder -------------------------------------------------

$libsPath = Resolve-HMailServerLibs

if (-not $RuntimeHashes.ContainsKey($Version))
{
    $known = ($RuntimeHashes.Keys | Sort-Object) -join ", "
    Throw "No pinned SHA-512 is recorded for the .NET Desktop Runtime $Version. Known versions: $known. Record it in `$RuntimeHashes in $PSCommandPath from Microsoft's release metadata."
}

$destDir = Join-Path -Path $libsPath -ChildPath "dotnet-desktop-runtime-$Version"
$fileName = "windowsdesktop-runtime-$Version-win-x64.exe"
$url = "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/$Version/$fileName"

# --- Download and verify --------------------------------------------------------

if (Test-Path $destDir)
{
    Write-Log "Removing existing folder $destDir for a clean fetch"
    Remove-Item -LiteralPath $destDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $destDir | Out-Null

$target = Join-Path -Path $destDir -ChildPath $fileName

Write-Log "Downloading $url"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -Uri $url -OutFile $target

$expectedHash = $RuntimeHashes[$Version]
$actualHash = (Get-FileHash -Path $target -Algorithm SHA512).Hash
if ($actualHash -ne $expectedHash)
{
    Remove-Item -LiteralPath $destDir -Recurse -Force
    Throw "The SHA-512 of $fileName did not match the pinned value. Expected $expectedHash, got $actualHash. The download was discarded."
}

Write-Log "SHA-512 verified"
Write-Log ".NET Desktop Runtime $Version fetched into $target"
