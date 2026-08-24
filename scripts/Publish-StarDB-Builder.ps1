param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'Kumori.StarDB.Builder\Kumori.StarDB.Builder.csproj'
$outputDirectory = Join-Path $repositoryRoot 'artifacts\star-db-builder'

if (Test-Path -LiteralPath $outputDirectory) {
    Remove-Item -LiteralPath $outputDirectory -Recurse -Force
}

dotnet publish $project -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
    -o $outputDirectory

if ($LASTEXITCODE -ne 0) {
    throw 'Kumori standalone star database builder publish failed.'
}

$rulesetDocumentation = Join-Path $outputDirectory 'osu.Game.Rulesets.Kumori.xml'
$stbiImportLibrary = Join-Path $outputDirectory 'stbi.lib'

if (Test-Path -LiteralPath $rulesetDocumentation) {
    Remove-Item -LiteralPath $rulesetDocumentation -Force
}

if (Test-Path -LiteralPath $stbiImportLibrary) {
    Remove-Item -LiteralPath $stbiImportLibrary -Force
}

$published = Get-Item -LiteralPath (Join-Path $outputDirectory 'Kumori.StarDB.Builder.exe')
$hash = Get-FileHash -LiteralPath $published.FullName -Algorithm SHA256

[pscustomobject]@{
    Path = $published.FullName
    Length = $published.Length
    LastWriteTime = $published.LastWriteTime
    SHA256 = $hash.Hash
} | Format-List
