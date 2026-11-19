[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'FuturePackageFixture\FuturePackageFixture.csproj'
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$workRoot = Join-Path ([IO.Path]::GetTempPath()) ('UpdateCenter.FutureFixtureBuild.' + [guid]::NewGuid().ToString('N'))

foreach ($version in @('2.0.2', '2.4.0')) {
    $destination = Join-Path $outputRoot $version
    [IO.Directory]::CreateDirectory($destination) | Out-Null
    foreach ($kind in @('Portable', 'Setup')) {
        $artifacts = Join-Path $workRoot "$version\$kind"
        $compiled = Join-Path $artifacts 'compiled'
        & dotnet build $project --configuration Release --artifacts-path $artifacts --output $compiled `
            "-p:FixtureVersion=$version" "-p:FixtureKind=$kind" --nologo
        if ($LASTEXITCODE -ne 0) { throw "SDK fixture build failed for $kind $version." }

        $file = "UpdateCenter.FutureFixture.$kind.dll"
        [IO.File]::Copy((Join-Path $compiled $file), (Join-Path $destination $file), $true)
    }
}
