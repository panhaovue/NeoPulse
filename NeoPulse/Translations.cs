namespace NeoPulse;

public static class Translations
{
    public static string Lang { get; set; } = "zh";

    public static string Get(string key) => Lang == "en" ? En[key] : Zh[key];

    private static readonly Dictionary<string, string> Zh = new()
    {
        // MainWindow
        ["DisplayItems"] = "显示项目",
        ["NetworkSpeed"] = "网络速度",
        ["Cpu"] = "CPU",
        ["CpuDesc"] = "使用率, 温度, 功率",
        ["Gpu"] = "GPU",
        ["GpuDesc"] = "使用率, 温度, 功率",
        ["MemoryUsage"] = "内存使用",
        ["Appearance"] = "外观",
        ["Theme"] = "主题",
        ["Auto"] = "自动",
        ["Light"] = "亮色",
        ["Dark"] = "暗色",
        ["BackgroundEffect"] = "背景效果",
        ["CardLayout"] = "卡片布局",
        ["Horizontal"] = "横向",
        ["Vertical"] = "纵向",
        ["Language"] = "语言",
        ["AutoSystem"] = "自动",
        ["LightTheme"] = "亮色",
        ["DarkTheme"] = "暗色",
        ["HorizontalCard"] = "横向",
        ["VerticalCard"] = "纵向",
        ["Upload"] = "↑",
        ["Download"] = "↓",
        ["CpuLabel"] = "CPU",
        ["GpuLabel"] = "GPU",
        ["RamLabel"] = "RAM",
        ["On"] = "开",
        ["Off"] = "关",
        ["OpenLogs"] = "打开日志文件夹",
        ["Settings"] = "设置",
        ["Exit"] = "退出",
    };

    private static readonly Dictionary<string, string> En = new()
    {
        ["DisplayItems"] = "Display Items",
        ["NetworkSpeed"] = "Network Speed",
        ["Cpu"] = "CPU",
        ["CpuDesc"] = "Usage, Temperature, Power",
        ["Gpu"] = "GPU",
        ["GpuDesc"] = "Usage, Temperature, Power",
        ["MemoryUsage"] = "Memory Usage",
        ["Appearance"] = "Appearance",
        ["Theme"] = "Theme",
        ["Auto"] = "Auto (System)",
        ["Light"] = "Light",
        ["Dark"] = "Dark",
        ["BackgroundEffect"] = "Background Effect",
        ["CardLayout"] = "Card Layout",
        ["Horizontal"] = "Horizontal",
        ["Vertical"] = "Vertical",
        ["Language"] = "Language",
        ["Upload"] = "↑",
        ["Download"] = "↓",
        ["CpuLabel"] = "CPU",
        ["GpuLabel"] = "GPU",
        ["RamLabel"] = "RAM",
        ["On"] = "On",
        ["Off"] = "Off",
        ["OpenLogs"] = "Open Logs Folder",
        ["Settings"] = "Settings",
        ["Exit"] = "Exit",
    };
}

