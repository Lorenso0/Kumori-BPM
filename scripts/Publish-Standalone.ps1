[CmdletBinding()]
param(
    [switch] $NoRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$outputDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot 'standalone'))
$destinationExecutable = Join-Path $repositoryRoot 'osu!.exe'

& (Join-Path $PSScriptRoot 'Test-ReleaseMetadata.ps1')

if (-not $outputDirectory.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean unexpected output directory '$outputDirectory'."
}

if (Test-Path -LiteralPath $outputDirectory) {
    Remove-Item -LiteralPath $outputDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$publishArguments = @{
    OutputDirectory = $outputDirectory
    SingleFile = $true
    NoRestore = $NoRestore
}
& (Join-Path $PSScriptRoot 'Publish-WithOfficialRuntime.ps1') @publishArguments

$publishedExecutable = Join-Path $outputDirectory 'osu!.exe'

if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
    throw "Standalone publish did not produce '$publishedExecutable'."
}

Copy-Item -LiteralPath $publishedExecutable -Destination $destinationExecutable -Force

& (Join-Path $PSScriptRoot 'Test-ReleaseMetadata.ps1') -ExecutablePath $destinationExecutable

$executable = Get-Item -LiteralPath $destinationExecutable
$hash = (Get-FileHash -LiteralPath $destinationExecutable -Algorithm SHA256).Hash.ToLowerInvariant()

Write-Host "Rebuilt $destinationExecutable"
Write-Host "Size $($executable.Length) bytes"
Write-Host "SHA-256 $hash"
