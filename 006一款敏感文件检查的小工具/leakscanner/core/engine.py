#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""扫描引擎：本地目录、远程仓库、用户全仓库、批量任务。

引擎只依赖 core 内部模块，完全脱离 GUI，可以在脚本或测试里直接调用：

    engine = ScanEngine(progress=print)
    result = engine.run(ScanRequest(kind=JOB_REMOTE, target="owner/repo"))
"""

from __future__ import annotations

import os
import threading
import time
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path
from typing import Callable, Iterable, Sequence

from .gitutil import STATUS_TRACKED, GitRepo, should_exclude_dir
from .github import GitHubClient, GitHubError, NotFoundError, find_sensitive
from .models import (
    JOB_BATCH,
    JOB_LOCAL,
    JOB_REMOTE,
    JOB_USER,
    ScanItem,
    ScanRequest,
    ScanResult,
    ScanTask,
)
from .rules import detect_kind, normalize_username, parse_github_url

Progress = Callable[[str, int | None], None]

COUNT_LIMIT = 300_000
SIZE_WORKERS = 6


class ScanError(Exception):
    """可以直接展示给用户看的扫描错误。"""


class ScanCancelled(Exception):
    """用户主动中止扫描。"""


class CancelToken:
    """线程安全的中止令牌。"""

    def __init__(self) -> None:
        self._event = threading.Event()

    def cancel(self) -> None:
        self._event.set()

    @property
    def cancelled(self) -> bool:
        return self._event.is_set()

    def raise_if_cancelled(self) -> None:
        if self._event.is_set():
            raise ScanCancelled()


class _Throttle:
    """进度回调节流：百分比不变、消息相同且间隔过短时不重复上报。"""

    def __init__(self, callback: Progress | None, interval: float = 0.08):
        self._callback = callback
        self._interval = interval
        self._last_time = 0.0
        self._last: tuple[str, int | None] | None = None

    def __call__(self, message: str, percent: int | None = None) -> None:
        if self._callback is None:
            return
        now = time.monotonic()
        payload = (message, percent)
        if payload == self._last and now - self._last_time < 0.4:
            return
        if now - self._last_time < self._interval and self._last and self._last[1] == percent:
            return
        self._last = payload
        self._last_time = now
        self._callback(message, percent)


class ScanEngine:
    """把一次 ScanRequest 变成 ScanResult。"""

    def __init__(self, progress: Progress | None = None, client_factory=GitHubClient):
        self.token = CancelToken()
        self._report = _Throttle(progress)
        self._client_factory = client_factory
        self._client: GitHubClient | None = None

    # ------------------------------------------------------------------ #
    # 公共入口
    # ------------------------------------------------------------------ #

    def cancel(self) -> None:
        self.token.cancel()

    def run(self, request: ScanRequest) -> ScanResult:
        started = time.perf_counter()
        self.token.raise_if_cancelled()

        if request.kind == JOB_LOCAL:
            result = self._scan_local(request.target)
        elif request.kind == JOB_REMOTE:
            result = self._scan_remote(request.target, request.token)
        elif request.kind == JOB_USER:
            result = self._scan_user(request.target, request.token)
        elif request.kind == JOB_BATCH:
            result = self._scan_batch(list(request.tasks or ()), request.token)
        else:
            raise ScanError(f"未知的扫描类型：{request.kind}")

        result.target = request.label if request.kind == JOB_BATCH else request.target
        result.elapsed = time.perf_counter() - started
        result.cancelled = self.token.cancelled
        return result

    # ------------------------------------------------------------------ #
    # 客户端
    # ------------------------------------------------------------------ #

    def _github(self, token: str | None) -> GitHubClient:
        if self._client is None:
            try:
                self._client = self._client_factory(token)
            except GitHubError as exc:
                raise ScanError(str(exc)) from exc
        return self._client

    # ------------------------------------------------------------------ #
    # 本地扫描
    # ------------------------------------------------------------------ #

    def _scan_local(self, target: str) -> ScanResult:
        root = Path(os.path.expanduser(target)).resolve()
        if not root.exists():
            raise ScanError(f"目录不存在：{root}")
        if not root.is_dir():
            raise ScanError(f"不是目录：{root}")

        source = root.name or str(root)
        self._report(f"正在遍历 {source} …", 3)

        total_files = self._count_files(root)
        candidates: list[tuple[str, str, str, int]] = []
        scanned = 0
        last_report = 0.0

        for current, dirs, files in os.walk(root, onerror=lambda _err: None):
            self.token.raise_if_cancelled()
            dirs[:] = [d for d in dirs if not should_exclude_dir(d)]

            for filename in files:
                scanned += 1
                kind = detect_kind(filename)
                if kind:
                    absolute = os.path.join(current, filename)
                    relative = os.path.relpath(absolute, root).replace("\\", "/")
                    try:
                        size = os.path.getsize(absolute)
                    except OSError:
                        size = 0
                    candidates.append((relative, absolute, kind, size))

                if total_files and scanned % 40 == 0:
                    now = time.monotonic()
                    if now - last_report > 0.08:
                        last_report = now
                        percent = 5 + int(scanned / total_files * 70)
                        self._report(f"扫描 {scanned}/{total_files} 个文件（命中 {len(candidates)}）", percent)

        self.token.raise_if_cancelled()
        self._report(f"扫描完成，共 {scanned} 个文件，命中 {len(candidates)} 个，正在核对 Git 状态 …", 80)

        repo = GitRepo(root)
        status_map = repo.status_map([c[0] for c in candidates])
        if not repo.available:
            status_note = "（非 Git 仓库）"
        else:
            status_note = ""

        items = []
        for relative, absolute, kind, size in candidates:
            tracked, status = status_map.get(relative, (False, "—"))
            items.append(
                ScanItem.build(
                    source=source,
                    path=relative,
                    kind=kind,
                    size=size,
                    status=f"{status}{status_note}",
                    tracked=tracked,
                    abs_path=absolute,
                )
            )

        self._report(f"完成：{len(items)} 个敏感文件", 100)
        return ScanResult(items=items, files_scanned=scanned)

    @staticmethod
    def _count_files(root: Path) -> int:
        total = 0
        for current, dirs, files in os.walk(root, onerror=lambda _err: None):
            dirs[:] = [d for d in dirs if not should_exclude_dir(d)]
            total += len(files)
            if total > COUNT_LIMIT:
                return 0  # 超大目录不再精确计数，进度改为按已扫描数推进
        return total

    # ------------------------------------------------------------------ #
    # 远程扫描
    # ------------------------------------------------------------------ #

    def _scan_remote(self, repo_input: str, token: str | None) -> ScanResult:
        repo = parse_github_url(repo_input)
        if not repo:
            raise ScanError("无法解析仓库地址，请使用 owner/repo 或完整 GitHub 链接")

        client = self._github(token)
        self._report(f"连接仓库 {repo} …", 5)
        try:
            info = client.get_repo(repo)
        except NotFoundError as exc:
            raise ScanError(f"仓库 {repo} 不存在或无权访问") from exc
        except GitHubError as exc:
            raise ScanError(str(exc)) from exc

        branches = [info.default_branch] if info.default_branch else ["main", "master"]
        tree: list[dict] = []
        used_branch = branches[0]
        for branch in branches:
            self.token.raise_if_cancelled()
            self._report(f"读取 {repo}@{branch} 文件树 …", 15)
            try:
                tree, truncated = client.get_tree(repo, branch)
            except NotFoundError:
                continue
            except GitHubError as exc:
                raise ScanError(str(exc)) from exc
            used_branch = branch
            if truncated:
                self._report("仓库文件数超过 GitHub 上限，文件树可能不完整", 18)
            break

        if not tree:
            raise ScanError(f"无法读取 {repo} 的文件树")

        hits = find_sensitive(tree, detect_kind)
        self._report(f"{repo}：{len(tree)} 个文件中命中 {len(hits)} 个", 35)

        items: list[ScanItem] = []
        if hits:
            self._report(f"正在获取 {len(hits)} 个文件的大小 …", 45)
            sizes = self._fetch_sizes(client, repo, used_branch, hits)
            for hit in hits:
                items.append(
                    ScanItem.build(
                        source=info.full_name if info.full_name else repo,
                        path=hit["path"],
                        kind=hit["kind"],
                        size=sizes.get(hit["path"], 0),
                        status="已推送",
                        tracked=True,
                        url=f"https://github.com/{repo}/blob/{used_branch}/{hit['path']}",
                    )
                )

        return ScanResult(items=items, files_scanned=len(tree), repos_scanned=1)

    def _fetch_sizes(
        self, client: GitHubClient, repo: str, branch: str, hits: Sequence[dict]
    ) -> dict[str, int]:
        sizes: dict[str, int] = {}
        workers = min(SIZE_WORKERS, len(hits)) or 1
        with ThreadPoolExecutor(max_workers=workers) as pool:
            futures = {
                pool.submit(client.get_blob_size, repo, branch, hit["path"]): hit["path"]
                for hit in hits
            }
            done = 0
            for future in as_completed(futures):
                path = futures[future]
                done += 1
                try:
                    sizes[path] = future.result()
                except Exception:  # 单个文件取大小失败不影响整体
                    sizes[path] = 0
                if done % 5 == 0:
                    percent = 45 + int(done / max(len(hits), 1) * 45)
                    self._report(f"获取文件信息 {done}/{len(hits)} …", percent)
        return sizes

    def _scan_user(self, username_input: str, token: str | None) -> ScanResult:
        username = normalize_username(username_input)
        if not username:
            raise ScanError("GitHub 用户名格式不正确")

        client = self._github(token)
        self._report(f"获取 {username} 的仓库列表 …", 3)
        try:
            repos = client.list_user_repos(username)
        except NotFoundError as exc:
            raise ScanError(f"用户 {username} 不存在") from exc
        except GitHubError as exc:
            raise ScanError(str(exc)) from exc

        if not repos:
            raise ScanError(f"用户 {username} 没有公开仓库")

        self._report(f"{username} 共 {len(repos)} 个公开仓库，开始逐个扫描 …", 8)
        combined = ScanResult()
        for index, repo in enumerate(repos, 1):
            self.token.raise_if_cancelled()
            self._report(f"[{index}/{len(repos)}] 扫描 {repo}", 8 + int((index - 1) / len(repos) * 88))
            try:
                combined.extend(self._scan_remote(repo, token))
            except ScanError as exc:  # 单个仓库失败不打断整体
                self._report(f"{repo} 跳过：{exc}", None)
        return combined

    # ------------------------------------------------------------------ #
    # 批量扫描
    # ------------------------------------------------------------------ #

    def _scan_batch(self, tasks: list[ScanTask], token: str | None) -> ScanResult:
        if not tasks:
            raise ScanError("任务列表为空")
        combined = ScanResult()
        total = len(tasks)
        for index, task in enumerate(tasks):
            self.token.raise_if_cancelled()
            base = int(index / total * 100)
            span = int(1 / total * 100)
            self._report(f"[{index + 1}/{total}] {task.type_name}：{task.target}", base)

            child = ScanEngine(progress=lambda msg, pct, b=base, s=span: self._report(
                msg, None if pct is None else b + int(pct * s / 100)
            ))
            child.token = self.token
            try:
                if task.kind == "local":
                    combined.extend(child._scan_local(task.target))
                else:
                    combined.extend(child._scan_remote(task.target, token))
            except ScanError as exc:
                self._report(f"跳过 {task.target}：{exc}", None)
        return combined


def iter_targets(tasks: Iterable[ScanTask]) -> list[str]:
    return [task.target for task in tasks]
