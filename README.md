# 实用工具合集

> **让代码服务于生活** | A collection of practical utilities (Python + C#) designed to solve real-world problems.

![Python](https://img.shields.io/badge/Python-3.10%2B-3776AB?style=flat-square&logo=python&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?style=flat-square&logo=windows&logoColor=white)
![Tools](https://img.shields.io/badge/Tools-7-informational?style=flat-square)
![Maintenance](https://img.shields.io/badge/Maintenance-Active-brightgreen?style=flat-square)

---

## 简介

一个**跨语言实用工具合集**，涵盖 Python 与 C# 两种技术栈，用尽量少的代码解决工作与生活中的具体痛点。

每个工具都是**独立目录、独立依赖、独立文档**，可以单独取用，互不干扰：

- **6 个桌面工具**：双击即用，部分提供打包好的 `.exe`
- **1 个 Web 服务**（007）：需要常驻运行，带多租户后台与服务器部署方案

---

## 工具一览

| ID | 工具 | 核心能力 | 形态 | 技术栈 | 文档 |
|:--:|:-----|:---------|:-----|:-------|:-----|
| **001** | **论文降重助手** | 多语言回译改写、数字保护、结果缓存、Word 导出 | 桌面 GUI | `Python` `PyQt5` `百度翻译 API` | [详情](./001一款论文文章降重的小工具/README.md) |
| **002** | **摄像头监控台** | 多路预览、运动/人脸检测、录像抓拍、企业微信告警 | 桌面 GUI | `C#` `.NET 10` `WPF` `OpenCvSharp4` | [详情](./002一款摄像头运动捕捉小工具/README.md) |
| **003** | **端口与句柄排查** | 端口占用、文件句柄锁定、一键结束进程、命令行模式 | 桌面 GUI | `C#` `.NET 10` `WPF` `P/Invoke` | [详情](./003一款查看端口占用的小工具/README.md) |
| **004** | **熄屏管家** | 定时关闭屏幕、电源方案切换、计划任务 | 桌面 GUI | `C#` `.NET 10` `WPF` `MVVM` | [详情](./004一款电脑定时关屏幕小工具/README.md) |
| **005** | **身份证校验器** | GB 11643 校验、归属地/生日/性别解析、批量验证 | 桌面 GUI | `Python` `PyQt6` | [详情](./005一款身份证规则校验小工具/README.md) |
| **006** | **敏感文件检查** | 本地/远程仓库扫描、风险分级、修复指引、报告导出 | 桌面 GUI | `Python` `PyQt6` `GitHub API` | [详情](./006一款敏感文件检查的小工具/README.md) |
| **007** | **TG 群消息监控** | 多账号群/频道监控、规则匹配、多租户 Web 后台、企业微信推送 | Web 服务 | `Python` `FastAPI` `Telethon` `SQLite` | [详情](./007一款电报群消息监控小工具/README.md) |
| ... | *更多工具开发中* | *Coming Soon* | ... | ... | ... |

> 每个工具的功能细节、截图、配置方式都在各自目录的 `README.md` 里，本文只做索引。

---

## 快速开始

### 环境要求

| 用途 | 要求 |
|:-----|:-----|
| Python 桌面工具（001 / 005 / 006） | Python 3.10+，Windows 10/11 |
| C# 桌面工具（002 / 003 / 004） | .NET 10 SDK，Windows 10/11 x64 |
| Web 服务（007） | Python 3.10+，Windows 或 Linux 服务器 |

### 桌面工具（Python）

```bash
# 001 论文降重助手
cd "001一款论文文章降重的小工具"
pip install -r requirements.txt
python main.py

# 005 身份证校验器
cd "005一款身份证规则校验小工具"
pip install pyqt6
python "一款身份证规则校验小工具.py"

# 006 敏感文件检查
cd "006一款敏感文件检查的小工具"
pip install -r requirements.txt
python "一款敏感文件检查的小工具.py"
```

### 桌面工具（C#）

用 Visual Studio / Rider 打开目录下的 `.slnx`，或直接用命令行运行：

```bash
# 002 摄像头监控台
cd "002一款摄像头运动捕捉小工具"
dotnet run --project src/CameraMotionCapture.App/CameraMotionCapture.App.csproj

# 003 端口与句柄排查
cd "003一款查看端口占用的小工具"
dotnet run --project PortMonitor/PortMonitor.csproj

# 004 熄屏管家
cd "004一款电脑定时关屏幕小工具"
dotnet run --project ScreenOffManager/ScreenOffManager.csproj
```

### Web 服务（007）

与桌面工具不同，007 是一个常驻运行的 Web 后台：

```bash
cd "007一款电报群消息监控小工具"
pip install -r requirements.txt
python web_app.py
```

启动后访问 `http://localhost:8000`，**首次访问会引导设置管理员账号**，随后进入登录页。

生产环境部署（Nginx + PM2 + HTTPS）见 [007 的 README](./007一款电报群消息监控小工具/README.md)。

### 使用打包版本

部分工具提供打包好的可执行文件：进入对应工具的 `dist/` 目录（或 Releases 页面）直接运行 `.exe`。`dist/` 属于构建产物，不纳入版本库。

---

## 项目结构

```
Miniproject/
├── 001一款论文文章降重的小工具/
│   ├── main.py                  # 入口
│   ├── app/                     # 界面与业务逻辑
│   ├── tools/                   # 翻译、改写等工具模块
│   └── requirements.txt
├── 002一款摄像头运动捕捉小工具/
│   ├── CameraMotionCapture.slnx
│   ├── src/
│   │   ├── CameraMotionCapture.App/     # WPF 界面层
│   │   ├── CameraMotionCapture.Core/    # 检测与录像核心
│   │   └── CameraMotionCapture.Shared/  # 公共模型
│   ├── models/                  # OpenCV 级联等模型文件
│   └── assets/
├── 003一款查看端口占用的小工具/
│   └── PortMonitor/             # .slnx + 源码
├── 004一款电脑定时关屏幕小工具/
│   └── ScreenOffManager/        # .slnx + 源码
├── 005一款身份证规则校验小工具/
│   └── 一款身份证规则校验小工具.py
├── 006一款敏感文件检查的小工具/
│   ├── 一款敏感文件检查的小工具.py
│   ├── leakscanner/             # 扫描核心包
│   └── tools/
├── 007一款电报群消息监控小工具/
│   ├── web_app.py               # FastAPI 服务入口
│   ├── templates/               # 后台、登录、初始化、分享页
│   ├── tg_helper.py             # 查询 chat_id / user_id
│   ├── ecosystem.config.js      # PM2 进程配置
│   └── tg-monitor.nginx.conf    # Nginx 反向代理示例
└── README.md                    # 本文档
```

> Python 与 C# 工具之间没有任何代码依赖，可以只下载你需要的那个目录。

---

## 开发规范

### 目录规范

每个工具一个自包含目录：

```
00X-工具名称/
├── README.md            # 必需：独立说明文档
├── <入口文件>            # Python: main.py 或具名脚本
│                        # C#: ToolName.slnx + 源码
├── requirements.txt     # Python 依赖（如有）
├── assets/ images/      # 图标、截图等静态资源
└── dist/                # 打包产物（不入库）
```

### 仓库约定

以下内容只在本地存在，不提交：

- `*.db` / `*.db-wal` / `*.db-shm`、`backups/` — 运行时数据库与备份
- `*.session` — 账号登录态（007），泄露等同于交出账号
- `media/`、`logs/`、`*.log` — 媒体存档与运行日志
- `dist/`、`bin/`、`obj/` — 构建产物
- `.workbuddy/`、`.idea/`、`.vscode/`、`__pycache__/` — 本地环境数据
- 测试目录（如 `tests/`）与 `requirements-dev.txt` — 仅本地开发使用，不随仓库分发

### 贡献指南

欢迎提交新工具，请遵循：

- **模块化**：一个工具一个目录，不跨工具引用代码
- **文档化**：必须包含 `README.md`，写清用途、依赖、启动方式、截图
- **易用性**：提供打包版本或一条能跑通的启动命令
- **安全性**：不提交密钥、Token、数据库与账号会话文件

---

## 说明

- 所有工具均在 Windows 10/11 上验证；007 额外支持 Linux 服务器部署。
- 涉及第三方接口（百度翻译、Telegram、企业微信）的工具，需要自备对应的 Key 或机器人地址。
