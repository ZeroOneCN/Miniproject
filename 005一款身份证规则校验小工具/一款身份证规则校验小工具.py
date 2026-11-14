# -*- coding: utf-8 -*-
"""
身份证规则校验工具 v2.0

功能：
  1. 单条校验：长度 / 字符 / 地区码 / 出生日期 / 校验位全规则校验，并解析归属地、性别、年龄、生肖、星座
  2. 批量校验：txt / csv 文件、剪贴板导入、拖拽导入，表格化结果、重复检测、统计、导出 CSV
  3. 号码生成：按性别与出生年份区间生成校验位合法的测试号码

仅依赖 PyQt6。
"""

from __future__ import annotations

import csv
import random
import re
import sys
import time
from dataclasses import dataclass, field
from datetime import date, datetime
from pathlib import Path

from PyQt6.QtCore import QRegularExpression, QSize, Qt, QTimer
from PyQt6.QtGui import (
    QColor,
    QFont,
    QIcon,
    QKeySequence,
    QRegularExpressionValidator,
    QShortcut,
)
from PyQt6.QtWidgets import (
    QApplication,
    QAbstractItemView,
    QCheckBox,
    QComboBox,
    QFileDialog,
    QFrame,
    QHBoxLayout,
    QHeaderView,
    QLabel,
    QLineEdit,
    QMainWindow,
    QMessageBox,
    QProgressBar,
    QPushButton,
    QSpinBox,
    QStatusBar,
    QTabWidget,
    QTableWidget,
    QTableWidgetItem,
    QTextEdit,
    QVBoxLayout,
    QWidget,
)

APP_NAME = "身份证规则校验工具"
APP_VERSION = "2.0"


# --------------------------------------------------------------------------- #
# 资源与图标
# --------------------------------------------------------------------------- #
def resource_path(*parts: str) -> Path:
    """兼容源码运行与 PyInstaller 单文件打包的资源定位。"""
    base = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parent))
    return base.joinpath(*parts)


def load_app_icon() -> QIcon:
    for name in ("assets/icon.ico", "assets/icon.png", "icon.ico"):
        path = resource_path(*name.split("/"))
        if path.exists():
            return QIcon(str(path))
    return QIcon()


# --------------------------------------------------------------------------- #
# 校验核心
# --------------------------------------------------------------------------- #
WEIGHTS = (7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2)
CHECK_CODES = "10X98765432"

PROVINCES = {
    "11": "北京市", "12": "天津市", "13": "河北省", "14": "山西省",
    "15": "内蒙古自治区", "21": "辽宁省", "22": "吉林省", "23": "黑龙江省",
    "31": "上海市", "32": "江苏省", "33": "浙江省", "34": "安徽省",
    "35": "福建省", "36": "江西省", "37": "山东省", "41": "河南省",
    "42": "湖北省", "43": "湖南省", "44": "广东省", "45": "广西壮族自治区",
    "46": "海南省", "50": "重庆市", "51": "四川省", "52": "贵州省",
    "53": "云南省", "54": "西藏自治区", "61": "陕西省", "62": "甘肃省",
    "63": "青海省", "64": "宁夏回族自治区", "65": "新疆维吾尔自治区",
    "71": "台湾省", "81": "香港特别行政区", "82": "澳门特别行政区",
}

ZODIAC = "鼠牛虎兔龙蛇马羊猴鸡狗猪"

CONSTELLATIONS = (
    (1, 20, "摩羯座"), (2, 19, "水瓶座"), (3, 21, "双鱼座"), (4, 20, "白羊座"),
    (5, 21, "金牛座"), (6, 22, "双子座"), (7, 23, "巨蟹座"), (8, 23, "狮子座"),
    (9, 23, "处女座"), (10, 24, "天秤座"), (11, 23, "天蝎座"), (12, 22, "射手座"),
)

# 生成号码时使用的真实行政区划代码样本
SAMPLE_AREAS = (
    ("110101", "北京市东城区"), ("110105", "北京市朝阳区"), ("120101", "天津市和平区"),
    ("310101", "上海市黄浦区"), ("500103", "重庆市渝中区"), ("320102", "江苏省南京市玄武区"),
    ("330106", "浙江省杭州市西湖区"), ("340104", "安徽省合肥市蜀山区"), ("350203", "福建省厦门市思明区"),
    ("360102", "江西省南昌市东湖区"), ("370102", "山东省济南市历下区"), ("410105", "河南省郑州市金水区"),
    ("420106", "湖北省武汉市武昌区"), ("430102", "湖南省长沙市芙蓉区"), ("440305", "广东省深圳市南山区"),
    ("441900", "广东省东莞市"), ("510107", "四川省成都市武侯区"), ("530102", "云南省昆明市五华区"),
    ("610113", "陕西省西安市雁塔区"), ("210102", "辽宁省沈阳市和平区"), ("230103", "黑龙江省哈尔滨市南岗区"),
    ("620102", "甘肃省兰州市城关区"), ("450103", "广西南宁市青秀区"), ("520102", "贵州省贵阳市南明区"),
)


def compute_check_code(body17: str) -> str:
    """ISO 7064:1983 MOD 11-2 校验位计算。"""
    total = sum(int(ch) * w for ch, w in zip(body17, WEIGHTS))
    return CHECK_CODES[total % 11]


def normalize_id(raw: str) -> str:
    """去掉空格、连字符与全角字符，统一大写 X。"""
    text = (raw or "").strip()
    for src, dst in (("　", ""), (" ", ""), ("\t", ""), ("-", ""), ("—", "")):
        text = text.replace(src, dst)
    text = text.replace("ｘ", "X").replace("Ｘ", "X").replace("×", "X")
    for i in range(10):
        text = text.replace(chr(0xFF10 + i), str(i))
    return text.upper()


