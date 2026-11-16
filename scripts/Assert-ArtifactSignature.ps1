# The caller must supply the signature returned by the Windows trust provider for this path.
function Assert-ArtifactSignature($Signature, [string]$Path, [string]$ExpectedFingerprint) {
    if ($Signature.Status -ne 'Valid' -or $Signature.SignatureType -ne 'Authenticode' -or !$Signature.SignerCertificate) {
        throw "Firma Authenticode non valida: $Path ($($Signature.Status))."
    }
    if ($null -eq $Signature.TimeStamperCertificate) { throw "Firma senza timestamp: $Path" }
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try { $fingerprint = [BitConverter]::ToString($sha256.ComputeHash($Signature.SignerCertificate.RawData)).Replace('-', '') }
    finally { $sha256.Dispose() }
    if ($fingerprint -ne $ExpectedFingerprint) { throw "Firmatario inatteso: $Path ($fingerprint)." }
    Write-Host "Firma Authenticode verificata: $Path ($fingerprint)."
}
