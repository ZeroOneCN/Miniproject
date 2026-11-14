#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""自定义控件：卡片、统计卡、分段选择器、导航按钮、风险徽章、Toast。"""

from __future__ import annotations

from PyQt6.QtCore import QEasingCurve, QPropertyAnimation, QRectF, QSize, Qt, QTimer, pyqtSignal
from PyQt6.QtGui import QColor, QFont, QPainter, QPen
from PyQt6.QtWidgets import (
    QButtonGroup,
    QFrame,
    QGraphicsOpacityEffect,
    QHBoxLayout,
    QLabel,
    QPushButton,
    QSizePolicy,
    QStyledItemDelegate,
    QStyle,
    QVBoxLayout,
    QWidget,
)

from ..core.rules import KIND_COLORS, KIND_NAMES, RISK_BACKGROUNDS, RISK_COLORS, RISK_NAMES
from . import theme


class Card(QFrame):
    """带标题栏的白色圆角卡片。"""

    def __init__(self, title: str = "", subtitle: str = "", padding: int = 18, parent=None):
        super().__init__(parent)
        self.setObjectName("Card")
        self._layout = QVBoxLayout(self)
        self._layout.setContentsMargins(padding, padding - 4, padding, padding)
        self._layout.setSpacing(12)
        self._header: QHBoxLayout | None = None
        self._actions: QHBoxLayout | None = None

        if title:
            self._header = QHBoxLayout()
            self._header.setContentsMargins(0, 0, 0, 0)
            self._header.setSpacing(8)
            title_label = QLabel(title)
            title_label.setObjectName("CardTitle")
            self._header.addWidget(title_label)
            if subtitle:
                sub = QLabel(subtitle)
                sub.setObjectName("Hint")
                self._header.addWidget(sub)
            self._header.addStretch(1)
            self._actions = QHBoxLayout()
            self._actions.setContentsMargins(0, 0, 0, 0)
            self._actions.setSpacing(8)
            self._header.addLayout(self._actions)
            self._layout.addLayout(self._header)

    def add_action(self, widget: QWidget) -> None:
        """把按钮放到卡片标题右侧。"""
        if self._actions is not None:
            self._actions.addWidget(widget)

    def add_widget(self, widget: QWidget, stretch: int = 0) -> None:
        self._layout.addWidget(widget, stretch)

    def add_layout(self, layout) -> None:
        self._layout.addLayout(layout)

    def add_stretch(self) -> None:
        self._layout.addStretch(1)

    @property
    def body(self) -> QVBoxLayout:
        return self._layout


class StatCard(QFrame):
    """顶部统计小卡：大数字 + 说明。"""

    def __init__(self, title: str, value: str = "0", accent: str = theme.PRIMARY, parent=None):
        super().__init__(parent)
        self.setObjectName("StatCard")
        self.setMinimumWidth(120)
        layout = QVBoxLayout(self)
        layout.setContentsMargins(16, 12, 16, 12)
        layout.setSpacing(2)

        self._value = QLabel(value)
        self._value.setObjectName("StatValue")
        self._value.setStyleSheet(f"color: {accent};")
        title_label = QLabel(title)
        title_label.setObjectName("StatTitle")

        layout.addWidget(self._value)
        layout.addWidget(title_label)

    def set_value(self, text: str | int) -> None:
        self._value.setText(str(text))


class SegmentedControl(QFrame):
    """iOS 风格分段选择器。"""

    changed = pyqtSignal(str)

    def __init__(self, options: list[tuple[str, str]], parent=None):
        super().__init__(parent)
        self.setObjectName("Segmented")
        layout = QHBoxLayout(self)
        layout.setContentsMargins(3, 3, 3, 3)
        layout.setSpacing(2)
        self._group = QButtonGroup(self)
        self._group.setExclusive(True)
        self._buttons: dict[str, QPushButton] = {}

        for key, label in options:
            button = QPushButton(label)
            button.setCheckable(True)
            button.setCursor(Qt.CursorShape.PointingHandCursor)
            button.setProperty("variant", "segment")
            button.setSizePolicy(QSizePolicy.Policy.Preferred, QSizePolicy.Policy.Fixed)
            self._group.addButton(button)
            layout.addWidget(button)
            self._buttons[key] = button
            button.clicked.connect(lambda _checked, k=key: self.changed.emit(k))

        if options:
            self._buttons[options[0][0]].setChecked(True)

    def current(self) -> str:
        for key, button in self._buttons.items():
            if button.isChecked():
                return key
        return next(iter(self._buttons), "")

    def set_current(self, key: str) -> None:
        if key in self._buttons:
            self._buttons[key].setChecked(True)
            self.changed.emit(key)

    def set_label(self, key: str, label: str) -> None:
        if key in self._buttons:
            self._buttons[key].setText(label)

    def button(self, key: str) -> QPushButton:
        return self._buttons[key]


class NavButton(QPushButton):
    """左侧导航按钮。"""

    def __init__(self, text: str, icon_text: str = "", parent=None):
        super().__init__(parent)
        self.setObjectName("NavButton")
        self.setCheckable(True)
        self.setCursor(Qt.CursorShape.PointingHandCursor)
        self.setText(f"  {icon_text}   {text}" if icon_text else text)
        self.setMinimumHeight(42)
        self.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Fixed)


