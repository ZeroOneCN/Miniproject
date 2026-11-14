#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""扫描结果面板：统计卡 + 筛选 + 结果表 + 进度 + 导出。"""

from __future__ import annotations

from datetime import datetime
from pathlib import Path

from PyQt6.QtCore import QUrl, Qt, pyqtSignal
from PyQt6.QtGui import QAction, QColor, QDesktopServices
from PyQt6.QtWidgets import (
    QAbstractItemView,
    QApplication,
    QCheckBox,
    QComboBox,
    QFileDialog,
    QFrame,
    QHBoxLayout,
    QHeaderView,
    QLabel,
    QMenu,
    QProgressBar,
    QPushButton,
    QSizePolicy,
    QStackedWidget,
    QTableWidget,
    QTableWidgetItem,
    QVBoxLayout,
    QWidget,
)

from ..core import report as report_mod
from ..core.models import ScanItem, ScanResult
from ..core.rules import KIND_NAMES
from . import theme
from .widgets import Card, KindDotDelegate, LineEdit, RiskBadgeDelegate, StatCard, show_toast

RISK_FILTERS: list[tuple[str, str, int]] = [
    ("全部风险", "all", -1),
    ("仅严重", "critical", 3),
    ("高危及以上", "high", 2),
    ("中危及以上", "medium", 1),
]

COLUMNS = ["风险", "来源", "类型", "文件路径", "大小", "状态"]
COLUMN_WIDTHS = {0: 84, 1: 168, 2: 132, 4: 86, 5: 112}


class EmptyState(QWidget):
    """结果为空时的占位页。"""

    def __init__(self, parent=None):
        super().__init__(parent)
        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 40, 0, 40)
        layout.setSpacing(8)
        layout.addStretch(1)

        title = QLabel("暂无扫描结果")
        title.setAlignment(Qt.AlignmentFlag.AlignCenter)
        title.setStyleSheet(f"font-size: 14px; font-weight: 600; color: {theme.TEXT_MUTED};")
        subtitle = QLabel("选择扫描模式，填写目标后点击「开始扫描」")
        subtitle.setAlignment(Qt.AlignmentFlag.AlignCenter)
        subtitle.setStyleSheet(f"font-size: 12px; color: {theme.TEXT_FAINT};")

        layout.addWidget(title)
        layout.addWidget(subtitle)
        layout.addStretch(1)


