using System.Collections;
using System.ComponentModel;
using System.Windows.Data;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

internal static class ReleaseAuditTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static async Task RunAsync()
    {
        var item = new PlanItem { Id = "CrystalDewWorld.CrystalDiskMark", Name = "CrystalDiskMark",
            Kind = "Software", InstalledVersion = "9.0.1", AvailableVersion = "9.0.3" };
        UpdateVerificationResult Verify(params string[] versions) => WinGetService.VerifyInstallation(item,
            (_, _) => (new ProcessResult(0, "", "", "fake list"), versions.Select(version =>
                new WinGetPackageRow(item.Name, item.Id, version, "", "winget")).ToList()),
            maxAttempts: 1, waitBeforeRetry: _ => { });
        foreach (var versions in new[] { new[] { "9.0.1", "9.0.3" }, new[] { "9.0.3", "9.0.1" } })
        {
            var ambiguous = Verify(versions);
            Check(!ambiguous.Verified && !ambiguous.IsDefinitive &&
                ambiguous.Status == UpdateVerificationStatuses.Unavailable, "Duplicate order affects verification.");
        }
        Check(Verify("9.0.3", "9.0.3").Verified, "Identical duplicate rows prevent verification.");
        Check(Verify("9.0.1").IsDefinitive && !Verify("9.0.1").Verified, "Old version passes verification.");
        Check(!Verify("Unknown").IsDefinitive && !Verify().IsDefinitive, "Unparseable output becomes a false failure.");
        item.AvailableVersion = "Unknown";
        Check(!Verify("9.0.1").Verified, "Unknown target passes verification.");
        item.AvailableVersion = "9.0.3";
        var attempt = 0;
        var delayed = WinGetService.VerifyInstallation(item, (_, _) =>
            (new ProcessResult(0, "", "", "fake list"),
                new List<WinGetPackageRow> { new(item.Name, item.Id, ++attempt == 1 ? "9.0.1" : "9.0.3", "", "winget") }),
            waitBeforeRetry: _ => { });
        Check(delayed.Verified && attempt == 2, "Delayed installed version is not retried.");
        var exitZeroOld = ElevatedUpdateRunner.EvaluateInteractiveResultForTest(item,
            new ProcessResult(0, "Installer finished", "", "fake installer"), _ => Verify("9.0.1"));
        Check(!exitZeroOld.Success && exitZeroOld.InstallerSucceeded, "Exit zero overrides a definitive old version.");
        var errorButInstalled = ElevatedUpdateRunner.EvaluateInteractiveResultForTest(item,
            new ProcessResult(1, "", "error", "fake installer"), _ => Verify("9.0.3"));
        Check(errorButInstalled.Success && errorButInstalled.Verified, "Verified target is reported as failure.");

        var selected = new UpdateItem { Id = item.Id, Kind = UpdateKind.Software, Name = item.Name };
        var status = new UpdateRunStatus { State = "Failed", Results = [errorButInstalled] };
        UpdateCoordinator.BuildControlledBatchFailure([selected], status, "status channel failed after result");
        Check(errorButInstalled.Success && errorButInstalled.Verified, "Status failure overwrites published success.");
        var invoked = false;
        UpdateCoordinator.ReportProgressSafely(_ => { invoked = true; throw new InvalidOperationException("Refresh during EditItem"); }, status);
        Check(invoked && status.Results.Single().Success, "UI exception changes technical result.");
        var view = new ListCollectionView(new ArrayList { new EditableSmokeItem() });
        var editable = (IEditableCollectionView)view;
        editable.EditItem(view.GetItemAt(0));
        Check(!MainViewModel.TryRefreshCollectionView(view, "audit-edit"), "Refresh during EditItem.");
        editable.CancelEdit();
        ((IEditableCollectionViewAddNewItem)view).AddNewItem(new object());
        Check(!MainViewModel.TryRefreshCollectionView(view, "audit-add"), "Refresh during AddNew.");
        editable.CancelNew();
        Check(MainViewModel.TryRefreshCollectionView(view, "audit-clean"), "Refresh fails after transaction ends.");

        var unverified = new ItemRunResult { Success = true, Verified = false,
            ExecutionState = ItemExecutionState.Terminal, TerminalDisposition = ItemTerminalDisposition.Succeeded };
        Check(MainViewModel.GetHistoryResultLabel(unverified) != "Riuscito" &&
            !MainViewModel.ShouldRemoveCompletedUpdate(unverified), "Unverified installation appears fully successful.");

        const string resource = @"C:\ProgramData\obs-studio-hook\obs.dll";
        const string exe = @"C:\Program Files (x86)\ASUS\ArmouryDevice\asus_framework.exe";
        var context = new WinGetRecoveryContext("OBSProject.OBSStudio", [], [], [], [resource], [resource], []);
        var obs = new UpdateItem { Id = context.PackageId, Name = "OBS", Kind = UpdateKind.Software };
        var blocker = new RestartManagerBlocker(16144, "ASUS NodeJS Web Framework", "",
            RestartManagerApplicationType.MainWindow, 0, false, RestartManagerRebootReason.None,
            exe, [resource], Liveness: RestartManagerProcessLiveness.Live);
        RestartManagerQueryResult Query(params RestartManagerBlocker[] blockers) =>
            new(true, true, [resource], blockers, RestartManagerRebootReason.None, 0, "fake RM");
        ItemRunResult Failure() => new() { Id = obs.Id, Name = obs.Name, Kind = "Software",
            Success = false, Outcome = UpdateOutcomes.Failed, FailureReason = UpdateFailureReasons.FilesInUse };
        var operations = new FakeWinGetProcessOperations(context, [], []);
        var recovery = new WinGetProcessRecoveryService(operations,
            new FakeRestartManagerService(Query(blocker), Query(blocker),
                Query(blocker with { ProcessId = 28036 }), Query(blocker with { ProcessId = 28036 }),
                Query(blocker with { ProcessId = 3900 }), Query(blocker with { ProcessId = 3900 })),
            pollingDelay: new FakeRecoveryPollingDelay());
        var failure = Failure();
        var preparation = recovery.PrepareRetry(obs, failure, new FakeWinGetRecoveryPrompt(true, true));
        Check(!preparation.ShouldRetry && operations.KillCalls == 1 && operations.CloseCalls == 1 &&
            failure.Message.Contains("ASUS NodeJS Web Framework") && failure.Message.Contains(exe),
            "Recurring external blocker retries, loops termination, or loses its identity in the final message.");
        var alternating = Enumerable.Range(0, 8).Select(i => i % 2 == 0 ? Query(blocker) : Query()).ToArray();
        var unstable = new WinGetProcessRecoveryService(new FakeWinGetProcessOperations(context, [], []),
            new FakeRestartManagerService(alternating), pollingDelay: new FakeRecoveryPollingDelay());
        Check(!unstable.PrepareRetry(obs, Failure(), new FakeWinGetRecoveryPrompt(true, true)).ShouldRetry,
            "A single clean query at the polling limit authorizes retry.");
        var clean = new WinGetProcessRecoveryService(new FakeWinGetProcessOperations(context, [], []),
            new FakeRestartManagerService(Query(), Query()), pollingDelay: new FakeRecoveryPollingDelay());
        var retryCalls = 0;
        var retried = await WinGetSingleRetryPolicy.ExecuteAsync(obs, Failure(),
            () => clean.PrepareRetry(obs, Failure(), new FakeWinGetRecoveryPrompt(true, true)),
            () => { retryCalls++; return Task.FromResult(Failure()); });
        Check(retryCalls == 1 && !retried.Success, "Installer retry is not bounded to one attempt.");
        LocalizationService.Initialize("en");
        Check(LocalizationService.Translate("Completato · da verificare") == "Completed · verify",
            "Missing English unverified status.");
        Check(LocalizationService.Translate("Da verificare") == "Needs verification",
            "Missing English Da verificare status.");
        Check(LocalizationService.Translate("Driver e chipset") == "Drivers and chipset", "EN Driver e chipset");
        Check(LocalizationService.Translate("Diagnosi driver problematici") == "Problem driver diagnostics", "EN Diagnosi driver problematici");
        Check(LocalizationService.Translate("Dispositivo") == "Device", "EN Dispositivo");
        Check(LocalizationService.Translate("Problema") == "Problem", "EN Problema");
        Check(LocalizationService.Translate("Riparazione") == "Repair", "EN Riparazione");
        Check(LocalizationService.Translate("Azione consigliata") == "Recommended action", "EN Azione consigliata");
        Check(LocalizationService.Translate("Controlli manuali e supporto ufficiale") == "Manual checks and official support", "EN Controlli manuali e supporto ufficiale");
        Check(LocalizationService.Translate("Fonte ufficiale") == "Official source", "EN Fonte ufficiale");
        Check(LocalizationService.Translate("Inventario driver installati") == "Installed driver inventory", "EN Inventario driver installati");
        Check(LocalizationService.Translate("Cerca nei dispositivi") == "Search devices", "EN Cerca nei dispositivi");
        Check(LocalizationService.Translate("Tutti") == "All", "EN Tutti");
        Check(LocalizationService.Translate("Aggiornato") == "Updated", "EN Aggiornato");
        Check(LocalizationService.Translate("Configura questo PC · Update Center") == "Configure this PC · Update Center", "EN Configura questo PC");
        Check(LocalizationService.Translate("Controlla il collegamento e scegli chi può gestire gli aggiornamenti") == "Check connection and choose who can manage updates", "EN Controlla il collegamento");
        Check(LocalizationService.Translate("Componente di rete non raggiungibile: tempo di attesa scaduto") == "Network component unreachable: timed out", "EN Componente di rete non raggiungibile");
        Check(LocalizationService.Translate("Salute dello storage") == "Storage health", "EN Salute dello storage");
        Check(LocalizationService.Translate("Caratteristiche hardware") == "Hardware specifications", "EN Caratteristiche hardware");
        Check(LocalizationService.Translate("SCHEDE VIDEO RILEVATE") == "DETECTED GRAPHICS CARDS", "EN SCHEDE VIDEO RILEVATE");
        Check(LocalizationService.Translate("Sano") == "Healthy", "EN Sano");
        Check(LocalizationService.Translate("Dimensione") == "Size", "EN Dimensione");
        Check(LocalizationService.Translate("Conferma rischio") == "Risk confirmation", "EN Conferma rischio");
        Check(LocalizationService.Translate("Disattivata") == "Disabled", "EN Disattivata");
        Check(LocalizationService.Translate("Ogni giorno") == "Daily", "EN Ogni giorno");
        Check(LocalizationService.Translate("Ogni settimana") == "Weekly", "EN Ogni settimana");

        var driverProblem = new DriverProblemItem();
        Check(driverProblem.ErrorTitle == "Issue detected" && driverProblem.SuggestedAction == "Open Device Manager to check the device.", "DriverProblemItem localization");
        var storageDevice = new StorageDeviceItem { HealthStatus = "Healthy" };
        Check(storageDevice.HealthLabel == "Healthy", "StorageDeviceItem HealthLabel in EN");
        var storageRowEn = StorageTableRowFactory.CreateRows([storageDevice]).Single();
        Check(storageRowEn.KindLabel == "Physical drive", "StorageTableRow KindLabel in EN");

        var sampleDetail = new HistoryEntry { Details = "App è stato aggiornato e verificato da 1.0 a 2.0 usando winget. Non è richiesto alcun riavvio. Dettaglio tecnico: Nessun dettaglio tecnico aggiuntivo." };
        Check(sampleDetail.DisplayDetails.Contains("was updated and verified from 1.0 to 2.0 using winget. No restart is required. Technical details: No additional technical details."),
            "History details remain untranslated in English.");
        Check(new HistoryEntry { Result = "Riuscito" }.DisplayResult == "Succeeded" &&
            new HistoryEntry { Result = "Fallito" }.DisplayResult == "Failed", "History labels remain Italian.");

        LocalizationService.Initialize("it");
        Check(LocalizationService.Translate("Drivers and chipset") == "Driver e chipset", "IT Driver e chipset");
        Check(LocalizationService.Translate("Problem driver diagnostics") == "Diagnosi driver problematici", "IT Diagnosi driver problematici");
        Check(storageDevice.HealthLabel == "Sano", "StorageDeviceItem HealthLabel in IT");
        var storageRowIt = StorageTableRowFactory.CreateRows([storageDevice]).Single();
        var staThread = new Thread(() =>
        {
            var testGrid = new System.Windows.Controls.Grid();
            testGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
            testGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition());
            var textBlock = new System.Windows.Controls.TextBlock { Text = "Aggiornamenti" };
            testGrid.Children.Add(textBlock);
            LocalizationService.Initialize("en");
            LocalizationService.ApplyTo(testGrid);
            Check(textBlock.Text == "Updates", "Grid with ColumnDefinition localization failed: " + textBlock.Text);
            LocalizationService.Initialize("it");
            LocalizationService.ApplyTo(testGrid);
            Check(textBlock.Text == "Aggiornamenti", "Static UI did not round-trip back to Italian: " + textBlock.Text);

            var combo = new System.Windows.Controls.ComboBox();
            combo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "Disattivata", Tag = "Off" });
            combo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "Ogni giorno", Tag = "Daily" });
            testGrid.Children.Add(combo);
            LocalizationService.Initialize("en");
            LocalizationService.ApplyTo(combo);
            Check(((System.Windows.Controls.ComboBoxItem)combo.Items[0]).Content?.ToString() == "Disabled" &&
                  ((System.Windows.Controls.ComboBoxItem)combo.Items[1]).Content?.ToString() == "Daily",
                "Static ComboBox items were not localized.");
            LocalizationService.Initialize("it");
            LocalizationService.ApplyTo(combo);
            Check(((System.Windows.Controls.ComboBoxItem)combo.Items[0]).Content?.ToString() == "Disattivata" &&
                  ((System.Windows.Controls.ComboBoxItem)combo.Items[1]).Content?.ToString() == "Ogni giorno",
                "Static ComboBox items did not return to Italian.");

            LocalizationService.Initialize("en");
            var dynamicEnglish = LocalizationService.Text("23 aggiornamenti disponibili", "23 updates available");
            Check(dynamicEnglish == "23 updates available", "Dynamic EN pair failed.");
            LocalizationService.Initialize("it");
            Check(LocalizationService.Translate(dynamicEnglish) == "23 aggiornamenti disponibili",
                "Dynamic localized state did not round-trip back to Italian.");
        });
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();
        staThread.Join();

        Console.WriteLine("PASS release-audit: verification, duplicate versions, UI transactions, result preservation, recurring blockers, bounded retry, EN labels.");
    }
}
