#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""数据模型：扫描项、扫描结果、扫描任务与请求。"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Iterable, Sequence

from .rules import KIND_ORDER, RISK_ORDER, RISK_RANK, assess_risk, format_size

# 扫描任务类型
JOB_LOCAL = "local"
JOB_REMOTE = "remote"
JOB_USER = "user"
JOB_BATCH = "batch"

TASK_LOCAL = "local"
TASK_REMOTE = "remote"

TASK_TYPE_NAMES = {TASK_LOCAL: "本地目录", TASK_REMOTE: "远程仓库"}


@dataclass(slots=True)
class ScanItem:
    """一个被判定为敏感的文件。"""

    source: str
    path: str
    kind: str
    size: int = 0
    status: str = "未知"
    tracked: bool = False
    url: str = ""
    abs_path: str = ""
    risk: str = "medium"
    reasons: tuple[str, ...] = ()

    @classmethod
    def build(
        cls,
        *,
        source: str,
        path: str,
        kind: str,
        size: int = 0,
        status: str = "未知",
        tracked: bool = False,
        url: str = "",
        abs_path: str = "",
    ) -> "ScanItem":
        risk, reasons = assess_risk(path, kind)
        return cls(
            source=source,
            path=path,
            kind=kind,
            size=size,
            status=status,
            tracked=tracked,
            url=url,
            abs_path=abs_path,
            risk=risk,
            reasons=reasons,
        )

    @property
    def size_human(self) -> str:
        return format_size(self.size)

    @property
    def risk_rank(self) -> int:
        return RISK_RANK.get(self.risk, 0)

    @property
    def reason_text(self) -> str:
        return "；".join(self.reasons)

    @property
    def is_remote(self) -> bool:
        return bool(self.url)

    @property
    def open_target(self) -> str:
        """双击时应该打开的地址。"""
        return self.url or self.abs_path


@dataclass
class ScanResult:
    """一次扫描（或一批扫描）的完整结果。"""

    items: list[ScanItem] = field(default_factory=list)
    target: str = ""
    files_scanned: int = 0
    repos_scanned: int = 0
    elapsed: float = 0.0
    cancelled: bool = False

    def extend(self, other: "ScanResult") -> None:
        self.items.extend(other.items)
        self.files_scanned += other.files_scanned
        self.repos_scanned += other.repos_scanned
        self.elapsed += other.elapsed
        self.cancelled = self.cancelled or other.cancelled

    def sorted_items(self) -> list[ScanItem]:
        """按风险降序、来源与路径升序排序。"""
        return sorted(self.items, key=lambda it: (-it.risk_rank, it.source, it.path))

    def count_by_risk(self) -> dict[str, int]:
        counts = {level: 0 for level in RISK_ORDER}
        for item in self.items:
            counts[item.risk] = counts.get(item.risk, 0) + 1
        return counts

    def count_by_kind(self) -> dict[str, int]:
        counts = {kind: 0 for kind in KIND_ORDER}
        for item in self.items:
            counts[item.kind] = counts.get(item.kind, 0) + 1
        return {kind: n for kind, n in counts.items() if n}

    @property
    def sources(self) -> list[str]:
        return sorted({item.source for item in self.items})

    @property
    def high_risk_count(self) -> int:
        return sum(1 for item in self.items if item.risk_rank >= RISK_RANK["high"])

    @property
    def committed_count(self) -> int:
        return sum(1 for item in self.items if item.tracked)

    @property
    def total_size(self) -> int:
        return sum(item.size for item in self.items)

    def filtered(self, predicate) -> list[ScanItem]:
        return [item for item in self.items if predicate(item)]


@dataclass(slots=True)
class ScanTask:
    """批量扫描队列中的一项。"""

    kind: str
    target: str

    @property
    def type_name(self) -> str:
        return TASK_TYPE_NAMES.get(self.kind, self.kind)


@dataclass
class ScanRequest:
    """一次扫描请求，交给扫描引擎执行。"""

    kind: str
    target: str = ""
    token: str | None = None
    tasks: Sequence[ScanTask] | None = None

    @property
    def label(self) -> str:
        if self.kind == JOB_BATCH:
            count = len(self.tasks or ())
            return f"批量扫描（{count} 个任务）"
        if self.kind == JOB_USER:
            return f"用户 {self.target} 的全部仓库"
        return self.target


def merge_items(items: Iterable[ScanItem]) -> list[ScanItem]:
    """去重合并（同一来源同一路径只保留一条）。"""
    seen: dict[tuple[str, str], ScanItem] = {}
    for item in items:
        seen.setdefault((item.source, item.path), item)
    return list(seen.values())
