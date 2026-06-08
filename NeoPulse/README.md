# Monitor

> [中文版 README](README_zh.md)

A lightweight Windows system monitor floating widget, built with WinUI 3.

![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![WinUI](https://img.shields.io/badge/WinUI-3-0078D4)
![License](https://img.shields.io/badge/License-MIT-green)

## Features

- CPU / GPU / Memory / Network real-time monitoring (0.5s refresh)
- Mica / Mica Alt / Acrylic background materials
- Horizontal / Vertical layout

## Run

```bash
dotnet run
```

## Publish Single File

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## Architecture

```
MainWindow ──PropertyChanged──► AppSettings (singleton)
                                    │
                                    ▼
                              WidgetViewModel ──► WidgetWindow
                                    ▲
                                    │
                              PeriodicTimer (0.5s, background thread)
                                    │
                              SystemMonitor.Update()
```

## Project Structure

```
Monitor/
├── ViewModels/
│   ├── ViewModelBase.cs
│   └── WidgetViewModel.cs
├── AppSettings.cs
├── WidgetWindow.xaml/.cs
├── MainWindow.xaml/.cs
├── SystemMonitor.cs
├── Win32.cs
├── Logger.cs
├── Translations.cs
└── Monitor.csproj
```

## Dependencies

| Package | Version | Purpose |
|---|---|---|
| CommunityToolkit.Mvvm | 8.4.0 | MVVM |
| Microsoft.WindowsAppSDK | 2.1.3 | WinUI 3 |
| LibreHardwareMonitorLib | 0.9.6 | Sensors |
| System.Drawing.Common | 10.0.0 | Icons |
| System.Diagnostics.PerformanceCounter | 10.0.0 | CPU usage |
| System.Management | 10.0.2 | WMI |
| Microsoft.Win32.SystemEvents | 10.0.8 | Theme events |

Config path: `%LOCALAPPDATA%\Monitor\settings.json`

MIT License
