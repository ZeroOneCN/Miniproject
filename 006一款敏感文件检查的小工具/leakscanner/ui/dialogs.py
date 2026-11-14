#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""对话框：修复指引。"""

from __future__ import annotations

from pathlib import Path

from PyQt6.QtWidgets import (
    QApplication,
    QDialog,
    QFileDialog,
    QHBoxLayout,
    QLabel,
    QPushButton,
    QTextBrowser,
    QVBoxLayout,
)

from ..core import report as report_mod
from ..core.models import ScanResult
from . import theme
from .widgets import show_toast


class SolutionsDialog(QDialog):
    """展示可复制的修复命令清单。"""

    def __init__(self, result: ScanResult, parent=None):
        super().__init__(parent)
        self._result = result
        self._text = report_mod.build_fix_guide(result)

        self.setWindowTitle("修复指引")
        self.setWindowIcon(theme.app_icon())
        self.resize(880, 680)

        layout = QVBoxLayout(self)
        layout.setContentsMargins(20, 18, 20, 16)
        layout.setSpacing(12)

        title = QLabel("修复指引")
        title.setObjectName("PageTitle")
        subtitle = QLabel("命令已按本次扫描结果填好路径，复制后核对再执行；重写历史前务必备份仓库")
        subtitle.setObjectName("PageSubtitle")
        layout.addWidget(title)
        layout.addWidget(subtitle)

        self._browser = QTextBrowser()
        self._browser.setPlainText(self._text)
        self._browser.setLineWrapMode(QTextBrowser.LineWrapMode.NoWrap)
        layout.addWidget(self._browser, 1)

        buttons = QHBoxLayout()
        buttons.setSpacing(10)

        copy_btn = QPushButton("复制全部")
        copy_btn.setProperty("variant", "primary")
        copy_btn.clicked.connect(self._copy)
        save_btn = QPushButton("保存为 txt")
        save_btn.clicked.connect(self._save)
        close_btn = QPushButton("关闭")
        close_btn.clicked.connect(self.accept)

        buttons.addWidget(copy_btn)
        buttons.addWidget(save_btn)
        buttons.addStretch(1)
        buttons.addWidget(close_btn)
        layout.addLayout(buttons)

    def _copy(self) -> None:
        QApplication.clipboard().setText(self._text)
        show_toast(self, "已复制到剪贴板", "ok")

    def _save(self) -> None:
        path, _ = QFileDialog.getSaveFileName(self, "保存修复指引", "fix_guide.txt", "文本文件 (*.txt)")
        if not path:
            return
        try:
            Path(path).write_text(self._text, encoding="utf-8")
        except OSError as exc:
            show_toast(self, f"保存失败：{exc}", "warn")
            return
        show_toast(self, f"已保存：{Path(path).name}", "ok")
