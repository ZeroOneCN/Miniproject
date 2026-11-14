#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""GitHub 仓库敏感文件检查工具（v3.0）。

包结构：
    leakscanner.core    纯逻辑层，不依赖 GUI，可单独测试
    leakscanner.ui      PyQt6 界面层，只负责展示与交互
"""

from __future__ import annotations

import sys
from pathlib import Path

APP_NAME = "敏感文件泄露检查"
APP_TITLE = "GitHub 仓库敏感文件检查工具"
VERSION = "3.0.0"
AUTHOR = "ZeroOneCN"
REPO_URL = "https://github.com/ZeroOneCN/Miniproject"


def resource_path(*parts: str) -> Path:
    """返回随程序分发的资源路径，兼容 PyInstaller 单文件模式。"""
    base = getattr(sys, "_MEIPASS", None)
    root = Path(base) if base else Path(__file__).resolve().parent.parent
    return root.joinpath(*parts)


def asset(*parts: str) -> Path:
    """assets 目录下的资源路径。"""
    return resource_path("assets", *parts)
