[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $UpstreamTag
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($UpstreamTag -notmatch '^(?<version>\d{4}\.\d+\.\d+)-lazer$') {
    throw "Invalid upstream lazer tag '$UpstreamTag'."
}

$newVersion = $Matches.version
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$utf8 = [System.Text.UTF8Encoding]::new($false)

[xml] $buildProperties = Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Build.props')
$releaseProperties = @($buildProperties.Project.PropertyGroup) |
                     Where-Object { $_.Label -eq 'Kumori Release' } |
                     Select-Object -First 1
$oldVersion = ([string] $releaseProperties.UpstreamVersion).Trim()

if ($oldVersion -eq $newVersion) {
    Write-Host "Already configured for $UpstreamTag."
    return
}

$upstreamCommit = (& git -C $repositoryRoot rev-list -n 1 $UpstreamTag).Trim()
if ($LASTEXITCODE -ne 0 -or $upstreamCommit -notmatch '^[0-9a-f]{40}$') {
    throw "Could not resolve commit for '$UpstreamTag'."
}

function Update-TextFile {
    param(
        [Parameter(Mandatory)] [string] $RelativePath,
        [Parameter(Mandatory)] [scriptblock] $Transform
    )

    $path = Join-Path $repositoryRoot $RelativePath
    $before = [System.IO.File]::ReadAllText($path)
    $after = & $Transform $before

    if ($after -eq $before) {
        throw "Updating '$RelativePath' made no changes."
    }

    [System.IO.File]::WriteAllText($path, $after, $utf8)
}

Update-TextFile 'Directory.Build.props' {
    param($content)
    $content = $content -replace "<UpstreamVersion>$([regex]::Escape($oldVersion))</UpstreamVersion>", "<UpstreamVersion>$newVersion</UpstreamVersion>"
    $content -replace '<KumoriRevision>\d+</KumoriRevision>', '<KumoriRevision>1</KumoriRevision>'
}

Update-TextFile 'BPM_MOD.md' {
    param($content)
    $content = $content -replace '(?m)^- Upstream release: `[^`]+`$', "- Upstream release: ``$UpstreamTag``"
    $content = $content -replace '(?m)^- Upstream commit: `[0-9a-f]+`$', "- Upstream commit: ``$upstreamCommit``"
    $content = $content -replace '(?m)^- Kumori release: `[^`]+`$', "- Kumori release: ``$newVersion-kumori.1``"
    $content -replace '(?m)^- Executable version: `[^`]+`$', "- Executable version: ``$UpstreamTag``"
}

Update-TextFile 'README.md' {
    param($content)
    $content -replace '(?m)^with a target-BPM gameplay mod\. It is based on the public\r?\n`[^`]+` release\.$', "with a target-BPM gameplay mod. It is based on the public`r`n``$UpstreamTag`` release."
}

Update-TextFile 'RELEASE.md' {
    param($content)
    $content = $content -replace '(?m)^`[^`]+-lazer`\.$', "``$UpstreamTag``."
    $content -replace '(?ms)(- Updated the upstream osu!lazer base from )`[^`]+`( to\r?\n  )`[^`]+`(\.)', "`${1}``$oldVersion-lazer```${2}``$UpstreamTag```${3}"
}

& (Join-Path $PSScriptRoot 'Test-ReleaseMetadata.ps1')
Write-Host "Updated release metadata from $oldVersion-lazer to $UpstreamTag ($upstreamCommit)."
