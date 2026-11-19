using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

internal static class ReleasePublisherVerifier
{
    // GitHub v1.1.5 asset 550111885, also confirmed by the published .sha256.
    // https://github.com/Illidan0S/UpdateCenter/releases/download/v1.1.5/UpdateCenter-v1.1.5-Portable.exe
    private const string OfficialPortable115Sha256 = "00912E56944F84B243ECF0F126CA2E67055F9BD3D1EAC98567E42636A198F826";
    private static readonly Assembly AppAssembly = typeof(ReleasePublisherVerifier).Assembly;
    private static readonly string? PublisherPin = ReadMetadata("ReleasePublisherSha256");
    private static readonly string? UnsignedPolicy = ReadMetadata("UnsignedReleaseVersion");
    private static readonly string? BuildFileVersion = AppAssembly.GetName().Version?.ToString();
    internal static readonly SemanticVersion WithdrawnVersion = new(2, 0, 1);
    internal static SemanticVersion? UnsignedSourceVersion =>
        SemanticVersion.TryParse(UnsignedPolicy, out var v) && v.ToString() == UnsignedPolicy &&
        v >= new SemanticVersion(2, 0, 0) && v != WithdrawnVersion &&
        BuildFileVersion == UnsignedPolicy + ".0" && string.IsNullOrEmpty(PublisherPin) ? v : null;

    private static string? ReadMetadata(string key) => AppAssembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .SingleOrDefault(attribute => attribute.Key == key)?.Value;

    internal static bool IsOfficialLegacyTarget(Stream target, string incomingVersion)
    {
        if (incomingVersion != "2.0.0") return false;
        target.Position = 0;
        return Convert.ToHexString(SHA256.HashData(target)) == OfficialPortable115Sha256;
    }

    internal static async Task VerifyReplacementAsync(string installedPath, Stream installed, string candidatePath,
        Stream candidate, string version, AppUpdateService releases)
    {
        if (!SemanticVersion.TryParse(version, out var incoming) || incoming.ToString() != version ||
            incoming == WithdrawnVersion || BuildFileVersion != version + ".0")
            throw new InvalidDataException("Versione di destinazione non ammessa.");
        if (version == "2.0.0" && IsOfficialLegacyTarget(installed, version))
        {
            if (UnsignedSourceVersion is not null)
            {
                await VerifyUnsignedAsync(candidatePath, candidate, CancellationToken.None).ConfigureAwait(false);
                await releases.VerifyOfficialAssetAsync(version, false, candidatePath, candidate, CancellationToken.None).ConfigureAwait(false);
            }
            else await VerifySignaturesAsync([candidatePath], RequirePublisherPin(PublisherPin), CancellationToken.None).ConfigureAwait(false);
            return;
        }

        // A signed successor may inherit an explicitly identified unsigned installation.
        // An invalid old signature cannot be reclassified as an unsigned predecessor.
        try { await VerifyAsync(installedPath, candidatePath, CancellationToken.None).ConfigureAwait(false); }
        catch (UnauthorizedAccessException)
        {
            var text = FileVersionInfo.GetVersionInfo(installedPath).ProductVersion?.Trim();
            if (!SemanticVersion.TryParse(text, out var previous) || text != previous.ToString() ||
                previous < new SemanticVersion(2, 0, 0) || previous == WithdrawnVersion || previous >= incoming) throw;
            await VerifySignaturesAsync([candidatePath], RequirePublisherPin(PublisherPin), CancellationToken.None).ConfigureAwait(false);
            await VerifyUnsignedAsync(installedPath, installed, CancellationToken.None).ConfigureAwait(false);
            await releases.VerifyOfficialAssetAsync(text, false, installedPath, installed, CancellationToken.None).ConfigureAwait(false);
            await releases.VerifyOfficialAssetAsync(version, false, candidatePath, candidate, CancellationToken.None).ConfigureAwait(false);
        }
    }

    // True selects the source-owned transaction, for an exactly authorized, actually unsigned package.
    internal static async Task<bool> VerifyDownloadAsync(string installedPath, string candidatePath, Stream candidate,
        AppUpdateInfo update, AppUpdateService releases, CancellationToken token)
    {
        if (update.AvailableVersion == WithdrawnVersion) throw new InvalidDataException("La versione 2.0.1 è stata ritirata.");
        if (UnsignedSourceVersion is { } current && update.InstalledVersion == current && update.AvailableVersion > current)
        {
            using var installed = File.OpenRead(installedPath);
            return await VerifyFromUnsignedSourceAsync(installedPath, installed, candidatePath, candidate,
                update.AvailableVersion, update.IsInstallerPackage, releases, token).ConfigureAwait(false);
        }
        await VerifyAsync(installedPath, candidatePath, token).ConfigureAwait(false);
        return false;
    }

