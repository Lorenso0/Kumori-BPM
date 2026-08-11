[CmdletBinding()]
param(
    [string] $ExecutablePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
[xml] $buildProperties = Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Build.props')
$releaseProperties = @($buildProperties.Project.PropertyGroup) |
                     Where-Object { $_.Label -eq 'Kumori Release' } |
                     Select-Object -First 1

if ($null -eq $releaseProperties) {
    throw 'Directory.Build.props does not contain the Kumori Release property group.'
}

$upstreamVersion = ([string] $releaseProperties.UpstreamVersion).Trim()
$kumoriRevision = ([string] $releaseProperties.KumoriRevision).Trim()
$kumoriExpression = ([string] $releaseProperties.KumoriVersion).Trim()

if ($upstreamVersion -notmatch '^\d{4}\.\d+\.\d+$') {
    throw "Invalid UpstreamVersion '$upstreamVersion'."
}
if ($kumoriRevision -notmatch '^\d+$' -or [int] $kumoriRevision -lt 1) {
    throw "Invalid KumoriRevision '$kumoriRevision'."
}
if ($kumoriExpression -ne '$(UpstreamVersion)-kumori.$(KumoriRevision)') {
    throw 'KumoriVersion must be derived from UpstreamVersion and KumoriRevision.'
}

$kumoriVersion = "$upstreamVersion-kumori.$kumoriRevision"
$lazerVersion = "$upstreamVersion-lazer"

[xml] $desktopProject = Get-Content -LiteralPath (Join-Path $repositoryRoot 'osu.Desktop/osu.Desktop.csproj')
$projectProperties = @($desktopProject.Project.PropertyGroup) |
                     Where-Object { $_.Label -eq 'Project' } |
                     Select-Object -First 1

if (([string] $projectProperties.Version).Trim() -ne '$(UpstreamVersion)-lazer') {
    throw 'The desktop product version must be derived from UpstreamVersion.'
}
if (([string] $projectProperties.FileVersion).Trim() -ne '$(UpstreamVersion)') {
    throw 'The desktop file version must be derived from UpstreamVersion.'
}

$requiredDocumentation = @(
    @{ Path = 'README.md'; Pattern = [regex]::Escape($lazerVersion) },
    @{ Path = 'BPM_MOD.md'; Pattern = [regex]::Escape($lazerVersion) },
    @{ Path = 'BPM_MOD.md'; Pattern = [regex]::Escape($kumoriVersion) },
    @{ Path = 'RELEASE.md'; Pattern = [regex]::Escape($lazerVersion) }
)

foreach ($document in $requiredDocumentation) {
    $content = Get-Content -LiteralPath (Join-Path $repositoryRoot $document.Path) -Raw
    if ($content -notmatch $document.Pattern) {
        throw "$($document.Path) does not contain expected release metadata for $lazerVersion / $kumoriVersion."
    }
}

if (-not [string]::IsNullOrWhiteSpace($ExecutablePath)) {
    $resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
    $versionInfo = (Get-Item -LiteralPath $resolvedExecutable).VersionInfo

    if (-not $versionInfo.ProductVersion.StartsWith($lazerVersion, [System.StringComparison]::Ordinal)) {
        throw "Executable product version '$($versionInfo.ProductVersion)' does not start with '$lazerVersion'."
    }
    if (-not $versionInfo.FileVersion.StartsWith($upstreamVersion, [System.StringComparison]::Ordinal)) {
        throw "Executable file version '$($versionInfo.FileVersion)' does not start with '$upstreamVersion'."
    }
}

Write-Host "Release metadata valid: $kumoriVersion based on $lazerVersion"
