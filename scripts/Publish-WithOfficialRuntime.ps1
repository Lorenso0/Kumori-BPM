[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $OutputDirectory,
    [switch] $SingleFile,
    [switch] $ContinuousIntegrationBuild,
    [switch] $NoRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)

if (-not $resolvedOutput.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Official-runtime publish output must remain under '$artifactsRoot'."
}

. (Join-Path $PSScriptRoot 'Official-LazerRuntime.ps1')

[xml] $buildProperties = Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Build.props')
$releaseProperties = @($buildProperties.Project.PropertyGroup) |
                     Where-Object { $_.Label -eq 'Kumori Release' } |
                     Select-Object -First 1
$upstreamVersion = ([string] $releaseProperties.UpstreamVersion).Trim()
$configuredRuntimeVersion = ([string] $releaseProperties.OfficialRuntimeVersion).Trim()

if ($configuredRuntimeVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw "Invalid OfficialRuntimeVersion '$configuredRuntimeVersion'."
}

$officialPackage = Get-OfficialLazerPackage -UpstreamVersion $upstreamVersion -ArtifactsRoot $artifactsRoot
$runtimeInfo = Get-OfficialLazerRuntimeInfo -PackagePath $officialPackage

if ($runtimeInfo.Version -ne $configuredRuntimeVersion) {
    throw "Official lazer $upstreamVersion uses runtime $($runtimeInfo.Version), but Kumori is configured for $configuredRuntimeVersion."
}

$nugetRoot = if ([string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
    Join-Path $env:USERPROFILE '.nuget/packages'
}
else {
    $env:NUGET_PACKAGES
}
$runtimePackRoot = Join-Path $nugetRoot "microsoft.netcore.app.runtime.win-x64/$configuredRuntimeVersion"

if (-not (Test-Path -LiteralPath $runtimePackRoot -PathType Container)) {
    if ($NoRestore) {
        throw "Runtime pack $configuredRuntimeVersion is not restored at '$runtimePackRoot'."
    }

    & dotnet restore (Join-Path $repositoryRoot 'osu.Desktop/osu.Desktop.csproj') -r win-x64 "-p:RuntimeFrameworkVersion=$configuredRuntimeVersion"
    if ($LASTEXITCODE -ne 0) {
        throw "Restoring official runtime pack $configuredRuntimeVersion failed with exit code $LASTEXITCODE."
    }
}

$patchRoot = Join-Path $artifactsRoot "official-lazer-runtime/patch-$([guid]::NewGuid().ToString('N'))"
$officialFiles = Join-Path $patchRoot 'official'
$backupFiles = Join-Path $patchRoot 'backup'
New-Item -ItemType Directory -Path $officialFiles -Force | Out-Null
New-Item -ItemType Directory -Path $backupFiles -Force | Out-Null
Export-OfficialLazerRuntimeFiles -PackagePath $officialPackage -Destination $officialFiles

$patchedFiles = @()

try {
    foreach ($fileName in $script:officialRuntimeFiles.Keys) {
        $runtimePackRelativePath = $script:officialRuntimeFiles[$fileName].Replace('{tfm}', $runtimeInfo.TargetFramework)
        $runtimePackFile = Join-Path $runtimePackRoot $runtimePackRelativePath
        if (-not (Test-Path -LiteralPath $runtimePackFile -PathType Leaf)) {
            throw "Runtime pack $configuredRuntimeVersion is missing '$runtimePackFile'."
        }

        Copy-Item -LiteralPath $runtimePackFile -Destination (Join-Path $backupFiles $fileName)
        Copy-Item -LiteralPath (Join-Path $officialFiles $fileName) -Destination $runtimePackFile -Force
        $patchedFiles += $runtimePackFile
    }

    $publishArguments = @(
        'publish',
        (Join-Path $repositoryRoot 'osu.Desktop/osu.Desktop.csproj'),
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-o', $resolvedOutput,
        "-p:RuntimeFrameworkVersion=$configuredRuntimeVersion"
    )

    if ($SingleFile) {
        $publishArguments += @('-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=None', '-p:DebugSymbols=false')
    }
    if ($ContinuousIntegrationBuild) {
        $publishArguments += '-p:ContinuousIntegrationBuild=true'
    }
    if ($NoRestore) {
        $publishArguments += '--no-restore'
    }

    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Official-runtime publish failed with exit code $LASTEXITCODE."
    }

    if (-not $SingleFile) {
        foreach ($fileName in $script:officialRuntimeFiles.Keys) {
            $publishedHash = (Get-FileHash -LiteralPath (Join-Path $resolvedOutput $fileName) -Algorithm SHA256).Hash
            $officialHash = (Get-FileHash -LiteralPath (Join-Path $officialFiles $fileName) -Algorithm SHA256).Hash

            if ($publishedHash -ne $officialHash) {
                throw "Published '$fileName' does not match official lazer $upstreamVersion."
            }
        }
    }
}
finally {
    foreach ($runtimePackFile in $patchedFiles) {
        $fileName = Split-Path $runtimePackFile -Leaf
        $backupFile = Join-Path $backupFiles $fileName

        if (Test-Path -LiteralPath $backupFile -PathType Leaf) {
            Copy-Item -LiteralPath $backupFile -Destination $runtimePackFile -Force
        }
    }

    if (Test-Path -LiteralPath $patchRoot) {
        Remove-Item -LiteralPath $patchRoot -Recurse -Force
    }
}

Write-Host "Published with the official lazer $upstreamVersion low-latency runtime ($configuredRuntimeVersion)."
