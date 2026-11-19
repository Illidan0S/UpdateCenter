# Shared by the Setup build and both release jobs. Only the workflow reads the
# repository variable; a project's version change alone cannot approve a release.
function Get-UnsignedReleaseMode([xml]$Project, [string]$Tag, [string]$ApprovedTag, [string]$Repository) {
    $versionNodes = $Project.SelectNodes('/Project/PropertyGroup/Version')
    $policyNodes = $Project.SelectNodes('/Project/PropertyGroup/UnsignedReleaseVersion')
    if ($versionNodes.Count -ne 1 -or $policyNodes.Count -gt 1) { throw 'Ambiguous project version or unsigned release policy.' }
    foreach ($node in (@($versionNodes) + @($policyNodes))) {
        if ($node.HasAttribute('Condition') -or $node.ParentNode.HasAttribute('Condition')) { throw 'Conditional release policy is ambiguous.' }
    }
    $version = $versionNodes[0].InnerText
    if ($version -cnotmatch '\A(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\z' -or
        $null -eq ($version -as [version])) { throw 'Expected a canonical three-component release version.' }
    if ($Tag -and $Tag -cne "v$version") { throw 'The selected stable tag differs from the project version.' }
    if ($policyNodes.Count -ne 1 -or $policyNodes[0].InnerText -cne $version) { return 'signed' }
    if ($version -ceq '2.0.0') { return 'historical' }
    if ([version]$version -le [version]'2.0.1') { return 'signed' }
    if ($Repository -cne 'Illidan0S/UpdateCenter' -or $Tag -cne "v$version" -or $ApprovedTag -cne $Tag) {
        throw 'Unsigned release requires the official repository, a verified tag and an exact UNSIGNED_UPDATE_APPROVED_TAG.'
    }
    return 'approved'
}

function Get-VerifiedArtifactMode([xml]$Project) {
    $mode = Get-UnsignedReleaseMode $Project $env:RELEASE_TAG $env:UNSIGNED_UPDATE_APPROVED_TAG $env:GITHUB_REPOSITORY
    if ($Project.SelectSingleNode('/Project/PropertyGroup/Version').InnerText -cne $env:RELEASE_VERSION -or
        $env:UNSIGNED_RELEASE -cne (($mode -ne 'signed').ToString().ToLowerInvariant())) {
        throw 'Downloaded artifacts and build policy disagree.'
    }
    return $mode
}

function Get-UnsignedManifestBytes([string]$Directory, [string]$Version) {
    $content = [ordered]@{
        schema = 1
        repository = 'Illidan0S/UpdateCenter'
        tag = "v$Version"
        portableSha256 = (Get-FileHash -LiteralPath (Join-Path $Directory "UpdateCenter-v$Version-Portable.exe") -Algorithm SHA256).Hash
        setupSha256 = (Get-FileHash -LiteralPath (Join-Path $Directory "UpdateCenter-Setup-v$Version.exe") -Algorithm SHA256).Hash
    }
    return ,([Text.Encoding]::UTF8.GetBytes(($content | ConvertTo-Json -Compress)))
}

function New-UnsignedUpdateManifest([string]$Directory, [string]$Version) {
    $bytes = Get-UnsignedManifestBytes $Directory $Version
    $path = Join-Path $Directory "UpdateCenter-v$Version-update.json"
    $stream = [IO.File]::Open($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
}

function Confirm-ReleaseAssets([string]$Directory, [string]$Version, [ValidateSet('signed','historical','approved')][string]$Mode) {
    $executables = @("UpdateCenter-Setup-v$Version.exe", "UpdateCenter-v$Version-Portable.exe")
    $expected = $executables + @($executables | ForEach-Object { "$_.sha256" })
    $marker = "UpdateCenter-v$Version-update.json"
    if ($Mode -ceq 'approved') { $expected += $marker }
    $entries = @(Get-ChildItem -LiteralPath $Directory -Force)
    if ($entries.Count -ne $expected.Count) { throw "Unexpected release asset count for $Version." }
    foreach ($entry in $entries) {
        if ($entry.PSIsContainer -or ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $entry.Name -cnotin $expected) {
            throw "Unexpected release entry $($entry.Name)."
        }
    }
    foreach ($name in $executables) {
        $path = Join-Path $Directory $name
        $sha = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
        $sidecar = Get-Item -LiteralPath "$path.sha256"
        $line = "$sha  $name"
        if ($sidecar.Length -gt $line.Length + 2) { throw "Invalid checksum size for $name." }
        $text = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($sidecar.FullName))
        if ($text -cnotin @($line, ($line + [char]10), ($line + [char]13 + [char]10))) { throw "Checksum mismatch for $name." }
    }
    if ($Mode -ceq 'approved') {
        $expectedBytes = Get-UnsignedManifestBytes $Directory $Version
        $path = Join-Path $Directory $marker
        if ((Get-Item -LiteralPath $path).Length -ne $expectedBytes.Length -or
            [Convert]::ToBase64String([IO.File]::ReadAllBytes($path)) -cne [Convert]::ToBase64String($expectedBytes)) {
            throw 'The approval manifest does not match the final release bytes, repository and tag.'
        }
    }
}
