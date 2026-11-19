[CmdletBinding()]
param([string]$SmokeRunner)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$policyPath = Join-Path $root 'scripts/ReleaseArtifactPolicy.ps1'
if (!(Test-Path -LiteralPath $policyPath -PathType Leaf)) { throw 'Missing exact-tag release and manifest contract.' }
. $policyPath

function Check([bool]$Value, [string]$Case) {
    if (!$Value) { throw "FAIL $Case" }
    Write-Output "PASS $Case"
}
function Reject([scriptblock]$Action, [string]$Case) {
    try { & $Action | Out-Null } catch { Write-Output "PASS $Case"; return }
    throw "FAIL $Case (accepted)"
}
function Project([string]$Version, [string]$OptIn) {
    [xml]"<Project><PropertyGroup><Version>$Version</Version><UnsignedReleaseVersion>$OptIn</UnsignedReleaseVersion></PropertyGroup></Project>"
}
$repo = 'Illidan0S/UpdateCenter'
$v = '8.7.6'
$tag = 'v8.7.6'
$app = Project $v $v
Check ((Get-UnsignedReleaseMode $app $tag $tag $repo) -ceq 'approved') 'ExactProjectTagAndRepositoryOptIn'
Check ((Get-UnsignedReleaseMode (Project $v '2.0.0') $tag $tag $repo) -ceq 'signed') 'StaleProjectPolicyNeedsSignature'
Check ((Get-UnsignedReleaseMode (Project '2.0.0' '2.0.0') 'v2.0.0' $tag $repo) -ceq 'historical') 'Corrected200HistoricalFourAssets'
Check ((Get-UnsignedReleaseMode (Project '2.0.1' '2.0.1') 'v2.0.1' 'v2.0.1' $repo) -ceq 'signed') 'Published201NeverAuthorized'
foreach ($bad in '', 'v8.7.5', 'V8.7.6', 'v8.*.*', ' v8.7.6') {
    Reject { Get-UnsignedReleaseMode $app $tag $bad $repo } "RejectApproval[$bad]"
}
Reject { Get-UnsignedReleaseMode $app '' $tag $repo } 'UntaggedOptInRefused'
Reject { Get-UnsignedReleaseMode $app 'V8.7.6' $tag $repo } 'TagCaseMatters'
Reject { Get-UnsignedReleaseMode $app $tag $tag 'different/UpdateCenter' } 'ForeignRepository'
Reject { Get-UnsignedReleaseMode (Project '08.7.6' '08.7.6') 'v08.7.6' 'v08.7.6' $repo } 'NoncanonicalVersion'
Check ((Get-UnsignedReleaseMode (Project '1.9.0' '1.9.0') 'v1.9.0' 'v1.9.0' $repo) -ceq 'signed') 'ArbitraryHistoricalVersionNeedsSignature'

$sandbox = [IO.Directory]::CreateTempSubdirectory('updatecenter-release-fixture-').FullName
$portable = "UpdateCenter-$tag-Portable.exe"
$setup = "UpdateCenter-Setup-$tag.exe"
$marker = "UpdateCenter-$tag-update.json"
$markerPath = Join-Path $sandbox $marker
try {
    foreach ($name in $portable, $setup) {
        $path = Join-Path $sandbox $name
        [IO.File]::WriteAllText($path, "fixture $name")
        $sha = (Get-FileHash -LiteralPath $path).Hash
        [IO.File]::WriteAllText("$path.sha256", "$sha  $name")
    }
    Confirm-ReleaseAssets $sandbox $v 'signed'
    Confirm-ReleaseAssets $sandbox $v 'historical'
    Reject { Confirm-ReleaseAssets $sandbox $v 'approved' } 'MissingManifest'
    New-UnsignedUpdateManifest $sandbox $v
    Confirm-ReleaseAssets $sandbox $v 'approved'
    $original = [IO.File]::ReadAllBytes($markerPath)
    $json = [Text.Encoding]::UTF8.GetString($original) | ConvertFrom-Json
    Check (($json.PSObject.Properties.Name -join ',') -ceq 'schema,repository,tag,portableSha256,setupSha256' -and
        $json.schema -is [long] -and $json.schema -eq 1 -and $json.repository -ceq $repo -and $json.tag -ceq $tag -and
        $json.portableSha256 -ceq (Get-FileHash -LiteralPath (Join-Path $sandbox $portable)).Hash -and
        $json.setupSha256 -ceq (Get-FileHash -LiteralPath (Join-Path $sandbox $setup)).Hash) 'ManifestBindsBothFinalByteStreams'
    if (!$SmokeRunner) {
        $SmokeRunner = Join-Path $PSScriptRoot 'UpdateCenter.SmokeTests/bin/Release/net10.0-windows10.0.17763.0/win-x64/UpdateCenter.SmokeTests.exe'
    }
    & $SmokeRunner --unsigned-update-authorization $markerPath
    if ($LASTEXITCODE -ne 0) { throw 'Production authorization reader rejected the workflow fixture.' }
    Reject { New-UnsignedUpdateManifest $sandbox $v } 'ExistingManifestNeverOverwritten'
    Reject { Confirm-ReleaseAssets $sandbox $v 'signed' } 'SignedReleaseRejectsUnexpectedManifest'
    Reject { Confirm-ReleaseAssets $sandbox $v 'historical' } 'HistoricalReleaseRejectsUnexpectedManifest'
    [IO.File]::WriteAllText($markerPath, [Text.Encoding]::UTF8.GetString($original).Replace($tag, 'v8.7.5'))
    Reject { Confirm-ReleaseAssets $sandbox $v 'approved' } 'IncorrectManifestTag'
    [IO.File]::WriteAllBytes($markerPath, $original)
    $portablePath = Join-Path $sandbox $portable
    [IO.File]::AppendAllText($portablePath, 'changed')
    [IO.File]::WriteAllText("$portablePath.sha256", "$( (Get-FileHash -LiteralPath $portablePath).Hash )  $portable")
    Reject { Confirm-ReleaseAssets $sandbox $v 'approved' } 'ReplacedPortableAndSidecarDoNotMatchApproval'
    [IO.File]::Delete($markerPath)
    $line = [IO.File]::ReadAllText("$portablePath.sha256")
    [IO.File]::WriteAllText("$portablePath.sha256", $line, [Text.Encoding]::Unicode)
    Reject { Confirm-ReleaseAssets $sandbox $v 'signed' } 'SidecarMustBeReadableByProduct'
} finally {
    foreach ($name in $portable, $setup, "$portable.sha256", "$setup.sha256", $marker) { [IO.File]::Delete((Join-Path $sandbox $name)) }
    [IO.Directory]::Delete($sandbox)
}

$workflow = Get-Content -LiteralPath (Join-Path $root '.github/workflows/release.yml') -Raw
Check (!$workflow.Contains('--clobber') -and $workflow.Contains('--verify-tag') -and
    $workflow.Contains('refs/tags/$releaseTag^{commit}') -and $workflow.Contains('vars.UNSIGNED_UPDATE_APPROVED_TAG')) 'WorkflowNeverReplacesAssetsAndBindsCommit'
Check ([regex]::Matches($workflow, '(?m)^\s+Confirm-ReleaseAssets ').Count -eq 2 -and
    [regex]::Matches($workflow, '(?m)^\s+New-UnsignedUpdateManifest ').Count -eq 1 -and
    [regex]::Matches($workflow, '(?m)^\s+\$mode = Get-VerifiedArtifactMode ').Count -eq 2) 'IndependentBuildAndPublicationValidation'