def constellation_of(birthday: date) -> str:
    """按起始日期判定星座；摩羯座跨年，故遍历到第一个尚未到达的边界。"""
    md = (birthday.month, birthday.day)
    for month, day, name in CONSTELLATIONS:
        if md < (month, day):
            return name
    return "摩羯座"


def zodiac_of(birthday: date) -> str:
    return ZODIAC[(birthday.year - 4) % 12]


@dataclass
class IDInfo:
    raw: str = ""
    normalized: str = ""
    ok: bool = False
    province: str = ""
    birthday: date | None = None
    gender: str = ""
    age: int | None = None
    zodiac: str = ""
    constellation: str = ""
    check_code: str = ""
    errors: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)

    @property
    def area(self) -> str:
        return self.province or "—"

    @property
    def birth_text(self) -> str:
        return self.birthday.strftime("%Y-%m-%d") if self.birthday else "—"

    @property
    def note(self) -> str:
        return "；".join(self.errors + self.warnings)


def validate_id(raw: str, today: date | None = None) -> IDInfo:
    """执行全部规则校验；errors 为空即视为通过。"""
    today = today or date.today()
    info = IDInfo(raw=raw or "")
    text = normalize_id(raw)
    info.normalized = text

    if not text:
        info.errors.append("号码为空")
        return info

    illegal = sorted({ch for ch in text if ch not in "0123456789X"})
    if illegal:
        info.errors.append("含非法字符：" + "、".join(illegal))
        return info

    if len(text) == 15:
        if not text.isdigit():
            info.errors.append("15 位老式号码应全部为数字")
            return info
        body = text[:6] + "19" + text[6:]
        upgraded = body + compute_check_code(body)
        info.warnings.append(f"15 位老式号码，已升级为 {upgraded}")
        text = upgraded
        info.normalized = text
    elif len(text) != 18:
        info.errors.append(f"长度应为 18 位（当前 {len(text)} 位）")
        return info

    if not text[:17].isdigit():
        info.errors.append("前 17 位必须全部为数字")
        return info

    province = PROVINCES.get(text[:2])
    if province:
        info.province = province
    else:
        info.errors.append(f"地区码「{text[:2]}」不属于有效省级行政区")

    if text[2:4] == "00":
        info.errors.append("第 3-4 位行政区划代码不能为 00")

    birth_text = text[6:14]
    try:
        birthday = date(int(birth_text[:4]), int(birth_text[4:6]), int(birth_text[6:8]))
    except ValueError:
        info.errors.append(f"出生日期「{birth_text}」不是合法日期")
        birthday = None
    if birthday:
        info.birthday = birthday
        if birthday.year < 1900:
            info.errors.append(f"出生年份 {birthday.year} 早于 1900 年")
            info.birthday = None
        elif birthday > today:
            info.errors.append(f"出生日期 {info.birth_text} 晚于当前日期")
            info.birthday = None
        else:
            info.age = (
                today.year - birthday.year
                - ((today.month, today.day) < (birthday.month, birthday.day))
            )
            if info.age >= 120:
                info.warnings.append(f"年龄 {info.age} 岁，请确认号码真实性")

    expected = compute_check_code(text[:17])
    info.check_code = expected
    if text[17] != expected:
        info.errors.append(f"末位校验码「{text[17]}」错误，应为「{expected}」")

    info.gender = "男" if int(text[16]) % 2 else "女"
    if birthday:
        info.zodiac = zodiac_of(birthday)
        info.constellation = constellation_of(birthday)

    info.ok = not info.errors
    return info


def extract_ids(text: str) -> list[str]:
    """从文本/CSV 中提取候选号码，保留异常行以便报错。"""
    result: list[str] = []
    for line in (text or "").splitlines():
        raw = line.strip()
        if not raw:
            continue
        tokens = [t for t in re.split(r"[,，;；\t| \u3000]+", raw) if t.strip()]
        hits = [t.strip() for t in tokens if len(normalize_id(t)) in (15, 18)]
        if hits:
            result.extend(hits)
        elif not re.search(r"[\u4e00-\u9fff]", raw):
            result.append(raw)  # 非表头的异常行，交给校验环节报错
    return result


def generate_ids(count: int, year_from: int, year_to: int, gender: str = "不限") -> list[str]:
    """生成校验位合法的测试号码（行政区划与顺序码均为随机样本）。"""
    lo, hi = min(year_from, year_to), max(year_from, year_to)
    today = date.today()
    out: list[str] = []
    for _ in range(count):
        area = random.choice(SAMPLE_AREAS)[0]
        year = random.randint(lo, hi)
        month = random.randint(1, 12)
        day = random.randint(1, 28)
        try:
            birthday = date(year, month, day)
        except ValueError:
            birthday = date(year, month, 1)
        if birthday > today:
            birthday = date(year, 1, 1)
        seq = random.randint(0, 99)
        if gender == "男":
            last = random.choice((1, 3, 5, 7, 9))
        elif gender == "女":
            last = random.choice((0, 2, 4, 6, 8))
        else:
            last = random.randint(0, 9)
        body = f"{area}{birthday:%Y%m%d}{seq:02d}{last}"
        out.append(body + compute_check_code(body))
    return out


