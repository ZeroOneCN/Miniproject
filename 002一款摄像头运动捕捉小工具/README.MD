# 摄像头监控系统

> 版本：v1.0 | 架构：.NET 10 WPF + Clean Architecture

基于 **OpenCvSharp4 + WPF + MVVM** 的本地摄像头监控系统，支持多摄像头预览、运动检测、人脸检测、录像存储、企业微信通知等功能。

---

## 功能特性

| 功能 | 说明 |
|------|------|
| **多摄像头支持** | 同时接入多个 USB/网络摄像头，多画面布局（1/2x2/3x3/4x4） |
| **实时预览** | 高清画面预览，支持画面缩放（鼠标滚轮） |
| **运动检测** | 可调灵敏度，支持运动触发录像和通知 |
| **人脸检测** | 基于 Haar Cascade 实时人脸检测，画面叠加人脸框 |
| **录像存储** | 全时录制/运动录制/定时录制，支持分段存储、自动清理 |
| **水印叠加** | 支持中文水印（微软雅黑）、时间戳、设备名叠加 |
| **企业微信通知** | 运动检测触发时通过 Webhook 发送图片通知 |
| **IP 摄像头接入** | 支持局域网 RTSP 网络摄像头接入 |
| **配置持久化** | 所有设置自动保存，重启后恢复 |

---

## 系统架构

```
CameraMotionCapture/
├── src/
│   ├── CameraMotionCapture.App/     # WPF 界面层 (MVVM)
│   │   ├── ViewModels/              # 视图模型
│   │   ├── Views/                   # XAML 视图
│   │   ├── Helpers/                 # 辅助工具（中文水印渲染等）
│   │   └── Converters/              # 值转换器
│   ├── CameraMotionCapture.Core/    # 核心业务层
│   │   ├── Models/                  # 数据模型
│   │   ├── Services/                # 业务服务
│   │   │   ├── Interfaces/          # 服务接口
│   │   │   └── Implementations/     # 服务实现
│   │   └── Constants.cs             # 全局常量
│   └── CameraMotionCapture.Shared/  # 共享层
├── docs/                            # 文档
└── CameraMotionCapture.slnx
```

### 架构原则

- **Clean Architecture**：分层清晰，依赖倒置
- **MVVM 模式**：UI 与业务逻辑解耦
- **依赖注入**：通过 `Microsoft.Extensions.DependencyInjection` 管理服务生命周期
- **异步编程**：设置保存、文件操作等使用 async/await 避免 UI 阻塞

---

## 技术栈

| 技术 | 用途 |
|------|------|
| .NET 10 / WPF | 桌面应用框架 |
| OpenCvSharp4 | 摄像头采集、图像处理 |
| Microsoft.Extensions.DI | 依赖注入容器 |
| Serilog | 结构化日志 |
| Ookii.Dialogs.Wpf | 文件夹选择对话框 |
| System.Drawing.Common | 中文水印渲染 (GDI+) |

---

## 快速开始

### 环境要求

- Windows 10/11 (64位)
- .NET 10 SDK
- USB 摄像头或支持 RTSP 的网络摄像头

### 编译运行

```bash
# 克隆仓库
git clone <repository-url>
cd 002一款摄像头运动捕捉小工具/CameraMotionCapture

# 还原依赖
dotnet restore

# 编译
dotnet build

# 运行
dotnet run --project src/CameraMotionCapture.App
```

### 打包为 exe

```bash
# 发布为单文件 exe
dotnet publish src/CameraMotionCapture.App -c Release -r win-x64 --self-contained -o dist
```

---

## 使用说明

### 基本操作

1. **启动程序**：自动检测并列出所有可用摄像头
2. **选择布局**：在工具栏切换单画面/2x2/3x3/4x4 布局
3. **切换摄像头**：单画面模式下使用下拉菜单切换摄像头
4. **开始监控**：点击「开始监控」按钮启动录像和检测
5. **设置**：点击「设置」按钮配置录像、运动检测、水印等参数

### 快捷键

- 鼠标滚轮：缩放当前摄像头画面
- 双击画面：全屏预览

---

## 配置说明

### 录像设置

- **保存路径**：录像和截图存储目录
- **编码格式**：XVID / MJPG / H264
- **录制模式**：全时录制 / 运动录制 / 定时录制
- **分段时长**：单个视频文件最大时长
- **空间阈值**：磁盘空间上限，超出自动清理

### 运动检测

- **灵敏度**：可滑条调节，数值越低越敏感
- **通知冷却**：两次通知之间的最小间隔（秒）
- **Webhook URL**：企业微信机器人地址

### 水印

- 时间戳、设备名、自定义文本水印
- 支持中文显示（微软雅黑字体）
- 水印位置：左上角（时间戳/设备名）、底部居中（自定义水印）

---

## 开发计划

详见 [docs/人脸识别与扩展方案.md](docs/人脸识别与扩展方案.md)

### 近期计划

- [ ] 人脸识别（人名/会员号识别）
- [ ] 人脸注册管理界面
- [ ] 识别日志记录

### 中期计划

- [ ] GPU 加速 (DirectML)
- [ ] 录像回放与检索
- [ ] RTMP/HLS 推流

---

## License

MIT