# 熄屏管家 · ScreenOffManager

> .NET 10 WPF + MVVM · Windows 10/11 x64

一个只管一件事的小工具：**修改 Windows 的「多久后关闭屏幕」**。开窗、点按钮、收工。

![程序首页](./images/程序首页.png)

---

## 功能特性

| 功能 | 说明 |
|------|------|
| **状态查看** | 实时显示当前电源方案名称与屏幕关闭时间，毫秒级刷新 |
| **快捷设置** | 永不关闭 / 1 分钟 / 恢复默认 10 分钟 |
| **自定义设置** | 输入 0–1440 分钟，非法输入有明确提示，并记住上次的值 |
| **读写方式** | 直接调用系统电源接口，不启动外部进程，无控制台窗口闪现 |
| **语言无关** | 不解析系统输出文本，中文 / 英文 / 其他语言的 Windows 读数一致 |
| **托盘常驻** | 关闭窗口只是收进托盘，右键菜单可直接切换，双击打开主界面 |
| **开机自启** | 一键写入 `HKCU\...\Run`，免管理员、免任务计划 |
| **单实例** | 重复启动会给出提示并退出，避免多开互抢设置 |
| **异常兜底** | 全局异常捕获 + 文件日志，出问题能查得清 |
| **紧凑固定界面** | 430px 定宽 + 高度自适应，禁止拉伸，没有多余留白 |

---

## 项目结构

```
004一款电脑定时关屏幕小工具/
├── README.md
├── images/
│   └── 程序首页.png
└── ScreenOffManager/
    ├── ScreenOffManager.slnx
    ├── ScreenOffManager.csproj
    ├── app.manifest                        # asInvoker + PerMonitorV2 DPI 感知
    ├── AssemblyInfo.cs
    ├── App.xaml / App.xaml.cs              # 入口：单实例、全局异常兜底、托盘生命周期
    ├── MainWindow.xaml / MainWindow.xaml.cs
    ├── Models/
    │   └── PowerSchemeInfo.cs              # 电源方案数据模型 + 时长格式化
    ├── Services/
    │   ├── PowerNative.cs                  # powrprof.dll P/Invoke（主路径）
    │   ├── PowerCfgFallback.cs             # powercfg 命令行（降级路径）
    │   ├── PowerSettingsService.cs         # 编排：原生优先，失败降级
    │   ├── AppSettings.cs                  # %AppData% 配置持久化
    │   ├── StartupManager.cs               # 开机自启（注册表 Run 项）
    │   ├── TrayService.cs                  # 托盘图标与右键菜单
    │   └── Log.cs                          # 极简文件日志
    ├── ViewModels/
    │   ├── ObservableObject.cs             # INotifyPropertyChanged 基类
    │   ├── RelayCommand.cs                 # ICommand 实现
    │   └── MainViewModel.cs                # 主视图模型
    └── Resources/
        ├── Styles.xaml                     # 全局样式（按钮/复选框/输入框/分组框）
        ├── app_icon.ico
        └── AppIcon.svg
```

### 分层原则

- **MVVM**：View 只做绑定，业务逻辑在 ViewModel，系统交互在 Service
- **双引擎**：`powrprof` 原生调用为主，异常时自动降级到 `powercfg`，两条路径都写日志
- **异步不卡 UI**：所有系统调用走 `Task.Run`，忙时按钮自动禁用
- **失败可诊断**：关键步骤写日志，出问题不用靠猜

---

## 技术栈

| 技术 | 用途 |
|------|------|
| .NET 10 / WPF | 桌面应用框架 |
| `powrprof.dll` P/Invoke | 读写电源方案（`PowerReadACValueIndex` / `PowerWriteACValueIndex` 等） |
| WinForms `NotifyIcon` | 托盘图标（`UseWindowsForms`，零第三方依赖） |
| `Microsoft.Win32.Registry` | 开机自启 |
| `System.Text.Json` | 配置持久化 |
| MVVM | UI 与业务解耦 |

---

## 快速开始

### 环境要求

- Windows 10/11 (x64)
- 运行 `dist` 里的 exe：**不需要安装任何 .NET 运行时**（运行时已打包进 exe）
- 自己编译：**.NET 10 SDK**

### 编译运行

```bash
cd ScreenOffManager

# 编译
dotnet build ScreenOffManager.csproj

# 运行
dotnet run --project ScreenOffManager.csproj
```

### 打包为单文件 exe

```bash
dotnet publish ScreenOffManager.csproj -c Release -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:EnableCompressionInSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o dist
```

产物：`dist/ScreenOffManager.exe`，**约 73MB，单个文件，拷到任何 Windows 10/11 x64 上双击即用**。

体积构成：自身代码约 200KB，其余为 .NET 运行时与 WPF 框架（`coreclr`、`System.Private.CoreLib`、`PresentationFramework` 等）。首次启动会把约 8MB 原生库解压到 `%TEMP%\.net\ScreenOffManager\`，耗时 1–2 秒；之后为正常启动速度。

> 若目标机已装 .NET 10 桌面运行时，可用 `--self-contained false` 把体积降到约 500KB。
> 注意 `EnableCompressionInSingleFile` 仅在自包含模式下可用（否则报 `NETSDK1176`），
> WPF 也不支持 `PublishTrimmed`。

---

## 使用说明

### 主界面

1. **查看状态**：启动即读取当前电源方案与关闭时间
2. **快捷设置**：永远不关 / 1 分钟 / 恢复默认 10 分钟
3. **自定义设置**：填分钟数后点「应用设置」（0 = 永不关闭）
4. **开机自动启动**：勾选即写入注册表，取消即删除

### 托盘

- 点窗口右上角 **×** ≠ 退出，只是收进托盘（首次会弹气泡说明）
- **双击**托盘图标：打开主界面
- **右键**托盘图标：永不关闭 / 1 分钟 / 10 分钟 / 开机自启 / 退出

### 数据位置

| 内容 | 路径 |
|------|------|
| 配置 | `%AppData%\ScreenOffManager\settings.json` |
| 日志 | `%AppData%\ScreenOffManager\logs\app-yyyyMMdd.log` |

---

## 已知限制

- **管理员权限**：开发机为内置 Administrator 且 UAC 关闭，**非管理员场景未实测**。若提示写入失败，请右键「以管理员身份运行」。
- **AC / DC 同步**：目前交流（插电）与直流（电池）写同一个值，暂不支持分开设置。
- **不支持定时计划**：不会在指定时间自动切换设置，仅在手动操作时生效。
- **方案名显示**：原生接口不依赖语言；若降级到 `powercfg`，中文方案名偶尔可能显示为乱码（不影响数值）。
