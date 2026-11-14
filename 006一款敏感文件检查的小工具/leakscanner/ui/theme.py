#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""主题：配色、字体、全局 QSS、图标加载。"""

from __future__ import annotations

from PyQt6.QtCore import QSize, Qt
from PyQt6.QtGui import QColor, QFont, QIcon, QPixmap
from PyQt6.QtWidgets import QApplication

from .. import asset

# --------------------------------------------------------------------------- #
# 调色板
# --------------------------------------------------------------------------- #

BG = "#F2F5FB"
SURFACE = "#FFFFFF"
SURFACE_ALT = "#F8FAFE"
BORDER = "#E3E9F3"
BORDER_STRONG = "#D2DBEA"
TEXT = "#1A2233"
TEXT_MUTED = "#6B798F"
TEXT_FAINT = "#96A2B6"
PRIMARY = "#3E6BFA"
PRIMARY_DARK = "#2743D8"
PRIMARY_SOFT = "#EDF1FE"
RAIL_TOP = "#101B33"
RAIL_BOTTOM = "#1C2B4F"
DANGER = "#C4372F"
SUCCESS = "#2E7D5B"

FONT_FAMILY = "Microsoft YaHei UI"
FONT_FAMILY_MONO = "Cascadia Mono"


def app_font() -> QFont:
    font = QFont(FONT_FAMILY, 10)
    font.setHintingPreference(QFont.HintingPreference.PreferFullHinting)
    return font


def app_icon() -> QIcon:
    """窗口 / 任务栏图标，优先使用多尺寸 ICO。"""
    for name in ("icon.ico", "icon.png"):
        path = asset(name)
        if path.exists():
            return QIcon(str(path))
    return QIcon()


def pixmap(name: str, size: int) -> QPixmap:
    path = asset(name)
    pixmap = QPixmap(str(path))
    if pixmap.isNull():
        return pixmap
    return pixmap.scaled(
        QSize(size, size),
        Qt.AspectRatioMode.KeepAspectRatio,
        Qt.TransformationMode.SmoothTransformation,
    )


def color(hex_value: str) -> QColor:
    return QColor(hex_value)


