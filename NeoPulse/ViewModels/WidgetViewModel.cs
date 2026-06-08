using Microsoft.UI.Xaml;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NeoPulse.ViewModels;

#pragma warning disable MVVMTK0045

public sealed partial class WidgetViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings _settings;

    public WidgetViewModel(AppSettings settings)
    {
        _settings = settings;
        _settings.PropertyChanged += OnSettingChanged;
    }

    public void Dispose()
    {
        _settings.PropertyChanged -= OnSettingChanged;
    }

    // ── Settings passthrough ──

    public bool ShowCpu => _settings.ShowCpu;
    public bool ShowGpu => _settings.ShowGpu;
    public bool ShowNetwork => _settings.ShowNetwork;
    public bool ShowMemory => _settings.ShowMemory;

    public Visibility CpuCardVisibility => ShowCpu ? Visibility.Visible : Visibility.Collapsed;
    public Visibility GpuCardVisibility => ShowGpu ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NetworkCardVisibility => ShowNetwork ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MemCardVisibility => ShowMemory ? Visibility.Visible : Visibility.Collapsed;

    // ── Hardware data (set from code-behind timer) ──

    [ObservableProperty] private string _cpuText = "0%";
    [ObservableProperty] private string _cpuTempText = "--°C";
    [ObservableProperty] private string _cpuPowerText = "--W";
    [ObservableProperty] private string _gpuText = "0%";
    [ObservableProperty] private string _gpuTempText = "--°C";
    [ObservableProperty] private string _gpuPowerText = "--W";
    [ObservableProperty] private string _memText = "0 B";
    [ObservableProperty] private string _memPercentText = "0%";
    [ObservableProperty] private string _uploadText = "0 B/s";
    [ObservableProperty] private string _downloadText = "0 B/s";

    // ── Bar targets (set from data update) ──

    [ObservableProperty] private double _cpuBarTarget;
    [ObservableProperty] private double _gpuBarTarget;
    [ObservableProperty] private double _memBarTarget;

    // ── Current bar widths (lerped by animation timer, written to Border.Width) ──

    [ObservableProperty] private double _cpuBarWidth;
    [ObservableProperty] private double _gpuBarWidth;
    [ObservableProperty] private double _memBarWidth;

    // ── Refresh ──

    public void RefreshSettings()
    {
        OnPropertyChanged((string?)null);
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.ShowCpu) or nameof(AppSettings.ShowGpu)
            or nameof(AppSettings.ShowNetwork) or nameof(AppSettings.ShowMemory))
        {
            RefreshSettings();
        }
    }
}
