#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""检测规则：文件模式、类型元数据、风险评级、通用格式化。

这一层是纯函数集合，界面与报告都从这里取同一份判定结果，
避免 v1 里「报告里写高危、界面上显示中危」这种各说各话。
"""

from __future__ import annotations

import re
from urllib.parse import urlparse

# --------------------------------------------------------------------------- #
# 敏感文件模式
# --------------------------------------------------------------------------- #

PATTERNS: dict[str, tuple[str, ...]] = {
    "sqlite": (r"\.db$", r"\.db3$", r"\.sqlite$", r"\.sqlite3$", r"\.sqlite2$"),
    "mysql": (r"\.sql$", r"\.mysqldump$", r"\.sql\.gz$"),
    "postgresql": (r"\.pgpass$", r"\.psql_history$", r"\.pgdump$"),
    "access": (r"\.accdb$", r"\.mdb$"),
    "secret": (
        r"^\.env$",
        r"\.env\.[\w.-]+$",
        r"credentials\.json$",
        r"secrets?\.json$",
        r"\.pypirc$",
        r"\.npmrc$",
        r"\.netrc$",
        r"(^|/)id_(rsa|dsa|ecdsa|ed25519)$",
        r"\.pem$",
        r"\.pfx$",
        r"\.p12$",
        r"\.keystore$",
        r"\.jks$",
    ),
    "other": (
        r"\.bak$",
        r"\.backup$",
        r"database\.json$",
        r"data\.json$",
        r"db_config\.json$",
        r"\.dump$",
    ),
}

# 类型顺序即界面与报告中的展示顺序
KIND_ORDER: tuple[str, ...] = ("sqlite", "mysql", "postgresql", "access", "secret", "other")

# 界面「使用说明」页展示用的可读后缀说明（正则不适合直接给人看）
KIND_DISPLAY: dict[str, str] = {
    "sqlite": ".db、.db3、.sqlite、.sqlite3、.sqlite2",
    "mysql": ".sql、.mysqldump、.sql.gz",
    "postgresql": ".pgpass、.psql_history、.pgdump",
    "access": ".accdb、.mdb",
    "secret": ".env、.env.*、credentials.json、secrets.json、.npmrc、.netrc、"
    "id_rsa 等私钥、.pem、.pfx、.p12、.keystore、.jks",
    "other": ".bak、.backup、database.json、data.json、db_config.json、.dump",
}

KIND_NAMES: dict[str, str] = {
    "sqlite": "SQLite 数据库",
    "mysql": "MySQL / SQL 脚本",
    "postgresql": "PostgreSQL 相关",
    "access": "Access 数据库",
    "secret": "凭证 / 密钥文件",
    "other": "备份 / 配置数据",
}

KIND_COLORS: dict[str, str] = {
    "sqlite": "#C0392B",
    "mysql": "#1F6FD0",
    "postgresql": "#2E7D5B",
    "access": "#7B4EA8",
    "secret": "#B3261E",
    "other": "#B5761A",
}

# --------------------------------------------------------------------------- #
# 风险等级
# --------------------------------------------------------------------------- #

RISK_ORDER: tuple[str, ...] = ("critical", "high", "medium", "low")

RISK_NAMES: dict[str, str] = {
    "critical": "严重",
    "high": "高危",
    "medium": "中危",
    "low": "低危",
}

RISK_COLORS: dict[str, str] = {
    "critical": "#B3261E",
    "high": "#D2601A",
    "medium": "#A9761A",
    "low": "#2E7D5B",
}

RISK_BACKGROUNDS: dict[str, str] = {
    "critical": "#FCE9E7",
    "high": "#FDEEE2",
    "medium": "#FCF5E3",
    "low": "#E9F5EF",
}

RISK_RANK: dict[str, int] = {"critical": 3, "high": 2, "medium": 1, "low": 0}

BINARY_DB_EXTS = frozenset(
    {".db", ".db3", ".sqlite", ".sqlite3", ".sqlite2", ".accdb", ".mdb"}
)

DUMP_HINTS = ("dump", "backup", "prod", "data", "full")
CREDENTIAL_HINTS = (
    "credential",
    "secret",
    "password",
    "passwd",
    "token",
    "api_key",
    "apikey",
    "private_key",
    "privatekey",
    "id_rsa",
)
USER_DATA_HINTS = ("user", "account", "member", "customer", "order")

_COMPILED: dict[str, tuple[re.Pattern[str], ...]] = {
    kind: tuple(re.compile(p) for p in patterns) for kind, patterns in PATTERNS.items()
}


def detect_kind(filename: str) -> str | None:
    """判断文件名（可含路径）是否命中敏感模式，返回类型标识。"""
    name = filename.replace("\\", "/").lower()
    for kind in KIND_ORDER:
        for pattern in _COMPILED[kind]:
            if pattern.search(name):
                return kind
    return None


def _basename(path: str) -> str:
    return path.replace("\\", "/").rsplit("/", 1)[-1].lower()


def _suffix(name: str) -> str:
    idx = name.rfind(".")
    return name[idx:] if idx > 0 else ""


def assess_risk(path: str, kind: str) -> tuple[str, tuple[str, ...]]:
    """按文件类型与文件名评估风险等级，返回 (等级, 原因列表)。

    规则自上而下叠加，后面的命中只会把等级往上推，不会降级。
    """
    name = _basename(path)
    ext = _suffix(name)
    reasons: list[str] = []

    if kind == "secret":
        level = "critical"
        reasons.append("凭证 / 密钥类文件，泄露即等同于交出口令")
    elif kind == "sqlite":
        level = "high"
        reasons.append("二进制数据库文件，可能包含真实业务数据")
    elif kind == "access":
        level = "high"
        reasons.append("Access 数据库文件，通常存放业务原始数据")
    elif kind == "postgresql":
        level = "high"
        reasons.append("PostgreSQL 凭证或历史记录文件")
    elif kind == "mysql":
        if any(hint in name for hint in DUMP_HINTS):
            level = "high"
            reasons.append("SQL 转储脚本，通常包含真实数据而非仅表结构")
        else:
            level = "medium"
            reasons.append("SQL 脚本可能暴露表结构与样例数据")
    else:
        level = "medium"
        reasons.append("备份或配置数据文件，可能残留历史敏感内容")

    if ext in BINARY_DB_EXTS and level != "critical":  # 如 data.db.bak
        level = "high"
        reasons.append("文件本体为数据库文件")

    if any(hint in name for hint in CREDENTIAL_HINTS):
        level = "critical"
        reasons.append("文件名包含凭据关键字")
    elif any(hint in name for hint in USER_DATA_HINTS) and RISK_RANK[level] < RISK_RANK["high"]:
        level = "high"
        reasons.append("文件名暗示包含用户数据")

    if name.endswith(".sql") and "schema" in name and RISK_RANK[level] > RISK_RANK["low"]:
        level = "low"
        reasons = ["仅表结构定义，通常不含真实数据"]

    return level, tuple(dict.fromkeys(reasons))


# --------------------------------------------------------------------------- #
# 通用工具
# --------------------------------------------------------------------------- #


def format_size(size_bytes: int) -> str:
    """字节数转人类可读字符串。"""
    if size_bytes is None or size_bytes < 0:
        return "未知"
    value = float(size_bytes)
    for unit in ("B", "KB", "MB", "GB"):
        if value < 1024:
            return f"{value:.0f} {unit}" if unit == "B" else f"{value:.1f} {unit}"
        value /= 1024
    return f"{value:.1f} TB"


def parse_github_url(value: str) -> str | None:
    """把各种写法的仓库地址归一成 owner/repo，失败返回 None。"""
    text = (value or "").strip().strip("/")
    if not text:
        return None

    if re.fullmatch(r"[\w.-]+/[\w.-]+", text):
        return _strip_git(text)

    ssh_match = re.search(r"git@[\w.-]+:([\w.-]+/[\w.-]+?)(?:\.git)?$", text)
    if ssh_match:
        return _strip_git(ssh_match.group(1))

    if "github.com" in text.lower():
        parsed = urlparse(text if "://" in text else f"https://{text}")
        segments = [seg for seg in parsed.path.split("/") if seg]
        if len(segments) >= 2:
            return _strip_git(f"{segments[0]}/{segments[1]}")
        return None

    return None


def _strip_git(repo: str) -> str:
    repo = repo.strip().strip("/")
    return repo[:-4] if repo.endswith(".git") else repo


def normalize_username(value: str) -> str | None:
    """从用户名或主页链接中提取 GitHub 用户名。"""
    text = (value or "").strip()
    if not text:
        return None
    if "github.com" in text.lower():
        parsed = urlparse(text if "://" in text else f"https://{text}")
        segments = [seg for seg in parsed.path.split("/") if seg]
        return segments[0] if segments else None
    return text.strip("/") if re.fullmatch(r"[\w.-]+", text) else None
