#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""三个页面：扫描检查、批量任务、使用说明。"""

from __future__ import annotations

import os
from pathlib import Path

from PyQt6.QtCore import Qt, pyqtSignal
from PyQt6.QtGui import QKeySequence, QShortcut
from PyQt6.QtWidgets import (
    QAbstractItemView,
    QFileDialog,
    QFrame,
    QHBoxLayout,
    QHeaderView,
    QLabel,
    QLineEdit,
    QPushButton,
    QScrollArea,
    QTableWidget,
    QTableWidgetItem,
    QVBoxLayout,
    QWidget,
)

from .. import APP_TITLE, AUTHOR, REPO_URL, VERSION
from ..core.models import (
    JOB_BATCH,
    JOB_LOCAL,
    JOB_REMOTE,
    JOB_USER,
    TASK_LOCAL,
    TASK_REMOTE,
    ScanRequest,
    ScanTask,
)
from ..core.rules import (
    KIND_DISPLAY,
    KIND_NAMES,
    KIND_ORDER,
    RISK_BACKGROUNDS,
    RISK_COLORS,
    RISK_NAMES,
    normalize_username,
    parse_github_url,
)
from . import theme
from .results_panel import ResultsPanel
from .widgets import Card, SegmentedControl, divider, field_label, hint_label, show_toast


class PageHeader(QWidget):
    """页面标题 + 说明。"""

    def __init__(self, title: str, subtitle: str, parent=None):
        super().__init__(parent)
        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(3)

        title_label = QLabel(title)
        title_label.setObjectName("PageTitle")
        subtitle_label = QLabel(subtitle)
        subtitle_label.setObjectName("PageSubtitle")

        layout.addWidget(title_label)
        layout.addWidget(subtitle_label)


