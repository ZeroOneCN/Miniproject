#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Git 辅助：批量获取文件的跟踪 / 忽略状态。

v1 对每个命中文件都要跑两次 git 子进程（`ls-files` + `check-ignore`），
几百个文件就是几百次进程创建。这里改成「整仓库一次 ls-files + 一次
check-ignore --stdin」，扫描上千个文件的耗时从秒级抖动降到一次批量调用。
"""

from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path
from typing import Iterable

EXCLUDE_DIRS = frozenset(
    {
        ".git",
        ".svn",
        ".hg",
        "__pycache__",
        "node_modules",
        ".venv",
        "venv",
        "env",
        ".idea",
        ".vscode",
        ".tox",
        ".eggs",
        ".mypy_cache",
        ".pytest_cache",
        "dist",
        "build",
        ".next",
        "target",
    }
)

STATUS_TRACKED = "已跟踪"
STATUS_IGNORED = "已忽略"
STATUS_UNTRACKED = "未跟踪"
STATUS_UNKNOWN = "—"

_NO_WINDOW = getattr(subprocess, "CREATE_NO_WINDOW", 0) if sys.platform == "win32" else 0


def should_exclude_dir(dirname: str) -> bool:
    """判断目录是否属于「不必扫描」的第三方 / 构建产物目录。"""
    return dirname.lower() in EXCLUDE_DIRS


def _git(args: list[str], cwd: str, stdin: str | None = None, timeout: int = 30):
    return subprocess.run(
        ["git", "-c", "core.quotepath=false", *args],
        cwd=cwd,
        input=stdin,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=timeout,
        creationflags=_NO_WINDOW,
    )


class GitRepo:
    """一个工作区目录对应的 Git 状态查询器（结果内部缓存）。"""

    def __init__(self, root: str | os.PathLike[str]):
        self.root = str(Path(root))
        self._tracked: set[str] | None = None
        self._available: bool | None = None

    @property
    def available(self) -> bool:
        """目录是否位于一个 Git 工作区内。"""
        if self._available is None:
            try:
                result = _git(["rev-parse", "--is-inside-work-tree"], self.root, timeout=10)
                self._available = result.returncode == 0 and result.stdout.strip() == "true"
            except (OSError, subprocess.SubprocessError):
                self._available = False
        return self._available

    @property
    def tracked(self) -> set[str]:
        """仓库内所有被跟踪的文件（相对当前目录，正斜杠）。"""
        if self._tracked is None:
            self._tracked = set()
            if self.available:
                try:
                    result = _git(["ls-files", "-z"], self.root)
                    if result.returncode == 0:
                        self._tracked = {
                            item.replace("\\", "/") for item in result.stdout.split("\0") if item
                        }
                except (OSError, subprocess.SubprocessError):
                    pass
        return self._tracked

    def _ignored(self, candidates: Iterable[str]) -> set[str]:
        paths = [p.replace("\\", "/") for p in candidates]
        if not paths or not self.available:
            return set()
        try:
            result = _git(["check-ignore", "--stdin"], self.root, stdin="\n".join(paths))
        except (OSError, subprocess.SubprocessError):
            return set()
        if result.returncode != 0:
            return set()
        return {line.strip().replace("\\", "/") for line in result.stdout.splitlines() if line.strip()}

    def status_map(self, rel_paths: Iterable[str]) -> dict[str, tuple[bool, str]]:
        """返回 {相对路径: (是否被跟踪, 状态描述)}。"""
        paths = list(dict.fromkeys(p.replace("\\", "/") for p in rel_paths))
        if not self.available:
            return {path: (False, STATUS_UNKNOWN) for path in paths}

        tracked = self.tracked
        ignored = self._ignored([p for p in paths if p not in tracked])

        mapping: dict[str, tuple[bool, str]] = {}
        for path in paths:
            if path in tracked:
                mapping[path] = (True, STATUS_TRACKED)
            elif path in ignored:
                mapping[path] = (False, STATUS_IGNORED)
            else:
                mapping[path] = (False, STATUS_UNTRACKED)
        return mapping
