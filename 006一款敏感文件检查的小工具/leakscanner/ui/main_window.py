#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""主窗口：左侧导航 + 右侧页面 + 扫描任务调度。"""

from __future__ import annotations

from PyQt6.QtCore import Qt, QThread, pyqtSignal
from PyQt6.QtWidgets import (
    QFrame,
    QHBoxLayout,
    QLabel,
    QMainWindow,
    QMessageBox,
    QStackedWidget,
    QVBoxLayout,
    QWidget,
)

from .. import APP_NAME, APP_TITLE, VERSION
from ..core.engine import ScanCancelled, ScanEngine, ScanError
from ..core.models import JOB_LOCAL, ScanRequest, ScanResult
from . import theme
from .dialogs import SolutionsDialog
from .pages import BatchPage, GuidePage, ScanPage
from .widgets import NavButton, show_toast

NAV_ITEMS = [("扫描检查", 0), ("批量任务", 1), ("使用说明", 2)]


class ScanWorker(QThread):
    """把扫描放到后台线程，通过信号回传进度与结果。"""

    progressed = pyqtSignal(str, object)
    succeeded = pyqtSignal(object)
    failed = pyqtSignal(str)
    stopped = pyqtSignal()

    def __init__(self, request: ScanRequest, parent=None):
        super().__init__(parent)
        self._request = request
        self._engine = ScanEngine(progress=self._on_progress)

    def _on_progress(self, message: str, percent: int | None) -> None:
        self.progressed.emit(message, percent)

    def run(self) -> None:  # noqa: D401 - QThread 入口
        try:
            result = self._engine.run(self._request)
        except ScanCancelled:
            self.stopped.emit()
        except ScanError as exc:
            self.failed.emit(str(exc))
        except Exception as exc:  # 兜底，避免线程里静默崩掉
            self.failed.emit(f"未预期的错误：{type(exc).__name__}: {exc}")
        else:
            self.succeeded.emit(result)

    def stop(self) -> None:
        self._engine.cancel()


