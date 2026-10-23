function Find-MsBuild {
    # The range covers VS2019 through VS2026, and -latest picks the newest installed, so a
    # machine with several Visual Studios uses the same one the build server does: VS2026 on
    # windows-2025-vs2026. The C++ projects use the v145 toolset, which only VS2026 provides;
    # the C# projects build with any of them.
    param(
        [string]$VsWhereMinVersion = '[16.0,19.0)'
    )

    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"

    $msbuild = $null
    if (Test-Path $vswhere) {
        try {
            $msbuild = & $vswhere -latest -version $VsWhereMinVersion -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
        } catch {
            $msbuild = $null
        }
    } else {
        Write-Verbose "vswhere not found at $vswhere"
    }

    if (-not $msbuild) {
        $msbuildCmd = Get-Command msbuild.exe -ErrorAction SilentlyContinue
        if ($msbuildCmd) { $msbuild = $msbuildCmd.Source }
    }

    return $msbuild
}