QSS = f"""
* {{
    font-family: "{FONT_FAMILY}";
    outline: none;
}}

QMainWindow, QDialog {{
    background: {BG};
}}

QFrame#Root, QFrame#Page {{
    background: {BG};
}}

QFrame#Sidebar {{
    background: qlineargradient(x1:0, y1:0, x2:0.2, y2:1,
                                stop:0 {RAIL_TOP}, stop:1 {RAIL_BOTTOM});
}}

QFrame#Card {{
    background: {SURFACE};
    border: 1px solid {BORDER};
    border-radius: 14px;
}}

QFrame#CardHeader {{
    background: transparent;
    border: none;
}}

QFrame#Segmented {{
    background: #EDF1F9;
    border: 1px solid #E2E8F3;
    border-radius: 10px;
}}

QFrame#Divider {{
    background: {BORDER};
    max-height: 1px;
    min-height: 1px;
    border: none;
}}

QFrame#StatCard {{
    background: {SURFACE};
    border: 1px solid {BORDER};
    border-radius: 12px;
}}

QFrame#Toast {{
    background: #1D2A44;
    border-radius: 10px;
}}

QLabel {{
    background: transparent;
    color: {TEXT};
}}

QLabel#PageTitle {{
    font-size: 20px;
    font-weight: 700;
    color: {TEXT};
}}

QLabel#PageSubtitle {{
    font-size: 12px;
    color: {TEXT_MUTED};
}}

QLabel#CardTitle {{
    font-size: 13px;
    font-weight: 700;
    color: {TEXT};
}}

QLabel#FieldLabel {{
    font-size: 12px;
    color: {TEXT_MUTED};
}}

QLabel#Hint {{
    font-size: 11px;
    color: {TEXT_FAINT};
}}

QLabel#StatValue {{
    font-size: 22px;
    font-weight: 700;
    color: {TEXT};
}}

QLabel#StatTitle {{
    font-size: 11px;
    color: {TEXT_MUTED};
}}

QLabel#BrandName {{
    font-size: 14px;
    font-weight: 700;
    color: #FFFFFF;
}}

QLabel#BrandMeta {{
    font-size: 11px;
    color: #92A3C4;
}}

QLabel#RailFooter {{
    font-size: 10px;
    color: #7C8DAF;
}}

QLabel#ToastText {{
    color: #F2F6FF;
    font-size: 12px;
}}

QPushButton {{
    background: {SURFACE};
    border: 1px solid {BORDER_STRONG};
    border-radius: 9px;
    padding: 8px 14px;
    color: #26324A;
    font-size: 12px;
    font-weight: 500;
}}
QPushButton:hover {{
    background: #F4F7FF;
    border-color: #BFCFEF;
}}
QPushButton:pressed {{
    background: #E7EDFC;
}}
QPushButton:disabled {{
    background: #F6F8FC;
    border-color: #EBEFF7;
    color: #AFB9CA;
}}

QPushButton[variant="primary"] {{
    background: {PRIMARY};
    border: 1px solid {PRIMARY};
    color: #FFFFFF;
    font-weight: 600;
    padding: 9px 22px;
}}
QPushButton[variant="primary"]:hover {{
    background: #3560E8;
    border-color: #3560E8;
}}
QPushButton[variant="primary"]:pressed {{
    background: {PRIMARY_DARK};
    border-color: {PRIMARY_DARK};
}}
QPushButton[variant="primary"]:disabled {{
    background: #C6D3F3;
    border-color: #C6D3F3;
    color: #F0F4FF;
}}

QPushButton[variant="ghost"] {{
    background: {PRIMARY_SOFT};
    border: 1px solid #DDE5FB;
    color: #33509C;
}}
QPushButton[variant="ghost"]:hover {{
    background: #E3EAFD;
}}
QPushButton[variant="ghost"]:disabled {{
    background: #F5F7FC;
    border-color: #EEF1F8;
    color: #B4BECF;
}}

QPushButton[variant="danger"] {{
    background: #FDF3F2;
    border: 1px solid #F2D7D4;
    color: {DANGER};
}}
QPushButton[variant="danger"]:hover {{
    background: #FBE9E7;
}}
QPushButton[variant="danger"]:disabled {{
    background: #F7F8FA;
    border-color: #EEF1F6;
    color: #C0C7D4;
}}

QPushButton[variant="segment"] {{
    background: transparent;
    border: none;
    border-radius: 8px;
    padding: 7px 16px;
    color: #5C6B85;
    font-size: 12px;
    font-weight: 500;
}}
QPushButton[variant="segment"]:hover {{
    background: #E4EAF7;
    color: #2C3E63;
}}
QPushButton[variant="segment"]:checked {{
    background: {SURFACE};
    color: {PRIMARY_DARK};
    font-weight: 600;
}}

QPushButton#NavButton {{
    background: transparent;
    border: none;
    border-left: 3px solid transparent;
    border-radius: 0px;
    padding: 10px 16px;
    text-align: left;
    color: #A5B5D3;
    font-size: 13px;
    font-weight: 500;
}}
QPushButton#NavButton:hover {{
    background: rgba(255, 255, 255, 0.07);
    color: #E7EEFF;
}}
QPushButton#NavButton:checked {{
    background: rgba(94, 141, 255, 0.20);
    border-left: 3px solid #7FA6FF;
    color: #FFFFFF;
    font-weight: 600;
}}

QLineEdit {{
    background: {SURFACE};
    border: 1px solid #DCE3F1;
    border-radius: 9px;
    padding: 8px 12px;
    color: {TEXT};
    font-size: 12px;
    selection-background-color: #C9D8FF;
    selection-color: {TEXT};
}}
QLineEdit:focus {{
    border: 1px solid {PRIMARY};
}}
QLineEdit:disabled {{
    background: #F4F6FB;
    border-color: #E9EEF7;
    color: #A6B1C4;
}}
QLineEdit#SearchBox {{
    padding-left: 12px;
    background: {SURFACE_ALT};
}}

QComboBox {{
    background: {SURFACE};
    border: 1px solid #DCE3F1;
    border-radius: 9px;
    padding: 7px 10px;
    color: {TEXT};
    font-size: 12px;
    min-width: 96px;
}}
QComboBox:hover {{
    border-color: #C4D2EF;
}}
QComboBox::drop-down {{
    border: none;
    width: 20px;
}}
QComboBox QAbstractItemView {{
    background: {SURFACE};
    border: 1px solid {BORDER};
    border-radius: 8px;
    padding: 4px;
    selection-background-color: {PRIMARY_SOFT};
    selection-color: {PRIMARY_DARK};
    outline: none;
}}

QCheckBox {{
    color: {TEXT_MUTED};
    font-size: 12px;
    spacing: 6px;
}}
QCheckBox::indicator {{
    width: 15px;
    height: 15px;
    border: 1px solid {BORDER_STRONG};
    border-radius: 4px;
    background: {SURFACE};
}}
QCheckBox::indicator:checked {{
    background: {PRIMARY};
    border-color: {PRIMARY};
    image: none;
}}

QTableWidget {{
    background: {SURFACE};
    border: none;
    gridline-color: transparent;
    font-size: 12px;
    color: {TEXT};
    selection-background-color: #E8EFFE;
    selection-color: {TEXT};
}}
QTableWidget::item {{
    padding: 0px 6px;
    border: none;
}}
QTableWidget::item:selected {{
    background: #E8EFFE;
}}
QHeaderView {{
    background: transparent;
}}
QHeaderView::section {{
    background: {SURFACE_ALT};
    border: none;
    border-bottom: 1px solid {BORDER};
    padding: 9px 10px;
    color: {TEXT_MUTED};
    font-size: 11px;
    font-weight: 600;
}}
QTableCornerButton::section {{
    background: {SURFACE_ALT};
    border: none;
}}

QProgressBar {{
    background: #E7EDF8;
    border: none;
    border-radius: 3px;
    min-height: 6px;
    max-height: 6px;
    text-align: center;
    color: transparent;
}}
QProgressBar::chunk {{
    background: qlineargradient(x1:0, y1:0, x2:1, y2:0, stop:0 #5C8DFF, stop:1 {PRIMARY_DARK});
    border-radius: 3px;
}}

QScrollArea {{
    background: transparent;
    border: none;
}}
QScrollBar:vertical {{
    background: transparent;
    width: 10px;
    margin: 2px 2px 2px 0px;
}}
QScrollBar::handle:vertical {{
    background: #CBD5E6;
    border-radius: 4px;
    min-height: 32px;
}}
QScrollBar::handle:vertical:hover {{
    background: #ADBBD3;
}}
QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical {{
    height: 0px;
}}
QScrollBar::add-page:vertical, QScrollBar::sub-page:vertical {{
    background: none;
}}
QScrollBar:horizontal {{
    background: transparent;
    height: 10px;
    margin: 0px 2px 2px 2px;
}}
QScrollBar::handle:horizontal {{
    background: #CBD5E6;
    border-radius: 4px;
    min-width: 32px;
}}
QScrollBar::add-line:horizontal, QScrollBar::sub-line:horizontal {{
    width: 0px;
}}
QScrollBar::add-page:horizontal, QScrollBar::sub-page:horizontal {{
    background: none;
}}

QMenu {{
    background: {SURFACE};
    border: 1px solid {BORDER};
    border-radius: 10px;
    padding: 6px;
}}
QMenu::item {{
    padding: 7px 22px 7px 14px;
    border-radius: 6px;
    color: {TEXT};
    font-size: 12px;
}}
QMenu::item:selected {{
    background: {PRIMARY_SOFT};
    color: {PRIMARY_DARK};
}}
QMenu::separator {{
    height: 1px;
    background: {BORDER};
    margin: 5px 10px;
}}

QTextBrowser {{
    background: {SURFACE};
    border: 1px solid {BORDER};
    border-radius: 12px;
    padding: 14px;
    color: #223049;
    font-family: "{FONT_FAMILY_MONO}", "{FONT_FAMILY}";
    font-size: 12px;
    selection-background-color: #C9D8FF;
}}

QToolTip {{
    background: #1D2A44;
    color: #F2F6FF;
    border: none;
    padding: 6px 8px;
    font-size: 11px;
}}
"""


def apply_theme(app: QApplication) -> None:
    app.setStyle("Fusion")
    app.setFont(app_font())
    app.setStyleSheet(QSS)
