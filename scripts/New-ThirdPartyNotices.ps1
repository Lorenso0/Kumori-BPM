[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $AssetsFile,

    [Parameter(Mandatory)]
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$assetsPath = (Resolve-Path -LiteralPath $AssetsFile).Path
$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
$packageRoots = @($assets.packageFolders.PSObject.Properties.Name)

if ($packageRoots.Count -eq 0) {
    throw "No NuGet package folders were recorded in '$assetsPath'."
}

$packages = @(
    foreach ($library in $assets.libraries.PSObject.Properties) {
        if ($library.Value.type -ne 'package') {
            continue
        }

        $separator = $library.Name.LastIndexOf('/')
        if ($separator -le 0) {
            throw "Unexpected NuGet library key '$($library.Name)'."
        }

        $id = $library.Name.Substring(0, $separator)
        $version = $library.Name.Substring($separator + 1)
        $packageDirectory = $null

        foreach ($root in $packageRoots) {
            $candidate = Join-Path $root (Join-Path $id.ToLowerInvariant() $version)
            if (Test-Path -LiteralPath $candidate) {
                $packageDirectory = $candidate
                break
            }
        }

        if ($null -eq $packageDirectory) {
            throw "Restored package directory not found for $id $version."
        }

        $nuspecFile = Get-ChildItem -LiteralPath $packageDirectory -Filter '*.nuspec' -File |
                      Select-Object -First 1
        if ($null -eq $nuspecFile) {
            throw "NuSpec metadata not found for $id $version."
        }

        [xml] $nuspec = Get-Content -LiteralPath $nuspecFile.FullName -Raw
        $metadata = $nuspec.package.metadata
        $licenseProperty = $metadata.PSObject.Properties['license']
        $licenseUrlProperty = $metadata.PSObject.Properties['licenseUrl']
        $projectUrlProperty = $metadata.PSObject.Properties['projectUrl']
        $copyrightProperty = $metadata.PSObject.Properties['copyright']

        $license = if ($null -ne $licenseProperty) {
            $licenseNode = $licenseProperty.Value
            if ($licenseNode.type -eq 'expression') {
                [string] $licenseNode.InnerText
            }
            else {
                "Package file: $([string] $licenseNode.InnerText)"
            }
        }
        elseif ($null -ne $licenseUrlProperty) {
            [string] $licenseUrlProperty.Value
        }
        else {
            'Not declared in NuGet metadata'
        }

        [pscustomobject] @{
            Id = $id
            Version = $version
            License = $license
            ProjectUrl = if ($null -ne $projectUrlProperty) { [string] $projectUrlProperty.Value } else { '' }
            Copyright = if ($null -ne $copyrightProperty) { [string] $copyrightProperty.Value } else { '' }
        }
    }
)

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# Third-party notices')
$lines.Add('')
$lines.Add('Kumori is based on osu!lazer and includes dependencies restored from NuGet.')
$lines.Add('Each dependency remains subject to its own licence. This list is generated')
$lines.Add('from the exact restore graph used to create this release.')
$lines.Add('')
$lines.Add('The upstream osu! source is Copyright (c) ppy Pty Ltd and licensed under')
$lines.Add('the MIT licence included as `LICENCE` in this archive.')
$lines.Add('')

foreach ($package in $packages | Sort-Object Id, Version) {
    $lines.Add("## $($package.Id) $($package.Version)")
    $lines.Add('')
    $lines.Add("- Licence: $($package.License)")
    if (-not [string]::IsNullOrWhiteSpace($package.ProjectUrl)) {
        $lines.Add("- Project: $($package.ProjectUrl)")
    }
    if (-not [string]::IsNullOrWhiteSpace($package.Copyright)) {
        $lines.Add("- Copyright: $($package.Copyright)")
    }
    $lines.Add('')
}

$outputDirectory = Split-Path -Parent $OutputPath
if ($outputDirectory) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

[System.IO.File]::WriteAllLines(
    [System.IO.Path]::GetFullPath($OutputPath),
    $lines,
    [System.Text.UTF8Encoding]::new($false)
)
