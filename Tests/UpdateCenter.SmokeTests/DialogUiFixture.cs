using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using UpdateCenter;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

internal static class DialogUiFixture
{
    public static List<UpdateItem> Items(int count=12) => Enumerable.Range(0,count).Select(i=>new UpdateItem
    {
        Id=$"QA.NoRealPackage.{i}", Name=$"QA {i:000} · Component with a deliberately long name for layout verification",
        Kind=i%3==0?UpdateKind.Driver:i%3==1?UpdateKind.Software:UpdateKind.Runtime,
        Source="QA fixture",InstalledVersion="1.2.3456.7890",AvailableVersion="2.3.4567.8901",
        IsImportant=i%3==0,RequiresRestart=i%3==0,RequiresRiskConfirmation=i%3==1,
        CanInstall=true,IsSelected=true,Status=LocalizationService.Text("In attesa","Waiting"),
        ResultDetails=LocalizationService.Text("Dati simulati: nessun installer eseguito.","Simulated data: no installer was run."),
        Diagnostics="QA only\nNo process launch. No network connection.\n"+new string('x',180)
    }).ToList();

    public static PreflightResult Preflight(bool blocked=false)
    {
        var preflight=new PreflightResult
        {
            PowerStatus=LocalizationService.Text("Alimentazione verificata (QA)","Power checked (QA)"),
            DiskStatus=LocalizationService.Text("Spazio disponibile simulato: 80 GB","Simulated free space: 80 GB")
        };
        preflight.Warnings.Add(LocalizationService.Text("Verifica simulata: salva il lavoro aperto prima di procedere. Nessun software verrà installato nell'host QA.","Simulated check: save open work before proceeding. No software will be installed by the QA host."));
        if(blocked)preflight.BlockingIssues.Add(LocalizationService.Text("Autorizzazione negata (simulazione). La conferma deve restare disabilitata.","Authorization denied (simulation). Confirmation must remain disabled."));
        return preflight;
    }

    public static UpdateRunStatus Result(string state,int count=5)
    {
        var dispositions=new[]{ItemTerminalDisposition.Succeeded,ItemTerminalDisposition.Failed,ItemTerminalDisposition.Cancelled,ItemTerminalDisposition.NotAutomaticallyApplicable,ItemTerminalDisposition.Succeeded};
        var result=new UpdateRunStatus { State="Completed",Total=count,Message=LocalizationService.Text("Sessione QA conclusa · nessuna operazione reale","QA session finished · no real operation"),RestartRequired=state=="restart",RestorePointRequested=true,RestorePointCreated=state!="mixed" };
        for(var i=0;i<count;i++)
        {
            var disposition=state=="cancelled"?ItemTerminalDisposition.Cancelled:state is "success" or "restart"?ItemTerminalDisposition.Succeeded:dispositions[i%5];
            result.Results.Add(new ItemRunResult { Id=$"QA.Result.{i}",Name=$"QA {i:000} · Software / runtime / driver component",Kind=i%3==0?"Driver":"Software",ExecutionState=ItemExecutionState.Terminal,
                TerminalDisposition=disposition,Success=disposition==ItemTerminalDisposition.Succeeded,Verified=disposition==ItemTerminalDisposition.Succeeded&&(state!="mixed"||i%5!=4),
                Outcome=disposition==ItemTerminalDisposition.Succeeded?UpdateOutcomes.Completed:UpdateOutcomes.Failed,
                Message=LocalizationService.Text("Esito simulato, nessuna modifica al sistema.","Simulated outcome, no system change."),Diagnostics=$"QA operation {i}\nExit code: simulated\n"+new string('x',180) });
        }
        return result;
    }