class ScanPage(QWidget):
    """单个扫描页。"""

    scan_requested = pyqtSignal(object)
    stop_requested = pyqtSignal()
    solutions_requested = pyqtSignal()

    MODES = [("local", "本地目录"), ("remote", "远程仓库"), ("user", "用户全部仓库")]

    def __init__(self, parent=None):
        super().__init__(parent)
        self.results = ResultsPanel()

        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(14)

        layout.addWidget(
            PageHeader("敏感文件扫描", "在本地目录或 GitHub 上找出会被泄露的数据库、密钥与备份文件")
        )
        layout.addWidget(self._build_target_card())
        layout.addWidget(self.results, 1)

        self.results.solutions_requested.connect(self.solutions_requested.emit)
        QShortcut(QKeySequence("Ctrl+Return"), self, activated=self._emit_scan)
        QShortcut(QKeySequence("Ctrl+Enter"), self, activated=self._emit_scan)

    # ------------------------------------------------------------------ #

    def _build_target_card(self) -> Card:
        card = Card("扫描目标")

        self.mode = SegmentedControl(self.MODES)
        self.mode.changed.connect(self._on_mode_changed)
        mode_row = QHBoxLayout()
        mode_row.setContentsMargins(0, 0, 0, 0)
        mode_row.addWidget(self.mode)
        mode_row.addStretch(1)
        card.add_layout(mode_row)

        self.target_label = field_label("目录路径")
        self.target_label.setFixedWidth(92)
        self.target_input = QLineEdit()
        self.target_input.setPlaceholderText("选择或输入要扫描的目录")
        self.target_input.setMinimumHeight(36)

        self.browse_btn = QPushButton("浏览…")
        self.browse_btn.setCursor(Qt.CursorShape.PointingHandCursor)
        self.browse_btn.clicked.connect(self._browse)

        target_row = QHBoxLayout()
        target_row.setContentsMargins(0, 0, 0, 0)
        target_row.setSpacing(10)
        target_row.addWidget(self.target_label)
        target_row.addWidget(self.target_input, 1)
        target_row.addWidget(self.browse_btn)
        card.add_layout(target_row)

        self.target_hint = hint_label(
            "本地扫描会自动跳过 .git、node_modules、dist、build 等目录，并标注文件是否已被 Git 跟踪"
        )
        hint_row = QHBoxLayout()
        hint_row.setContentsMargins(102, 0, 0, 0)
        hint_row.addWidget(self.target_hint)
        card.add_layout(hint_row)

        self.token_label = field_label("GitHub Token")
        self.token_label.setFixedWidth(92)
        self.token_input = QLineEdit()
        self.token_input.setEchoMode(QLineEdit.EchoMode.Password)
        self.token_input.setPlaceholderText("可选，留空则使用匿名额度（60 次/小时）")
        self.token_input.setMinimumHeight(36)
        self.reveal_btn = QPushButton("显示")
        self.reveal_btn.setFixedWidth(60)
        self.reveal_btn.setCursor(Qt.CursorShape.PointingHandCursor)
        self.reveal_btn.clicked.connect(self._toggle_token_visibility)

        token_row = QHBoxLayout()
        token_row.setContentsMargins(0, 0, 0, 0)
        token_row.setSpacing(10)
        token_row.addWidget(self.token_label)
        token_row.addWidget(self.token_input, 1)
        token_row.addWidget(self.reveal_btn)
        card.add_layout(token_row)

        card.add_widget(divider())

        self.start_btn = QPushButton("开始扫描")
        self.start_btn.setProperty("variant", "primary")
        self.start_btn.setCursor(Qt.CursorShape.PointingHandCursor)
        self.start_btn.setMinimumHeight(38)
        self.start_btn.clicked.connect(self._emit_scan)

        self.stop_btn = QPushButton("停止")
        self.stop_btn.setProperty("variant", "danger")
        self.stop_btn.setCursor(Qt.CursorShape.PointingHandCursor)
        self.stop_btn.setMinimumHeight(38)
        self.stop_btn.setEnabled(False)
        self.stop_btn.clicked.connect(self.stop_requested.emit)

        self.guide_btn = QPushButton("修复指引")
        self.guide_btn.setCursor(Qt.CursorShape.PointingHandCursor)
        self.guide_btn.setMinimumHeight(38)
        self.guide_btn.clicked.connect(self.solutions_requested.emit)

        action_row = QHBoxLayout()
        action_row.setContentsMargins(0, 2, 0, 0)
        action_row.setSpacing(10)
        action_row.addWidget(self.start_btn)
        action_row.addWidget(self.stop_btn)
        action_row.addWidget(self.guide_btn)
        action_row.addStretch(1)
        shortcut_hint = hint_label("Ctrl + Enter 快速开始")
        shortcut_hint.setWordWrap(False)
        action_row.addWidget(shortcut_hint)
        card.add_layout(action_row)

        return card

    # ------------------------------------------------------------------ #

    def _on_mode_changed(self, key: str) -> None:
        if key == "local":
            self.target_label.setText("目录路径")
            self.target_input.setPlaceholderText("选择或输入要扫描的目录")
            self.target_hint.setText(
                "本地扫描会自动跳过 .git、node_modules、dist、build 等目录，并标注文件是否已被 Git 跟踪"
            )
            self.browse_btn.setVisible(True)
            self._set_token_enabled(False)
        elif key == "remote":
            self.target_label.setText("仓库地址")
            self.target_input.setPlaceholderText("owner/repo，或粘贴 GitHub 仓库链接")
            self.target_hint.setText("支持 owner/repo、https 链接与 git@ SSH 形式；私有仓库需要填写 Token")
            self.browse_btn.setVisible(False)
            self._set_token_enabled(True)
        else:
            self.target_label.setText("GitHub 用户名")
            self.target_input.setPlaceholderText("例如 ZeroOneCN，粘贴主页链接也可以")
            self.target_hint.setText("将逐个扫描该用户名下的全部公开仓库，仓库多时耗时较长")
            self.browse_btn.setVisible(False)
            self._set_token_enabled(True)

    def _set_token_enabled(self, enabled: bool) -> None:
        self.token_input.setEnabled(enabled)
        self.reveal_btn.setEnabled(enabled)
        if not enabled:
            self.token_input.setEchoMode(QLineEdit.EchoMode.Password)
            self.reveal_btn.setText("显示")

    def _toggle_token_visibility(self) -> None:
        if self.token_input.echoMode() == QLineEdit.EchoMode.Password:
            self.token_input.setEchoMode(QLineEdit.EchoMode.Normal)
            self.reveal_btn.setText("隐藏")
        else:
            self.token_input.setEchoMode(QLineEdit.EchoMode.Password)
            self.reveal_btn.setText("显示")

    def _browse(self) -> None:
        current = self.target_input.text().strip()
        directory = QFileDialog.getExistingDirectory(
            self, "选择要扫描的目录", current if os.path.isdir(current) else os.getcwd()
        )
        if directory:
            self.target_input.setText(str(Path(directory)))

    def _emit_scan(self) -> None:
        if not self.start_btn.isEnabled():
            return

        mode = self.mode.current()
        raw = self.target_input.text().strip()

        if mode == "local":
            if not raw:
                show_toast(self.window(), "请先选择要扫描的目录", "warn")
                return
            if not os.path.isdir(raw):
                show_toast(self.window(), "目录不存在或不是文件夹", "warn")
                return
            request = ScanRequest(kind=JOB_LOCAL, target=raw)
        elif mode == "remote":
            target = parse_github_url(raw)
            if not target:
                show_toast(self.window(), "仓库地址无法解析，请使用 owner/repo 格式", "warn")
                return
            request = ScanRequest(kind=JOB_REMOTE, target=target, token=self._token())
        else:
            target = normalize_username(raw)
            if not target:
                show_toast(self.window(), "GitHub 用户名格式不正确", "warn")
                return
            request = ScanRequest(kind=JOB_USER, target=target, token=self._token())

        self.scan_requested.emit(request)

    def _token(self) -> str | None:
        token = self.token_input.text().strip()
        return token or None

    # ------------------------------------------------------------------ #

    def set_busy(self, busy: bool) -> None:
        self.start_btn.setEnabled(not busy)
        self.stop_btn.setEnabled(busy)

    def focus_target(self) -> None:
        self.target_input.setFocus()


