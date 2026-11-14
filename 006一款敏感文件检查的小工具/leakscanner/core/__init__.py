#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""纯逻辑层：规则、模型、Git 辅助、GitHub 客户端、扫描引擎、报告生成。"""

from .engine import CancelToken, ScanCancelled, ScanEngine
from .models import ScanItem, ScanRequest, ScanResult, ScanTask
from .rules import (
    KIND_COLORS,
    KIND_DISPLAY,
    KIND_NAMES,
    RISK_COLORS,
    RISK_NAMES,
    RISK_RANK,
    assess_risk,
    detect_kind,
    format_size,
    parse_github_url,
)

__all__ = [
    "CancelToken",
    "ScanCancelled",
    "ScanEngine",
    "ScanItem",
    "ScanRequest",
    "ScanResult",
    "ScanTask",
    "KIND_COLORS",
    "KIND_DISPLAY",
    "KIND_NAMES",
    "RISK_COLORS",
    "RISK_NAMES",
    "RISK_RANK",
    "assess_risk",
    "detect_kind",
    "format_size",
    "parse_github_url",
]
