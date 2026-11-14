#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""离屏渲染界面截图，用于更新 README（仅开发时使用）。

用法：
    python tools/screenshot.py
"""

from __future__ import annotations

import os
import sys
from pathlib import Path

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))

from PyQt6.QtGui import QFontDatabase  # noqa: E402
from PyQt6.QtWidgets import QApplication  # noqa: E402

from leakscanner.core.engine import ScanEngine  # noqa: E402
from leakscanner.core.models import (  # noqa: E402
    JOB_BATCH,
    TASK_LOCAL,
    TASK_REMOTE,
    ScanRequest,
    ScanResult,
    ScanTask,
)
from leakscanner.ui import theme  # noqa: E402
from leakscanner.ui.dialogs import SolutionsDialog  # noqa: E402
from leakscanner.ui.main_window import MainWindow  # noqa: E402

OUT = ROOT / "images"


def pump(app: QApplication, rounds: int = 8) -> None:
    for _ in range(rounds):
        app.processEvents()


def load_windows_fonts() -> None:
    """离屏渲染时 Qt 拿不到系统字体库，手动挂载雅黑等常用字体。"""
    for name in ("msyh.ttc", "msyhbd.ttc", "msyhl.ttc", "segoeui.ttf", "consola.ttf", "CascadiaMono.ttf"):
        path = Path("C:/Windows/Fonts") / name
        if path.exists():
            QFontDatabase.addApplicationFont(str(path))


def main() -> None:
    OUT.mkdir(exist_ok=True)
    app = QApplication(sys.argv)
    load_windows_fonts()
    theme.apply_theme(app)

    window = MainWindow()
    window.resize(1320, 880)
    window.show()
    pump(app)

    # 用真实的扫描结果填充界面：本地仓库目录 + 一个带数据库文件的公开仓库
    scan_target = ROOT.parent if (ROOT.parent / ".git").exists() else ROOT
    engine = ScanEngine()
    tasks = [ScanTask(kind=TASK_LOCAL, target=str(scan_target))]
    if "--offline" not in sys.argv:
        tasks.append(ScanTask(kind=TASK_REMOTE, target="ZeroOneCN/Simnotice"))
    try:
        result = engine.run(ScanRequest(kind=JOB_BATCH, tasks=tasks))
    except Exception as exc:  # 没网时退化为纯本地截图
        print(f"批量扫描失败，退回本地扫描：{exc}")
        result = ScanEngine().run(ScanRequest(kind=JOB_BATCH, tasks=tasks[:1]))
    if not isinstance(result, ScanResult):
        raise SystemExit("扫描结果异常")
    window._switch(0)
    window._scan_page.target_input.setText(str(scan_target))
    window._scan_page.results.set_result(result)
    window._scan_page.results.set_progress(100, f"完成 · 用时 {result.elapsed:.1f} 秒")
    pump(app)
    window.grab().save(str(OUT / "程序首页.png"))

    # 批量任务页
    window._switch(1)
    page = window._batch_page
    for task in (
        ScanTask(kind=TASK_REMOTE, target="ZeroOneCN/Simnotice"),
        ScanTask(kind=TASK_REMOTE, target="ZeroOneCN/Miniproject"),
        ScanTask(kind=TASK_LOCAL, target=str(scan_target)),
    ):
        page._append(task)
    page.target_input.setText("ZeroOneCN/tgmonitor")
    pump(app)
    window.grab().save(str(OUT / "批量任务.png"))

    # 修复指引
    dialog = SolutionsDialog(window._scan_page.results.result, window)
    dialog.resize(880, 660)
    dialog.show()
    pump(app)
    dialog.grab().save(str(OUT / "修复指引.png"))
    dialog.close()

    for name in ("程序首页.png", "批量任务.png", "修复指引.png"):
        path = OUT / name
        print(f"{path.relative_to(ROOT)}  {path.stat().st_size / 1024:.0f} KB")


if __name__ == "__main__":
    main()