class BatchPage(QWidget):
    """批量任务页。"""

    scan_requested = pyqtSignal(object)
    stop_requested = pyqtSignal()

    def __init__(self, parent=None):
        super().__init__(parent)
        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(14)

        layout.addWidget(
            PageHeader("批量任务", "把多个本地目录与仓库排进队列，一次跑完并汇总结果")
        )

        body = QHBoxLayout()
        body.setContentsMargins(0, 0, 0, 0)
        body.setSpacing(14)
        body.addWidget(self._build_queue_card(), 3)
        body.addWidget(self._build_add_card(), 2)
        layout.addLayout(body, 1)

    # ------------------------------------------------------------------ #

    def _build_queue_card(self) -> Card:
        card = Card("任务队列")

        self.remove_btn = QPushButton("删除选中")
        self.remove_btn.setCursor(Qt.CursorShape.PointingHandCursor)
        self.remove_btn.clicked.connect(self._remove_selected)
        self.clear_btn = QPushButton("清空")
        self.clear_btn.setCursor(Qt.CursorShape.PointingHandCursor)
        self.clear_btn.clicked.connect(self._clear)
        card.add_action(self.remove_btn)
        card.add_action(self.clear_btn)

        self.queue = QTableWidget(0, 2)
        self.queue.setHorizontalHeaderLabels(["类型", "目标"])
        self.queue.verticalHeader().setVisible(False)
        self.queue.verticalHeader().setDefaultSectionSize(36)
        self.queue.setShowGrid(False)
        self.queue.setFrameShape(QFrame.Shape.NoFrame)
        self.queue.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectRows)
        self.queue.setSelectionMode(QAbstractItemView.SelectionMode.ExtendedSelection)
        self.queue.setEditTriggers(QAbstractItemView.EditTrigger.NoEditTriggers)
        header = self.queue.horizontalHeader()
        header.setSectionResizeMode(0, QHeaderView.ResizeMode.Fixed)
        header.setSectionResizeMode(1, QHeaderView.ResizeMode.Stretch)
        self.queue.setColumnWidth(0, 96)
        header.setHighlightSections(False)
        header.setDefaultAlignment(
            Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter
        )
        card.add_widget(self.queue, 1)

        self.queue_hint = hint_label("队列为空。右侧添加任务，或直接从剪贴板批量粘贴。")
        card.add_widget(self.queue_hint)
        return card

    def _build_add_card(self) -> Card:
        card = Card("添加任务")

        self.task_type = SegmentedControl([(TASK_REMOTE, "远程仓库"), (TASK_LOCAL, "本地目录")])
        self.task_type.changed.connect(self._on_type_changed)
        type_row = QHBoxLayout()
        type_row.setContentsMargins(0, 0, 0, 0)
        type_row.addWidget(self.task_type)
        type_row.addStretch(1)
        card.add_layout(type_row)

        self.target_input = QLineEdit()
        self.target_input.setPlaceholderText("owner/repo 或 GitHub 链接")
        self.target_input.setMinimumHeight(36)
        self.target_input.returnPressed.connect(self._add_task)
        card.add_widget(self.target_input)

        self.add_btn = QPushButton("添加到队列")
        self.add_btn.setProperty("variant", "ghost")
        self.add_btn.setCursor(Qt.CursorShape.PointingHandCursor)
        self.add_btn.clicked.connect(self._add_task)

        self.pick_btn = QPushButton("选择目录")
        self.pick_btn.setCursor(Qt.CursorShape.PointingHandCursor)
        self.pick_btn.clicked.connect(self._add_directory)
        self.pick_btn.setVisible(False)

        self.paste_btn = QPushButton("从剪贴板导入")
        self.paste_btn.setCursor(Qt.CursorShape.PointingHandCursor)
        self.paste_btn.clicked.connect(self._import_clipboard)

        add_row = QHBoxLayout()
        add_row.setContentsMargins(0, 0, 0, 0)
        add_row.setSpacing(8)
        add_row.addWidget(self.add_btn)
        add_row.addWidget(self.pick_btn)
        add_row.addWidget(self.paste_btn)
        add_row.addStretch(1)
        card.add_layout(add_row)

        card.add_widget(divider())

        card.add_widget(field_label("GitHub Token（可选）"))
        self.token_input = QLineEdit()
        self.token_input.setEchoMode(QLineEdit.EchoMode.Password)
        self.token_input.setPlaceholderText("批量扫描远程仓库时建议填写")
        self.token_input.setMinimumHeight(36)
        card.add_widget(self.token_input)
        card.add_widget(
            hint_label("队列会按顺序执行，单个目标失败会自动跳过，不影响其余任务。")
        )

        card.add_stretch()

        self.start_btn = QPushButton("开始批量扫描")
        self.start_btn.setProperty("variant", "primary")
        self.start_btn.setCursor(Qt.CursorShape.PointingHandCursor)
        self.start_btn.setMinimumHeight(38)
        self.start_btn.clicked.connect(self._emit_scan)

        self.stop_btn = QPushButton("停止")
        self.stop_btn.setProperty("variant", "danger")
        self.stop_btn.setCursor(Qt.CursorShape.PointingHandCursor)
        self.stop_btn.setMinimumHeight(38)
        self.stop_btn.setEnabled(False)
        self.stop_btn.clicked.connect(self.stop_requested.emit)

        action_row = QHBoxLayout()
        action_row.setContentsMargins(0, 0, 0, 0)
        action_row.setSpacing(10)
        action_row.addWidget(self.start_btn)
        action_row.addWidget(self.stop_btn)
        action_row.addStretch(1)
        card.add_layout(action_row)

        return card

    # ------------------------------------------------------------------ #

    def _on_type_changed(self, key: str) -> None:
        if key == TASK_LOCAL:
            self.target_input.setPlaceholderText("本地目录绝对路径")
            self.pick_btn.setVisible(True)
        else:
            self.target_input.setPlaceholderText("owner/repo 或 GitHub 链接")
            self.pick_btn.setVisible(False)

    def _append(self, task: ScanTask) -> None:
        row = self.queue.rowCount()
        self.queue.insertRow(row)
        type_cell = QTableWidgetItem(task.type_name)
        type_cell.setData(Qt.ItemDataRole.UserRole, (task.kind, task.target))
        target_cell = QTableWidgetItem(task.target)
        target_cell.setToolTip(task.target)
        self.queue.setItem(row, 0, type_cell)
        self.queue.setItem(row, 1, target_cell)
        self._refresh_hint()

    def _add_task(self) -> None:
        raw = self.target_input.text().strip()
        if not raw:
            show_toast(self.window(), "请输入目标地址", "warn")
            return

        if self.task_type.current() == TASK_LOCAL:
            if not os.path.isdir(raw):
                show_toast(self.window(), "目录不存在", "warn")
                return
            self._append(ScanTask(kind=TASK_LOCAL, target=str(Path(raw))))
        else:
            repo = parse_github_url(raw)
            if not repo:
                show_toast(self.window(), "仓库地址无法解析", "warn")
                return
            self._append(ScanTask(kind=TASK_REMOTE, target=repo))

        self.target_input.clear()

    def _add_directory(self) -> None:
        directory = QFileDialog.getExistingDirectory(self, "选择要扫描的目录", os.getcwd())
        if directory:
            self._append(ScanTask(kind=TASK_LOCAL, target=str(Path(directory))))

    def _import_clipboard(self) -> None:
        from PyQt6.QtWidgets import QApplication

        text = QApplication.clipboard().text().strip()
        if not text:
            show_toast(self.window(), "剪贴板为空", "warn")
            return

        added = 0
        for line in text.splitlines():
            line = line.strip()
            if not line:
                continue
            repo = parse_github_url(line)
            if repo:
                self._append(ScanTask(kind=TASK_REMOTE, target=repo))
                added += 1
            elif os.path.isdir(line):
                self._append(ScanTask(kind=TASK_LOCAL, target=str(Path(line))))
                added += 1

        if added:
            show_toast(self.window(), f"已导入 {added} 个任务", "ok")
        else:
            show_toast(self.window(), "没有识别到有效的仓库地址或目录", "warn")

    def _remove_selected(self) -> None:
        rows = sorted({index.row() for index in self.queue.selectionModel().selectedRows()}, reverse=True)
        for row in rows:
            self.queue.removeRow(row)
        self._refresh_hint()

    def _clear(self) -> None:
        self.queue.setRowCount(0)
        self._refresh_hint()

    def _refresh_hint(self) -> None:
        count = self.queue.rowCount()
        if count:
            self.queue_hint.setText(f"共 {count} 个任务，扫描完成后结果会在「扫描检查」页汇总展示")
        else:
            self.queue_hint.setText("队列为空。右侧添加任务，或直接从剪贴板批量粘贴。")

    def tasks(self) -> list[ScanTask]:
        tasks = []
        for row in range(self.queue.rowCount()):
            cell = self.queue.item(row, 0)
            if cell is None:
                continue
            payload = cell.data(Qt.ItemDataRole.UserRole)
            if payload:
                tasks.append(ScanTask(kind=payload[0], target=payload[1]))
        return tasks

    def _emit_scan(self) -> None:
        tasks = self.tasks()
        if not tasks:
            show_toast(self.window(), "请先添加至少一个任务", "warn")
            return
        token = self.token_input.text().strip() or None
        self.scan_requested.emit(ScanRequest(kind=JOB_BATCH, tasks=tasks, token=token))

    def set_busy(self, busy: bool) -> None:
        self.start_btn.setEnabled(not busy)
        self.stop_btn.setEnabled(busy)
        self.add_btn.setEnabled(not busy)


