using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using NeoPulse.ViewModels;

namespace NeoPulse;

#pragma warning disable MVVMTK0045 // WinUI 3 AOT partial property warning

public partial class AppSettings : ViewModelBase
{
    private static AppSettings? _instance;
    public static AppSettings Instance => _instance ??= Load();

    [ObservableProperty] private bool _showCpu = true;
    [ObservableProperty] private bool _showGpu = true;
    [ObservableProperty] private bool _showNetwork = true;
    [ObservableProperty] private bool _showMemory = true;

    [ObservableProperty] private string _layout = "horizontal";
    [ObservableProperty] private string _language = "zh";
    [ObservableProperty] private string _themeMode = "auto";
    [ObservableProperty] private string _backdrop = "Mica";

    public int LastX { get; set; } = -1;
    public int LastY { get; set; } = -1;

    // ── Index helpers for ComboBox TwoWay binding ──

    [ObservableProperty] private int _themeModeIndex;
    [ObservableProperty] private int _backdropIndex;
    [ObservableProperty] private int _layoutIndex;
    [ObservableProperty] private int _langIndex;

    partial void OnThemeModeIndexChanged(int value)
    {
        ThemeMode = value switch { 1 => "light", 2 => "dark", _ => "auto" };
    }
    partial void OnBackdropIndexChanged(int value)
    {
        Backdrop = value switch { 1 => "Mica Alt", 2 => "Acrylic", _ => "Mica" };
    }
    partial void OnLayoutIndexChanged(int value)
    {
        Layout = value == 1 ? "vertical" : "horizontal";
    }
    partial void OnLangIndexChanged(int value)
    {
        Language = value == 1 ? "en" : "zh";
    }

    // ── Translation labels ──

    public string LabelDisplayItems => Translations.Get("DisplayItems");
    public string LabelNetworkSpeed => Translations.Get("NetworkSpeed");
    public string LabelCpu => Translations.Get("Cpu");
    public string LabelCpuDesc => Translations.Get("CpuDesc");
    public string LabelGpu => Translations.Get("Gpu");
    public string LabelGpuDesc => Translations.Get("GpuDesc");
    public string LabelMemory => Translations.Get("MemoryUsage");
    public string LabelAppearance => Translations.Get("Appearance");
    public string LabelTheme => Translations.Get("Theme");
    public string LabelBackdrop => Translations.Get("BackgroundEffect");
    public string LabelLayout => Translations.Get("CardLayout");
    public string LabelLanguage => Translations.Get("Language");
    public string LabelOn => Translations.Get("On");
    public string LabelOff => Translations.Get("Off");
    public string LabelOpenLogs => Translations.Get("OpenLogs");

    // ── Toggle enable states ──

    [ObservableProperty] private bool _isCpuEnabled = true;
    [ObservableProperty] private bool _isGpuEnabled = true;
    [ObservableProperty] private bool _isNetworkEnabled = true;
    [ObservableProperty] private bool _isMemoryEnabled = true;

    partial void OnShowCpuChanged(bool value) => UpdateToggleStates();
    partial void OnShowGpuChanged(bool value) => UpdateToggleStates();
    partial void OnShowNetworkChanged(bool value) => UpdateToggleStates();
    partial void OnShowMemoryChanged(bool value) => UpdateToggleStates();

    partial void OnThemeModeChanged(string value)
    {
        ThemeModeIndex = value switch { "light" => 1, "dark" => 2, _ => 0 };
    }
    partial void OnBackdropChanged(string value)
    {
        BackdropIndex = value switch { "Mica Alt" => 1, "Acrylic" => 2, _ => 0 };
    }
    partial void OnLayoutChanged(string value)
    {
        LayoutIndex = value == "vertical" ? 1 : 0;
    }
    partial void OnLanguageChanged(string value)
    {
        Translations.Lang = value;
        LangIndex = value == "en" ? 1 : 0;
        OnPropertyChanged(nameof(Language));
        OnPropertyChanged(nameof(LabelDisplayItems));
        OnPropertyChanged(nameof(LabelNetworkSpeed));
        OnPropertyChanged(nameof(LabelCpu));
        OnPropertyChanged(nameof(LabelCpuDesc));
        OnPropertyChanged(nameof(LabelGpu));
        OnPropertyChanged(nameof(LabelGpuDesc));
        OnPropertyChanged(nameof(LabelMemory));
        OnPropertyChanged(nameof(LabelAppearance));
        OnPropertyChanged(nameof(LabelTheme));
        OnPropertyChanged(nameof(LabelBackdrop));
        OnPropertyChanged(nameof(LabelLayout));
        OnPropertyChanged(nameof(LabelLanguage));
        OnPropertyChanged(nameof(LabelOn));
        OnPropertyChanged(nameof(LabelOff));
        OnPropertyChanged(nameof(LabelOpenLogs));
    }

    // ── Persistence ──

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NeoPulse",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var s = JsonSerializer.Deserialize<AppSettings>(json);
                if (s != null)
                {
                    s.UpdateToggleStates();
                    return s;
                }
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }

    private void UpdateToggleStates()
    {
        int cnt = (ShowCpu ? 1 : 0) + (ShowGpu ? 1 : 0) + (ShowNetwork ? 1 : 0) + (ShowMemory ? 1 : 0);
        bool one = cnt <= 1;
        IsCpuEnabled = !one || !ShowCpu;
        IsGpuEnabled = !one || !ShowGpu;
        IsNetworkEnabled = !one || !ShowNetwork;
        IsMemoryEnabled = !one || !ShowMemory;
    }

    public void NotifyAll()
    {
        OnPropertyChanged((string?)null);
    }
}
