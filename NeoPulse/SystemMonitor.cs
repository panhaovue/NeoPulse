using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Management;
using LibreHardwareMonitor.Hardware;

namespace NeoPulse;

public sealed partial class SystemMonitor : IDisposable
{
    private PerformanceCounter? _cpuCounter;
    private readonly List<PerformanceCounter> _gpuCounters = [];
    private long _lastBytesSent;
    private long _lastBytesReceived;
    private DateTime _lastNetworkCheck = DateTime.Now;
    private bool _disposed;
    private Computer? _computer;

    // Cached at startup from WMI
    private string _cpuName = "";
    private string _gpuName = "";
    private bool _isWmiSupported;

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
    public int CpuPowerMw { get; private set; }

    public SystemMonitor()
    {
        InitCpu();
        InitGpu();
        InitNetwork();
        InitHardwareMonitor();
        ProbeWmiSupport();
        InitWmiHardwareInfo();
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

    private void ProbeWmiSupport()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
            using var results = searcher.Get();
            _isWmiSupported = results.Count > 0;
        }
        catch
        {
            _isWmiSupported = false;
            Logger.Info("WMI probe failed (non-admin or restricted), skipping WMI queries");
        }
    }

    private void InitWmiHardwareInfo()
    {
        if (!_isWmiSupported) return;
        // Cache CPU name and TDP at startup
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, MaxClockSpeed FROM Win32_Processor");
            foreach (ManagementObject obj in searcher.Get())
            {
                _cpuName = Convert.ToString(obj["Name"]) ?? "";
                break;
            }
        }
        catch { }
        // Cache GPU name
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController WHERE AdapterCompatibility IS NOT NULL");
            foreach (ManagementObject obj in searcher.Get())
            {
                _gpuName = Convert.ToString(obj["Name"]) ?? "";
                break;
            }
        }
        catch { }
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

        if (!_isWmiSupported) return;
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

        if (_gpuCounters.Count == 0 && _isWmiSupported)
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
        try { UpdateCpu(); } catch (Exception ex) { Logger.Warn($"UpdateCpu: {ex.Message}"); }
        try { UpdateGpu(); } catch (Exception ex) { Logger.Warn($"UpdateGpu: {ex.Message}"); }
        try { UpdateMemory(); } catch (Exception ex) { Logger.Warn($"UpdateMemory: {ex.Message}"); }
        try { UpdateNetwork(); } catch (Exception ex) { Logger.Warn($"UpdateNetwork: {ex.Message}"); }
        try { UpdateHardwareSensors(); } catch (Exception ex) { Logger.Warn($"UpdateHardwareSensors: {ex.Message}"); }
    }

    private void UpdateCpu()
    {
        try { CpuUsage = _cpuCounter?.NextValue() ?? 0; }
        catch { CpuUsage = 0; }
    }

    private void UpdateGpu()
    {
        // Use LibreHardwareMonitor GPU load sensor
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
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Load && sensor.Value.HasValue && sensor.Value.Value > 0)
                        {
                            GpuUsage = sensor.Value.Value;
                            return;
                        }
                    }
                }
            }
            catch { }
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
            < 1024              => string.Create(8, bytesPerSec, (d, v) => AppendNum(d, v, 0, " B/s")),
            < 1024 * 1024       => string.Create(12, bytesPerSec / 1024, (d, v) => AppendNum(d, v, 1, " KB/s")),
            < 1024 * 1024 * 1024 => string.Create(12, bytesPerSec / (1024 * 1024), (d, v) => AppendNum(d, v, 1, " MB/s")),
            _                   => string.Create(14, bytesPerSec / (1024.0 * 1024 * 1024), (d, v) => AppendNum(d, v, 2, " GB/s")),
        };
    }

    public static string FormatBytes(ulong bytes)
    {
        return bytes switch
        {
            < 1024UL               => string.Create(6, (double)bytes, (d, v) => AppendNum(d, v, 0, " B")),
            < 1024UL * 1024        => string.Create(10, bytes / 1024.0, (d, v) => AppendNum(d, v, 1, " KB")),
            < 1024UL * 1024 * 1024 => string.Create(10, bytes / (1024.0 * 1024), (d, v) => AppendNum(d, v, 1, " MB")),
            _                      => string.Create(10, bytes / (1024.0 * 1024 * 1024), (d, v) => AppendNum(d, v, 1, " GB")),
        };
    }

    private static void AppendNum(Span<char> dst, double value, int decimals, string suffix)
    {
        string num = decimals == 0 ? ((long)value).ToString() : value.ToString($"F{decimals}");
        num.AsSpan().CopyTo(dst);
        suffix.AsSpan().CopyTo(dst.Slice(num.Length));
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

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    public void Dispose()
    {
        if (_disposed) return;
        if (_cpuCounter != null) try { _cpuCounter.Dispose(); } catch { }
        foreach (var c in _gpuCounters) try { c.Dispose(); } catch { }
        try { _computer?.Close(); } catch { }
        _disposed = true;
    }

    private void UpdateHardwareSensors()
    {
        CpuTemp = -1;
        GpuTemp = -1;
        CpuPowerMw = -1;
        GpuPowerMw = -1;

        if (_computer == null) return;

        try
        {
            foreach (var hardware in _computer.Hardware)
            {
                hardware.Update();
                foreach (var sub in hardware.SubHardware) sub.Update();

                var (cpuT, gpuT) = CheckHardwareSensors(hardware, CpuTemp, GpuTemp);
                if (cpuT > 0) CpuTemp = cpuT;
                if (gpuT > 0) GpuTemp = gpuT;

                // Power sensors
                foreach (var sensor in hardware.Sensors)
                {
                    if (!sensor.Value.HasValue) continue;
                    if (sensor.SensorType == SensorType.Power && sensor.Value.Value > 0)
                    {
                        int mw = (int)(sensor.Value.Value * 1000.0);
                        if (hardware.HardwareType == HardwareType.Cpu && CpuPowerMw < 0)
                            CpuPowerMw = mw;
                        else if ((hardware.HardwareType == HardwareType.GpuNvidia ||
                                  hardware.HardwareType == HardwareType.GpuAmd ||
                                  hardware.HardwareType == HardwareType.GpuIntel) && GpuPowerMw < 0 && mw < 500000)
                            GpuPowerMw = mw;
                    }
                }
                foreach (var sub in hardware.SubHardware)
                {
                    var (c2, g2) = CheckHardwareSensors(sub, CpuTemp, GpuTemp);
                    if (c2 > 0) CpuTemp = c2;
                    if (g2 > 0) GpuTemp = g2;

                    foreach (var sensor in sub.Sensors)
                    {
                        if (!sensor.Value.HasValue) continue;
                        if (sensor.SensorType == SensorType.Power && sensor.Value.Value > 0)
                        {
                            int mw = (int)(sensor.Value.Value * 1000.0);
                            if (hardware.HardwareType == HardwareType.Cpu && CpuPowerMw < 0)
                                CpuPowerMw = mw;
                            else if ((hardware.HardwareType == HardwareType.GpuNvidia ||
                                      hardware.HardwareType == HardwareType.GpuAmd ||
                                      hardware.HardwareType == HardwareType.GpuIntel) && GpuPowerMw < 0 && mw < 500000)
                                GpuPowerMw = mw;
                        }
                    }
                }

                if (CpuTemp > 0 && GpuTemp > 0 && CpuPowerMw > 0 && GpuPowerMw > 0)
                    break;
            }
        }
        catch { }

        // WMI fallback for CPU temp and power when not admin
        if (_isWmiSupported)
        {
            if (CpuTemp < 0)
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT HighPrecisionTemperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        var raw = Convert.ToDouble(obj["HighPrecisionTemperature"]);
                        if (raw > 0) { CpuTemp = raw / 10.0 - 273.15; break; }
                    }
                }
                catch { }
            }
            if (CpuPowerMw < 0 && CpuUsage > 0)
            {
                // Estimate power from CPU usage and TDP
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT TDP FROM Win32_Processor");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        var tdp = Convert.ToDouble(obj["TDP"]);
                        if (tdp > 0) { CpuPowerMw = (int)(tdp * CpuUsage / 100.0 * 1000); break; }
                    }
                }
                catch { }
                // Fallback: assume 45W TDP
                if (CpuPowerMw < 0)
                    CpuPowerMw = (int)(45.0 * CpuUsage / 100.0 * 1000);
            }
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
