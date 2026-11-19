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
| **立即熄屏** | 广播 `SC_MONITORPOWER` 直接关屏，不等空闲判定，任何程序的显示请求都拦不住 |
| **占用诊断** | 读取 `powercfg /requests`，列出正在阻止熄屏或睡眠的进程，便于对症处理 |
| **关闭即退出** | 关窗即结束进程，不常驻后台、不留托盘图标 |
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
    ├── App.xaml / App.xaml.cs              # 入口：单实例、全局异常兜底
    ├── MainWindow.xaml / MainWindow.xaml.cs
    ├── Models/
    │   └── PowerSchemeInfo.cs              # 电源方案数据模型 + 时长格式化
    ├── Services/
    │   ├── PowerNative.cs                  # powrprof.dll P/Invoke（主路径）
    │   ├── PowerCfgFallback.cs             # powercfg 命令行（降级路径）
    │   ├── PowerSettingsService.cs         # 编排：原生优先，失败降级
    │   ├── DisplayPowerService.cs          # 强制熄屏 + 电源请求占用诊断
    │   ├── AppSettings.cs                  # %AppData% 配置持久化
    │   ├── StartupManager.cs               # 开机自启（注册表 Run 项）
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
4. **立即熄屏**：立刻关闭显示器，不依赖空闲计时，也不管别的程序在不在占用
5. **检查谁在占用**：列出正在阻止熄屏 / 睡眠的进程
6. **开机自动启动**：勾选即写入注册表，取消即删除

### 屏幕没按时关闭的排查顺序

设了 N 分钟但屏幕还亮着，按这个顺序查：

1. **先看「关闭时间」是不是「永不关闭」** —— 其他软件（省电助手、厂商工具、系统更新）会改写这项，程序启动时显示的就是真实值
2. **点「检查谁在占用」** —— 有程序在放视频、远程控制、投屏、跑模拟器时，会主动持有显示器请求，Windows 就不会熄屏。对话框里列出的进程就是元凶，关掉它即可
3. **确认没有持续输入** —— 鼠标/键盘的轻微动作都会重置空闲计时
4. **实在要立刻黑屏** —— 点「立即熄屏」，它绕过空闲判定直接关显示器

> 「检查谁在占用」依赖 `powercfg /requests`，需要管理员权限；普通权限下会提示读取失败，此时以管理员身份运行即可。

### 退出程序

- 点窗口右上角 **×** 即退出，进程随之结束，不常驻后台
- 程序不提供托盘图标，也不支持手动最小化到托盘
- 需要再次使用时重新启动即可

### 数据位置

| 内容 | 路径 |
|------|------|
| 配置 | `%AppData%\ScreenOffManager\settings.json` |
| 日志 | `%AppData%\ScreenOffManager\logs\app-yyyyMMdd.log` |

---

## 已知限制

- **管理员权限**：开发机为内置 Administrator 且 UAC 关闭，**非管理员场景未实测**。若提示写入失败，请右键「以管理员身份运行」。
- **AC / DC 同步**：目前交流（插电）与直流（电池）写同一个值，暂不支持分开设置。
- **占用诊断需提权**：`powercfg /requests` 要管理员权限，普通权限下只能提示失败。
- **不支持定时计划**：不会在指定时间自动切换设置，仅在手动操作时生效。
- **方案名显示**：原生接口不依赖语言；若降级到 `powercfg`，中文方案名偶尔可能显示为乱码（不影响数值）。