class RiskBadgeDelegate(QStyledItemDelegate):
    """把「风险等级」画成圆角徽章。"""

    def paint(self, painter: QPainter, option, index) -> None:
        level = index.data(Qt.ItemDataRole.UserRole) or "medium"
        painter.save()
        painter.setRenderHint(QPainter.RenderHint.Antialiasing, True)
        rect = QRectF(option.rect).adjusted(8, 7, -8, -7)
        painter.setPen(Qt.PenStyle.NoPen)
        painter.setBrush(QColor(RISK_BACKGROUNDS.get(level, "#F1F3F8")))
        painter.drawRoundedRect(rect, 8, 8)

        font = QFont(option.font)
        font.setBold(True)
        font.setPointSizeF(9.0)
        painter.setFont(font)
        painter.setPen(QColor(RISK_COLORS.get(level, theme.TEXT_MUTED)))
        painter.drawText(rect, Qt.AlignmentFlag.AlignCenter, RISK_NAMES.get(level, level))
        painter.restore()


class KindDotDelegate(QStyledItemDelegate):
    """「类型」列：彩色圆点 + 文本。"""

    def paint(self, painter: QPainter, option, index) -> None:
        kind = index.data(Qt.ItemDataRole.UserRole) or "other"
        painter.save()
        painter.setRenderHint(QPainter.RenderHint.Antialiasing, True)

        if option.state & QStyle.StateFlag.State_Selected:
            painter.setPen(Qt.PenStyle.NoPen)
            painter.setBrush(QColor("#E8EFFE"))
            painter.drawRect(option.rect)

        dot_rect = QRectF(option.rect.left() + 10, option.rect.center().y() - 3, 7, 7)
        painter.setPen(Qt.PenStyle.NoPen)
        painter.setBrush(QColor(KIND_COLORS.get(kind, theme.TEXT_MUTED)))
        painter.drawEllipse(dot_rect)

        text_rect = option.rect.adjusted(24, 0, -6, 0)
        painter.setPen(QColor(theme.TEXT))
        painter.drawText(
            text_rect,
            Qt.AlignmentFlag.AlignVCenter | Qt.AlignmentFlag.AlignLeft,
            KIND_NAMES.get(kind, kind),
        )
        painter.restore()


class Toast(QFrame):
    """左下角浮出的轻提示。"""

    def __init__(self, parent: QWidget, text: str, kind: str = "info"):
        super().__init__(parent)
        self.setObjectName("Toast")
        self.setAttribute(Qt.WidgetAttribute.WA_TransparentForMouseEvents, True)
        color = {"info": "#1D2A44", "ok": "#1E4B3A", "warn": "#4A2C1D"}.get(kind, "#1D2A44")
        self.setStyleSheet(f"QFrame#Toast {{ background: {color}; border-radius: 10px; }}")

        layout = QHBoxLayout(self)
        layout.setContentsMargins(16, 10, 16, 10)
        label = QLabel(text)
        label.setObjectName("ToastText")
        layout.addWidget(label)

        self._effect = QGraphicsOpacityEffect(self)
        self.setGraphicsEffect(self._effect)
        self._effect.setOpacity(0.0)

        self.adjustSize()
        self._animation = QPropertyAnimation(self._effect, b"opacity", self)
        self._animation.setDuration(180)
        self._animation.setStartValue(0.0)
        self._animation.setEndValue(1.0)
        self._animation.setEasingCurve(QEasingCurve.Type.OutCubic)

    def popup(self) -> None:
        self.show()
        self.raise_()
        self._animation.start()
        QTimer.singleShot(2400, self._fade_out)

    def _fade_out(self) -> None:
        fade = QPropertyAnimation(self._effect, b"opacity", self)
        fade.setDuration(260)
        fade.setStartValue(1.0)
        fade.setEndValue(0.0)
        fade.finished.connect(self.deleteLater)
        fade.start()
        self._fade_animation = fade


def show_toast(parent: QWidget, text: str, kind: str = "info") -> None:
    """在窗口底部居中弹出一条提示。"""
    toast = Toast(parent, text, kind)
    x = max(16, int((parent.width() - toast.width()) / 2))
    y = max(16, parent.height() - toast.height() - 48)
    toast.move(x, y)
    toast.popup()


def divider() -> QFrame:
    line = QFrame()
    line.setObjectName("Divider")
    line.setFrameShape(QFrame.Shape.HLine)
    return line


def hint_label(text: str) -> QLabel:
    label = QLabel(text)
    label.setObjectName("Hint")
    label.setWordWrap(True)
    return label


def field_label(text: str) -> QLabel:
    label = QLabel(text)
    label.setObjectName("FieldLabel")
    return label


def pill(text: str, fg: str, bg: str, bold: bool = True) -> QLabel:
    label = QLabel(text)
    weight = "700" if bold else "500"
    label.setStyleSheet(
        f"color: {fg}; background: {bg}; border-radius: 7px;"
        f"padding: 3px 9px; font-size: 11px; font-weight: {weight};"
    )
    label.setAlignment(Qt.AlignmentFlag.AlignCenter)
    label.setSizePolicy(QSizePolicy.Policy.Maximum, QSizePolicy.Policy.Fixed)
    return label
