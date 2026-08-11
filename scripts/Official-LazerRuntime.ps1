$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:officialRuntimeFiles = [ordered]@{
    'clrjit.dll'                  = 'runtimes/win-x64/native/clrjit.dll'
    'coreclr.dll'                 = 'runtimes/win-x64/native/coreclr.dll'
    'System.Private.CoreLib.dll' = 'runtimes/win-x64/lib/{tfm}/System.Private.CoreLib.dll'
}

function Get-OfficialLazerPackage {
    param(
        [Parameter(Mandatory)] [string] $UpstreamVersion,
        [Parameter(Mandatory)] [string] $ArtifactsRoot
    )

    if ($UpstreamVersion -notmatch '^\d{4}\.\d+\.\d+$') {
        throw "Invalid upstream lazer version '$UpstreamVersion'."
    }

    $tag = "$UpstreamVersion-lazer"
    $packageName = "osulazer-$tag-full.nupkg"
    $installedPackage = Join-Path $env:LOCALAPPDATA "osulazer/packages/$packageName"

    if (Test-Path -LiteralPath $installedPackage -PathType Leaf) {
        return (Resolve-Path -LiteralPath $installedPackage).Path
    }

    $cacheDirectory = Join-Path $ArtifactsRoot 'official-lazer-runtime'
    $cachedPackage = Join-Path $cacheDirectory $packageName

    if (Test-Path -LiteralPath $cachedPackage -PathType Leaf) {
        return (Resolve-Path -LiteralPath $cachedPackage).Path
    }

    New-Item -ItemType Directory -Path $cacheDirectory -Force | Out-Null
    $downloadPath = "$cachedPackage.download"
    $downloadUrl = "https://github.com/ppy/osu/releases/download/$tag/$packageName"

    try {
        $previousProgressPreference = $ProgressPreference
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest -Uri $downloadUrl -OutFile $downloadPath
        Move-Item -LiteralPath $downloadPath -Destination $cachedPackage -Force
    }
    finally {
        $ProgressPreference = $previousProgressPreference

        if (Test-Path -LiteralPath $downloadPath) {
            Remove-Item -LiteralPath $downloadPath -Force
        }
    }

    return (Resolve-Path -LiteralPath $cachedPackage).Path
}

function Get-OfficialLazerRuntimeInfo {
    param([Parameter(Mandatory)] [string] $PackagePath)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)

    try {
        $depsEntry = $archive.GetEntry('lib/app/osu!.deps.json')
        if ($null -eq $depsEntry) {
            throw "Official lazer package '$PackagePath' does not contain osu!.deps.json."
        }

        $stream = $depsEntry.Open()
        $reader = [System.IO.StreamReader]::new($stream)

        try {
            $deps = $reader.ReadToEnd() | ConvertFrom-Json
        }
        finally {
            $reader.Dispose()
            $stream.Dispose()
        }

        $targetName = [string] $deps.runtimeTarget.name
        $target = $deps.targets.$targetName
        $runtimeKey = @(
            $target.PSObject.Properties.Name |
                Where-Object { $_ -like 'runtimepack.Microsoft.NETCore.App.Runtime.win-x64/*' }
        )

        if ($runtimeKey.Count -ne 1 -or $runtimeKey[0] -notmatch '/(?<version>\d+\.\d+\.\d+)$') {
            throw "Could not determine the official Windows runtime version from '$PackagePath'."
        }

        $runtimeVersion = $Matches.version

        foreach ($fileName in $script:officialRuntimeFiles.Keys) {
            if ($null -eq $archive.GetEntry("lib/app/$fileName")) {
                throw "Official lazer package '$PackagePath' is missing '$fileName'."
            }
        }

        if ($targetName -notmatch '^\.NETCoreApp,Version=v(?<major>\d+)\.(?<minor>\d+)/win-x64$') {
            throw "Could not determine the official target framework from '$targetName'."
        }

        return [pscustomobject]@{
            Version = $runtimeVersion
            TargetFramework = "net$($Matches.major).$($Matches.minor)"
            PackagePath = $PackagePath
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Export-OfficialLazerRuntimeFiles {
    param(
        [Parameter(Mandatory)] [string] $PackagePath,
        [Parameter(Mandatory)] [string] $Destination
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)

    try {
        foreach ($fileName in $script:officialRuntimeFiles.Keys) {
            $entry = $archive.GetEntry("lib/app/$fileName")
            if ($null -eq $entry) {
                throw "Official lazer package '$PackagePath' is missing '$fileName'."
            }

            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $Destination $fileName), $true)
        }
    }
    finally {
        $archive.Dispose()
    }
}
