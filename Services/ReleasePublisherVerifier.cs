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
    private static readonly Version UnsignedVersion = new(2, 0, 0, 0);
    private static readonly string? PublisherPin = ReadMetadata("ReleasePublisherSha256");
    private static readonly bool IsUnsigned200 = AppAssembly.GetName().Version == UnsignedVersion &&
        ReadMetadata("UnsignedReleaseVersion") == "2.0.0" && string.IsNullOrEmpty(PublisherPin);

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
        if (version == "2.0.0" && AppAssembly.GetName().Version == UnsignedVersion &&
            IsOfficialLegacyTarget(installed, version))
        {
            if (IsUnsigned200)
                await releases.VerifyOfficialAssetAsync("2.0.0", false, candidatePath, candidate, CancellationToken.None).ConfigureAwait(false);
            else
                await VerifySignaturesAsync([candidatePath], RequirePublisherPin(), CancellationToken.None).ConfigureAwait(false);
            return;
        }

        if (AppAssembly.GetName().Version > UnsignedVersion &&
            FileVersionInfo.GetVersionInfo(installedPath).FileVersion?.Trim() == "2.0.0.0")
        {
            try
            {
                await VerifyAsync(installedPath, candidatePath, CancellationToken.None).ConfigureAwait(false);
                return;
            }
            catch (UnauthorizedAccessException)
            {
                await VerifySignaturesAsync([candidatePath], RequirePublisherPin(), CancellationToken.None).ConfigureAwait(false);
                await releases.VerifyOfficialAssetAsync("2.0.0", false, installedPath, installed, CancellationToken.None).ConfigureAwait(false);
                await releases.VerifyOfficialAssetAsync(version, false, candidatePath, candidate, CancellationToken.None).ConfigureAwait(false);
                return;
            }
        }

        await VerifyAsync(installedPath, candidatePath, CancellationToken.None).ConfigureAwait(false);
    }

    internal static async Task VerifyDownloadAsync(string installedPath, string candidatePath, Stream candidate,
        AppUpdateInfo update, AppUpdateService releases, CancellationToken cancellationToken)
    {
        if (IsUnsigned200 && update.AvailableVersion > new SemanticVersion(2, 0, 0))
        {
            await VerifySignaturesAsync([candidatePath], "", cancellationToken).ConfigureAwait(false);
            using var installed = File.OpenRead(installedPath);
            await releases.VerifyOfficialAssetAsync("2.0.0", false, installedPath, installed, cancellationToken).ConfigureAwait(false);
            await releases.VerifyOfficialAssetAsync(update.AvailableVersion.ToString(), update.IsInstallerPackage,
                candidatePath, candidate, cancellationToken).ConfigureAwait(false);
            return;
        }
        await VerifyAsync(installedPath, candidatePath, cancellationToken).ConfigureAwait(false);
    }

    private static string RequirePublisherPin()
    {
        if (PublisherPin is null || PublisherPin.Length != 64 || !PublisherPin.All(Uri.IsHexDigit))
            throw new UnauthorizedAccessException("Impronta del firmatario della release non configurata.");
        return PublisherPin.ToUpperInvariant();
    }

    // Windows trust evaluation must cover the embedded PE signature and its signer.
    // Compare exact certificate bytes so another publicly trusted publisher cannot authorize an update.
    internal static Task VerifyAsync(string installedPath, string candidatePath, CancellationToken cancellationToken) =>
        VerifySignaturesAsync([installedPath, candidatePath], "", cancellationToken);

    internal static async Task VerifySignaturesAsync(string[] paths, string fingerprint, CancellationToken cancellationToken)
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