# --------------------------------------------------------------------------- #
# 样式
# --------------------------------------------------------------------------- #
STYLE = """
QMainWindow, #Root { background-color: #F4F6FB; }
QWidget { font-family: 'Microsoft YaHei UI', 'Microsoft YaHei', 'Segoe UI'; font-size: 13px; color: #0F172A; }

#Header { background: qlineargradient(x1:0, y1:0, x2:1, y2:1, stop:0 #4A74FF, stop:1 #2743D8); }
#HeaderTitle { color: #FFFFFF; font-size: 17px; font-weight: 700; }
#HeaderSub { color: rgba(255, 255, 255, 0.82); font-size: 12px; }
#HeaderBadge { color: #FFFFFF; background: rgba(255, 255, 255, 0.18); border-radius: 9px;
               padding: 2px 8px; font-size: 11px; }

#Card { background-color: #FFFFFF; border: 1px solid #E4E9F2; border-radius: 12px; }
#CardTitle { font-size: 13px; font-weight: 700; color: #334155; }
#Hint { color: #7C8899; font-size: 12px; }
#StatLabel { color: #475569; font-size: 12px; }

QLineEdit {
    background-color: #F7F9FD; border: 1px solid #E4E9F2; border-radius: 8px;
    padding: 9px 12px; selection-background-color: #3E6BFA;
}
QLineEdit:focus { background-color: #FFFFFF; border: 1px solid #3E6BFA; }

QTextEdit, QPlainTextEdit {
    background-color: #FFFFFF; border: 1px solid #E4E9F2; border-radius: 8px;
    padding: 6px; selection-background-color: #3E6BFA;
}

QPushButton {
    background-color: #FFFFFF; border: 1px solid #DCE3EF; border-radius: 8px;
    padding: 8px 16px; color: #334155;
}
QPushButton:hover { background-color: #EEF3FE; border-color: #B9C8E8; }
QPushButton:pressed { background-color: #E1E9FB; }
QPushButton:disabled { color: #AAB4C4; background-color: #F4F6FA; border-color: #ECEFF5; }

QPushButton#Primary {
    background-color: #3E6BFA; border: 1px solid #3E6BFA; color: #FFFFFF; font-weight: 600;
}
QPushButton#Primary:hover { background-color: #2F55E0; border-color: #2F55E0; }
QPushButton#Primary:pressed { background-color: #2743D8; }
QPushButton#Primary:disabled { background-color: #A9BBF6; border-color: #A9BBF6; color: #FFFFFF; }

QTabWidget::pane { border: none; background: transparent; }
QTabBar::tab {
    background: transparent; color: #64748B; padding: 8px 20px; margin-right: 4px;
    border: none; border-bottom: 2px solid transparent; font-size: 13px;
}
QTabBar::tab:hover { color: #3E6BFA; }
QTabBar::tab:selected { color: #2743D8; font-weight: 600; border-bottom: 2px solid #3E6BFA; }

QTableWidget {
    background-color: #FFFFFF; border: 1px solid #E4E9F2; border-radius: 10px;
    gridline-color: transparent; alternate-background-color: #F8FAFE;
    selection-background-color: #E8EFFE; selection-color: #0F172A;
}
QTableWidget::item { padding: 4px 6px; }
QHeaderView::section {
    background-color: #F5F7FB; color: #5A6B82; border: none;
    border-bottom: 1px solid #E4E9F2; padding: 7px 6px; font-weight: 600;
}
QTableCornerButton::section { background-color: #F5F7FB; border: none; }

QProgressBar {
    background-color: #E7EDF8; border: none; border-radius: 3px; height: 6px; text-align: center;
}
QProgressBar::chunk { background-color: #3E6BFA; border-radius: 3px; }

QCheckBox { color: #475569; spacing: 6px; }
QCheckBox::indicator { width: 15px; height: 15px; border: 1px solid #C6D0E0;
                       border-radius: 4px; background: #FFFFFF; }
QCheckBox::indicator:hover { border-color: #3E6BFA; }
QCheckBox::indicator:checked {
    background-color: #3E6BFA; border-color: #3E6BFA;
    image: url("__CHECK_ICON__");
}

QSpinBox, QComboBox {
    background-color: #FFFFFF; border: 1px solid #DCE3EF; border-radius: 8px;
    padding: 5px 8px; min-height: 20px;
}
QSpinBox:focus, QComboBox:focus { border-color: #3E6BFA; }
QSpinBox::up-button, QSpinBox::down-button {
    width: 16px; border: none; background: transparent;
}
QSpinBox::up-button:hover, QSpinBox::down-button:hover { background: #EEF3FE; border-radius: 4px; }
QSpinBox::up-arrow {
    width: 0; height: 0; border-left: 4px solid transparent;
    border-right: 4px solid transparent; border-bottom: 5px solid #7C8899;
}
QSpinBox::down-arrow {
    width: 0; height: 0; border-left: 4px solid transparent;
    border-right: 4px solid transparent; border-top: 5px solid #7C8899;
}
QComboBox::drop-down { border: none; width: 22px; }
QComboBox::down-arrow {
    width: 0; height: 0; border-left: 4px solid transparent;
    border-right: 4px solid transparent; border-top: 5px solid #7C8899;
    margin-right: 6px;
}
QComboBox QAbstractItemView {
    background-color: #FFFFFF; border: 1px solid #E4E9F2; border-radius: 6px;
    selection-background-color: #E8EFFE; selection-color: #0F172A; outline: none;
    padding: 4px;
}

QStatusBar { background-color: #FFFFFF; border-top: 1px solid #E4E9F2; color: #64748B;
             min-height: 28px; }
QStatusBar::item { border: none; }
QStatusBar QLabel { color: #64748B; padding: 2px 0; }

QScrollBar:vertical { background: transparent; width: 10px; margin: 2px; }
QScrollBar::handle:vertical { background: #D3DBE9; border-radius: 5px; min-height: 24px; }
QScrollBar::handle:vertical:hover { background: #B9C5DA; }
QScrollBar::add-line, QScrollBar::sub-line { height: 0; }
QScrollBar:horizontal { background: transparent; height: 10px; margin: 2px; }
QScrollBar::handle:horizontal { background: #D3DBE9; border-radius: 5px; min-width: 24px; }
QScrollBar::add-line, QScrollBar::sub-line { width: 0; }
"""


