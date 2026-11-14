#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""GitHub 仓库敏感文件检查工具 · 程序入口。

直接运行：
    python 一款敏感文件检查的小工具.py

源码结构见 leakscanner/ 包，界面与逻辑分层，详见 README。
"""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from leakscanner.ui.app import run  # noqa: E402

if __name__ == "__main__":
    raise SystemExit(run())
