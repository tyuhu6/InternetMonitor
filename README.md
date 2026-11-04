# 网络监控 InternetMonitor

一个轻量的 Windows 网速监控小工具：实时显示整机上传/下载速度，并把读数贴到任务栏上。单文件 exe，双击即用，不依赖 .NET 运行时。

## 功能

- **实时速率**：整机下载/上传速度（含 IPv6 流量），每秒刷新
- **速率曲线**：最近 60 秒双色走势，一眼看出网络是否稳定
- **任务栏读数**：贴在通知区域左侧，随任务栏自动隐藏一起滑入滑出，鼠标可穿透、不抢焦点
- **流量统计**：今日 / 本周 / 本月累计流量 + 近 7 天柱状图（下方为下载，上方为上传）
- **外观自定义**：浅色/深色主题、字体、字号缩放、背景色与上下行强调色（内置 HSV 取色器）
- **窗口行为**：无边框圆角、整窗可拖动、窗口置顶、最小化到任务栏、点 × 关闭到托盘
- **托盘菜单**：右键可随时开关主窗口 / 任务栏读数 / 置顶
- **开机自启**：写入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，可在设置里开关
- **单实例运行**，并记住窗口位置

## 下载与使用

到 [Releases](../../releases) 页面下载 `InternetMonitor.exe`，双击运行即可。

- 系统要求：Windows 10 / 11（x64）
- 首次启动会稍慢 1~2 秒，单文件版会把内部原生库解压到 `%TEMP%\.net` 缓存
- 程序不写注册表以外的任何系统位置，配置和流量历史保存在 `%LOCALAPPDATA%\InternetMonitor\`

关闭主窗口只是收进托盘，真正退出请右键托盘图标选择「退出」。

## 自行构建

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

```powershell
# 调试运行
dotnet run

# 打包成单文件 exe（输出到 dist/）
dotnet publish -c Release -o dist
```

`InternetMonitor.csproj` 里已经固定了自包含 + 单文件 + 压缩的发布参数，所以 `dotnet publish` 出来的 `dist\InternetMonitor.exe` 可以直接拷到其它机器运行。

## 项目结构

```
InternetMonitor/
├── InternetMonitor.csproj     工程与发布配置
├── App.xaml(.cs)              应用入口、默认主题资源、单实例与全局异常兜底
├── MainWindow.xaml(.cs)       主窗口：速率、曲线、流量统计、托盘菜单
├── SettingsWindow.xaml(.cs)   设置窗口：显示项、字体字号、配色、开机自启
├── ColorPicker.xaml(.cs)      自绘取色器（SV 方块 + 色相条 + HEX + 预设色）
├── TaskbarSpeedWindow.xaml(.cs) 任务栏贴边读数
├── SpeedChart.cs              60 秒速率曲线（贝塞尔平滑 + 渐变填充）
├── UsageBars.cs               近 7 天堆叠柱状图
├── ColorWidgets.cs            HSV 取色控件与颜色换算
├── AppIcons.cs                托盘/应用矢量图标绘制
├── NetworkStatsReader.cs      基于 GetIfEntry2 的网卡字节采集
├── AppState.cs                状态与流量历史持久化
├── Appearance.cs              主题色派生与运行时资源写入
└── StartupManager.cs          开机自启注册表读写
```

## 实现要点

- 网卡统计走 `GetIfEntry2` + `MIB_IF_ROW2`，直接读系统累计收发字节，包含虚拟网卡与 IPv6 流量
- 任务栏读数用 `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT` 做无焦点穿透窗口，按 16ms 跟随 `Shell_TrayWnd` 的位置，自动隐藏时与任务栏同进同退
- 全屏应用或窗口最大化时自动隐藏读数，避免遮挡
- 任务栏明暗通过截屏采样判断，深色/浅色任务栏都能保证可读性
- 主题色由背景色派生（向黑/白偏移与混色），切换浅色/深色时所有控件同步更新
- 所有设置与每日流量写入单个 JSON 文件，异常时兜底记录到 `error.log`

## 许可

个人自用项目，未指定开源协议。
