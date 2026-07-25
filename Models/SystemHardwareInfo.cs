using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UpdateCenter.Models;

public sealed class SystemHardwareInfo : INotifyPropertyChanged
{
    private string _cpuName = "Rilevamento in corso…";
    private string _cpuCores = "—";
    private string _gpuName = "Rilevamento in corso…";
    private string _gpuConfiguration = "Identificazione in corso…";
    private string _vramTotal = "—";
    private string _vramDetails = "—";
    private string _vramUnavailableReason = "Non esposta dal driver o da Windows";
    private string _gpuMemoryUsageHeading = "MEMORIA GPU IN USO";
    private GpuMemoryDisplayMode _gpuMemoryDisplayMode = GpuMemoryDisplayMode.Unknown;
    private string _ramTotal = "—";
    private string _resolution = "—";
    private string _refreshRate = "—";
    private string _operatingSystem = "—";
    private string _computerModel = "—";
    private double _cpuUsage;
    private double _ramUsage;
    private double _gpuUsage;
    private string _ramUsed = "—";
    private string _vramUsed = "—";
    private string _gpuMetricsSource = "Contatori GPU di Windows";
    private string _cpuTemperature = "Non disponibile";
    private string _gpuTemperature = "Non disponibile";
    private bool _hasCpuTemperature;
    private bool _hasGpuTemperature;
    private string _monitoringStatus = "Preparazione del monitoraggio…";

    public string CpuName { get => _cpuName; private set => Set(ref _cpuName, value); }
    public string CpuCores { get => _cpuCores; private set => Set(ref _cpuCores, value); }
    public string GpuName { get => _gpuName; private set => Set(ref _gpuName, value); }
    public string GpuConfiguration { get => _gpuConfiguration; private set => Set(ref _gpuConfiguration, value); }
    public string VramTotal { get => _vramTotal; private set => Set(ref _vramTotal, value); }
    public string VramDetails { get => _vramDetails; private set => Set(ref _vramDetails, value); }
    public string GpuMemoryUsageHeading { get => _gpuMemoryUsageHeading; private set => Set(ref _gpuMemoryUsageHeading, value); }
    public string RamTotal { get => _ramTotal; private set => Set(ref _ramTotal, value); }
    public string Resolution { get => _resolution; private set => Set(ref _resolution, value); }
    public string RefreshRate { get => _refreshRate; private set => Set(ref _refreshRate, value); }
    public string OperatingSystem { get => _operatingSystem; private set => Set(ref _operatingSystem, value); }
    public string ComputerModel { get => _computerModel; private set => Set(ref _computerModel, value); }
    public double CpuUsage { get => _cpuUsage; private set { if (Set(ref _cpuUsage, value)) OnPropertyChanged(nameof(CpuUsageLabel)); } }
    public double RamUsage { get => _ramUsage; private set { if (Set(ref _ramUsage, value)) OnPropertyChanged(nameof(RamUsageLabel)); } }
    public double GpuUsage { get => _gpuUsage; private set { if (Set(ref _gpuUsage, value)) OnPropertyChanged(nameof(GpuUsageLabel)); } }
    public string RamUsed { get => _ramUsed; private set => Set(ref _ramUsed, value); }
    public string VramUsed { get => _vramUsed; private set => Set(ref _vramUsed, value); }
    public string GpuMetricsSource { get => _gpuMetricsSource; private set => Set(ref _gpuMetricsSource, value); }
    public string CpuTemperature { get => _cpuTemperature; private set => Set(ref _cpuTemperature, value); }
    public string GpuTemperature { get => _gpuTemperature; private set => Set(ref _gpuTemperature, value); }
    public bool HasCpuTemperature { get => _hasCpuTemperature; private set => Set(ref _hasCpuTemperature, value); }
    public bool HasGpuTemperature { get => _hasGpuTemperature; private set => Set(ref _hasGpuTemperature, value); }
    public string MonitoringStatus { get => _monitoringStatus; set => Set(ref _monitoringStatus, value); }
    public string CpuUsageLabel => $"{CpuUsage:0}%";
    public string RamUsageLabel => $"{RamUsage:0}%";
    public string GpuUsageLabel => $"{GpuUsage:0}%";

