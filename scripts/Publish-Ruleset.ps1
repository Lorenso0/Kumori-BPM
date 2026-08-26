param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [string] $Version
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'osu.Game.Rulesets.Kumori\osu.Game.Rulesets.Kumori.csproj'
$outputDirectory = Join-Path $repositoryRoot 'artifacts\rulesets'
$buildDirectory = Join-Path $repositoryRoot "osu.Game.Rulesets.Kumori\bin\$Configuration\net8.0"
$rulesetDll = 'osu.Game.Rulesets.Kumori.dll'

$buildArguments = @('build', $project, '-c', $Configuration)
if ($Version) {
    $buildArguments += "-p:Version=$Version"
}

dotnet @buildArguments
if ($LASTEXITCODE -ne 0) {
    throw 'Kumori ruleset build failed.'
}

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $buildDirectory $rulesetDll) -Destination (Join-Path $outputDirectory $rulesetDll) -Force

$published = Get-Item -LiteralPath (Join-Path $outputDirectory $rulesetDll)
$hash = Get-FileHash -LiteralPath $published.FullName -Algorithm SHA256

[pscustomobject]@{
    Path = $published.FullName
    Length = $published.Length
    LastWriteTime = $published.LastWriteTime
    SHA256 = $hash.Hash
} | Format-List
