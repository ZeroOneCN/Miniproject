#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""应用启动：设置任务栏身份、套用主题、打开主窗口。"""

from __future__ import annotations

import ctypes
import sys

from PyQt6.QtCore import QLibraryInfo, QTranslator
from PyQt6.QtWidgets import QApplication

from .. import APP_NAME, APP_TITLE, VERSION
from . import theme
from .main_window import MainWindow

APP_ID = "ZeroOneCN.LeakScanner.3"


def _set_windows_app_id() -> None:
    """让 Windows 任务栏使用本程序图标，而不是 Python 的图标。"""
    if sys.platform != "win32":
        return
    try:
        ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID(APP_ID)
    except Exception:
        pass


def _install_translator(app: QApplication) -> QTranslator | None:
    """加载 Qt 自带中文翻译，让消息框、文件对话框等标准按钮也显示中文。

    打包后翻译文件可能缺失，此时返回 None 并静默降级——输入框与说明文字的
    右键菜单是自己实现的，不依赖翻译文件。
    """
    translator = QTranslator(app)
    path = QLibraryInfo.path(QLibraryInfo.LibraryPath.TranslationsPath)
    if translator.load("qtbase_zh_CN", path):
        app.installTranslator(translator)
        return translator
    return None


def run() -> int:
    _set_windows_app_id()

    app = QApplication(sys.argv)
    app.setApplicationName(APP_NAME)
    app.setApplicationDisplayName(APP_TITLE)
    app.setApplicationVersion(VERSION)
    app.setWindowIcon(theme.app_icon())
    theme.apply_theme(app)
    app.qt_translator = _install_translator(app)  # 持有引用，避免被回收

    window = MainWindow()
    window.show()
    return app.exec()
