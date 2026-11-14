#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""支持 `python -m leakscanner` 启动。"""

from .ui.app import run

if __name__ == "__main__":
    raise SystemExit(run())
