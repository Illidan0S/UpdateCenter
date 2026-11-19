using System.Reflection;
using UpdateCenter.Contracts;
using UpdateCenter.Models;
using UpdateCenter.Services;
using UpdateCenter.ViewModels;

internal static class HardwareNetworkUiFixture
{
    // In-memory data only. Call after AppPaths.InitializeRoot with an isolated root.
    public static void Populate(MainViewModel vm, string state = "populated", int count = 500)
    {
        var empty = state == "empty";
        var text = new Func<string,string,string>(LocalizationService.Text);
        vm.HardwareInfo.MonitoringStatus = text("Profilo QA isolato · dati simulati", "Isolated QA profile · simulated data");
        if (!empty)
        {
            foreach (var (key,value) in new (string,string)[] {
                ("CpuName","QA CPU · 16-core workstation processor with a long model name"), ("CpuCores","16 core · 32 thread"),
                ("GpuName","QA Graphics 1 · Dedicated GPU\nQA Graphics 2 · Integrated GPU"),
                ("GpuConfiguration",text("Due GPU simulate, nessun sensore interrogato.","Two simulated GPUs, no sensors queried.")),
                ("VramTotal","16 GB"),("VramDetails","GPU 1: 12 GB · GPU 2: 4 GB"),("VramUsed","4.2 GB / 16 GB"),
                ("GpuMemoryUsageHeading",text("Memoria dedicata in uso","Dedicated memory in use")),("GpuMetricsSource","QA fixture"),
                ("RamTotal","32 GB"),("RamUsed","12.8 GB / 32 GB"),("CpuTemperature","54 °C"),("GpuTemperature","61 °C"),
                ("Resolution","2560 × 1440"),("RefreshRate","144 Hz"),("OperatingSystem","Windows · QA build"),
                ("ComputerModel","QA workstation · system manufacturer / model") }) Set(vm.HardwareInfo,key,value);
            Set(vm.HardwareInfo,"CpuUsage",32d); Set(vm.HardwareInfo,"RamUsage",40d); Set(vm.HardwareInfo,"GpuUsage",27d);
            Set(vm.HardwareInfo,"HasCpuTemperature",true); Set(vm.HardwareInfo,"HasGpuTemperature",true);
        }
        vm.StorageRows.Clear();
        vm.StorageDevices.Clear(); vm.StorageVolumes.Clear();
        for (var i=0; i<(empty?0:count); i++)
        {
            vm.StorageDevices.Add(new StorageDeviceItem { Name=$"QA SSD {i:000}", BusType="NVMe" });
            vm.StorageVolumes.Add(new StorageVolumeItem { Label=$"QA volume {i:000}", SizeBytes=1L<<40 });
        }
        for (var i=0; i<(empty?0:count); i++) vm.StorageRows.Add(new StorageTableRow {
            Name=$"QA SSD {i:000} · NVMe storage device with a long model name",KindLabel="SSD",Detail="NVMe · QA",
            VolumesLabel="C: · D:",VolumesDetail=text("Sistema · Archivio dati simulato","System · Simulated data archive"),CapacityLabel="1 TB",
            HealthLabel=i%3==0?text("Attenzione","Attention"):i%3==1?text("Regolare","Healthy"):text("Non disponibile","Unavailable"),
            HealthDetail=i%3==0?text("SMART: verifica consigliata (simulazione)","SMART: check recommended (simulation)"):text("Dati di test","Test data"),
            TemperatureLabel=i%3==2?"—":"38 °C",IsHealthy=i%3==1,IsHealthUnknown=i%3==2 });
        Set(vm,"StorageHealthStatus",empty?text("Dati storage non ancora disponibili.","Storage data not available yet."):text("Dati SMART simulati: stato regolare, attenzione e sconosciuto.","Simulated SMART data: healthy, attention and unknown states."));

        var network=vm.Network;
        network.Agents.Clear(); network.ScanResults.Clear();
        var agentCount=empty?0:Math.Max(5,count/10);
        for (var i=0;i<agentCount;i++)
        {
            var agent=new NetworkAgentItem();
            var id=Guid.NewGuid();
            Set(agent,"AgentId",id);Set(agent,"DisplayName",$"QA PC {i:000} · Remote workstation with a long descriptive name");
            Set(agent,"Address",$"192.0.2.{i%250+1}");Set(agent,"AgentVersion","1.1.5");Set(agent,"MachineName",$"QA-PC-{i:000}");
            Set(agent,"IsPaired",i%5<3 && state!="denied");Set(agent,"HasController",i%5<4);
            Set(agent,"ConnectionRequestsEnabled",i%5==4);Set(agent,"ConnectionRequestsExpiresUtc",DateTime.UtcNow.AddHours(1));
            agent.IsSelected=i==0;
            agent.ConnectionState=state=="denied"?text("Non autorizzato","Not authorized"):(i%5) switch {
                0=>text("Raggiungibile","Reachable"),1=>text("Non raggiungibile","Unreachable"),
                2=>text("Errore","Error"),3=>text("Collegato a un altro PC","Connected to another PC"),_=>text("Rilevato","Discovered") };
            agent.OperationStatus=i%5==2?text("Autorizzazione negata (QA)","Authorization denied (QA)"):text("Nessuna operazione","No operation");
            agent.LastScanText="07/11/2026 12:30";
            Invoke(network,"AddAgent",agent);
            var updates=new List<RemoteUpdateItem>();
            var scan=new ScanResult { MachineName=agent.MachineName, Updates=updates };
            for(var j=0;j<count/agentCount;j++) updates.Add(new RemoteUpdateItem {
                Id=$"QA.NotARealPackage.{i}.{j}",Name=$"QA {j:000} · Application with a long name for layout verification",
                Kind=j%3==0?"Driver":j%3==1?"Software":"Runtime",Source="QA fixture · verified-source presentation",
                InstalledVersion="1.2.3456.7890",AvailableVersion="2.3.4567.8901",DownloadSizeBytes=128*1024*1024,
                CanInstall=j%4!=0,RequiresRiskConfirmation=j%4==2,IsImportant=j%3==0,RequiresRestart=j%3==0,
                Status=j%4==3?text("Errore","Error"):text("Da aggiornare","Update available"),
                ResultDetails=j%4==3?text("Errore simulato: messaggio lungo senza chiamate remote.","Simulated error: long message without remote calls."):"" });
            agent.SetScanResults(Guid.NewGuid(),scan);
            if(i%5!=0 || state=="denied") foreach(var item in agent.Updates) item.MarkDeviceDisconnected();
        }
        network.SelectedAgent=network.Agents.FirstOrDefault();
        Invoke(network,"LoadSelectedResults");
        Set(network,"StatusText",state=="denied"?text("Autorizzazione negata. Nessuna operazione remota eseguita.","Authorization denied. No remote operation was performed."):text("Profilo QA isolato · nessun collegamento remoto", "Isolated QA profile · no remote connection"));
        Set(network,"LocalConnectionStateText",text("Controller non collegato (QA)","Controller disconnected (QA)"));
        Set(network,"LocalConnectionDetailText",text("Verifica grafica con dati simulati", "Visual verification with simulated data"));
        network.NotifyLanguageChanged();
        vm.NotifyCounts();
    }

    public static void Set(object target,string name,object value)=>target.GetType().GetProperty(name)!.SetValue(target,value);
    public static void Invoke(object target,string name,params object[] args)=>target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(target,args);
}
