# NeoPulse

> [English README](README.md)

轻量高颜值 Windows 系统监控悬浮小组件，基于 WinUI 3。

## 功能

- CPU / GPU / 内存 / 网络实时监控（0.5s 刷新）
- Mica / Mica Alt / Acrylic 背景材质
- 横向 / 纵向布局

## 运行

```bash
dotnet run
```

## 发布单文件

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## 架构

```
MainWindow ──PropertyChanged──► AppSettings (单例)
                                    │
                                    ▼
                              WidgetViewModel ──► WidgetWindow
                                    ▲
                                    │
                              PeriodicTimer (0.5s, 后台线程)
                                    │
                              SystemMonitor.Update()
```

## 项目结构

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

## 依赖

| 包 | 版本 | 用途 |
|---|---|---|
| CommunityToolkit.Mvvm | 8.4.0 | MVVM |
| Microsoft.WindowsAppSDK | 2.1.3 | WinUI 3 |
| LibreHardwareMonitorLib | 0.9.6 | 传感器 |
| System.Drawing.Common | 10.0.0 | 图标 |
| System.Diagnostics.PerformanceCounter | 10.0.0 | CPU 使用率 |
| System.Management | 10.0.2 | WMI |
| Microsoft.Win32.SystemEvents | 10.0.8 | 主题事件 |

配置路径：`%LOCALAPPDATA%\Monitor\settings.json`

MIT License
