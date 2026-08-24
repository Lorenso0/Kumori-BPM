$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'Kumori.StarDB.Builder\Kumori.StarDB.Builder.csproj'

dotnet run --project $project -c Release
exit $LASTEXITCODE