def make_card(title: str | None = None) -> tuple[QFrame, QVBoxLayout]:
    card = QFrame()
    card.setObjectName("Card")
    layout = QVBoxLayout(card)
    layout.setContentsMargins(16, 14, 16, 14)
    layout.setSpacing(10)
    if title:
        label = QLabel(title)
        label.setObjectName("CardTitle")
        layout.addWidget(label)
    return card, layout


# --------------------------------------------------------------------------- #
# 主窗口
# --------------------------------------------------------------------------- #
class IDValidatorWindow(QMainWindow):
    CHUNK = 250

    def __init__(self) -> None:
        super().__init__()
        self.setWindowTitle(f"{APP_NAME} v{APP_VERSION}")
        self.setWindowIcon(load_app_icon())
        self.resize(1000, 730)
        self.setMinimumSize(880, 640)
        self.setAcceptDrops(True)

        self._batch_items: list[str] = []
        self._batch_results: list[IDInfo] = []
        self._batch_index = 0
        self._batch_start = 0.0
        self._dup_counter: dict[str, int] = {}

        self._live_timer = QTimer(self)
        self._live_timer.setSingleShot(True)
        self._live_timer.setInterval(220)
        self._live_timer.timeout.connect(self._live_check)

        self._batch_timer = QTimer(self)
        self._batch_timer.setInterval(0)
        self._batch_timer.timeout.connect(self._batch_step)

        self.init_ui()

    # ---------------------------- 界面搭建 ---------------------------- #
    def init_ui(self) -> None:
        root = QWidget()
        root.setObjectName("Root")
        self.setCentralWidget(root)
        outer = QVBoxLayout(root)
        outer.setContentsMargins(0, 0, 0, 0)
        outer.setSpacing(0)

        outer.addWidget(self._build_header())

        body = QWidget()
        body_layout = QVBoxLayout(body)
        body_layout.setContentsMargins(18, 12, 18, 10)
        body_layout.setSpacing(10)

        self.tabs = QTabWidget()
        self.tabs.addTab(self._build_single_tab(), "单条校验")
        self.tabs.addTab(self._build_batch_tab(), "批量校验")
        self.tabs.addTab(self._build_generate_tab(), "号码生成")
        self.tabs.currentChanged.connect(self._on_tab_changed)
        body_layout.addWidget(self.tabs)
        outer.addWidget(body, 1)

        status = QStatusBar()
        self.setStatusBar(status)
        self.status_label = QLabel("就绪 · 支持直接拖入 txt / csv 文件")
        status.addWidget(self.status_label)
        status.addPermanentWidget(QLabel(f"v{APP_VERSION}"))

        self.setStyleSheet(
            STYLE.replace("__CHECK_ICON__", resource_path("assets", "check.svg").as_posix())
        )
        self._bind_shortcuts()

    def _build_header(self) -> QWidget:
        header = QFrame()
        header.setObjectName("Header")
        header.setFixedHeight(78)
        layout = QHBoxLayout(header)
        layout.setContentsMargins(20, 12, 20, 12)
        layout.setSpacing(14)

        icon_label = QLabel()
        icon_label.setFixedSize(46, 46)
        icon_label.setStyleSheet("background: #FFFFFF; border-radius: 11px;")
        icon_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        pix = load_app_icon().pixmap(QSize(40, 40))
        if not pix.isNull():
            icon_label.setPixmap(pix)
        layout.addWidget(icon_label)

        texts = QVBoxLayout()
        texts.setSpacing(3)
        title_row = QHBoxLayout()
        title_row.setSpacing(8)
        title = QLabel(APP_NAME)
        title.setObjectName("HeaderTitle")
        badge = QLabel("GB 11643")
        badge.setObjectName("HeaderBadge")
        title_row.addWidget(title)
        title_row.addWidget(badge)
        title_row.addStretch(1)
        sub = QLabel("中华人民共和国居民身份证号码 · 规则校验与数据解析")
        sub.setObjectName("HeaderSub")
        texts.addLayout(title_row)
        texts.addWidget(sub)
        layout.addLayout(texts, 1)
        return header

    def _build_single_tab(self) -> QWidget:
        page = QWidget()
        layout = QVBoxLayout(page)
        layout.setContentsMargins(0, 14, 0, 0)
        layout.setSpacing(12)

        # 输入卡片
        card, card_layout = make_card("号码输入")
        row = QHBoxLayout()
        row.setSpacing(10)

        self.id_input = QLineEdit()
        self.id_input.setPlaceholderText("请输入 18 位身份证号码，例如 11010519491231002X")
        self.id_input.setMaxLength(24)
        self.id_input.setClearButtonEnabled(True)
        font = QFont("Consolas", 14)
        font.setLetterSpacing(QFont.SpacingType.AbsoluteSpacing, 0.6)
        self.id_input.setFont(font)
        validator = QRegularExpressionValidator(QRegularExpression("[0-9Xx ]{0,24}"))
        self.id_input.setValidator(validator)
        self.id_input.returnPressed.connect(self.verify_single_id)
        self.id_input.textChanged.connect(lambda _=None: self._live_timer.start())
        row.addWidget(self.id_input, 1)

        self.verify_btn = QPushButton("校验")
        self.verify_btn.setObjectName("Primary")
        self.verify_btn.setMinimumWidth(92)
        self.verify_btn.clicked.connect(self.verify_single_id)
        row.addWidget(self.verify_btn)
        card_layout.addLayout(row)

        tools = QHBoxLayout()
        tools.setSpacing(8)
        self.live_check = QCheckBox("输入时即时校验")
        self.live_check.setChecked(True)
        self.live_check.stateChanged.connect(lambda _=None: self._live_timer.start())
        tools.addWidget(self.live_check)
        tools.addStretch(1)

        paste_btn = QPushButton("粘贴")
        paste_btn.clicked.connect(self._paste_id)
        demo_btn = QPushButton("填入示例")
        demo_btn.clicked.connect(self._fill_demo)
        clear_btn = QPushButton("清空")
        clear_btn.clicked.connect(self._clear_single)
        self.copy_btn = QPushButton("复制结果")
        self.copy_btn.clicked.connect(self._copy_result)
        for btn in (paste_btn, demo_btn, clear_btn, self.copy_btn):
            tools.addWidget(btn)
        card_layout.addLayout(tools)
        layout.addWidget(card)

        # 结果卡片
        card2, card2_layout = make_card("校验结果")
        self.result_text = QTextEdit()
        self.result_text.setReadOnly(True)
        self.result_text.setMinimumHeight(300)
        card2_layout.addWidget(self.result_text)
        layout.addWidget(card2, 1)

        self._render_placeholder()
        return page

    def _build_batch_tab(self) -> QWidget:
        page = QWidget()
        layout = QVBoxLayout(page)
        layout.setContentsMargins(0, 14, 0, 0)
        layout.setSpacing(12)

        card, card_layout = make_card()
        row = QHBoxLayout()
        row.setSpacing(8)
        open_btn = QPushButton("选择文件")
        open_btn.setObjectName("Primary")
        open_btn.clicked.connect(self.choose_batch_file)
        clip_btn = QPushButton("从剪贴板导入")
        clip_btn.clicked.connect(self.import_from_clipboard)
        export_btn = QPushButton("导出 CSV")
        export_btn.clicked.connect(self.export_results)
        clear_btn = QPushButton("清空")
        clear_btn.clicked.connect(self._clear_batch)
        for btn in (open_btn, clip_btn, export_btn, clear_btn):
            row.addWidget(btn)
        row.addStretch(1)
        self.batch_file_label = QLabel("支持 .txt / .csv，可用分隔符：换行、逗号、分号、制表符")
        self.batch_file_label.setObjectName("Hint")
        row.addWidget(self.batch_file_label)
        card_layout.addLayout(row)

        self.progress = QProgressBar()
        self.progress.setMaximum(1)
        self.progress.setValue(0)
        self.progress.setTextVisible(False)
        card_layout.addWidget(self.progress)

        stat_row = QHBoxLayout()
        stat_row.setSpacing(18)
        self.stat_total = QLabel("共 0 条")
        self.stat_pass = QLabel("通过 0")
        self.stat_fail = QLabel("未通过 0")
        self.stat_dup = QLabel("重复 0")
        self.stat_cost = QLabel("耗时 —")
        for label in (self.stat_total, self.stat_pass, self.stat_fail, self.stat_dup, self.stat_cost):
            label.setObjectName("StatLabel")
            stat_row.addWidget(label)
        stat_row.addStretch(1)
        card_layout.addLayout(stat_row)
        layout.addWidget(card)

        self.table = QTableWidget(0, 7)
        self.table.setHorizontalHeaderLabels(
            ["#", "身份证号", "结论", "归属地", "出生日期", "性别", "说明"]
        )
        self.table.verticalHeader().setVisible(False)
        self.table.setAlternatingRowColors(True)
        self.table.setShowGrid(False)
        self.table.setWordWrap(False)
        self.table.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectRows)
        self.table.setEditTriggers(QAbstractItemView.EditTrigger.NoEditTriggers)
        header = self.table.horizontalHeader()
        header.setSectionResizeMode(0, QHeaderView.ResizeMode.Fixed)
        self.table.setColumnWidth(0, 52)
        header.setSectionResizeMode(1, QHeaderView.ResizeMode.Fixed)
        self.table.setColumnWidth(1, 186)
        header.setSectionResizeMode(2, QHeaderView.ResizeMode.Fixed)
        self.table.setColumnWidth(2, 78)
        header.setSectionResizeMode(3, QHeaderView.ResizeMode.Fixed)
        self.table.setColumnWidth(3, 130)
        header.setSectionResizeMode(4, QHeaderView.ResizeMode.Fixed)
        self.table.setColumnWidth(4, 104)
        header.setSectionResizeMode(5, QHeaderView.ResizeMode.Fixed)
        self.table.setColumnWidth(5, 56)
        header.setSectionResizeMode(6, QHeaderView.ResizeMode.Stretch)
        self.table.cellDoubleClicked.connect(self._open_row_in_single)
        layout.addWidget(self.table, 1)
        return page

    def _build_generate_tab(self) -> QWidget:
        page = QWidget()
        layout = QVBoxLayout(page)
        layout.setContentsMargins(0, 14, 0, 0)
        layout.setSpacing(12)

        card, card_layout = make_card("生成参数")
        row = QHBoxLayout()
        row.setSpacing(10)

        row.addWidget(QLabel("数量"))
        self.count_spin = QSpinBox()
        self.count_spin.setRange(1, 1000)
        self.count_spin.setValue(10)
        self.count_spin.setFixedWidth(90)
        row.addWidget(self.count_spin)

        row.addSpacing(8)
        row.addWidget(QLabel("性别"))
        self.gender_combo = QComboBox()
        self.gender_combo.addItems(["不限", "男", "女"])
        self.gender_combo.setFixedWidth(90)
        row.addWidget(self.gender_combo)

        row.addSpacing(8)
        row.addWidget(QLabel("出生年份"))
        self.year_from = QSpinBox()
        self.year_from.setRange(1900, date.today().year)
        self.year_from.setValue(1960)
        self.year_from.setFixedWidth(96)
        row.addWidget(self.year_from)
        row.addWidget(QLabel("—"))
        self.year_to = QSpinBox()
        self.year_to.setRange(1900, date.today().year)
        self.year_to.setValue(2005)
        self.year_to.setFixedWidth(96)
        row.addWidget(self.year_to)

        row.addStretch(1)
        gen_btn = QPushButton("生成")
        gen_btn.setObjectName("Primary")
        gen_btn.clicked.connect(self.generate)
        row.addWidget(gen_btn)
        card_layout.addLayout(row)

        hint = QLabel("生成的号码校验位合法，行政区划与顺序码为随机样本，仅用于测试，不对应真实公民。")
        hint.setObjectName("Hint")
        card_layout.addWidget(hint)
        layout.addWidget(card)

        card2, card2_layout = make_card("生成结果")
        tools = QHBoxLayout()
        tools.setSpacing(8)
        tools.addStretch(1)
        copy_btn = QPushButton("复制全部")
        copy_btn.clicked.connect(self._copy_generated)
        save_btn = QPushButton("保存为 txt")
        save_btn.clicked.connect(self._save_generated)
        tools.addWidget(copy_btn)
        tools.addWidget(save_btn)
        card2_layout.addLayout(tools)

        self.gen_output = QTextEdit()
        self.gen_output.setReadOnly(True)
        self.gen_output.setFont(QFont("Consolas", 12))
        card2_layout.addWidget(self.gen_output, 1)
        layout.addWidget(card2, 1)
        return page

    # ---------------------------- 交互逻辑 ---------------------------- #
    def _bind_shortcuts(self) -> None:
        QShortcut(QKeySequence("Ctrl+Return"), self, activated=self.verify_single_id)
        QShortcut(QKeySequence("Ctrl+O"), self, activated=self.choose_batch_file)
        QShortcut(QKeySequence("Ctrl+E"), self, activated=self.export_results)

    def _on_tab_changed(self, index: int) -> None:
        messages = {
            0: "单条校验 · 输入 18 位号码后回车即可",
            1: "批量校验 · 可选择文件、粘贴文本，或直接把文件拖进窗口",
            2: "号码生成 · 生成的号码可用于测试校验逻辑",
        }
        if not self._batch_timer.isActive():
            self.status_label.setText(messages.get(index, "就绪"))

    def _live_check(self) -> None:
        if not self.live_check.isChecked():
            return
        text = normalize_id(self.id_input.text())
        if len(text) in (15, 18):
            self.verify_single_id(silent=True)
        elif not text:
            self._render_placeholder()

    def _render_placeholder(self) -> None:
        self.result_text.setHtml(
            "<div style='color:#94A3B8; font-family:Microsoft YaHei UI;'>"
            "<p style='font-size:14px;'>等待输入…</p>"
            "<p style='font-size:12px;'>支持 18 位二代身份证，也兼容 15 位老式号码（自动升级校验）。</p>"
            "</div>"
        )
        self._current_result = None

    def _paste_id(self) -> None:
        text = QApplication.clipboard().text().strip()
        if not text:
            self.status_label.setText("剪贴板中没有文本")
            return
        self.id_input.setText(extract_ids(text)[0] if extract_ids(text) else text)

    def _fill_demo(self) -> None:
        self.id_input.setText("11010519491231002X")
        self.verify_single_id()

    def _clear_single(self) -> None:
        self.id_input.clear()
        self._render_placeholder()
        self.id_input.setFocus()

    def _copy_result(self) -> None:
        info = getattr(self, "_current_result", None)
        if not info:
            self.status_label.setText("还没有可复制的结果")
            return
        lines = [
            f"号码：{info.normalized}",
            f"结论：{'校验通过' if info.ok else '未通过'}",
            f"归属地：{info.area}",
            f"出生日期：{info.birth_text}",
            f"性别：{info.gender or '—'}",
            f"年龄：{info.age if info.age is not None else '—'}",
            f"校验码：{info.check_code}",
        ]
        if info.errors:
            lines.append("问题：" + "；".join(info.errors))
        if info.warnings:
            lines.append("提示：" + "；".join(info.warnings))
        QApplication.clipboard().setText("\n".join(lines))
        self.status_label.setText("结果已复制到剪贴板")

    def verify_single_id(self, silent: bool = False) -> None:
        raw = self.id_input.text().strip()
        if not raw:
            self._render_placeholder()
            if not silent:
                self.status_label.setText("请先输入身份证号")
            return
        info = validate_id(raw)
        self._current_result = info
        self.result_text.setHtml(self._result_html(info))
        if not silent:
            self.status_label.setText(
                ("校验通过：" + info.normalized) if info.ok else ("未通过：" + info.errors[0])
            )
        if info.ok and len(normalize_id(raw)) == 15:
            self.status_bar_hint("15 位老式号码已升级校验")

    def status_bar_hint(self, text: str) -> None:
        self.status_label.setText(text)

    @staticmethod
    def _result_html(info: IDInfo) -> str:
        ok = info.ok
        accent = "#15803D" if ok else "#DC2626"
        head_bg = "#ECFDF3" if ok else "#FEF2F2"
        title = "✓ 校验通过" if ok else "✗ 未通过"

        rows = [
            ("归属地", info.area),
            ("出生日期", info.birth_text),
            ("性别", info.gender or "—"),
            ("年龄", f"{info.age} 岁" if info.age is not None else "—"),
            ("生肖", info.zodiac or "—"),
            ("星座", info.constellation or "—"),
            ("校验码", f"{info.check_code}（{'一致' if ok else '不一致'}）"),
            ("标准化号码", info.normalized),
        ]
        cells = "".join(
            f"<tr><td style='color:#64748B; padding:5px 14px 5px 0;'>{k}</td>"
            f"<td style='color:#0F172A; padding:5px 0;'>{v}</td></tr>"
            for k, v in rows
        )

        blocks = [
            f"<div style='background:{head_bg}; border-radius:8px; padding:10px 14px;'>"
            f"<span style='color:{accent}; font-size:16px; font-weight:700;'>{title}</span></div>",
            f"<table cellspacing='0' cellpadding='0' style='margin-top:6px;'>{cells}</table>",
        ]
        if info.errors:
            items = "".join(
                f"<li style='margin-bottom:3px;'>{e}</li>" for e in info.errors
            )
            blocks.append(
                "<div style='margin-top:10px; color:#B91C1C; font-size:12px;'>发现问题："
                f"<ul style='margin:4px 0 0 0;'>{items}</ul></div>"
            )
        if info.warnings:
            items = "".join(f"<li style='margin-bottom:3px;'>{w}</li>" for w in info.warnings)
            blocks.append(
                "<div style='margin-top:8px; color:#B45309; font-size:12px;'>提示："
                f"<ul style='margin:4px 0 0 0;'>{items}</ul></div>"
            )
        return f"<div style='font-family:Microsoft YaHei UI;'>{''.join(blocks)}</div>"

    # ---------------------------- 批量 ---------------------------- #
    def choose_batch_file(self) -> None:
        name, _ = QFileDialog.getOpenFileName(
            self, "选择身份证号码文件", "", "号码文件 (*.txt *.csv);;所有文件 (*.*)"
        )
        if name:
            self.load_batch_file(name)

    def import_from_clipboard(self) -> None:
        text = QApplication.clipboard().text()
        items = extract_ids(text)
        if not items:
            self.status_label.setText("剪贴板中没有可识别的号码")
            return
        self._start_batch(items, "剪贴板")

    def load_batch_file(self, path: str) -> None:
        raw_text = None
        for encoding in ("utf-8-sig", "utf-8", "gb18030", "utf-16"):
            try:
                raw_text = Path(path).read_text(encoding=encoding)
                break
            except (UnicodeDecodeError, UnicodeError):
                continue
            except OSError as exc:
                QMessageBox.warning(self, "读取失败", f"无法读取文件：\n{exc}")
                return
        if raw_text is None:
            QMessageBox.warning(self, "读取失败", "文件编码无法识别，请另存为 UTF-8 后重试。")
            return

        items = extract_ids(raw_text)
        if not items:
            self.status_label.setText("文件中没有找到号码")
            return
        self.tabs.setCurrentIndex(1)
        self._start_batch(items, Path(path).name)

    def _start_batch(self, items: list[str], source: str) -> None:
        self._batch_timer.stop()
        self._batch_items = items
        self._batch_results = []
        self._batch_index = 0
        self._batch_start = time.perf_counter()
        self._dup_counter = {}
        for item in items:
            key = normalize_id(item)
            if len(key) in (15, 18):
                self._dup_counter[key] = self._dup_counter.get(key, 0) + 1

        self.table.setRowCount(len(items))
        self.progress.setMaximum(len(items))
        self.progress.setValue(0)
        self.batch_file_label.setText(f"数据源：{source} · {len(items)} 条")
        self.stat_total.setText(f"共 {len(items)} 条")
        self.stat_pass.setText("通过 0")
        self.stat_fail.setText("未通过 0")
        self.stat_dup.setText("重复 0")
        self.stat_cost.setText("耗时 —")
        self.status_label.setText(f"正在校验 {len(items)} 条号码…")
        self._batch_timer.start()

    def _batch_step(self) -> None:
        end = min(self._batch_index + self.CHUNK, len(self._batch_items))
        pass_count = sum(1 for info in self._batch_results if info.ok)
        dup_count = 0
        for row in range(self._batch_index, end):
            raw = self._batch_items[row]
            info = validate_id(raw)
            self._batch_results.append(info)
            if info.ok:
                pass_count += 1
            key = info.normalized
            duplicated = self._dup_counter.get(key, 0) > 1
            if duplicated:
                dup_count += 1
            self._fill_row(row, info, duplicated)

        self._batch_index = end
        self.progress.setValue(end)
        seen_dup = sum(
            1 for info in self._batch_results
            if self._dup_counter.get(info.normalized, 0) > 1
        )
        self.stat_pass.setText(f"通过 {pass_count}")
        self.stat_fail.setText(f"未通过 {len(self._batch_results) - pass_count}")
        self.stat_dup.setText(f"重复 {seen_dup}")

        if end >= len(self._batch_items):
            self._batch_timer.stop()
            cost = time.perf_counter() - self._batch_start
            self.stat_cost.setText(f"耗时 {cost:.2f}s")
            self.status_label.setText(
                f"校验完成 · 通过 {pass_count} 条，未通过 {len(self._batch_results) - pass_count} 条"
            )

    def _open_row_in_single(self, row: int, _column: int) -> None:
        item = self.table.item(row, 1)
        if not item or not item.text():
            return
        self.id_input.setText(item.text())
        self.tabs.setCurrentIndex(0)
        self.verify_single_id()
        self.id_input.setFocus()

    def _fill_row(self, row: int, info: IDInfo, duplicated: bool) -> None:
        note = info.note
        if duplicated:
            note = (note + "；" if note else "") + "号码重复"

        values = [
            str(row + 1),
            info.normalized or info.raw,
            "通过" if info.ok else "未通过",
            info.area,
            info.birth_text,
            info.gender or "—",
            note,
        ]
        for col, text in enumerate(values):
            item = QTableWidgetItem(text)
            if col in (0, 2, 4, 5):
                item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)
            if col == 2:
                item.setForeground(QColor("#15803D" if info.ok else "#DC2626"))
            if col == 6 and not info.ok:
                item.setForeground(QColor("#B91C1C"))
            elif col == 6 and info.warnings:
                item.setForeground(QColor("#B45309"))
            self.table.setItem(row, col, item)

    def export_results(self) -> None:
        if not self._batch_results:
            self.status_label.setText("没有可导出的结果")
            return
        default = f"身份证校验结果_{datetime.now():%Y%m%d_%H%M%S}.csv"
        path, _ = QFileDialog.getSaveFileName(
            self, "导出结果", default, "CSV 文件 (*.csv)"
        )
        if not path:
            return
        try:
            with open(path, "w", newline="", encoding="utf-8-sig") as handle:
                writer = csv.writer(handle)
                writer.writerow(
                    ["序号", "身份证号", "结论", "归属地", "出生日期", "性别", "年龄", "说明"]
                )
                for index, info in enumerate(self._batch_results, 1):
                    writer.writerow([
                        index,
                        info.normalized or info.raw,
                        "通过" if info.ok else "未通过",
                        info.area,
                        info.birth_text,
                        info.gender or "",
                        info.age if info.age is not None else "",
                        info.note,
                    ])
        except OSError as exc:
            QMessageBox.warning(self, "导出失败", str(exc))
            return
        self.status_label.setText(f"已导出 {len(self._batch_results)} 条到 {Path(path).name}")

    def _clear_batch(self) -> None:
        self._batch_timer.stop()
        self._batch_items = []
        self._batch_results = []
        self._dup_counter = {}
        self.table.setRowCount(0)
        self.progress.setMaximum(1)
        self.progress.setValue(0)
        self.stat_total.setText("共 0 条")
        self.stat_pass.setText("通过 0")
        self.stat_fail.setText("未通过 0")
        self.stat_dup.setText("重复 0")
        self.stat_cost.setText("耗时 —")
        self.batch_file_label.setText("支持 .txt / .csv，可用分隔符：换行、逗号、分号、制表符")
        self.status_label.setText("批量结果已清空")

    # ---------------------------- 生成 ---------------------------- #
    def generate(self) -> None:
        count = self.count_spin.value()
        ids = generate_ids(
            count,
            self.year_from.value(),
            self.year_to.value(),
            self.gender_combo.currentText(),
        )
        self.gen_output.setPlainText("\n".join(ids))
        self.status_label.setText(f"已生成 {count} 条测试号码")

    def _copy_generated(self) -> None:
        text = self.gen_output.toPlainText().strip()
        if not text:
            self.status_label.setText("还没有生成号码")
            return
        QApplication.clipboard().setText(text)
        self.status_label.setText("已复制到剪贴板")

    def _save_generated(self) -> None:
        text = self.gen_output.toPlainText().strip()
        if not text:
            self.status_label.setText("还没有生成号码")
            return
        default = f"测试身份证_{datetime.now():%Y%m%d_%H%M%S}.txt"
        path, _ = QFileDialog.getSaveFileName(self, "保存号码", default, "文本文件 (*.txt)")
        if not path:
            return
        try:
            Path(path).write_text(text + "\n", encoding="utf-8")
        except OSError as exc:
            QMessageBox.warning(self, "保存失败", str(exc))
            return
        self.status_label.setText(f"已保存到 {Path(path).name}")

    # ---------------------------- 拖拽 ---------------------------- #
    def dragEnterEvent(self, event) -> None:  # noqa: N802
        if event.mimeData().hasUrls():
            event.acceptProposedAction()

    def dropEvent(self, event) -> None:  # noqa: N802
        urls = [u for u in event.mimeData().urls() if u.isLocalFile()]
        if not urls:
            return
        suffix = Path(urls[0].toLocalFile()).suffix.lower()
        if suffix in (".txt", ".csv"):
            self.load_batch_file(urls[0].toLocalFile())
        else:
            self.status_label.setText("仅支持拖入 .txt 或 .csv 文件")


def main() -> None:
    app = QApplication(sys.argv)
    app.setApplicationName(APP_NAME)
    app.setApplicationDisplayName(APP_NAME)
    app.setWindowIcon(load_app_icon())
    try:  # Windows 任务栏使用独立图标而非 python.exe 的图标
        import ctypes

        ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID("cn.tool.idcard.validator")
    except Exception:
        pass
    window = IDValidatorWindow()
    window.show()
    sys.exit(app.exec())


if __name__ == "__main__":
    main()
