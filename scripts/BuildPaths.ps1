# Local Windows build outputs. Walk existing ancestors before allowing any writes.
function Test-BuildCloudTag([string]$Tag) {
    # Documented Cloud Files tags have no name-surrogate bit: they do not redirect paths.
    return $Tag -match '^9000[0-9a-f]01a$'
}

function Assert-NoBuildReparsePoint([string]$Path) {
    $current = $Path
    while ($current) {
        try {
            $attrs = [IO.File]::GetAttributes($current)
            if ($attrs -band [IO.FileAttributes]::ReparsePoint) {
                $fsutil = Join-Path ([Environment]::SystemDirectory) 'fsutil.exe'
                $metadata = & $fsutil reparsepoint query $current 2>&1
                if ($LASTEXITCODE -ne 0) { throw "Tag reparse non verificabile: $current" }
                $tag = [regex]::Match(($metadata -join "`n"), '(?i)\b0x([0-9a-f]{8})\b').Groups[1].Value
                if (!(Test-BuildCloudTag $tag)) { throw "Reparse point non ammesso: $current ($tag)" }
            }
        } catch [IO.FileNotFoundException] {
        } catch [IO.DirectoryNotFoundException] {
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
}

function Resolve-BuildDirectory([string]$ProjectRoot, [string]$Requested, [string]$Kind) {
    $base = Join-Path $ProjectRoot '_output'
    $default = switch ($Kind) {
        'Portable' { Join-Path $ProjectRoot 'dist' }
        'Setup' { Join-Path $ProjectRoot 'installer-dist' }
        'Preview' {
            $base = Join-Path $base 'network-preview'
            Join-Path $base ([guid]::NewGuid().ToString('N'))
        }
        default { throw "Tipo di build sconosciuto: $Kind" }
    }
    if (!$Requested) { $Requested = $default }
    $path = $Requested.Replace('/', '\')
    if ($path.StartsWith('.\')) { $path = $path.Substring(2) }
    if ($path.StartsWith('\') -or $path -match '(^|\\)\.{1,2}(\\|$)' -or
        ($path.Contains(':') -and $path -notmatch '^[a-zA-Z]:\\[^:]*$')) {
        throw "Percorso locale non valido: $Requested"
    }
    if (![IO.Path]::IsPathRooted($path)) { $path = Join-Path $ProjectRoot $path }
    # Reject aliases/streams, wildcard syntax and Win32 names normalized by the filesystem.
    if ($path -match '[. ](\\|$)|[\x00-\x1f<>"|?*\[\]]|\\(?:CON|PRN|AUX|NUL|(?:COM|LPT)[0-9\u00b9\u00b2\u00b3])(?:\.|\\|$)') {
        throw "Nome non valido: $Requested"
    }
    $path = [IO.Path]::GetFullPath($path).TrimEnd('\')
    if ($path -ne $default -and !$path.StartsWith(($base + '\'), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Output non consentito: $Requested"
    }
    if ($Kind -eq 'Preview' -and [IO.Path]::GetDirectoryName($path) -ne $base) {
        throw "La preview richiede una sottocartella diretta di $base."
    }
    Assert-NoBuildReparsePoint $path
    return $path
}

function New-BuildDirectory([string]$Path) {
    if (Test-Path -LiteralPath $Path) { throw "Output esistente: $Path. Scegli una nuova -OutputDirectory." }
    $parent = [IO.Path]::GetDirectoryName($Path)
    $null = [IO.Directory]::CreateDirectory($parent)
    # Move refuses existing destinations, including one just claimed by another build.
    $claim = Join-Path $parent ('.build-' + [guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $claim
    try { [IO.Directory]::Move($claim, $Path) }
    finally { if ([IO.Directory]::Exists($claim)) { [IO.Directory]::Delete($claim, $false) } }
}