class ResultsPanel(QWidget):
    """结果展示与导出。"""

    solutions_requested = pyqtSignal()

    def __init__(self, parent=None):
        super().__init__(parent)
        self._result = ScanResult()
        self._rows: list[ScanItem] = []
        self._sort_column: int | None = None
        self._sort_desc = True

        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(12)

        layout.addLayout(self._build_stats())

        self._card = Card("扫描结果", "按风险等级从高到低排列")
        self._build_header_actions()
        self._card.add_widget(self._build_toolbar())
        self._build_table()
        self._build_footer()
        layout.addWidget(self._card, 1)

        self._update_stats()

    # ------------------------------------------------------------------ #
    # 构建
    # ------------------------------------------------------------------ #

    def _build_stats(self) -> QHBoxLayout:
        row = QHBoxLayout()
        row.setContentsMargins(0, 0, 0, 0)
        row.setSpacing(12)
        self._stat_total = StatCard("敏感文件", "0", theme.TEXT)
        self._stat_high = StatCard("高危及以上", "0", theme.DANGER)
        self._stat_sources = StatCard("涉及来源", "0", theme.PRIMARY)
        self._stat_committed = StatCard("已提交 / 推送", "0", "#B5761A")
        for card in (self._stat_total, self._stat_high, self._stat_sources, self._stat_committed):
            row.addWidget(card)
        return row

    def _build_header_actions(self) -> None:
        self._export_btn = QPushButton("导出报告")
        self._export_btn.setProperty("variant", "ghost")
        self._export_btn.setCursor(Qt.CursorShape.PointingHandCursor)
        self._export_btn.setMenu(self._build_export_menu())

        self._clear_btn = QPushButton("清空")
        self._clear_btn.setCursor(Qt.CursorShape.PointingHandCursor)
        self._clear_btn.clicked.connect(self.clear)

        self._card.add_action(self._export_btn)
        self._card.add_action(self._clear_btn)

    def _build_toolbar(self) -> QWidget:
        """搜索与筛选单独一行，标题栏留给导出按钮，窄窗口也不会挤爆。"""
        self._search = LineEdit()
        self._search.setObjectName("SearchBox")
        self._search.setPlaceholderText("搜索路径 / 来源 …")
        self._search.setClearButtonEnabled(True)
        self._search.setMinimumWidth(150)
        self._search.setMaximumWidth(260)
        self._search.textChanged.connect(lambda _text: self._render())

        self._risk_filter = QComboBox()
        for label, _key, _rank in RISK_FILTERS:
            self._risk_filter.addItem(label)
        self._risk_filter.currentIndexChanged.connect(lambda _index: self._render())

        self._only_committed = QCheckBox("仅已提交")
        self._only_committed.stateChanged.connect(lambda _state: self._render())

        bar = QWidget()
        row = QHBoxLayout(bar)
        row.setContentsMargins(0, 0, 0, 2)
        row.setSpacing(10)
        row.addWidget(self._search)
        row.addWidget(self._risk_filter)
        row.addWidget(self._only_committed)
        row.addStretch(1)
        return bar

    def _build_export_menu(self) -> QMenu:
        menu = QMenu(self)
        for label, suffix, key in (
            ("文本报告  .txt", "txt", "text"),
            ("JSON 数据  .json", "json", "json"),
            ("CSV 表格  .csv", "csv", "csv"),
        ):
            action = QAction(label, self)
            action.triggered.connect(lambda _checked=False, k=key: self._export(k))
            menu.addAction(action)
        menu.addSeparator()
        copy_action = QAction("复制文件路径", self)
        copy_action.triggered.connect(self._copy_paths)
        menu.addAction(copy_action)
        guide_action = QAction("查看修复指引", self)
        guide_action.triggered.connect(self.solutions_requested.emit)
        menu.addAction(guide_action)
        return menu

    def _build_table(self) -> None:
        self._stack = QStackedWidget()
        self._table = QTableWidget(0, len(COLUMNS))
        self._table.setHorizontalHeaderLabels(COLUMNS)
        self._table.verticalHeader().setVisible(False)
        self._table.verticalHeader().setDefaultSectionSize(38)
        self._table.setShowGrid(False)
        self._table.setFrameShape(QFrame.Shape.NoFrame)
        self._table.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectRows)
        self._table.setSelectionMode(QAbstractItemView.SelectionMode.ExtendedSelection)
        self._table.setEditTriggers(QAbstractItemView.EditTrigger.NoEditTriggers)
        self._table.setWordWrap(False)
        self._table.setAlternatingRowColors(False)
        self._table.setItemDelegateForColumn(0, RiskBadgeDelegate(self._table))
        self._table.setItemDelegateForColumn(2, KindDotDelegate(self._table))

        header = self._table.horizontalHeader()
        for column, width in COLUMN_WIDTHS.items():
            header.setSectionResizeMode(column, QHeaderView.ResizeMode.Fixed)
            self._table.setColumnWidth(column, width)
        header.setSectionResizeMode(3, QHeaderView.ResizeMode.Stretch)
        header.setHighlightSections(False)
        header.setSectionsClickable(True)
        header.sectionClicked.connect(self._on_header_clicked)

        self._table.itemDoubleClicked.connect(self._open_item)
        self._table.setContextMenuPolicy(Qt.ContextMenuPolicy.CustomContextMenu)
        self._table.customContextMenuRequested.connect(self._show_context_menu)

        self._empty = EmptyState()
        # sizeHint 由外部高度决定，否则滚动区会按表格默认高度估算出多余滚动条
        self._stack.setSizePolicy(
            QSizePolicy.Policy.Preferred, QSizePolicy.Policy.Ignored
        )
        self._stack.addWidget(self._table)
        self._stack.addWidget(self._empty)
        self._stack.setCurrentWidget(self._empty)
        self._card.add_widget(self._stack, 1)

    def _build_footer(self) -> None:
        footer = QHBoxLayout()
        footer.setContentsMargins(0, 2, 0, 0)
        footer.setSpacing(12)

        self._progress = QProgressBarCompat()
        self._status = QLabel("就绪")
        self._status.setObjectName("Hint")
        self._count_label = QLabel("")
        self._count_label.setObjectName("Hint")

        footer.addWidget(self._progress, 1)
        footer.addWidget(self._count_label)
        footer.addWidget(self._status)
        self._card.add_layout(footer)

    # ------------------------------------------------------------------ #
    # 对外接口
    # ------------------------------------------------------------------ #

    def set_result(self, result: ScanResult) -> None:
        self._result = result
        self._sort_column = None
        self._update_stats()
        self._render()
        self._stack.setCurrentWidget(self._table if result.items else self._empty)

    def clear(self) -> None:
        self._result = ScanResult()
        self._rows = []
        self._table.setRowCount(0)
        self._stack.setCurrentWidget(self._empty)
        self._progress.setValue(0)
        self._status.setText("就绪")
        self._count_label.setText("")
        self._update_stats()

    def set_progress(self, percent: int | None, message: str = "") -> None:
        if percent is None:
            self._progress.set_busy(True)
        else:
            self._progress.set_busy(False)
            self._progress.setValue(max(0, min(100, percent)))
        if message:
            self._status.setText(message)

    def reset_progress(self, message: str = "就绪") -> None:
        self._progress.set_busy(False)
        self._progress.setValue(0)
        self._status.setText(message)

    @property
    def result(self) -> ScanResult:
        return self._result

    # ------------------------------------------------------------------ #
    # 内部逻辑
    # ------------------------------------------------------------------ #

    def _update_stats(self) -> None:
        result = self._result
        self._stat_total.set_value(len(result.items))
        self._stat_high.set_value(result.high_risk_count)
        self._stat_sources.set_value(len(result.sources))
        self._stat_committed.set_value(result.committed_count)

    def _filtered_items(self) -> list[ScanItem]:
        keyword = self._search.text().strip().lower()
        min_rank = RISK_FILTERS[self._risk_filter.currentIndex()][2]
        only_committed = self._only_committed.isChecked()

        items = []
        for item in self._result.items:
            if min_rank >= 0 and item.risk_rank < min_rank:
                continue
            if only_committed and not item.tracked:
                continue
            if keyword:
                haystack = " ".join(
                    (item.path, item.source, KIND_NAMES.get(item.kind, ""), item.reason_text)
                ).lower()
                if keyword not in haystack:
                    continue
            items.append(item)
        return self._sort_items(items)

    def _sort_items(self, items: list[ScanItem]) -> list[ScanItem]:
        if self._sort_column is None:
            return sorted(items, key=lambda it: (-it.risk_rank, it.source, it.path))

        def key(item: ScanItem):
            if self._sort_column == 0:
                return item.risk_rank
            if self._sort_column == 1:
                return item.source.lower()
            if self._sort_column == 2:
                return KIND_NAMES.get(item.kind, item.kind)
            if self._sort_column == 3:
                return item.path.lower()
            if self._sort_column == 4:
                return item.size
            return item.status

        return sorted(items, key=key, reverse=self._sort_desc)

    def _render(self) -> None:
        items = self._filtered_items()
        self._rows = items
        self._table.setRowCount(len(items))
        self._table.setUpdatesEnabled(False)

        for row, item in enumerate(items):
            risk_cell = QTableWidgetItem()
            risk_cell.setData(Qt.ItemDataRole.UserRole, item.risk)
            risk_cell.setToolTip(f"{item.risk}：{item.reason_text}")

            source_cell = QTableWidgetItem(item.source)
            source_cell.setToolTip(item.source)

            kind_cell = QTableWidgetItem()
            kind_cell.setData(Qt.ItemDataRole.UserRole, item.kind)
            kind_cell.setToolTip(KIND_NAMES.get(item.kind, item.kind))

            path_cell = QTableWidgetItem(item.path)
            path_cell.setToolTip(f"{item.path}\n{item.reason_text}")

            size_cell = QTableWidgetItem(item.size_human)
            size_cell.setTextAlignment(Qt.AlignmentFlag.AlignCenter)

            status_cell = QTableWidgetItem(item.status)
            status_color = theme.DANGER if item.tracked else theme.TEXT_MUTED
            status_cell.setForeground(QColor(status_color))
            status_cell.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
            status_cell.setToolTip(f"Git 状态：{item.status}")

            for column, cell in enumerate(
                (risk_cell, source_cell, kind_cell, path_cell, size_cell, status_cell)
            ):
                cell.setData(Qt.ItemDataRole.UserRole + 1, row)
                self._table.setItem(row, column, cell)

        self._table.setUpdatesEnabled(True)
        self._stack.setCurrentWidget(self._table if items else self._empty)
        total = len(self._result.items)
        self._count_label.setText(f"显示 {len(items)} / {total}" if total else "")
        self._update_header()

    def _update_header(self) -> None:
        labels = []
        for column, name in enumerate(COLUMNS):
            if self._sort_column == column:
                labels.append(f"{name} {'▼' if self._sort_desc else '▲'}")
            else:
                labels.append(name)
        self._table.setHorizontalHeaderLabels(labels)

        for column in range(len(COLUMNS)):
            alignment = (
                Qt.AlignmentFlag.AlignLeft
                if column in (1, 2, 3)
                else Qt.AlignmentFlag.AlignHCenter
            )
            item = self._table.horizontalHeaderItem(column)
            if item is not None:
                item.setTextAlignment(alignment | Qt.AlignmentFlag.AlignVCenter)

    def _on_header_clicked(self, column: int) -> None:
        if column == 5:
            return
        if self._sort_column == column:
            self._sort_desc = not self._sort_desc
        else:
            self._sort_column = column
            self._sort_desc = column != 3
        self._render()

    def _selected_items(self) -> list[ScanItem]:
        items = []
        for index in self._table.selectionModel().selectedRows():
            cell = self._table.item(index.row(), 0)
            if cell is None:
                continue
            row = cell.data(Qt.ItemDataRole.UserRole + 1)
            if row is not None and 0 <= row < len(self._rows):
                items.append(self._rows[row])
        return items

    def _open_item(self, cell: QTableWidgetItem) -> None:
        selected = self._selected_items()
        if not selected:
            return
        item = selected[0]
        if item.url:
            QDesktopServices.openUrl(QUrl(item.url))
        elif item.abs_path and Path(item.abs_path).exists():
            QDesktopServices.openUrl(QUrl.fromLocalFile(str(Path(item.abs_path).parent)))

    def _show_context_menu(self, position) -> None:
        items = self._selected_items()
        if not items:
            return

        menu = QMenu(self)
        first = items[0]
        if first.is_remote:
            menu.addAction("在浏览器中打开", lambda: QDesktopServices.openUrl(QUrl(first.url)))
            menu.addAction("复制链接", lambda: self._copy("\n".join(i.url for i in items)))
        else:
            menu.addAction("打开所在文件夹", lambda: self._open_folder(first.abs_path))
            menu.addAction("复制路径", lambda: self._copy("\n".join(i.abs_path for i in items)))
        menu.addSeparator()
        menu.addAction("复制风险说明", lambda: self._copy("\n".join(f"{i.path}: {i.reason_text}" for i in items)))
        menu.addAction("查看修复指引", self.solutions_requested.emit)
        menu.exec(self._table.viewport().mapToGlobal(position))

    def _open_folder(self, file_path: str) -> None:
        path = Path(file_path)
        if not path.exists():
            show_toast(self.window(), "路径已不存在", "warn")
            return
        QDesktopServices.openUrl(QUrl.fromLocalFile(str(path.parent)))

    def _copy(self, text: str) -> None:
        if not text:
            return
        QApplication.clipboard().setText(text)
        show_toast(self.window(), f"已复制 {len(text.splitlines())} 行", "ok")

    def _copy_paths(self) -> None:
        items = self._selected_items() or self._rows
        self._copy("\n".join(i.url or i.abs_path or i.path for i in items))

    def _export(self, fmt: str) -> None:
        if not self._result.items:
            show_toast(self.window(), "没有可导出的结果", "warn")
            return

        suffix = {"text": "txt", "json": "json", "csv": "csv"}[fmt]
        filters = {
            "text": "文本报告 (*.txt)",
            "json": "JSON 数据 (*.json)",
            "csv": "CSV 表格 (*.csv)",
        }[fmt]
        default_name = f"leak_report_{datetime.now():%Y%m%d_%H%M%S}.{suffix}"
        path, _ = QFileDialog.getSaveFileName(self.window(), "导出扫描报告", default_name, filters)
        if not path:
            return
        try:
            report_mod.save(path, self._result)
        except OSError as exc:
            show_toast(self.window(), f"导出失败：{exc}", "warn")
            return
        show_toast(self.window(), f"已导出：{Path(path).name}", "ok")


class QProgressBarCompat(QFrame):
    """细长进度条，支持「不确定进度」的忙状态。"""

    def __init__(self, parent=None):
        super().__init__(parent)
        layout = QHBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        self._bar = QProgressBar()
        self._bar.setRange(0, 100)
        self._bar.setValue(0)
        self._bar.setTextVisible(False)
        self._bar.setFixedHeight(6)
        layout.addWidget(self._bar)

    def setValue(self, value: int) -> None:
        self._bar.setValue(value)

    def value(self) -> int:
        return self._bar.value()

    def set_busy(self, busy: bool) -> None:
        if busy:
            self._bar.setRange(0, 0)
            self._bar.setFixedHeight(6)
        else:
            self._bar.setRange(0, 100)
