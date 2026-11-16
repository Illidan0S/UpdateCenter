using System.Text.Json;
using UpdateCenter.Models;
using UpdateCenter.Services;

internal static class R2EngineTests
{
    private const string Sid = "S-1-5-21-111-222-333-1001";
    internal static void Run()
    {
        Check(AppPaths.DataDirectory != AppPaths.ResolveDefaultRoot(), "Production root selected");
        Action[] tests = [BoundNativeElevation, UnboundElevationStaysClosed, BoundCandidateWins,
            AmbiguousRecipeStaysClosed, UnsupportedTechnologyStaysClosed, BindingDrift,
            ConsentMutation, UacOutcomes, RequirementsMatrix, DependenciesRejected, InstallerFirstDependencyRejected,
            BoundPrivilegeCannotBeStripped, ExternalFailureNoRetry, RunnerConsentAndCancellation, LocalizedMessages];
        var failed = 0;
        foreach (var test in tests)
            try { test(); Console.WriteLine("PASS R2 " + test.Method.Name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL R2 " + test.Method.Name + ": " + ex.Message); }
        Console.WriteLine($"R2 engine: {tests.Length-failed} PASS, {failed} FAIL; simulated processes/UAC only.");
        if (failed != 0) throw new InvalidOperationException("R2 engine failures");
    }
    private static UpdateItem Item() => new() { Id = "Fixture.Tool", Name = "Fixture", Kind = UpdateKind.Software,
        Source = "winget", InstalledVersion = "1.0", AvailableVersion = "2.0" };
    private static string Manifest(string elevation = "elevationRequired", string type = "exe", string scope = "machine") =>
        $"PackageIdentifier: Fixture.Tool\nPackageVersion: 2.0\nInstallers:\n- Architecture: x64\n  Scope: {scope}\n  InstallerType: {type}\n  ElevationRequirement: {elevation}\n  UpgradeBehavior: install\n  ProductCode: FixtureKey\n  InstallerUrl: https://example.org/setup.exe\n  InstallerSha256: {new string('A',64)}\n";
    private static string Identity(string scope = "machine") => $"Fixture [Fixture.Tool]\nVersion: 1.0\nLocal Identifier: ARP\\{(scope == "machine" ? "Machine" : "User")}\\X64\\FixtureKey\nInstalled Scope: {scope}\nInstalled Architecture: x64\n";
    private static WinGetScopeGuard Guard(string manifest, Func<IReadOnlyList<string>, ProcessResult>? effect = null,
        string sid = Sid, string scope = "machine", bool admin = false) => new(
        (args, _, _) => Task.FromResult(args[0] == "--version" ? new ProcessResult(0, "v1.29.290", "") :
            args[0] == "list" ? new ProcessResult(0, Identity(scope), "") : effect?.Invoke(args) ?? new ProcessResult(0,"", "")),
        (_, _, _) => Task.FromResult<string?>(manifest), () => sid, (_, _) => scope, "x64",
        isAdministrator: () => admin, windowsVersion: new Version(10,0,17763,0));
    private static PlanItem Bound(string manifest)
    {
        var item = Item(); item.WinGetBinding = Guard(manifest).PrepareAsync(item, manifest, default).GetAwaiter().GetResult();
        Check(item.WinGetBinding is not null, "Fixture binding unavailable");
        return ElevatedUpdateRunner.ToPlanItem(item);
    }
    private static void BoundNativeElevation()
    {
        foreach (var type in new[] { "exe", "inno", "nullsoft", "burn" })
        foreach (var interactive in new[] { false, true })
        {
            var manifest = Manifest(type: type); var plan = Bound(manifest); var calls = 0;
            WinGetCommand dispatch = (args, _, _) => { calls++;
                Check(args.Contains("--scope") && args.Contains("machine") && args.Contains("--installer-type") && args.Contains(type)
                    && args.Contains("--architecture") && args.Contains("x64") && args.Contains("2.0") && args.Contains("winget"), "Approved recipe constraints missing");
                Check(!args.Contains("--force") && !args.Contains("--uninstall-previous"), "Unsafe override");
                return Task.FromResult(new ProcessResult(0, "simulated UAC accepted and installer completed", "")); };
            var result = interactive ? WinGetService.RunInteractive(plan, dispatch, Guard(manifest)) : WinGetService.Upgrade(plan,true,dispatch,Guard(manifest));
            Check(result.Success && calls == 1, "Unique bound native elevation did not dispatch exactly once");
        }
    }
    private static void UnboundElevationStaysClosed()
    {
        var manifest = Manifest(); var plan = ElevatedUpdateRunner.ToPlanItem(Item()); var calls = 0;
        var result = WinGetService.Upgrade(plan,true,(_,_,_)=>{calls++; return Task.FromResult(new ProcessResult(0,"", ""));},Guard(manifest));
        Check(result.PreflightRejected && calls == 0, "Stripped binding authorized elevation");
    }
    private static void BoundCandidateWins()
    {
        var manifest = Manifest("elevationProhibited") + "- Architecture: x64\n  Scope: user\n  InstallerType: exe\n  ElevationRequirement: elevationRequired\n  InstallerUrl: https://example.org/user.exe\n  InstallerSha256: " + new string('B',64) + "\n";
        var plan = Bound(manifest);
        Check(Guard(manifest).ValidateAsync(plan).GetAwaiter().GetResult() is null, "Unselected elevation candidate blocked bound recipe");
    }
    private static void AmbiguousRecipeStaysClosed()
    {
        var single = Manifest(); var manifest = single + single[(single.IndexOf("- Architecture:",StringComparison.Ordinal))..];
        var binding = Guard(manifest).PrepareAsync(Item(),manifest,default).GetAwaiter().GetResult();
        if (binding is null) return;
        var item=Item(); item.WinGetBinding=binding;
        Check(Guard(manifest).ValidateAsync(ElevatedUpdateRunner.ToPlanItem(item)).GetAwaiter().GetResult() is not null, "Multiple candidate recipes authorized native elevation");
    }
    private static void UnsupportedTechnologyStaysClosed()
    {
        foreach (var type in new[] { "msix", "appx", "msi", "wix" })
        {
            var manifest = Manifest(type:type); var item=Item(); item.WinGetBinding=Guard(manifest).PrepareAsync(item,manifest,default).GetAwaiter().GetResult();
            Check(Guard(manifest).ValidateAsync(ElevatedUpdateRunner.ToPlanItem(item)).GetAwaiter().GetResult() is not null, "Unreviewed elevation technology allowed: " + type);
        }
    }
    private static void BindingDrift()
    {
        var manifest=Manifest(); var plan=Bound(manifest);
        foreach (var guard in new[] { Guard(manifest.Replace("setup.exe","other.exe")), Guard(manifest,sid:"S-1-5-21-111-222-333-1002"), Guard(manifest,scope:"user") })
            Check(guard.ValidateAsync(plan).GetAwaiter().GetResult() is not null,"Manifest/account/scope drift allowed");
    }
    private static RunnerPlanSnapshot Snapshot(PlanItem item) => RunnerPlanSnapshot.Create(new UpdatePlan { Items=[item], SilentSoftwareInstall=true }, false);
    private static void ConsentMutation()
    {
        var plan=Bound(Manifest()); var snapshot=Snapshot(plan); var gate=new UnifiedConsentGate([snapshot]);
        Check(gate.Confirm(_=>true,()=>{},()=>true),"Consent failed");
        plan.AvailableVersion="3.0";
        var rejected=false; try { gate.Consume(0,Snapshot(plan)); } catch(InvalidOperationException) { rejected=true; }
        Check(rejected,"Changed plan consumed consent");
        plan=Bound(Manifest()); snapshot=Snapshot(plan); gate=new UnifiedConsentGate([snapshot]); gate.Confirm(_=>true,()=>{},()=>true);
        var json=JsonSerializer.Serialize(plan).Replace("elevationRequired","elevatesSelf");
        var changed=JsonSerializer.Deserialize<PlanItem>(json)!;
        rejected=false; try { gate.Consume(0,Snapshot(changed)); } catch(InvalidOperationException) { rejected=true; }
        Check(rejected,"Elevation mode absent from consent or mutation accepted");
    }
    private static void UacOutcomes()
    {
        var plan=Bound(Manifest());
        foreach(var code in new[] { unchecked((int)0x800704C7), 1223 })
        {
            var queries=0;
            var result=ElevatedUpdateRunner.EvaluateInteractiveResultForTest(plan,new(code,"UAC cancelled", ""),_=>{queries++; return new(){Verified=true,Status="Verified",IsDefinitive=true};});
            Check(result.Outcome==UpdateOutcomes.Cancelled && !result.Success && !result.Verified && queries==0,"UAC denial was verified/promoted or classified as failure");
        }
        var accepted=ElevatedUpdateRunner.EvaluateInteractiveResultForTest(plan,new(0,"", ""),_=>new(){Verified=true,IsDefinitive=true,Status="Verified"});
        Check(accepted.Verified,"Accepted UAC+positive evidence not verified");
        var failed=ElevatedUpdateRunner.EvaluateInteractiveResultForTest(plan,new(1,"", ""),_=>new(){IsDefinitive=true,Status="Failed"});
        Check(!failed.Success && failed.Outcome=="Failed","Failure became success");
    }
    private static void RequirementsMatrix()
    {
        foreach(var elevation in new[] { "elevationProhibited", "elevatesSelf", "" })
        {
            var manifest=elevation.Length==0 ? Manifest().Replace("  ElevationRequirement: elevationRequired\n", "") : Manifest(elevation);
            var parsed=WinGetInstallerMetadata.Parse(manifest);
            var result=WinGetScopeGuard.AssessRequirements(parsed,"Fixture.Tool","2.0","x64","machine");
            Check(result.CanContinue,"Existing non-required elevation lost support");
            if(elevation.Length==0) Check(result.State==WinGetRequirementState.Unknown,"Missing privilege evidence claimed certainty");
        }
        var prohibited=WinGetScopeGuard.AssessRequirements(WinGetInstallerMetadata.Parse(Manifest("elevationProhibited")),"Fixture.Tool","2.0","x64","machine",isAdministrator:true);
        Check(!prohibited.CanContinue,"Elevated context ignored prohibition");
    }
    private static void DependenciesRejected()
    {
        foreach (var declaration in new[] { "Dependencies: {}\n", "Dependencies:\n  PackageDependencies:\n  - PackageIdentifier: Other.Tool\n" })
        {
            var manifest=Manifest().Replace("Installers:",declaration+"Installers:");
            var plan=Bound(manifest);
            Check(Guard(manifest).ValidateAsync(plan).GetAwaiter().GetResult() is not null,"Unapproved dependency installer allowed");
        }
    }
    private static void InstallerFirstDependencyRejected()
    {
        var manifest=Manifest().Replace("- Architecture: x64", "- Dependencies: { PackageDependencies: [{ PackageIdentifier: Other.Tool }] }\n  Architecture: x64");
        var plan=Bound(manifest);
        Check(Guard(manifest).ValidateAsync(plan).GetAwaiter().GetResult() is not null,
            "Installer-first dependency authorized an extra unapproved installer");
    }
    private static void BoundPrivilegeCannotBeStripped()
    {
        var manifest=Manifest(); var plan=Bound(manifest);
        plan.WinGetBinding=plan.WinGetBinding! with { ElevationRequirement="" };
        Check(Guard(manifest).ValidateAsync(plan).GetAwaiter().GetResult() is not null,"Stripped privilege field allowed");
        plan=Bound(manifest); plan.WinGetBinding=plan.WinGetBinding! with { InstallerType="inno" };
        Check(Guard(manifest).ValidateAsync(plan).GetAwaiter().GetResult() is not null,"Installer type substitution allowed");
        plan=Bound(manifest); plan.Source="other";
        Check(Guard(manifest).ValidateAsync(plan).GetAwaiter().GetResult() is not null,"Source substitution allowed");
    }
    private static void ExternalFailureNoRetry()
    {
        foreach(var code in new[] { unchecked((int)0x800704C7), unchecked((int)0x8A150006), unchecked((int)0x800702E4) })
        {
            var manifest=Manifest(); var plan=Bound(manifest); var calls=0;
            var result=WinGetService.Upgrade(plan,true,(_,_,_)=>{calls++; return Task.FromResult(new ProcessResult(code,"", ""));},Guard(manifest));
            Check(result.ExitCode==code && calls==1,"Denied/missing elevation retried");
        }
        var m=Manifest(); var p=Bound(m); var timedCalls=0;
        var threw=false;
        try { WinGetService.Upgrade(p,true,(_,_,_)=>{timedCalls++; throw new TimeoutException("simulated timeout");},Guard(m)); }
        catch(TimeoutException) { threw=true; }
        Check(threw && timedCalls==1,"Timeout was retried or hidden");
        using var cts=new CancellationTokenSource(); cts.Cancel(); var cancelledCalls=0;
        threw=false;
        try { WinGetService.Upgrade(p,true,(_,_,_)=>{cancelledCalls++; return Task.FromResult(new ProcessResult(0,"", ""));},Guard(m),cts.Token); }
        catch(OperationCanceledException) { threw=true; }
        Check(threw && cancelledCalls==0,"Pre-cancelled operation dispatched");
    }
    private static void RunnerConsentAndCancellation()
    {
        foreach(var approve in new[] { false,true })
        foreach(var code in new[] { 0, unchecked((int)0x800704C7), unchecked((int)0x8A150006) })
        {
            var manifest=Manifest(); var first=Bound(manifest); var second=ElevatedUpdateRunner.ToPlanItem(Item()); second.Id="Fixture.Second";
            var id=Guid.NewGuid().ToString("N"); var path=Path.Combine(AppPaths.DataDirectory,$"update-plan-{id}.json");
            var plan=new UpdatePlan { Items=[first,second], SilentSoftwareInstall=true,
                StatusFile=Path.Combine(AppPaths.DataDirectory,$"update-status-{id}.json"),
                PauseFile=Path.Combine(AppPaths.DataDirectory,$"update-pause-{id}.signal"),PauseOwnerProcessId=Environment.ProcessId };
            JsonStorage.WriteAtomic(path,plan);
            var consent=new SimulatedConsent(approve); var effects=new SimulatedEffects(consent,manifest,code);
            ElevatedUpdateRunner.Run(path,false,consent,effects,default);
            var status=JsonStorage.Read<UpdateRunStatus>(plan.StatusFile)!;
            Check(consent.Calls==1 && status.Results.Count==2,"One summary/complete terminal ledger violated");
            if(!approve) Check(effects.Calls==0 && status.Results.All(x=>x.Outcome=="Cancelled"),"Denied consent reached installer");
            else if(code==unchecked((int)0x800704C7))
                Check(effects.Calls==1 && status.State=="Cancelled" && status.Results.All(x=>x.Outcome=="Cancelled"),"UAC denial left batch pending or launched next item");
            else Check(effects.Calls==2 && status.State=="Completed" && status.Results[0].Verified==(code==0),"Batch did not settle with real outcomes");
        }
    }
    private sealed class SimulatedConsent(bool approved) : IRunnerConsent
    {
        public int Calls { get; private set; }
        public bool Authorized { get; private set; }
        public bool Confirm(RunnerPlanSnapshot plan,CancellationToken token) { Calls++; Authorized=approved; return approved; }
    }
    private sealed class SimulatedEffects(SimulatedConsent consent,string manifest,int code) : IRunnerEffects
    {
        public int Calls { get; private set; }
        public bool IsAdministrator=>false;
        public (bool Created,string Message) CreateRestorePoint()=>throw new InvalidOperationException("Unexpected restore effect");
        public ItemRunResult InstallDriver(PlanItem item,Action<int,string> progress)=>throw new InvalidOperationException("Unexpected driver effect");
        public ItemRunResult InstallSoftware(PlanItem item,bool silent,bool interactive)
        {
            Check(consent.Authorized,"Installer preceded consent"); Calls++;
            var result=item.Id=="Fixture.Tool" ? WinGetService.Upgrade(item,silent,(_,_,_)=>Task.FromResult(new ProcessResult(code,"fixture result", "")),Guard(manifest)) : new ProcessResult(0,"", "");
            return ElevatedUpdateRunner.EvaluateInteractiveResultForTest(item,result,_=>new(){Verified=result.Success,IsDefinitive=true,Status=result.Success ? "Verified" : "Failed"});
        }
    }
    private static void LocalizedMessages()
    {
        foreach(var language in new[] { "it","en" })
        {
            LocalizationService.Initialize(language); var manifest=Manifest(); var p=Bound(manifest);
            foreach(var message in new[] { LocalizationService.WinGetNativeElevationMessage,
                LocalizationService.WinGetSelectionUnknownMessage, LocalizationService.WinGetNativeInterruptedMessage,
                LocalizationService.WinGetUacCancelledMessage })
                Check(!message.Contains('\uFFFD'), "Corrupted Italian UI text");
            var assessment=WinGetScopeGuard.AssessRequirements(WinGetInstallerMetadata.Parse(manifest),p.Id,p.AvailableVersion,"x64","machine",p.WinGetBinding);
            Check(assessment.CanContinue && assessment.Message.Contains(language=="it" ? "amministrativi" : "administrator"),"Native UAC explanation not localized");
            Check(LocalizationService.TranslateHistoryDetails(LocalizationService.WinGetNativeElevationMessage).Contains(language=="it" ? "amministrativi" : "administrator"),"History native UAC translation lost");
        }
    }
    private static void Check(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
}
