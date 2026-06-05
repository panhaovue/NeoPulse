using System.Runtime.InteropServices;
using System.Diagnostics;

namespace TaskbarMonitor.Settings;

internal sealed class NvmlHelper : IDisposable
{
    private bool _available;
    private bool _useSmi;
    private IntPtr _device;
    private DateTime _lastSmi = DateTime.MinValue;
    private float _smiGpu, _smiMem;
    private int _smiTemp, _smiPower;
    private bool _disposed;
    private string? _smiPath;

    public bool Available => _available;

    // Pre-load nvml.dll from known paths before any P/Invoke call
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    static NvmlHelper()
    {
        string[] dllPaths =
        [
            @"C:\Windows\System32\nvml.dll",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"NVIDIA Corporation\NVSMI\nvml.dll"),
        ];
        foreach (var p in dllPaths)
        {
            try
            {
                if (File.Exists(p) && LoadLibrary(p) != IntPtr.Zero)
                    break;
            }
            catch { }
        }
    }

    public NvmlHelper()
    {
        // Try NVML API first
        try
        {
            int ret = nvmlInit_v2();
            if (ret != 0) ret = nvmlInit();
            if (ret != 0) throw new Exception("nvmlInit failed: " + ret);
            ret = nvmlDeviceGetHandleByIndex_v2(0, out _device);
            if (ret != 0) ret = nvmlDeviceGetHandleByIndex(0, out _device);
            if (ret != 0) throw new Exception("nvmlDeviceGetHandle failed: " + ret);
            _available = true;
        }
        catch
        {
            _available = false;
        }

        // Always locate nvidia-smi as fallback
        _smiPath = FindNvidiaSmi();
        if (!_available && _smiPath != null)
            _useSmi = true;
    }

    private static string? FindNvidiaSmi()
    {
        string[] paths =
        [
            @"C:\Windows\System32\nvidia-smi.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"NVIDIA Corporation\NVSMI\nvidia-smi.exe"),
            @"C:\Program Files (x86)\NVIDIA Corporation\NVSMI\nvidia-smi.exe",
        ];
        foreach (var p in paths)
            if (File.Exists(p)) return p;
        // Last resort: try PATH
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = "nvidia-smi",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string line = proc.StandardOutput.ReadLine() ?? "";
                proc.WaitForExit();
                if (!string.IsNullOrWhiteSpace(line) && File.Exists(line.Trim()))
                    return line.Trim();
            }
        }
        catch { }
        return null;
    }

    public (float gpu, float mem) GetUtilization()
    {
        if (_available)
        {
            try
            {
                int ret = nvmlDeviceGetUtilizationRates(_device, out var util);
                if (ret == 0) return (util.gpu, util.mem);
            }
            catch { }
        }
        if (_useSmi) { RunSmi(); return (_smiGpu, _smiMem); }
        return (0, 0);
    }

    public int GetTemperature()
    {
        if (_available)
        {
            try
            {
                int ret = nvmlDeviceGetTemperature(_device, 0, out uint temp);
                if (ret == 0) return (int)temp;
            }
            catch { }
        }
        if (_useSmi) { RunSmi(); return _smiTemp; }
        return -1;
    }

    public int GetPower()
    {
        if (_available)
        {
            try
            {
                int ret = nvmlDeviceGetPowerUsage(_device, out uint power);
                if (ret == 0) return (int)(power / 1000.0 + 0.5);
            }
            catch { }
        }
        if (_useSmi) { RunSmi(); return _smiPower; }
        return -1;
    }

    private void RunSmi()
    {
        if ((DateTime.Now - _lastSmi).TotalSeconds < 3) return;
        _lastSmi = DateTime.Now;
        try
        {
            if (_smiPath == null) return;
            var psi = new ProcessStartInfo
            {
                FileName = _smiPath,
                Arguments = "--query-gpu=temperature.gpu,utilization.gpu,utilization.memory,power.draw --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) return;
            string line = proc.StandardOutput.ReadLine() ?? "";
            proc.WaitForExit(3000);
            var parts = line.Split(',');
            if (parts.Length >= 4)
            {
                int.TryParse(parts[0].Trim(), out _smiTemp);
                float.TryParse(parts[1].Trim(), out _smiGpu);
                float.TryParse(parts[2].Trim(), out _smiMem);
                float.TryParse(parts[3].Trim(), out float watts);
                _smiPower = (int)(watts + 0.5f);
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_available) try { nvmlShutdown(); } catch { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct nvmlUtilization { public uint gpu; public uint mem; }

    [DllImport("nvml.dll")] private static extern int nvmlInit();
    [DllImport("nvml.dll")] private static extern int nvmlInit_v2();
    [DllImport("nvml.dll")] private static extern int nvmlShutdown();
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetHandleByIndex(int index, out IntPtr device);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetHandleByIndex_v2(int index, out IntPtr device);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetTemperature(IntPtr device, int sensorType, out uint temp);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out nvmlUtilization utilization);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint power);
}