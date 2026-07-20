[CmdletBinding()]
param(
    [string] $ReleaseVersion,
    [switch] $NoRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
[xml] $buildProperties = Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Build.props')
$releasePropertyGroup = @($buildProperties.Project.PropertyGroup) |
                        Where-Object { $_.Label -eq 'Kumori Release' } |
                        Select-Object -First 1
if ($null -eq $releasePropertyGroup -or $null -eq $releasePropertyGroup.KumoriVersion) {
    throw 'Directory.Build.props does not define KumoriVersion in the Kumori Release property group.'
}
$configuredVersion = ([string] $releasePropertyGroup.KumoriVersion).Trim()

if ([string]::IsNullOrWhiteSpace($ReleaseVersion)) {
    $ReleaseVersion = $configuredVersion
}

if ($ReleaseVersion -notmatch '^\d{4}\.\d+\.\d+-kumori\.\d+$') {
    throw "Invalid Kumori release version '$ReleaseVersion'."
}
if ($ReleaseVersion -ne $configuredVersion) {
    throw "Release version '$ReleaseVersion' does not match KumoriVersion '$configuredVersion'."
}

$releaseRoot = Join-Path $repositoryRoot 'artifacts/releases'
$workingRoot = Join-Path $releaseRoot "_work-$ReleaseVersion"
$stageDirectory = Join-Path $workingRoot 'stage'
$zipPath = Join-Path $releaseRoot "kumori-osu-$ReleaseVersion-win-x64.zip"
$checksumPath = "$zipPath.sha256"

$resolvedArtifacts = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$resolvedWorking = [System.IO.Path]::GetFullPath($workingRoot)
if (-not $resolvedWorking.StartsWith($resolvedArtifacts + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean unexpected working directory '$resolvedWorking'."
}

if (Test-Path -LiteralPath $workingRoot) {
    Remove-Item -LiteralPath $workingRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $stageDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null

$publishArguments = @(
    'publish',
    (Join-Path $repositoryRoot 'osu.Desktop/osu.Desktop.csproj'),
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-o', $stageDirectory,
    '-p:ContinuousIntegrationBuild=true'
)
if ($NoRestore) {
    $publishArguments += '--no-restore'
}

& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Get-ChildItem -LiteralPath $stageDirectory -Recurse -File |
    Where-Object { $_.Extension -in @('.pdb', '.xml') } |
    Remove-Item -Force

Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENCE') -Destination $stageDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'RELEASE.md') -Destination (Join-Path $stageDirectory 'README.md')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'BPM_MOD.md') -Destination $stageDirectory
[System.IO.File]::WriteAllText(
    (Join-Path $stageDirectory 'KUMORI_VERSION'),
    "$ReleaseVersion`n",
    [System.Text.UTF8Encoding]::new($false)
)

& (Join-Path $PSScriptRoot 'New-ThirdPartyNotices.ps1') `
    -AssetsFile (Join-Path $repositoryRoot 'osu.Desktop/obj/project.assets.json') `
    -OutputPath (Join-Path $stageDirectory 'THIRD_PARTY_NOTICES.md')

$forbiddenFiles = @(
    Get-ChildItem -LiteralPath $stageDirectory -Recurse -File |
        Where-Object {
            $_.Extension -in @('.pdb', '.xml', '.nupkg', '.snupkg') -or
            $_.Name -match '\.Tests?\.dll$'
        }
)
if ($forbiddenFiles.Count -gt 0) {
    $paths = ($forbiddenFiles.FullName -join [Environment]::NewLine)
    throw "Release audit found forbidden files:$([Environment]::NewLine)$paths"
}

$requiredFiles = @(
    'osu!.exe',
    'osu!.dll',
    'osu.Game.dll',
    'osu.Game.Rulesets.Osu.dll',
    'osu.Game.Rulesets.Taiko.dll',
    'osu.Game.Rulesets.Catch.dll',
    'osu.Game.Rulesets.Mania.dll',
    'LICENCE',
    'README.md',
    'BPM_MOD.md',
    'THIRD_PARTY_NOTICES.md',
    'KUMORI_VERSION'
)
foreach ($requiredFile in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $stageDirectory $requiredFile))) {
        throw "Release audit is missing required file '$requiredFile'."
    }
}

foreach ($outputPath in @($zipPath, $checksumPath)) {
    if (Test-Path -LiteralPath $outputPath) {
        Remove-Item -LiteralPath $outputPath -Force
    }
}

Compress-Archive -Path (Join-Path $stageDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumLine = "$hash  $([System.IO.Path]::GetFileName($zipPath))`n"
[System.IO.File]::WriteAllText($checksumPath, $checksumLine, [System.Text.UTF8Encoding]::new($false))

$archiveSize = (Get-Item -LiteralPath $zipPath).Length
$fileCount = @(Get-ChildItem -LiteralPath $stageDirectory -Recurse -File).Count
Write-Host "Created $zipPath"
Write-Host "SHA-256 $hash"
Write-Host "Packaged $fileCount files ($([math]::Round($archiveSize / 1MB, 2)) MiB compressed)."
