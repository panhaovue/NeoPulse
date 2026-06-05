using System.Text.Json;

namespace TaskbarMonitor.Settings;

public class AppSettings
{
    public bool ShowCpu { get; set; } = true;
    public bool ShowGpu { get; set; } = true;
    public bool ShowNetwork { get; set; } = true;
    public bool ShowMemory { get; set; } = true;
    public string Position { get; set; } = "right";
    public string LabelFont { get; set; } = "Segoe UI";
    public string ValueFont { get; set; } = "Consolas";
    public string ThemeMode { get; set; } = "auto";
    public string Backdrop { get; set; } = "Mica";

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TaskbarMonitor",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
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

    public static bool IsSystemDarkMode()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize", false);
            if (key != null)
            {
                var val = key.GetValue("AppsUseLightTheme");
                if (val is int i)
                    return i == 0;
            }
        }
        catch { }
        return true;
    }
}