class GuidePage(QWidget):
    """使用说明页：检测范围、修复流程、关于。"""

    def __init__(self, parent=None):
        super().__init__(parent)
        outer = QVBoxLayout(self)
        outer.setContentsMargins(0, 0, 0, 0)
        outer.setSpacing(14)

        outer.addWidget(PageHeader("使用说明", "检测规则、风险判定与泄露后的处理流程"))

        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        container = QWidget()
        content = QVBoxLayout(container)
        content.setContentsMargins(0, 0, 8, 0)
        content.setSpacing(14)

        content.addWidget(self._build_rules_card())
        content.addWidget(self._build_risk_card())
        content.addWidget(self._build_fix_card())
        content.addWidget(self._build_about_card())
        content.addStretch(1)

        scroll.setWidget(container)
        outer.addWidget(scroll, 1)

    # ------------------------------------------------------------------ #

    def _build_rules_card(self) -> Card:
        card = Card("检测范围", "命中以下规则的文件会被列为风险项")
        grid = QVBoxLayout()
        grid.setSpacing(6)
        for name, patterns in [(KIND_NAMES[k], KIND_DISPLAY[k]) for k in KIND_ORDER]:
            line = QHBoxLayout()
            name_label = QLabel(name)
            name_label.setFixedWidth(130)
            name_label.setStyleSheet(f"color: {theme.TEXT}; font-size: 12px;")
            pattern_label = QLabel(patterns)
            pattern_label.setWordWrap(True)
            pattern_label.setStyleSheet(
                f"color: {theme.TEXT_MUTED}; font-size: 12px;"
                f"font-family: '{theme.FONT_FAMILY_MONO}', Consolas;"
            )
            line.addWidget(name_label, 0, Qt.AlignmentFlag.AlignTop)
            line.addWidget(pattern_label, 1)
            grid.addLayout(line)
        card.add_layout(grid)
        return card

    def _build_risk_card(self) -> Card:
        card = Card("风险等级", "由文件类型 + 文件名共同判定，同一结论用于界面与报告")
        notes = [
            ("critical", "凭证、密钥、.env、credentials.json —— 泄露等同交出口令"),
            ("high", "二进制数据库（.db/.sqlite/.mdb）、Access、SQL 转储、含 user/account 关键字的文件"),
            ("medium", "普通 SQL 脚本、.bak / .backup 备份文件"),
            ("low", "仅表结构的 schema.sql 之类"),
        ]
        for level, text in notes:
            pill_label = QLabel(RISK_NAMES[level])
            pill_label.setFixedWidth(56)
            pill_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
            pill_label.setStyleSheet(
                f"color: {RISK_COLORS[level]}; background: {RISK_BACKGROUNDS[level]};"
                f"border-radius: 7px; padding: 3px 0px; font-size: 11px; font-weight: 700;"
            )
            text_label = QLabel(text)
            text_label.setWordWrap(True)
            text_label.setStyleSheet(f"color: {theme.TEXT_MUTED}; font-size: 12px;")
            line = QHBoxLayout()
            line.setSpacing(10)
            line.addWidget(pill_label, 0, Qt.AlignmentFlag.AlignTop)
            line.addWidget(text_label, 1)
            card.add_layout(line)
        return card

    def _build_fix_card(self) -> Card:
        card = Card("泄露后的处理流程", "扫描结果里点「修复指引」可生成带具体路径的命令清单")
        steps = [
            ("1", "阻断新增", "把 *.db、*.sqlite、.env、*.bak 等模式写进 .gitignore，避免再次误提交。"),
            ("2", "停止跟踪", "git rm --cached <文件>，本地文件保留，只是不再被 Git 管理。"),
            ("3", "重写历史", "已经推送过的文件必须重写历史：git filter-repo 或 BFG，然后 git push --force。"),
            ("4", "善后", "轮换所有可能泄露的密码与密钥，开启 Secret Scanning，安装 gitleaks 钩子。"),
        ]
        for index, title, detail in steps:
            row = QHBoxLayout()
            row.setSpacing(10)
            badge = QLabel(index)
            badge.setFixedSize(22, 22)
            badge.setAlignment(Qt.AlignmentFlag.AlignCenter)
            badge.setStyleSheet(
                f"background: {theme.PRIMARY_SOFT}; color: {theme.PRIMARY_DARK};"
                "border-radius: 11px; font-size: 11px; font-weight: 700;"
            )
            text_box = QVBoxLayout()
            text_box.setSpacing(2)
            title_label = QLabel(title)
            title_label.setStyleSheet(f"color: {theme.TEXT}; font-size: 12px; font-weight: 600;")
            detail_label = QLabel(detail)
            detail_label.setWordWrap(True)
            detail_label.setStyleSheet(f"color: {theme.TEXT_MUTED}; font-size: 12px;")
            text_box.addWidget(title_label)
            text_box.addWidget(detail_label)
            row.addWidget(badge, 0, Qt.AlignmentFlag.AlignTop)
            row.addLayout(text_box, 1)
            card.add_layout(row)
        return card

    def _build_about_card(self) -> Card:
        card = Card("关于")
        info = QLabel(
            f"{APP_TITLE}  v{VERSION}\n"
            f"作者：{AUTHOR}　·　仓库：{REPO_URL}\n"
            "技术栈：Python 3.10+ / PyQt6 / GitHub REST API"
        )
        info.setWordWrap(True)
        info.setStyleSheet(f"color: {theme.TEXT_MUTED}; font-size: 12px; line-height: 160%;")
        card.add_widget(info)
        tip = hint_label(
            "隐私说明：Token 仅保存在当前进程内存中，不写盘、不上传；扫描结果也只留在本机。"
        )
        card.add_widget(tip)
        return card
