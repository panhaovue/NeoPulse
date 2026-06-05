using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Management;
using LibreHardwareMonitor.Hardware;

namespace TaskbarMonitor;

public sealed class SystemMonitor : IDisposable
{
    private PerformanceCounter? _cpuCounter;
    private readonly List<PerformanceCounter> _gpuCounters = [];
    private long _lastBytesSent;
    private long _lastBytesReceived;
    private DateTime _lastNetworkCheck = DateTime.Now;
    private bool _disposed;
    private TaskbarMonitor.Settings.NvmlHelper? _nvml;
    private Computer? _computer;

    public double CpuUsage { get; private set; }
    public double GpuUsage { get; private set; }
    public double MemoryPercent { get; private set; }
    public ulong MemoryUsedBytes { get; private set; }
    public ulong MemoryTotalBytes { get; private set; }
    public double UploadSpeed { get; private set; }
    public double DownloadSpeed { get; private set; }
    public double CpuTemp { get; private set; }
    public double GpuTemp { get; private set; }
    public int GpuPowerMw { get; private set; }

    public SystemMonitor()
    {
        _nvml = new TaskbarMonitor.Settings.NvmlHelper();
        InitCpu();
        InitGpu();
        InitNetwork();
        InitHardwareMonitor();
    }

    private void InitHardwareMonitor()
    {
        try
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
            };
            _computer.Open();
        }
        catch { _computer = null; }
    }

    private void InitCpu()
    {
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue();
        }
        catch { _cpuCounter = null; }
    }

    private void InitGpu()
    {
        try
        {
            if (PerformanceCounterCategory.Exists("GPU Engine"))
            {
                var cat = new PerformanceCounterCategory("GPU Engine");
                foreach (var name in cat.GetInstanceNames())
                {
                    if (name.Contains("engtype_3D") || name.Contains("engtype_Compute") || name.Contains("engtype_Copy") || name.Contains("engtype_Display"))
                    {
                        try
                        {
                            var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", name);
                            counter.NextValue();
                            _gpuCounters.Add(counter);
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }
        if (_gpuCounters.Count == 0) InitGpuFallback();
    }

    private void InitGpuFallback()
    {
        try
        {
            if (PerformanceCounterCategory.Exists("GPU Adapter Memory"))
            {
                var cat = new PerformanceCounterCategory("GPU Adapter Memory");
                foreach (var name in cat.GetInstanceNames())
                {
                    try
                    {
                        var counter = new PerformanceCounter("GPU Adapter Memory", "Dedicated Usage", name);
                        counter.NextValue();
                        _gpuCounters.Add(counter);
                    }
                    catch { }
                }
                if (_gpuCounters.Count > 0) return;
            }
        }
        catch { }

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
            foreach (ManagementObject obj in searcher.Get())
            {
                var engine = Convert.ToString(obj["Name"]);
                if (engine.Contains("3D") || engine.Contains("Compute") || engine.Contains("Copy") || engine.Contains("Render"))
                {
                    if (obj["PercentTime"] != null)
                    {
                        try
                        {
                            var val = Convert.ToDouble(obj["PercentTime"]);
                            if (val > 0)
                            {
                                var name = Convert.ToString(obj["Name"]);
                                var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", name);
                                counter.NextValue();
                                _gpuCounters.Add(counter);
                            }
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }

        if (_gpuCounters.Count == 0)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController WHERE AdapterCompatibility IS NOT NULL");
                foreach (ManagementObject obj in searcher.Get())
                {
                    if (obj["CurrentHorizontalResolution"] != null && obj["CurrentVerticalResolution"] != null)
                    {
                        try
                        {
                            var name = Convert.ToString(obj["Name"]);
                            var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", name);
                            counter.NextValue();
                            _gpuCounters.Add(counter);
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }
    }

    private void InitNetwork()
    {
        var (sent, recv) = GetNetworkTotals();
        _lastBytesSent = sent;
        _lastBytesReceived = recv;
    }

    public void Update()
    {
        UpdateCpu();
        UpdateGpu();
        UpdateMemory();
        UpdateNetwork();
        UpdateTemperatures();
        UpdateGpuPower();
    }

    private void UpdateCpu()
    {
        try { CpuUsage = _cpuCounter?.NextValue() ?? 0; }
        catch { CpuUsage = 0; }
    }

    private void UpdateGpu()
    {
        // Try NVML first (most reliable for NVIDIA GPUs)
        if (_nvml != null)
        {
            var (nvmlGpu, _) = _nvml.GetUtilization();
            if (nvmlGpu > 0) { GpuUsage = nvmlGpu; return; }
        }
        // Fallback to PerformanceCounters
        try
        {
            float gpu = 0;
            foreach (var c in _gpuCounters) gpu += c.NextValue();
            GpuUsage = Math.Min(gpu, 100);
        }
        catch { GpuUsage = 0; }
    }

    private void UpdateMemory()
    {
        try
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref m))
            {
                MemoryPercent = m.dwMemoryLoad;
                MemoryUsedBytes = m.ullTotalPhys - m.ullAvailPhys;
                MemoryTotalBytes = m.ullTotalPhys;
            }
        }
        catch { }
    }

    private void UpdateNetwork()
    {
        try
        {
            var now = DateTime.Now;
            var elapsed = (now - _lastNetworkCheck).TotalSeconds;
            if (elapsed < 0.5) return;

            var (sent, recv) = GetNetworkTotals();
            UploadSpeed = (sent - _lastBytesSent) / elapsed;
            DownloadSpeed = (recv - _lastBytesReceived) / elapsed;
            _lastBytesSent = sent;
            _lastBytesReceived = recv;
            _lastNetworkCheck = now;
        }
        catch
        {
            UploadSpeed = 0;
            DownloadSpeed = 0;
        }
    }

    private static (long sent, long recv) GetNetworkTotals()
    {
        long sent = 0, recv = 0;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            // Skip virtual/tunnel adapters (VPN, WSL, Hyper-V, Docker, etc.)
            string desc = (nic.Description ?? "").ToLowerInvariant();
            if (desc.Contains("virtual") || desc.Contains("tunnel") ||
                desc.Contains("hyper-v") || desc.Contains("pseudo") ||
                desc.Contains("vpn") || desc.Contains("wsl") ||
                desc.Contains("docker") || desc.Contains("bluetooth"))
                continue;
            // Also skip adapters with no speed or very low speed (virtual adapters)
            if (nic.Speed == -1 || nic.Speed == 0 || nic.Speed < 100000000)
                continue;
            // Skip adapters that don't have a valid IPv4 address
            bool hasIp = false;
            foreach (var ip in nic.GetIPProperties().UnicastAddresses)
            {
                if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                { hasIp = true; break; }
            }
            if (!hasIp) continue;

            try
            {
                var stats = nic.GetIPStatistics();
                sent += stats.BytesSent;
                recv += stats.BytesReceived;
            }
            catch { }
        }
        return (sent, recv);
    }

    public static string FormatSpeed(double bytesPerSec)
    {
        return bytesPerSec switch
        {
            < 1024 => $"{bytesPerSec:F0} B/s",
            < 1024 * 1024 => $"{bytesPerSec / 1024:F1} KB/s",
            < 1024 * 1024 * 1024 => $"{bytesPerSec / (1024 * 1024):F1} MB/s",
            _ => $"{bytesPerSec / (1024 * 1024 * 1024):F2} GB/s"
        };
    }

    public static string FormatBytes(ulong bytes)
    {
        return bytes switch
        {
            < 1024UL => $"{bytes} B",
            < 1024UL * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024UL * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB"
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    public void Dispose()
    {
        if (_disposed) return;
        _cpuCounter?.Dispose();
        foreach (var c in _gpuCounters) c.Dispose();
        _nvml?.Dispose();
        try { _computer?.Close(); } catch { }
        _disposed = true;
    }

    private void UpdateGpuPower()
    {
        GpuPowerMw = -1;
        // NVML power (most accurate for NVIDIA)
        if (_nvml != null)
        {
            int p = _nvml.GetPower();
            if (p > 0) { GpuPowerMw = p * 1000; return; }
        }
        // LibreHardwareMonitor fallback
        if (_computer != null)
        {
            try
            {
                foreach (var hardware in _computer.Hardware)
                {
                    if (hardware.HardwareType != HardwareType.GpuNvidia &&
                        hardware.HardwareType != HardwareType.GpuAmd &&
                        hardware.HardwareType != HardwareType.GpuIntel) continue;
                    hardware.Update();
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Power && sensor.Value.HasValue)
                        {
                            int mw = (int)(sensor.Value.Value * 1000.0);
                            if (mw > 0 && mw < 500000) { GpuPowerMw = mw; break; }
                        }
                    }
                    if (GpuPowerMw > 0) break;
                }
            }
            catch { }
        }
        // LibreHardwareMonitor sub-hardware fallback
        if (GpuPowerMw < 0 && _computer != null)
        {
            try
            {
                foreach (var hardware in _computer.Hardware)
                {
                    if (hardware.HardwareType != HardwareType.GpuNvidia &&
                        hardware.HardwareType != HardwareType.GpuAmd &&
                        hardware.HardwareType != HardwareType.GpuIntel) continue;
                    foreach (var sub in hardware.SubHardware)
                    {
                        sub.Update();
                        foreach (var sensor in sub.Sensors)
                        {
                            if (sensor.SensorType == SensorType.Power && sensor.Value.HasValue)
                            {
                                int mw = (int)(sensor.Value.Value * 1000.0);
                                if (mw > 0 && mw < 500000) { GpuPowerMw = mw; break; }
                            }
                        }
                        if (GpuPowerMw > 0) break;
                    }
                    if (GpuPowerMw > 0) break;
                }
            }
            catch { }
        }
    }

    private void UpdateTemperatures()
    {
        CpuTemp = -1;
        GpuTemp = -1;

        // CPU temperature: Win32_PerfFormattedData_Counters_ThermalZoneInformation (no admin required)
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\cimv2",
                "SELECT HighPrecisionTemperature, Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");
            foreach (ManagementObject obj in searcher.Get())
            {
                // HighPrecisionTemperature is in tenths of Kelvin
                if (obj["HighPrecisionTemperature"] != null)
                {
                    double hp = Convert.ToDouble(obj["HighPrecisionTemperature"]);
                    double celsius = (hp / 10.0) - 273.15;
                    if (celsius > 0 && celsius < 150) { CpuTemp = celsius; break; }
                }
                else if (obj["Temperature"] != null)
                {
                    double t = Convert.ToDouble(obj["Temperature"]);
                    double celsius = t - 273.15;
                    if (celsius > 0 && celsius < 150) { CpuTemp = celsius; break; }
                }
            }
        }
        catch { }

        // GPU temperature: LibreHardwareMonitor (no admin for GPU)
        if (_computer != null)
        {
            try
            {
                foreach (var hardware in _computer.Hardware)
                {
                    if (hardware.HardwareType != HardwareType.GpuNvidia &&
                        hardware.HardwareType != HardwareType.GpuAmd &&
                        hardware.HardwareType != HardwareType.GpuIntel)
                        continue;

                    hardware.Update();
                    foreach (var sub in hardware.SubHardware)
                        sub.Update();

                    var (_, g1) = CheckHardwareSensors(hardware, -1, GpuTemp);
                    GpuTemp = g1;

                    foreach (var sub in hardware.SubHardware)
                    {
                        var (_, g2) = CheckHardwareSensors(sub, -1, GpuTemp);
                        GpuTemp = g2;
                    }
                    if (GpuTemp > 0) break;
                }
            }
            catch { }
        }

        // GPU fallback: NVML (NVIDIA)
        if (GpuTemp < 0 && _nvml != null)
        {
            int gpuT = _nvml.GetTemperature();
            if (gpuT > 0) GpuTemp = gpuT;
        }


        // CPU fallback: WMI thermal zones (requires admin)
        if (CpuTemp < 0)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM MSAcpi_ThermalZoneTemperature");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var temp = Convert.ToDouble(obj["CurrentTemperature"]);
                    double celsius = (temp / 10.0) - 273.15;
                    if (celsius > 0 && celsius < 150) { CpuTemp = celsius; break; }
                }
            }
            catch { }
        }
    }

    private static (double cpu, double gpu) CheckHardwareSensors(IHardware hardware, double cpuTemp, double gpuTemp)
    {
        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.SensorType != SensorType.Temperature || sensor.Value == null)
                continue;
            float temp = sensor.Value.Value;
            if (temp <= 0 || temp > 150) continue;

            string name = (sensor.Name ?? "").ToLowerInvariant();

            if (hardware.HardwareType == HardwareType.Cpu)
            {
                if (cpuTemp < 0 || name.Contains("package") || name.Contains("tdie") || name.Contains("average"))
                    cpuTemp = temp;
            }
            else if (hardware.HardwareType == HardwareType.GpuNvidia ||
                     hardware.HardwareType == HardwareType.GpuAmd ||
                     hardware.HardwareType == HardwareType.GpuIntel)
            {
                if (gpuTemp < 0)
                    gpuTemp = temp;
            }
        }
        return (cpuTemp, gpuTemp);
    }
}