class MainWindow(QMainWindow):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle(f"{APP_TITLE} v{VERSION}")
        self.setWindowIcon(theme.app_icon())
        self.resize(1320, 880)
        self.setMinimumSize(1120, 740)

        self._worker: ScanWorker | None = None
        self._nav_buttons: list[NavButton] = []

        self._build_ui()
        self._connect()
        self._switch(0)

    # ------------------------------------------------------------------ #
    # 界面
    # ------------------------------------------------------------------ #

    def _build_ui(self) -> None:
        root = QFrame()
        root.setObjectName("Root")
        self.setCentralWidget(root)

        layout = QHBoxLayout(root)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(0)
        layout.addWidget(self._build_sidebar())

        page_frame = QFrame()
        page_frame.setObjectName("Page")
        page_layout = QVBoxLayout(page_frame)
        page_layout.setContentsMargins(24, 22, 24, 20)

        self._stack = QStackedWidget()
        self._scan_page = ScanPage()
        self._batch_page = BatchPage()
        self._guide_page = GuidePage()
        self._stack.addWidget(self._scan_page)
        self._stack.addWidget(self._batch_page)
        self._stack.addWidget(self._guide_page)
        page_layout.addWidget(self._stack)

        layout.addWidget(page_frame, 1)

    def _build_sidebar(self) -> QFrame:
        rail = QFrame()
        rail.setObjectName("Sidebar")
        rail.setFixedWidth(218)

        layout = QVBoxLayout(rail)
        layout.setContentsMargins(0, 24, 0, 18)
        layout.setSpacing(0)

        brand = QHBoxLayout()
        brand.setContentsMargins(18, 0, 18, 0)
        brand.setSpacing(10)

        logo = QLabel()
        logo.setFixedSize(36, 36)
        logo.setPixmap(theme.pixmap("icon.png", 36))
        logo.setScaledContents(True)

        titles = QVBoxLayout()
        titles.setSpacing(1)
        name = QLabel(APP_NAME)
        name.setObjectName("BrandName")
        meta = QLabel(f"Leak Scanner · v{VERSION}")
        meta.setObjectName("BrandMeta")
        titles.addWidget(name)
        titles.addWidget(meta)

        brand.addWidget(logo)
        brand.addLayout(titles, 1)
        layout.addLayout(brand)

        nav = QVBoxLayout()
        nav.setContentsMargins(0, 26, 0, 0)
        nav.setSpacing(2)
        for text, index in NAV_ITEMS:
            button = NavButton(text)
            button.clicked.connect(lambda _checked=False, i=index: self._switch(i))
            nav.addWidget(button)
            self._nav_buttons.append(button)
        layout.addLayout(nav)

        layout.addStretch(1)

        footer = QLabel("仅在本机运行 · Token 不落盘")
        footer.setObjectName("RailFooter")
        footer.setContentsMargins(20, 0, 18, 0)
        footer.setWordWrap(True)
        layout.addWidget(footer)

        return rail

    def _connect(self) -> None:
        self._scan_page.scan_requested.connect(self.start_scan)
        self._scan_page.stop_requested.connect(self.stop_scan)
        self._scan_page.solutions_requested.connect(self.show_solutions)
        self._batch_page.scan_requested.connect(self.start_scan)
        self._batch_page.stop_requested.connect(self.stop_scan)

    # ------------------------------------------------------------------ #
    # 导航
    # ------------------------------------------------------------------ #

    def _switch(self, index: int) -> None:
        self._stack.setCurrentIndex(index)
        for position, button in enumerate(self._nav_buttons):
            button.setChecked(position == index)
        if index == 0:
            self._scan_page.focus_target()

    # ------------------------------------------------------------------ #
    # 扫描调度
    # ------------------------------------------------------------------ #

    def start_scan(self, request: ScanRequest) -> None:
        if self._worker is not None:
            show_toast(self, "已有扫描在进行中", "warn")
            return

        results = self._scan_page.results
        results.clear()
        results.reset_progress("正在准备扫描 …")
        self._set_busy(True)

        if request.kind != JOB_LOCAL:
            self._switch(0)

        self._worker = ScanWorker(request, self)
        self._worker.progressed.connect(self._on_progress)
        self._worker.succeeded.connect(self._on_success)
        self._worker.failed.connect(self._on_failed)
        self._worker.stopped.connect(self._on_stopped)
        self._worker.finished.connect(self._on_worker_finished)
        self._worker.start()

    def stop_scan(self) -> None:
        if self._worker is None:
            return
        self._worker.stop()
        self._scan_page.results.set_progress(None, "正在停止 …")

    def _on_progress(self, message: str, percent) -> None:
        self._scan_page.results.set_progress(percent, message)

    def _on_success(self, result: ScanResult) -> None:
        panel = self._scan_page.results
        panel.set_result(result)
        self._switch(0)

        tail = f"完成 · 用时 {result.elapsed:.1f} 秒"
        if result.cancelled:
            tail += "（已中止，结果不完整）"
        panel.set_progress(100, tail)

        if result.items:
            show_toast(
                self,
                f"发现 {len(result.items)} 个敏感文件，其中 {result.high_risk_count} 个高危及以上",
                "warn",
            )
        else:
            show_toast(self, "扫描完成，未发现敏感文件", "ok")

    def _on_failed(self, message: str) -> None:
        panel = self._scan_page.results
        panel.set_progress(0, "扫描失败")
        QMessageBox.warning(self, "扫描失败", message)

    def _on_stopped(self) -> None:
        panel = self._scan_page.results
        panel.set_progress(0, "已停止")
        show_toast(self, "扫描已停止", "warn")

    def _on_worker_finished(self) -> None:
        self._set_busy(False)
        if self._worker is not None:
            self._worker.deleteLater()
            self._worker = None

    def _set_busy(self, busy: bool) -> None:
        self._scan_page.set_busy(busy)
        self._batch_page.set_busy(busy)

    def show_solutions(self) -> None:
        dialog = SolutionsDialog(self._scan_page.results.result, self)
        dialog.exec()

    # ------------------------------------------------------------------ #

    def closeEvent(self, event) -> None:  # noqa: N802 - Qt 命名
        if self._worker is not None and self._worker.isRunning():
            answer = QMessageBox.question(
                self,
                "扫描进行中",
                "扫描尚未结束，确定要退出吗？",
                QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
                QMessageBox.StandardButton.No,
            )
            if answer != QMessageBox.StandardButton.Yes:
                event.ignore()
                return
            self._worker.stop()
            self._worker.wait(2000)
        event.accept()
