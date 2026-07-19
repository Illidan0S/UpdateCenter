using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using UpdateCenter.Models;

namespace UpdateCenter.Services;

public sealed class SystemHardwareService
{
    private readonly object _cpuGate = new();
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private bool _hasCpuSample;

    public Task<HardwareOverviewSnapshot> ReadOverviewAsync(CancellationToken cancellationToken) =>
        Task.Run(() => ReadOverview(cancellationToken), cancellationToken);

    private static HardwareOverviewSnapshot ReadOverview(CancellationToken cancellationToken)
    {
        var cpuNames = new List<string>();
        var gpus = new List<GpuInfo>();
        long cores = 0;
        long threads = 0;
        long ramBytes = 0;
        var manufacturer = "";
        var model = "";
        var osCaption = "";
        var osVersion = "";
        var osBuild = "";
        var osArchitecture = "";

        TryQueryWmi("root\\cimv2", "SELECT Name,NumberOfCores,NumberOfLogicalProcessors FROM Win32_Processor", row =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = SafeString(() => Convert.ToString(row.Name)).Trim();
            if (!string.IsNullOrWhiteSpace(name)) cpuNames.Add(name);
            cores += Math.Max(SafeLong(() => Convert.ToInt64(row.NumberOfCores, CultureInfo.InvariantCulture)) ?? 0, 0);
            threads += Math.Max(SafeLong(() => Convert.ToInt64(row.NumberOfLogicalProcessors, CultureInfo.InvariantCulture)) ?? 0, 0);
        });

        TryQueryWmi("root\\cimv2", "SELECT Manufacturer,Model,TotalPhysicalMemory FROM Win32_ComputerSystem", row =>
        {
            if (!string.IsNullOrWhiteSpace(manufacturer)) return;
            manufacturer = SafeString(() => Convert.ToString(row.Manufacturer)).Trim();
            model = SafeString(() => Convert.ToString(row.Model)).Trim();
            ramBytes = SafeLong(() => Convert.ToInt64(row.TotalPhysicalMemory, CultureInfo.InvariantCulture)) ?? 0;
        });

        TryQueryWmi("root\\cimv2", "SELECT Name,AdapterRAM,CurrentHorizontalResolution,CurrentVerticalResolution,CurrentRefreshRate FROM Win32_VideoController", row =>
        {
            var name = SafeString(() => Convert.ToString(row.Name)).Trim();
            if (string.IsNullOrWhiteSpace(name)) return;
            gpus.Add(new GpuInfo(
                name,
                SafeLong(() => Convert.ToInt64(row.AdapterRAM, CultureInfo.InvariantCulture)) ?? 0,
                SafeLong(() => Convert.ToInt64(row.CurrentHorizontalResolution, CultureInfo.InvariantCulture)) ?? 0,
                SafeLong(() => Convert.ToInt64(row.CurrentVerticalResolution, CultureInfo.InvariantCulture)) ?? 0,
                SafeLong(() => Convert.ToInt64(row.CurrentRefreshRate, CultureInfo.InvariantCulture)) ?? 0));
        });

        TryQueryWmi("root\\cimv2", "SELECT Caption,Version,BuildNumber,OSArchitecture FROM Win32_OperatingSystem", row =>
        {
            if (!string.IsNullOrWhiteSpace(osCaption)) return;
            osCaption = SafeString(() => Convert.ToString(row.Caption)).Trim();
            osVersion = SafeString(() => Convert.ToString(row.Version)).Trim();
            osBuild = SafeString(() => Convert.ToString(row.BuildNumber)).Trim();
            osArchitecture = SafeString(() => Convert.ToString(row.OSArchitecture)).Trim();
        });

        cancellationToken.ThrowIfCancellationRequested();
        var topology = ReadProcessorTopology();
        if (cores <= 0) cores = topology.Cores;
        if (threads <= 0) threads = topology.Threads;
        if (threads < cores) threads = cores;

