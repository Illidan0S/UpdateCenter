using System.Diagnostics;
using System.Net;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using UpdateCenter.Models;
using UpdateCenter.Services;

internal static class FutureUnsignedUpdateTests
{
    internal static async Task RunAsync(string? packaged)
    {
        if (AppPaths.DataDirectory.Equals(AppPaths.ResolveDefaultRoot(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("An isolated profile is required.");
        var fixtures = Environment.GetEnvironmentVariable("UPDATECENTER_FUTURE_FIXTURES");
        if (string.IsNullOrWhiteSpace(fixtures) || !Path.IsPathFullyQualified(fixtures) || !Directory.Exists(fixtures))
            throw new InvalidOperationException("Set UPDATECENTER_FUTURE_FIXTURES to the directory populated by Tests/Build-FutureUnsignedFixtures.ps1.");
        AppPaths.EnsureCreated();
        var current = packaged is null ? typeof(ReleasePublisherVerifier).Assembly.Location : Path.GetFullPath(packaged);
        var hash = Digest(current);
        if (packaged is not null)
        {
            const string name = "UpdateCenter-v2.0.0-Portable.exe";
            CheckWindowsVersion(current, new(2, 0, 0));
            if (Path.GetFileName(current) != name || File.ReadAllText(current + ".sha256").TrimEnd('\r','\n') != $"{hash}  {name}")
                throw new InvalidDataException("The supplied Portable or its sidecar does not match the expected package.");
            Console.WriteLine("PASS SuppliedPortableHasExpectedVersionAndMatchesItsSidecar");
        }
        foreach (var version in new[] { new SemanticVersion(2, 0, 2), new SemanticVersion(2, 4, 0) })
        {
            var text = version.ToString();
            string PathFor(bool setup) => Path.Combine(AppPaths.UpdatesDirectory, Package(text, setup));
            foreach (var setup in new[] { false, true })
                StageFixture(fixtures, version, setup ? "Setup" : "Portable", PathFor(setup));
            if (Digest(PathFor(false)) == Digest(PathFor(true)))
                throw new InvalidDataException("The Portable and Setup fixtures must have distinct bytes.");
            var httpFixture = new Fixture(text, hash, new FileInfo(current).Length, PathFor(false), PathFor(true));
            using var http = new HttpClient(httpFixture);
            var service = new AppUpdateService(http);
            foreach (var setup in new[] { false, true })
            {
                var candidatePath = PathFor(setup);
                using var candidate = File.OpenRead(candidatePath);
                var info = httpFixture.Offer(version, setup);
                Task<bool> Verify() => ReleasePublisherVerifier.VerifyDownloadAsync(current, candidatePath, candidate, info, service, default);
                if (!await Verify())
                    throw new InvalidOperationException("The verified unsigned candidate must select the explicitly authorized unsigned branch.");
                Console.WriteLine("PASS AuthorizedFuture" + (setup ? "Setup" : "Portable") + text);
                httpFixture.Authorize = false;
                await Reject(Verify, "UnsignedFutureRequiresExplicitAuthorization");
                httpFixture.Authorize = true;
                httpFixture.FalseCurrent = true;
                await Reject(Verify, "UnofficialCurrentCannotAuthorizeFuture");
                httpFixture.FalseCurrent = false;
                httpFixture.WrongSidecar = true;
                await Reject(Verify, "FutureRequiresCorrectNamedSidecar");
                httpFixture.WrongSidecar = false;
                httpFixture.FalseManifestHash = true;
                await Reject(Verify, "FutureRequiresAuthorizedBytes");
                httpFixture.FalseManifestHash = false;
                await Reject(() => ReleasePublisherVerifier.VerifyDownloadAsync(current, candidatePath, candidate,
                    httpFixture.Offer(new(2, 0, 1), setup), service, default), "WithdrawnVersionRefusedAtDispatch");
            }
            // The production reader observes Windows PE metadata from disk. No simulated executable is launched.
            if (text == "2.4.0") await CheckTransaction(current, version, httpFixture, service);
        }
    }

    private static string Digest(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
    private static string Package(string version, bool setup) => setup ? $"UpdateCenter-Setup-v{version}.exe" : $"UpdateCenter-v{version}-Portable.exe";
    private static void StageFixture(string fixtures, SemanticVersion version, string kind, string destination)
    {
        var name = $"UpdateCenter.FutureFixture.{kind}";
        var source = Path.Combine(fixtures, version.ToString(), name + ".dll");
        CheckWindowsVersion(source, version);
        using var file = File.OpenRead(source);
        using var pe = new PEReader(file);
        if (!pe.PEHeaders.IsDll || !pe.HasMetadata || pe.PEHeaders.CorHeader?.EntryPointTokenOrRelativeVirtualAddress != 0)
            throw new InvalidDataException("Expected a managed fixture DLL without an entry point.");
        var metadata = pe.GetMetadataReader();
        if (!metadata.IsAssembly || metadata.GetString(metadata.GetAssemblyDefinition().Name) != name ||
            metadata.MethodDefinitions.Count != 0 || metadata.ManifestResources.Count != 1)
            throw new InvalidDataException("Expected the SDK resource fixture without executable methods.");
        // Use the production package filename in staging and retain the compiler-produced bytes.
        File.Copy(source, destination);
        if (Digest(source) != Digest(destination)) throw new InvalidDataException("Fixture staging changed the bytes.");
    }

    private static void CheckWindowsVersion(string path, SemanticVersion version)
    {
        var info = FileVersionInfo.GetVersionInfo(path);
        var expected = new Version(version.Major, version.Minor, version.Patch, 0);
        var file = new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
        var product = new Version(info.ProductMajorPart, info.ProductMinorPart, info.ProductBuildPart, info.ProductPrivatePart);
        if (file != expected || product != expected || info.FileVersion?.Trim() != expected.ToString() ||
            info.ProductVersion?.Trim() != version.ToString())
            throw new InvalidDataException($"Unexpected fixed or display PE version for {Path.GetFileName(path)}.");
    }

    private static async Task Reject(Func<Task> action, string scenario)
    {
        try { await action(); }
        catch (Exception e) when (e is InvalidDataException or UnauthorizedAccessException)
        { Console.WriteLine("PASS " + scenario); return; }
        throw new InvalidOperationException(scenario);
    }

    private sealed class Fixture(string version, string currentHash, long currentSize, string portablePath, string setupPath) : HttpMessageHandler
    {
        internal bool Authorize = true, FalseCurrent, WrongSidecar, FalseManifestHash;
        private const string Root = "https://github.com/Illidan0S/UpdateCenter/releases/";
        private readonly string _portable = Digest(portablePath), _setup = Digest(setupPath);
        internal AppUpdateInfo Offer(SemanticVersion offered, bool setup)
        {
            var name = Package(offered.ToString(), setup);
            var url = Root + $"download/v{offered}/{name}";
            return new AppUpdateInfo { InstalledVersion = new(2, 0, 0), AvailableVersion = offered, AssetName = name,
                DownloadUri = new(url), Sha256Uri = new(url + ".sha256"), ReleasePageUri = new(Root + "tag/v" + offered),
                ApiSha256 = setup ? _setup : _portable, DownloadSize = new FileInfo(setup ? setupPath : portablePath).Length,
                ReleaseNotes = "", IsInstallerPackage = setup };
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var url = request.RequestUri!.AbsoluteUri;
            var previous = url.Contains("/v2.0.0", StringComparison.Ordinal);
            var v = previous ? "2.0.0" : version;
            var tag = "v" + v;
            var marker = $"UpdateCenter-{tag}-update.json";
            var portable = previous ? (FalseCurrent ? new string('0', 64) : currentHash) : _portable;
            var setup = previous ? new string('A',64) : _setup;
            var markerData = JsonSerializer.SerializeToUtf8Bytes(new { schema = 1, repository = "Illidan0S/UpdateCenter", tag,
                portableSha256 = FalseManifestHash ? new string('0',64) : portable, setupSha256 = setup });
            var names = new Dictionary<string,(string Hash,long Size)>();
            names[Package(v,false)] = (portable, previous ? currentSize : new FileInfo(portablePath).Length);
            names[Package(v,true)] = (setup, previous ? 1 : new FileInfo(setupPath).Length);
            foreach (var n in names.Keys.ToArray()) names[n+".sha256"] = (new string('B',64),100);
            if (Authorize && !previous) names[marker] = (Convert.ToHexString(SHA256.HashData(markerData)),markerData.Length);
            var prefix = Root + "download/" + tag + "/";
            HttpContent content;
            if (url == "https://api.github.com/repos/Illidan0S/UpdateCenter/releases/tags/" + tag)
                content = new StringContent(JsonSerializer.Serialize(new { tag_name = tag, draft = false, prerelease = false,
                    html_url = Root + "tag/" + tag, assets = names.Select(x => new { name=x.Key, state="uploaded",
                    digest="sha256:" + x.Value.Hash, size=x.Value.Size, browser_download_url=prefix+x.Key }) }));
            else if (url == prefix + marker) content = new ByteArrayContent(markerData);
            else if (url.StartsWith(prefix,StringComparison.Ordinal) && url.EndsWith(".sha256",StringComparison.Ordinal))
            {
                var n = url.Substring(prefix.Length, url.Length-prefix.Length-7);
                content = new StringContent(names[n].Hash + "  " + (WrongSidecar && !previous ? "foreign.exe" : n) + "\r\n");
            }
            else throw new InvalidOperationException("Unexpected fixture URL " + url);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private static async Task CheckTransaction(string current, SemanticVersion version, Fixture fixture, AppUpdateService service)
    {
        var session = Guid.NewGuid();
        var target = Path.Combine(Directory.CreateDirectory(Path.Combine(AppPaths.DataDirectory, "previous")).FullName, "UpdateCenter.exe");
        var helper = PortableSourceUpdate.HelperPath(session);
        var candidate = Path.Combine(AppPaths.UpdatesDirectory, Package(version.ToString(), false));
        var backup = target + ".update-backup";
        var expected = Digest(candidate);
        var original = Digest(current);
        File.Copy(current, target);
        File.WriteAllText(helper, "altered helper");
        int handedOff = 0, confirmed = 0, relaunched = 0;
        bool fail = true;
        Task Transition(string destination, SemanticVersion selected) => PortableSourceUpdate.VerifyAndApplyAsync(
            destination, selected, session, service, () => { handedOff++; return Task.CompletedTask; },
            (path, saved, downloaded, versionArg) =>
            {
                if (path != target || saved != backup || downloaded != candidate || versionArg != version.ToString() ||
                    Digest(path) != expected || Digest(saved) != original)
                    throw new InvalidDataException("Transaction inputs do not match the verified files.");
                confirmed++;
                if (fail) throw new TimeoutException("Simulated window failed to render.");
            }, default, restored => { if (restored == target && Digest(target) == original) relaunched++; });

        await Reject(() => Transition(target, version), "AlteredSourceHelperRejected");
        await Reject(() => Transition(candidate, version), "SourceUpdateRejectsStagingTarget");
        await Reject(() => Transition(Path.Combine(Path.GetDirectoryName(target)!, "foreign.exe"), version),
            "SourceUpdateRejectsUnrelatedDestination");
        await Reject(() => Transition(target, new(2,0,1)), "WithdrawnVersionRefusedAtCommit");
        await Reject(() => Transition(target, new(2,0,0)), "EqualVersionRefusedAtCommit");
        File.Copy(current, helper, true);
        var heldOriginal = target + ".verified-fixture";
        File.Move(target, heldOriginal);
        File.WriteAllText(target, "unrelated original");
        await Reject(() => Transition(target, version), "DifferentInstalledIdentityRejected");
        File.Delete(target);
        File.Move(heldOriginal, target);
        fixture.Authorize = false;
        await Reject(() => Transition(target, version), "WithdrawalBeforeCommitRetainsCurrent");
        fixture.Authorize = true;
        fixture.FalseManifestHash = true;
        await Reject(() => Transition(target, version), "ChangedAuthorizationBeforeCommitRetainsCurrent");
        fixture.FalseManifestHash = false;
        if (handedOff != 0 || confirmed != 0 || relaunched != 0 || File.Exists(backup) || Digest(target) != original)
            throw new InvalidOperationException("An unauthenticated transaction affected the current copy.");

        try { await Transition(target, version); throw new InvalidOperationException("Unconfirmed startup accepted."); }
        catch (TimeoutException) { }
        if (handedOff != 1 || confirmed != 1 || relaunched != 1 || File.Exists(backup) || Digest(target) != original)
            throw new InvalidOperationException("The original was not restored and confirmed for relaunch.");
        Console.WriteLine("PASS SourceUpdateRestoresAndRelaunchesVerifiedOriginal");
        File.WriteAllText(backup, "existing backup");
        AppUpdateService.RestartVerifiedOriginal(target,Convert.FromHexString(original), _ => relaunched++);
        File.Delete(backup);
        AppUpdateService.RestartVerifiedOriginal(target,new byte[32], _ => relaunched++);
        if (relaunched != 1) throw new InvalidOperationException("Backup or mismatched identity authorized a relaunch.");
        fail = false;
        await Transition(target, version);
        if (handedOff != 2 || confirmed != 2 || relaunched != 1 || File.Exists(backup) || Digest(target) != expected)
            throw new InvalidOperationException("The explicitly selected and acknowledged package was not committed.");
        Console.WriteLine("PASS SourceUpdateCommitsSelectedFutureAfterWindowAcknowledgement");
        File.Delete(helper);
        File.Delete(target);
    }
}