    public static Window Create(string kind,string state)
    {
        var items=Items();
        if(kind=="confirmation") return new UpdateConfirmationWindow(items,Preflight(state=="denied"),true,true);
        if(kind=="remote")
        {
            var remoteVm=new MainViewModel();HardwareNetworkUiFixture.Populate(remoteVm,"populated",50);
            return new UpdateConfirmationWindow(remoteVm.Network.ScanResults.Where(x=>x.CanInstall).ToList(),remoteVm.Network.Agents.ToList());
        }
        if(kind=="recovery")return new WinGetProcessRecoveryWindow(
            LocalizationService.Text("Chiusura forzata","Force close"),
            LocalizationService.Text("Il componente QA non si è chiuso normalmente. Vuoi terminarlo forzatamente? Potresti perdere dati non salvati. Questa finestra è una simulazione isolata.","The QA component did not close normally. Force it to close? You may lose unsaved data. This window is an isolated simulation."),
            "QA process A · QA process B\nPID: simulated · No real process is inspected or terminated.",
            LocalizationService.Text("Termina e riprova","Terminate and retry"));
        if(kind=="app")
        {
            var window=new AppUpdateWindow(AppUpdate(),RejectingAppService());
            window.Loaded+=(_,_)=>
            {
                Find<UIElement>(window,"ProgressPanel").Visibility=Visibility.Visible;
                Find<ProgressBar>(window,"DownloadProgress").Value=state=="error"?0:42;
                SetText(window,"DownloadStatus",state=="error"?LocalizationService.Text("Aggiornamento non applicato: errore QA simulato.","Update not applied: simulated QA error."):LocalizationService.Text("Download simulato · nessuna connessione","Simulated download · no connection"));
                Call(window,"SetBusy",state!="error");
                if(state=="error" && window.FindName("ErrorDetailsPanel") is UIElement details)
                {
                    details.Visibility=Visibility.Visible;SetText(window,"ErrorDetailsText","QA exception details\nNo network or installer was invoked.");
                    window.Dispatcher.InvokeAsync(()=>((FrameworkElement)details).BringIntoView(),System.Windows.Threading.DispatcherPriority.Loaded);
                }
            };
            return window;
        }
        var vm=new MainViewModel();var pause=new UpdatePauseController();
        typeof(MainViewModel).GetField("_activePauseController",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(vm,pause);
        Set(vm,"IsBusy",true);Set(vm,"Progress",state=="preparing"?2d:state=="verifying"?92d:state=="download"?35d:65d);
        Set(vm,"StatusText",LocalizationService.Text("Operazione simulata · nessuna installazione","Simulated operation · no installation"));
        Set(vm,"CurrentItemText",state switch { "preparing"=>LocalizationService.Text("Preparazione dei pacchetti (QA)","Preparing packages (QA)"),"download"=>LocalizationService.Text("Download in corso (QA)","Downloading (QA)"),"verifying"=>LocalizationService.Text("Verifica dell'installazione (QA)","Verifying installation (QA)"),_=>LocalizationService.Text("Installazione · elemento 2 di 12 (QA)","Installing · item 2 of 12 (QA)") });
        if(state=="paused") {Set(vm,"IsUpdatePaused",true);Set(vm,"IsUpdatePauseRequested",true);}
        // Reflection keeps this fixture compatible with the pre-milestone binary for baseline screenshots.
        var constructor=typeof(UpdateProgressWindow).GetConstructors().Single();
        var progress=(UpdateProgressWindow)constructor.Invoke(constructor.GetParameters().Length==1?[vm]:[vm,items]);
        progress.Closed+=(_,_)=>pause.Cleanup();
        progress.Loaded+=(_,_)=>
        {
            if(state is "success" or "mixed" or "cancelled" or "restart")progress.ShowCompleted(Result(state));
            else if(state is "error" or "denied")progress.ShowFailure(
                LocalizationService.Text(state=="denied"?"Autorizzazione negata":"Aggiornamento non completato",state=="denied"?"Authorization denied":"Update not completed"),
                LocalizationService.Text("Errore simulato. Nessuna operazione reale è stata avviata.\nDettagli QA: consenso mancante o componente non disponibile.","Simulated error. No real operation was started.\nQA details: missing consent or unavailable component."));
        };
        return progress;
    }

    public static AppUpdateInfo AppUpdate()=>new()
    {
        InstalledVersion=new(2,0,1),AvailableVersion=new(2,0,2),
        ReleaseNotes="## Italiano\n- Correzione dimostrativa della tabella dopo la scansione.\n- Un riepilogo più leggibile nella pagina Aggiornamenti.\n\n## English\n- Example fix for the table after scanning.\n- A clearer summary on the Updates page.",
        DownloadSize=128*1024*1024,
        DownloadUri=new("https://github.com/Illidan0S/UpdateCenter/releases/download/v2.0.2/UpdateCenter-v2.0.2-Portable.exe"),Sha256Uri=new("https://github.com/Illidan0S/UpdateCenter/releases/download/v2.0.2/UpdateCenter-v2.0.2-Portable.exe.sha256"),AssetName="UpdateCenter-v2.0.2-Portable.exe",IsInstallerPackage=false,ReleasePageUri=new("https://github.com/Illidan0S/UpdateCenter/releases/tag/v2.0.2")
    };
    public static AppUpdateService RejectingAppService(Action<HttpRequestMessage>? onRequest = null) =>
        new(new HttpClient(new RejectNetwork(onRequest)));
    private sealed class RejectNetwork(Action<HttpRequestMessage>? onRequest) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            onRequest?.Invoke(request);
            return Task.FromException<HttpResponseMessage>(new HttpRequestException("QA transport: network access is forbidden."));
        }
    }
    public static T Find<T>(FrameworkElement window,string name) where T:class=>(T)window.FindName(name);
    public static void Set(object target,string name,object value)=>target.GetType().GetProperty(name)!.SetValue(target,value);
    public static void Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(target,args);
    private static void SetText(FrameworkElement w,string name,string text)
    {if(w.FindName(name) is TextBlock block)block.Text=text;else if(w.FindName(name) is TextBox box)box.Text=text;}
}
