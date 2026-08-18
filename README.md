# 实用工具合集

> **让代码服务于生活** | A collection of practical utilities (Python + C#) designed to solve real-world problems.

![Python](https://img.shields.io/badge/Python-3.10%2B-3776AB?style=flat-square&logo=python&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?style=flat-square&logo=windows&logoColor=white)
![Maintenance](https://img.shields.io/badge/Maintenance-Active-brightgreen?style=flat-square)

---

## 简介

本项目是一个**跨语言实用工具合集**，涵盖 Python 和 C# 两种技术栈，通过轻量级代码解决日常工作与生活中的具体痛点。每个工具独立模块化，即插即用。

---

## 工具列表

| ID | 工具名称 | 核心功能 | 技术栈 |
|:--:|:--------|:---------|:-------|
| **001** | **论文降重助手** | 智能文本改写、同义词替换、查重对比 | `Python`, `API` |
| **002** | **摄像头监控系统** | 多摄像头监控、运动检测、人脸识别、录像存储、企业微信通知 | `C#`, `.NET 10`, `WPF`, `OpenCvSharp4` |
| **003** | **端口占用查看器** | 端口扫描、进程关联、一键结束进程、命令行查看 | `C#`, `.NET 10`, `WPF`, `P/Invoke` |
| **004** | **屏幕关闭管理** | 屏幕关闭时间设置、电源方案管理、一键快捷操作 | `C#`, `.NET 10`, `WPF`, `MVVM` |
| **005** | **身份证校验器** | 身份证格式/日期/校验位校验、批量验证 | `Python`, `PyQt6` |
| **006** | **敏感文件检查** | GitHub 仓库敏感文件扫描、风险评级、报告导出 | `Python`, `Tkinter`, `GitHub API` |
| **007** | **TG群聊监控** | 多账号Telegram群/频道消息监控、规则匹配、企业微信推送、历史消息记录 | `Python`, `FastAPI`, `Telethon` |
| ... | *更多工具开发中...* | *Coming Soon* | ... |

---

## 快速开始

### 环境要求

- **Python 工具**：Python 3.10+
- **C# 工具**：.NET 10 SDK
- **操作系统**：Windows 10/11 (64位)

### 运行方式

**方式 A：直接运行源码**

```bash
# Python 工具
cd 00X-工具目录
pip install -r requirements.txt   # 如有依赖
python 主程序.py

# C# 工具
cd 00X-工具目录/ToolName
dotnet run --project src/ToolName/ToolName.csproj
```

**方式 B：使用打包程序**

进入对应工具的 `dist` 目录，直接双击运行 `.exe` 文件即可（部分工具已提供打包版本）。

---

## 项目结构

```
Pythonproject/
├── 001-工具目录/
│   ├── images/           # 截图
│   ├── README.md         # 独立文档
│   └── main.py           # Python 入口
├── 002-工具目录/
│   ├── ToolName/          # C# 解决方案目录
│   │   ├── ToolName.slnx
│   │   └── src/
│   │       └── ToolName/
│   │           ├── Models/
│   │           ├── Services/
│   │           ├── ViewModels/
│   │           └── Views/
│   ├── images/
│   └── README.md
├── ...                   # 更多工具
└── README.md             # 本文档
```

---

## 开发指南

### 目录规范

每个工具遵循以下目录规范：

```
00X-工具名称/
├── images/              # 程序截图、演示素材
├── README.md            # 独立说明文档
├── 主程序.py             # Python 入口文件
└── ToolName/             # C# 解决方案目录
    ├── ToolName.slnx     # 解决方案文件
    └── src/
        └── ToolName/     # 项目源码
            ├── Models/
            ├── Services/
            ├── ViewModels/
            ├── Views/
            └── ...
```

### 贡献指南

欢迎提交新的工具！请遵循以下原则：

- 模块化：每个工具独立目录，不互相依赖
- 文档化：每个工具必须包含 README.md
- 易用性：提供打包版本或清晰的运行说明