    internal static async Task<bool> VerifyFromUnsignedSourceAsync(string installedPath, Stream installed,
        string candidatePath, Stream candidate, SemanticVersion incoming, bool setup, AppUpdateService releases,
        CancellationToken token)
    {
        if (UnsignedSourceVersion is not { } current || incoming <= current || incoming == WithdrawnVersion)
            throw new InvalidDataException("Versione di destinazione non ammessa.");
        await releases.VerifyOfficialAssetAsync(current.ToString(), false, installedPath, installed, token).ConfigureAwait(false);
        await releases.VerifyOfficialAssetAsync(incoming.ToString(), setup, candidatePath, candidate, token).ConfigureAwait(false);
        if (await releases.CheckUnsignedAuthorizationAsync(incoming.ToString(), setup, candidate, token).ConfigureAwait(false))
        {
            CheckUnsignedHeader(installed);
            CheckUnsignedHeader(candidate);
            await VerifySignatureStatusAsync([installedPath, candidatePath], "", true, token).ConfigureAwait(false);
            return true;
        }
        await VerifySignaturesAsync([candidatePath], "", token).ConfigureAwait(false);
        return false;
    }

    private static async Task VerifyUnsignedAsync(string path, Stream locked, CancellationToken token)
    {
        CheckUnsignedHeader(locked);
        await VerifySignatureStatusAsync([path], "", true, token).ConfigureAwait(false);
    }

    private static void CheckUnsignedHeader(Stream locked)
    {
        locked.Position = 0;
        using (var pe = new System.Reflection.PortableExecutable.PEReader(locked, System.Reflection.PortableExecutable.PEStreamOptions.LeaveOpen))
        {
            var certificate = pe.PEHeaders.PEHeader?.CertificateTableDirectory;
            if (certificate is not { Size: 0, RelativeVirtualAddress: 0 })
                throw new InvalidDataException("Il file contiene dati di firma incompatibili con la policy senza firma.");
        }
    }

    private static string RequirePublisherPin(string? pin)
    {
        if (pin is null || pin.Length != 64 || !pin.All(Uri.IsHexDigit))
            throw new UnauthorizedAccessException("Impronta del firmatario della release non configurata.");
        return pin.ToUpperInvariant();
    }

    // Windows trust evaluation must cover the embedded PE signature and its signer.
    // Compare exact certificate bytes so another publicly trusted publisher cannot authorize an update.
    internal static Task VerifyAsync(string installedPath, string candidatePath, CancellationToken cancellationToken) =>
        VerifySignaturesAsync([installedPath, candidatePath], "", cancellationToken);

    internal static Task VerifySignaturesAsync(string[] paths, string fingerprint, CancellationToken cancellationToken) =>
        VerifySignatureStatusAsync(paths, fingerprint, false, cancellationToken);

    private static async Task VerifySignatureStatusAsync(string[] paths, string fingerprint, bool requireUnsigned, CancellationToken cancellationToken)
    {
        if (fingerprint.Length != 0 && (fingerprint.Length != 64 || !fingerprint.All(Uri.IsHexDigit)))
            throw new UnauthorizedAccessException("Impronta del firmatario non valida.");
        var powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        // Only Base64 data is inserted into the command, even for paths containing quotes or shell syntax.
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            Microsoft.PowerShell.Core\Import-Module ($PSHOME + '\Modules\Microsoft.PowerShell.Security\Microsoft.PowerShell.Security.psd1')
            $expected = $null
            foreach ($encoded in @({{string.Join(",", paths.Select(path => "'" + Encode(path) + "'"))}})) {
                $path = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($encoded))
                $signature = Microsoft.PowerShell.Security\Get-AuthenticodeSignature -LiteralPath $path
                if ({{(requireUnsigned ? "$true" : "$false")}}) {
                    if ($signature.Status -ne 'NotSigned' -or $signature.SignerCertificate) { exit 1 }
                    continue
                }
                if ($signature.Status -ne 'Valid' -or $signature.SignatureType -ne 'Authenticode' -or
                    !$signature.SignerCertificate -or !$signature.TimeStamperCertificate) { exit 1 }
                $certificate = [Convert]::ToBase64String($signature.SignerCertificate.RawData)
                if ('{{fingerprint}}') {
                    $sha256 = [Security.Cryptography.SHA256]::Create()
                    try { $actual = [BitConverter]::ToString($sha256.ComputeHash($signature.SignerCertificate.RawData)).Replace('-', '') }
                    finally { $sha256.Dispose() }
                    if ($actual -cne '{{fingerprint}}') { exit 1 }
                }
                if ($expected -and $expected -cne $certificate) { exit 1 }
                $expected = $certificate
            }
            exit 0
            """;
        var message = LocalizationService.Text(
            "Firma del publisher non verificata. Scarica manualmente la release dalla pagina GitHub ufficiale di Update Center.",
            "The publisher signature could not be verified. Download the release manually from the official Update Center GitHub page.");
        try
        {
            var result = await ProcessRunner.RunAsync(powershell,
                ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Encode(command)],
                cancellationToken, TimeSpan.FromMinutes(1)).ConfigureAwait(false);
            if (!result.Success) throw new UnauthorizedAccessException(message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
        {
            throw new UnauthorizedAccessException(message, ex);
        }
    }

    private static string Encode(string value) => Convert.ToBase64String(Encoding.Unicode.GetBytes(value));
}