    public void ApplyOverview(HardwareOverviewSnapshot snapshot)
    {
        CpuName = snapshot.CpuName;
        CpuCores = snapshot.CpuCores;
        GpuName = snapshot.GpuName;
        GpuConfiguration = snapshot.GpuConfiguration;
        VramTotal = snapshot.VramTotal;
        VramDetails = snapshot.VramDetails;
        _vramUnavailableReason = snapshot.VramUnavailableReason;
        GpuMemoryUsageHeading = snapshot.GpuMemoryUsageHeading;
        _gpuMemoryDisplayMode = snapshot.GpuMemoryDisplayMode;
        RamTotal = snapshot.RamTotal;
        Resolution = snapshot.Resolution;
        RefreshRate = snapshot.RefreshRate;
        OperatingSystem = snapshot.OperatingSystem;
        ComputerModel = snapshot.ComputerModel;
    }

    public void ApplyMetrics(HardwareMetricsSnapshot metrics)
    {
        CpuUsage = Clamp(metrics.CpuUsage);
        RamUsage = Clamp(metrics.RamUsage);
        GpuUsage = Clamp(metrics.GpuUsage);
        RamUsed = metrics.RamUsed;
        VramUsed = FormatGpuMemoryUsage(metrics);
        GpuMetricsSource = metrics.GpuMetricsSource;
        HasCpuTemperature = IsValidTemperature(metrics.CpuTemperature);
        HasGpuTemperature = IsValidTemperature(metrics.GpuTemperature);
        CpuTemperature = FormatTemperature(metrics.CpuTemperature, "Non esposta da Windows/firmware");
        GpuTemperature = FormatTemperature(metrics.GpuTemperature, "Non esposta dal driver video");
        MonitoringStatus = metrics.Status;
    }

    private static string FormatTemperature(double? value, string unavailableText) => value.HasValue && value.Value is >= 1 and <= 125
        ? $"{value.Value:0.#} °C"
        : unavailableText;

    private static bool IsValidTemperature(double? value) => value is >= 1 and <= 125;

    private string FormatGpuMemoryUsage(HardwareMetricsSnapshot metrics)
    {
        var dedicated = metrics.DedicatedGpuMemoryUsedBytes;
        var shared = metrics.SharedGpuMemoryUsedBytes;
        var sharedLimit = metrics.SharedGpuMemoryLimitBytes;
        var sharedLabel = shared > 0
            ? sharedLimit > 0
                ? $"{FormatBytes(shared)} di {FormatBytes(sharedLimit)} condivisi"
                : $"{FormatBytes(shared)} condivisi"
            : "";

        return _gpuMemoryDisplayMode switch
        {
            GpuMemoryDisplayMode.Integrated => !string.IsNullOrWhiteSpace(sharedLabel)
                ? sharedLabel
                : _vramUnavailableReason,
            GpuMemoryDisplayMode.Discrete => dedicated > 0
                ? FormatBytes(dedicated)
                : !string.IsNullOrWhiteSpace(sharedLabel) ? sharedLabel : _vramUnavailableReason,
            _ when dedicated > 0 && !string.IsNullOrWhiteSpace(sharedLabel) =>
                $"Dedicata: {FormatBytes(dedicated)} · Condivisa: {sharedLabel}",
            _ when dedicated > 0 => FormatBytes(dedicated),
            _ when !string.IsNullOrWhiteSpace(sharedLabel) => sharedLabel,
            _ => _vramUnavailableReason
        };
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
            return $"{bytes / (1024d * 1024 * 1024):0.#} GB";
        if (bytes >= 1024L * 1024)
            return $"{bytes / (1024d * 1024):0.#} MB";
        return $"{Math.Max(bytes, 0) / 1024d:0.#} KB";
    }

    private static double Clamp(double? value) => Math.Clamp(value ?? 0, 0, 100);

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record HardwareOverviewSnapshot(
    string CpuName,
    string CpuCores,
    string GpuName,
    string GpuConfiguration,
    string VramTotal,
    string VramDetails,
    string VramUnavailableReason,
    string GpuMemoryUsageHeading,
    GpuMemoryDisplayMode GpuMemoryDisplayMode,
    string RamTotal,
    string Resolution,
    string RefreshRate,
    string OperatingSystem,
    string ComputerModel);

public sealed record HardwareMetricsSnapshot(
    double? CpuUsage,
    double? RamUsage,
    double? GpuUsage,
    string RamUsed,
    long DedicatedGpuMemoryUsedBytes,
    long SharedGpuMemoryUsedBytes,
    long SharedGpuMemoryLimitBytes,
    string GpuMetricsSource,
    double? CpuTemperature,
    double? GpuTemperature,
    string Status);
