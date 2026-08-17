# 端口占用与进程管理

> 版本：v1.0 | 架构：.NET 10 WPF + MVVM

基于 **C# / .NET 10 WPF** 的 Windows 桌面工具，通过 P/Invoke 调用 Windows 原生 API 查询端口占用情况，并支持一键结束对应进程。

![程序首页](./images/程序首页.png)

---

## 功能特性

| 功能 | 说明 |
|------|------|
| **端口查询** | 输入端口号，快速查询占用该端口的 TCP/UDP 连接 |
| **协议筛选** | 支持全部 / TCP / UDP 三种模式过滤 |
| **进程详情** | 显示 PID、进程名、本地地址、远端地址、TCP 连接状态 |
| **命令行查看** | 选中记录可查看进程完整命令行，支持显示/隐藏切换 |
| **一键结束进程** | 选中记录后直接结束对应进程（带确认对话框） |
| **复制信息** | 支持复制 PID / 命令行到剪贴板 |
| **自动刷新** | 2 秒间隔自动刷新，实时监控端口变化 |
| **SVG 图标** | 窗口标题栏矢量图标 + 多分辨率 EXE 图标 |

---

## 系统架构

```
PortMonitor/
├── PortMonitor.slnx             # 解决方案文件
├── src/
│   └── PortMonitor/             # 项目源码
│       ├── App.xaml / .cs       # 应用程序入口
│       ├── MainWindow.xaml / .cs# 主窗口 (UI + 事件绑定)
│       ├── Converters.cs        # 值转换器
│       ├── Resources/
│       │   ├── Styles.xaml      # 按钮样式（危险/普通）
│       │   ├── app_icon.ico     # EXE 多分辨率图标
│       │   └── app_icon.png     # 窗口图标
│       ├── Models/
│       │   ├── ConnectionInfo.cs   # 连接数据模型
│       │   └── ProtocolFilter.cs   # 协议筛选枚举
│       ├── Services/
│       │   └── NativeNetworkService.cs # P/Invoke 调用 iphlpapi.dll
│       └── ViewModels/
│           ├── ObservableObject.cs   # MVVM 基类
│           ├── RelayCommand.cs       # ICommand 实现
│           └── MainViewModel.cs     # 主视图模型
├── images/                      # 截图
└── README.MD                    # 本文档
```

### 架构原则

- **MVVM 模式**：UI 与业务逻辑通过数据绑定解耦
- **P/Invoke 原生 API**：直接调用 `GetExtendedTcpTable` / `GetExtendedUdpTable` 获取含 PID 的连接信息，性能优于 WMI 查询
- **异步编程**：端口查询、进程结束等操作使用 async/await 避免 UI 线程阻塞
- **进程缓存**：3 秒缓存周期，减少重复查询进程信息的开销

---

## 技术栈

| 技术 | 用途 |
|------|------|
| .NET 10 / WPF | 桌面应用框架 |
| iphlpapi.dll (P/Invoke) | 获取 TCP/UDP 连接表 |
| System.Management (WMI) | 查询进程完整命令行 |
| System.Diagnostics.Process | 进程管理与结束 |

---

## 快速开始

### 环境要求

- Windows 10/11 (64位)
- .NET 10 SDK

### 编译运行

```bash
# 进入项目目录
cd 003一款查看端口占用的小工具/PortMonitor

# 还原依赖
dotnet restore src/PortMonitor/PortMonitor.csproj

# 编译
dotnet build src/PortMonitor/PortMonitor.csproj

# 运行
dotnet run --project src/PortMonitor/PortMonitor.csproj
```

### 打包为 exe

```bash
# 发布为单文件 exe（含压缩，约 62MB）
dotnet publish src/PortMonitor/PortMonitor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ../dist
```

打包完成后生成 `dist\PortMonitor.exe`，可直接双击运行。

---

## 使用说明

### 基本操作

1. **查询端口**：输入端口号（0-65535），选择协议筛选（全部/TCP/UDP），点击"检查端口"或按 Enter 键
2. **查看详情**：在结果列表中选中一行，下方命令行详情区域显示该进程的完整命令行
3. **结束进程**：选中记录后点击"结束进程"按钮，确认后结束对应进程
4. **复制信息**：选中记录后点击"复制 PID"或"复制命令行"复制到剪贴板
5. **自动刷新**：勾选"自动刷新"后每 2 秒自动重新查询

### 快捷键

- Enter：快速执行端口查询

---

## 配置说明

### 端口查询

- **端口号**：0-65535 之间的有效端口
- **协议筛选**：全部 / TCP / UDP
- **自动刷新**：开启后每 2 秒自动刷新结果

### 命令行

- **显示命令行**：勾选后在详情区域显示选中进程的完整命令行
- 部分系统进程可能因权限问题无法读取命令行参数

---

## 开发计划

### 近期计划

- [ ] 端口范围扫描（支持扫描连续端口段）
- [ ] 结果导出（CSV / TXT）
- [ ] 进程树展示（显示进程父子关系）

### 中期计划

- [ ] 网络连接监控（实时流量统计）
- [ ] 端口监听模式（端口被占用时弹出通知）
- [ ] 多语言支持