        var cpuName = string.Join(" · ", cpuNames.Distinct(StringComparer.CurrentCultureIgnoreCase));
        if (string.IsNullOrWhiteSpace(cpuName))
            cpuName = ReadRegistryString(@"HARDWARE\\DESCRIPTION\\System\\CentralProcessor\\0", "ProcessorNameString");
        if (string.IsNullOrWhiteSpace(cpuName)) cpuName = "Processore non rilevato";

        if (ramBytes <= 0)
        {
            var memory = new MemoryStatusEx();
            if (GlobalMemoryStatusEx(memory)) ramBytes = (long)Math.Min(memory.TotalPhysical, (ulong)long.MaxValue);
        }

        if (string.IsNullOrWhiteSpace(manufacturer))
            manufacturer = ReadRegistryString(@"HARDWARE\\DESCRIPTION\\System\\BIOS", "SystemManufacturer");
        if (string.IsNullOrWhiteSpace(model))
            model = ReadRegistryString(@"HARDWARE\\DESCRIPTION\\System\\BIOS", "SystemProductName");

        MergeGpuInformation(gpus, TryReadNvidiaAdapterInformation());
        MergeRegistryGpuInformation(gpus);
        gpus = gpus
            .Select(x => x with { Name = CleanHardwareText(x.Name) })
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => group.OrderByDescending(x => x.AdapterRam).First())
            .ToList();

        var display = ReadCurrentDisplayMode();
        var activeGpu = gpus.FirstOrDefault(x => x.Width > 0 && x.Height > 0) ?? gpus.FirstOrDefault();
        var width = display.Width > 0 ? display.Width : activeGpu?.Width ?? 0;
        var height = display.Height > 0 ? display.Height : activeGpu?.Height ?? 0;
        var frequency = display.RefreshRate > 1 ? display.RefreshRate : activeGpu?.RefreshRate ?? 0;

        var gpuName = gpus.Count > 0
            ? string.Join(" · ", gpus.Select(x => x.Name))
            : "Scheda video non rilevata";
        var vram = gpus.Where(x => x.AdapterRam > 0).ToList();
        var hasIntegratedGpu = gpus.Any(x =>
            x.Name.Contains("Integrated", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Contains("Iris", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Contains("UHD", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Contains("Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase));
        var vramLabel = vram.Count == 0
            ? hasIntegratedGpu ? "Memoria condivisa dinamicamente con la RAM" : "Non esposta dal driver video"
            : string.Join(" · ", vram.Select(x => gpus.Count == 1
                ? FormatBytes(x.AdapterRam)
                : $"{x.Name}: {FormatBytes(x.AdapterRam)}"));

        var osParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(osCaption)) osParts.Add(osCaption);
        if (!string.IsNullOrWhiteSpace(osVersion)) osParts.Add($"versione {osVersion}");
        if (!string.IsNullOrWhiteSpace(osBuild)) osParts.Add($"build {osBuild}");
        if (!string.IsNullOrWhiteSpace(osArchitecture)) osParts.Add(osArchitecture);
        if (osParts.Count == 0)
        {
            osParts.Add(RuntimeInformation.OSDescription.Trim());
            osParts.Add(RuntimeInformation.OSArchitecture.ToString());
        }

        var computerLabel = string.Join(" ", new[] { manufacturer, model }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var cpuCores = cores > 0
            ? $"{cores} core · {Math.Max(threads, cores)} thread"
            : threads > 0 ? $"{threads} processori logici" : "Non rilevati";

        return new HardwareOverviewSnapshot(
            cpuName,
            cpuCores,
            gpuName,
            vramLabel,
            ramBytes > 0 ? FormatBytes(ramBytes) : "Non rilevata",
            width > 0 && height > 0 ? $"{width} × {height}" : "Non rilevata",
            frequency > 1 ? $"{frequency} Hz" : "Non rilevata",
            string.Join(" · ", osParts.Where(x => !string.IsNullOrWhiteSpace(x))),
            string.IsNullOrWhiteSpace(computerLabel) ? Environment.MachineName : computerLabel);
    }

    public Task<HardwareMetricsSnapshot> ReadMetricsAsync(CancellationToken cancellationToken) =>
        Task.Run(() => ReadMetrics(cancellationToken), cancellationToken);

    private HardwareMetricsSnapshot ReadMetrics(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cpuUsage = ReadCpuUsage();
        var (ramUsage, ramUsed) = ReadMemoryUsage();
        var (gpuUsage, vramUsed) = ReadGpuPerformanceCounters();
        var (cpuTemperature, gpuTemperature) = ReadTemperatures();
        var nvidia = TryReadNvidiaMetrics();
        if (nvidia is not null)
        {
            gpuUsage = nvidia.Usage ?? gpuUsage;
            gpuTemperature = nvidia.Temperature ?? gpuTemperature;
            if (!string.IsNullOrWhiteSpace(nvidia.MemoryUsed)) vramUsed = nvidia.MemoryUsed;
        }

        var temperaturesAvailable = cpuTemperature.HasValue || gpuTemperature.HasValue;
        var status = temperaturesAvailable
            ? "Dati aggiornati automaticamente ogni 3 secondi."
            : "Utilizzo aggiornato ogni 3 secondi · sensori temperatura non esposti dal firmware/driver.";

        return new HardwareMetricsSnapshot(
            cpuUsage,
            ramUsage,
            gpuUsage,
            ramUsed,
            vramUsed,
            cpuTemperature,
            gpuTemperature,
            status);
    }

    private double? ReadCpuUsage()
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime)) return null;
        var idle = ToUInt64(idleTime);
        var kernel = ToUInt64(kernelTime);
        var user = ToUInt64(userTime);

        lock (_cpuGate)
        {
            if (!_hasCpuSample)
            {
                _previousIdle = idle;
                _previousKernel = kernel;
                _previousUser = user;
                _hasCpuSample = true;
                return 0;
            }

            var idleDelta = idle - _previousIdle;
            var kernelDelta = kernel - _previousKernel;
            var userDelta = user - _previousUser;
            _previousIdle = idle;
            _previousKernel = kernel;
            _previousUser = user;
            var total = kernelDelta + userDelta;
            return total == 0 ? 0 : (total - idleDelta) * 100d / total;
        }
    }

    private static (double? Usage, string Used) ReadMemoryUsage()
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status)) return (null, "Non disponibile");
        var used = status.TotalPhysical - status.AvailablePhysical;
        var usage = status.TotalPhysical == 0 ? 0 : used * 100d / status.TotalPhysical;
        return (usage, $"{FormatBytes((long)used)} di {FormatBytes((long)status.TotalPhysical)}");
    }

    private static (double? Usage, string VramUsed) ReadGpuPerformanceCounters()
    {
        double? usage = null;
        long dedicatedUsage = 0;
        TryQueryWmi("root\\cimv2",
            "SELECT UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine",
            row =>
            {
                var value = SafeDouble(() => Convert.ToDouble(row.UtilizationPercentage, CultureInfo.InvariantCulture));
                if (value.HasValue) usage = Math.Max(usage ?? 0, value.Value);
            });
        TryQueryWmi("root\\cimv2",
            "SELECT DedicatedUsage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory",
            row =>
            {
                var value = SafeLong(() => Convert.ToInt64(row.DedicatedUsage, CultureInfo.InvariantCulture));
                if (value.HasValue) dedicatedUsage = Math.Max(dedicatedUsage, value.Value);
            });
        return (usage, dedicatedUsage > 0 ? FormatBytes(dedicatedUsage) : "Non disponibile");
    }

    private static (double? Cpu, double? Gpu) ReadTemperatures()
    {
        double? cpu = null;
        double? gpu = null;
        foreach (var nameSpace in new[] { "root\\LibreHardwareMonitor", "root\\OpenHardwareMonitor" })
        {
            TryQueryWmi(nameSpace, "SELECT Name,Identifier,SensorType,Value FROM Sensor WHERE SensorType='Temperature'", row =>
            {
                var value = SafeDouble(() => Convert.ToDouble(row.Value, CultureInfo.InvariantCulture));
                if (!value.HasValue || value.Value <= 0 || value.Value > 125) return;
                var identity = $"{SafeString(() => Convert.ToString(row.Name))} {SafeString(() => Convert.ToString(row.Identifier))}";
                if (identity.Contains("gpu", StringComparison.OrdinalIgnoreCase))
                    gpu = Math.Max(gpu ?? 0, value.Value);
                else if (identity.Contains("cpu", StringComparison.OrdinalIgnoreCase) ||
                         identity.Contains("core", StringComparison.OrdinalIgnoreCase))
                    cpu = Math.Max(cpu ?? 0, value.Value);
            });
            if (cpu.HasValue || gpu.HasValue) break;
        }

        return (cpu, gpu);
    }

    private static NvidiaMetrics? TryReadNvidiaMetrics()
    {
        var candidates = new[]
        {
            "nvidia-smi.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe")
        };
        foreach (var executable in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (Path.IsPathRooted(executable) && !File.Exists(executable)) continue;
                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                startInfo.ArgumentList.Add("--query-gpu=temperature.gpu,utilization.gpu,memory.used");
                startInfo.ArgumentList.Add("--format=csv,noheader,nounits");
                using var process = Process.Start(startInfo);
                if (process is null) continue;
                if (!process.WaitForExit(1800))
                {
                    try { process.Kill(true); } catch { }
                    continue;
                }
                var line = process.StandardOutput.ReadLine();
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length < 3) continue;
                var temperature = double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var temp) ? temp : (double?)null;
                var usage = double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var load) ? load : (double?)null;
                var memory = double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var usedMb)
                    ? $"{usedMb:0.#} MB"
                    : "";
                return new NvidiaMetrics(temperature, usage, memory);
            }
            catch { }
        }
        return null;
    }

    private static List<GpuInfo> TryReadNvidiaAdapterInformation()
    {
        var adapters = new List<GpuInfo>();
        var candidates = new[]
        {
            "nvidia-smi.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe")
        };
        foreach (var executable in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (Path.IsPathRooted(executable) && !File.Exists(executable)) continue;
                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                startInfo.ArgumentList.Add("--query-gpu=name,memory.total");
                startInfo.ArgumentList.Add("--format=csv,noheader,nounits");
                using var process = Process.Start(startInfo);
                if (process is null) continue;
                if (!process.WaitForExit(1800))
                {
                    try { process.Kill(true); } catch { }
                    continue;
                }
                while (process.StandardOutput.ReadLine() is { } line)
                {
                    var parts = line.Split(',', 2, StringSplitOptions.TrimEntries);
                    if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0])) continue;
                    var memory = double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var totalMb)
                        ? (long)Math.Max(totalMb * 1024d * 1024d, 0)
                        : 0;
                    adapters.Add(new GpuInfo(parts[0], memory, 0, 0, 0));
                }
                if (adapters.Count > 0) return adapters;
            }
            catch { }
        }
        return adapters;
    }

    private static void TryQueryWmi(string nameSpace, string query, Action<dynamic> consume)
    {
        object? locatorObject = null;
        object? serviceObject = null;
        object? resultsObject = null;
        try
        {
            var locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
            if (locatorType is null) return;
            locatorObject = Activator.CreateInstance(locatorType);
            dynamic locator = locatorObject!;
            serviceObject = locator.ConnectServer(".", nameSpace);
            dynamic service = serviceObject;
            resultsObject = service.ExecQuery(query);
            dynamic results = resultsObject;
            foreach (var row in results)
            {
                try { consume(row); }
                finally { ReleaseCom(row); }
            }
        }
        catch { }
        finally
        {
            ReleaseCom(resultsObject);
            ReleaseCom(serviceObject);
            ReleaseCom(locatorObject);
        }
    }

    private static (long Cores, long Threads) ReadProcessorTopology()
    {
        var threads = (long)GetActiveProcessorCount(ushort.MaxValue);
        if (threads <= 0) threads = Environment.ProcessorCount;
        uint length = 0;
        _ = GetLogicalProcessorInformationEx(0, IntPtr.Zero, ref length);
        if (length == 0) return (0, threads);

        var buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            if (!GetLogicalProcessorInformationEx(0, buffer, ref length)) return (0, threads);
            long cores = 0;
            var offset = 0;
            var totalLength = (int)length;
            while (offset + 8 <= totalLength)
            {
                var relationship = Marshal.ReadInt32(buffer, offset);
                var size = Marshal.ReadInt32(buffer, offset + 4);
                if (size < 8 || offset + size > totalLength) break;
                if (relationship == 0) cores++;
                offset += size;
            }
            return (cores, threads);
        }
        catch
        {
            return (0, threads);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static (long Width, long Height, long RefreshRate) ReadCurrentDisplayMode()
    {
        try
        {
            var mode = new DevMode
            {
                DeviceName = "",
                FormName = "",
                Size = (ushort)Marshal.SizeOf<DevMode>()
            };
            if (EnumDisplaySettings(null, -1, ref mode))
                return (mode.PelsWidth, mode.PelsHeight, mode.DisplayFrequency);
        }
        catch { }

        var width = GetSystemMetrics(0);
        var height = GetSystemMetrics(1);
        var frequency = 0;
        var deviceContext = GetDC(IntPtr.Zero);
        try
        {
            if (deviceContext != IntPtr.Zero) frequency = GetDeviceCaps(deviceContext, 116);
        }
        finally
        {
            if (deviceContext != IntPtr.Zero) _ = ReleaseDC(IntPtr.Zero, deviceContext);
        }
        return (Math.Max(width, 0), Math.Max(height, 0), Math.Max(frequency, 0));
    }

    private static void MergeRegistryGpuInformation(List<GpuInfo> gpus)
    {
        try
        {
            using var videoRoot = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Video");
            if (videoRoot is null) return;
            foreach (var adapterId in videoRoot.GetSubKeyNames())
            {
                using var adapterRoot = videoRoot.OpenSubKey(adapterId);
                if (adapterRoot is null) continue;
                foreach (var instanceName in adapterRoot.GetSubKeyNames())
                {
                    if (!int.TryParse(instanceName, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)) continue;
                    using var instance = adapterRoot.OpenSubKey(instanceName);
                    if (instance is null) continue;
                    var name = ReadRegistryText(instance.GetValue("HardwareInformation.AdapterString"));
                    var bytes = ReadRegistryByteSize(instance.GetValue("HardwareInformation.qwMemorySize"));
                    if (bytes <= 0) bytes = ReadRegistryByteSize(instance.GetValue("HardwareInformation.MemorySize"));
                    if (string.IsNullOrWhiteSpace(name) && bytes <= 0) continue;

                    var index = gpus.FindIndex(x => GpuNamesMatch(x.Name, name));
                    if (index >= 0)
                    {
                        if (bytes > gpus[index].AdapterRam)
                            gpus[index] = gpus[index] with { AdapterRam = bytes };
                    }
                    else if (!string.IsNullOrWhiteSpace(name))
                    {
                        gpus.Add(new GpuInfo(name, bytes, 0, 0, 0));
                    }
                }
            }
        }
        catch { }
    }

    private static void MergeGpuInformation(List<GpuInfo> target, IEnumerable<GpuInfo> additional)
    {
        foreach (var gpu in additional)
        {
            var index = target.FindIndex(x => GpuNamesMatch(x.Name, gpu.Name));
            if (index < 0)
            {
                target.Add(gpu);
                continue;
            }
            if (gpu.AdapterRam > target[index].AdapterRam)
                target[index] = target[index] with { AdapterRam = gpu.AdapterRam };
        }
    }

    private static bool GpuNamesMatch(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        return left.Contains(right, StringComparison.CurrentCultureIgnoreCase) ||
               right.Contains(left, StringComparison.CurrentCultureIgnoreCase);
    }

    private static string CleanHardwareText(string value) => string.Join(" · ", value
        .Split('·', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(part => !part.Equals("System.Byte[]", StringComparison.OrdinalIgnoreCase)));

    private static long ReadRegistryByteSize(object? value)
    {
        try
        {
            return value switch
            {
                long number when number > 0 => number,
                int number => unchecked((uint)number),
                uint number => number,
                ulong number => (long)Math.Min(number, (ulong)long.MaxValue),
                byte[] bytes when bytes.Length >= 8 => (long)Math.Min(BitConverter.ToUInt64(bytes, 0), (ulong)long.MaxValue),
                byte[] bytes when bytes.Length >= 4 => BitConverter.ToUInt32(bytes, 0),
                _ => 0
            };
        }
        catch { return 0; }
    }

    private static string ReadRegistryText(object? value)
    {
        try
        {
            var text = value switch
            {
                string stringValue => stringValue,
                string[] values => string.Join(" ", values),
                byte[] bytes when bytes.Length > 1 => DecodeRegistryText(bytes),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
            };
            text = text.Trim().TrimEnd('\0').Trim();
            return text.Equals("System.Byte[]", StringComparison.OrdinalIgnoreCase) ? "" : text;
        }
        catch { return ""; }
    }

    private static string DecodeRegistryText(byte[] bytes)
    {
        // AdapterString viene normalmente salvato come UTF-16LE. Se non contiene
        // byte nulli, usa ANSI/UTF-8 come fallback per i driver meno comuni.
        var looksUnicode = bytes.Length % 2 == 0 && bytes.Where((_, index) => index % 2 == 1).Count(x => x == 0) >= bytes.Length / 6;
        return looksUnicode
            ? Encoding.Unicode.GetString(bytes)
            : Encoding.UTF8.GetString(bytes);
    }

    private static string ReadRegistryString(string path, string valueName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            return Convert.ToString(key?.GetValue(valueName))?.Trim() ?? "";
        }
        catch { return ""; }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }

    private static ulong ToUInt64(FileTime value) => ((ulong)value.High << 32) | value.Low;
    private static double? SafeDouble(Func<double> getter) { try { return getter(); } catch { return null; } }
    private static long? SafeLong(Func<long> getter) { try { return getter(); } catch { return null; } }
    private static string SafeString(Func<string?> getter) { try { return getter() ?? ""; } catch { return ""; } }
    private static void ReleaseCom(object? value) { try { if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); } catch { } }

    [DllImport("kernel32.dll")]
    private static extern uint GetActiveProcessorCount(ushort groupNumber);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLogicalProcessorInformationEx(int relationshipType, IntPtr buffer, ref uint returnedLength);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplaySettingsW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNumber, ref DevMode deviceMode);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr deviceContext, int index);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public ushort SpecVersion;
        public ushort DriverVersion;
        public ushort Size;
        public ushort DriverExtra;
        public uint Fields;
        public int PositionX;
        public int PositionY;
        public uint DisplayOrientation;
        public uint DisplayFixedOutput;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TTOption;
        public short Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public ushort LogPixels;
        public uint BitsPerPel;
        public uint PelsWidth;
        public uint PelsHeight;
        public uint DisplayFlags;
        public uint DisplayFrequency;
        public uint ICMMethod;
        public uint ICMIntent;
        public uint MediaType;
        public uint DitherType;
        public uint Reserved1;
        public uint Reserved2;
        public uint PanningWidth;
        public uint PanningHeight;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    private sealed record GpuInfo(string Name, long AdapterRam, long Width, long Height, long RefreshRate);
    private sealed record NvidiaMetrics(double? Temperature, double? Usage, string MemoryUsed);
}
