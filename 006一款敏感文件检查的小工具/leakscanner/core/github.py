#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""GitHub REST API 客户端：统一超时、重试、限流与错误语义。

v1 把 requests 调用散落在扫描逻辑里，每处各自 if status_code == 403/404，
错误信息还没人看（只打印一行进度就返回空列表）。这里收敛成一个客户端，
上层的扫描器只需要处理三种异常：限流、找不到、网络异常。
"""

from __future__ import annotations

import time
from dataclasses import dataclass
from typing import Any, Iterable

try:
    import requests
except ImportError:  # pragma: no cover - 运行环境缺失依赖时的兜底
    requests = None  # type: ignore[assignment]

API_ROOT = "https://api.github.com"
RAW_ROOT = "https://raw.githubusercontent.com"
USER_AGENT = "leakscanner/3.0 (+https://github.com/ZeroOneCN/Miniproject)"


class GitHubError(Exception):
    """GitHub 相关错误基类。"""


class RateLimitError(GitHubError):
    """触发 API 限流。"""

    def __init__(self, message: str = "GitHub API 请求次数已用尽", reset_at: int | None = None):
        if reset_at:
            message = f"{message}，请于 {time.strftime('%H:%M', time.localtime(reset_at))} 后重试"
        super().__init__(message)
        self.reset_at = reset_at


class NotFoundError(GitHubError):
    """仓库 / 用户 / 分支不存在，或无权访问。"""


class AuthError(GitHubError):
    """Token 无效或权限不足。"""


class NetworkError(GitHubError):
    """网络层异常。"""


@dataclass(slots=True)
class RepoInfo:
    full_name: str
    default_branch: str = "main"
    private: bool = False
    size_kb: int = 0
    description: str = ""


@dataclass(slots=True)
class RateLimit:
    remaining: int = 0
    limit: int = 60
    reset_at: int = 0

    @property
    def description(self) -> str:
        return f"API 配额 {self.remaining}/{self.limit}"


class GitHubClient:
    """轻量封装，只实现本工具用到的几个端点。"""

    def __init__(self, token: str | None = None, timeout: int = 20, retries: int = 2):
        if requests is None:  # pragma: no cover
            raise GitHubError("缺少 requests 依赖，请执行 pip install requests")
        self.token = (token or "").strip() or None
        self.timeout = timeout
        self.retries = max(0, retries)
        self.session = requests.Session()
        self.session.headers.update({"User-Agent": USER_AGENT})
        self._rate_limit = RateLimit()

    # ------------------------------------------------------------------ #
    # 底层请求
    # ------------------------------------------------------------------ #

    def _headers(self, api: bool = True) -> dict[str, str]:
        headers = {"Accept": "application/vnd.github+json"} if api else {}
        if self.token:
            headers["Authorization"] = f"Bearer {self.token}"
        return headers

    def _request(self, url: str, params: dict[str, Any] | None = None, api: bool = True):
        last_error: Exception | None = None
        for attempt in range(self.retries + 1):
            try:
                response = self.session.get(
                    url, params=params, headers=self._headers(api), timeout=self.timeout
                )
            except requests.exceptions.RequestException as exc:  # type: ignore[union-attr]
                last_error = exc
                if attempt < self.retries:
                    time.sleep(1.2 * (attempt + 1))
                    continue
                raise NetworkError(f"网络连接失败：{exc}") from exc

            self._capture_rate_limit(response)

            if response.status_code == 200:
                return response

            if response.status_code in (500, 502, 503, 504) and attempt < self.retries:
                time.sleep(1.5 * (attempt + 1))
                continue

            self._raise_for_status(response, url)

        raise NetworkError(f"请求失败：{last_error}")

    def _capture_rate_limit(self, response) -> None:
        headers = response.headers
        if "X-RateLimit-Remaining" in headers:
            try:
                self._rate_limit = RateLimit(
                    remaining=int(headers.get("X-RateLimit-Remaining", 0)),
                    limit=int(headers.get("X-RateLimit-Limit", 60)),
                    reset_at=int(headers.get("X-RateLimit-Reset", 0)),
                )
            except ValueError:
                pass

    def _raise_for_status(self, response, url: str) -> None:
        status = response.status_code
        if status == 403 and response.headers.get("X-RateLimit-Remaining") == "0":
            raise RateLimitError(reset_at=int(response.headers.get("X-RateLimit-Reset", 0) or 0))
        if status == 403:
            raise RateLimitError("GitHub 拒绝访问（可能触发二级限流或 Token 权限不足）")
        if status == 401:
            raise AuthError("Token 无效或已过期，请重新生成")
        if status == 404:
            raise NotFoundError("目标不存在，或当前 Token 无权访问")
        raise GitHubError(f"GitHub 返回 HTTP {status}（{url}）")

    @property
    def rate_limit(self) -> RateLimit:
        return self._rate_limit

    # ------------------------------------------------------------------ #
    # 业务端点
    # ------------------------------------------------------------------ #

    def get_repo(self, repo: str) -> RepoInfo:
        data = self._request(f"{API_ROOT}/repos/{repo}")
        payload = data.json()
        return RepoInfo(
            full_name=payload.get("full_name", repo),
            default_branch=payload.get("default_branch") or "main",
            private=bool(payload.get("private")),
            size_kb=int(payload.get("size") or 0),
            description=payload.get("description") or "",
        )

    def get_tree(self, repo: str, ref: str) -> tuple[list[dict[str, Any]], bool]:
        """递归获取文件树，返回 (条目列表, 是否被截断)。"""
        data = self._request(
            f"{API_ROOT}/repos/{repo}/git/trees/{ref}", params={"recursive": "1"}
        ).json()
        return list(data.get("tree") or ()), bool(data.get("truncated"))

    def list_user_repos(self, username: str) -> list[str]:
        """列出用户的公开仓库（含分页）。"""
        repos: list[str] = []
        page = 1
        while True:
            data = self._request(
                f"{API_ROOT}/users/{username}/repos",
                params={"page": page, "per_page": 100, "sort": "pushed"},
            ).json()
            if not isinstance(data, list) or not data:
                break
            repos.extend(item["full_name"] for item in data if item.get("full_name"))
            if len(data) < 100:
                break
            page += 1
        return repos

    def get_blob_size(self, repo: str, ref: str, path: str) -> int:
        """取文件大小：优先 raw HEAD（不消耗 API 配额），失败再退回 contents API。"""
        raw_url = f"{RAW_ROOT}/{repo}/{ref}/{path}"
        try:
            response = self.session.head(
                raw_url, headers=self._headers(api=False), timeout=8, allow_redirects=True
            )
            if response.status_code == 200:
                length = response.headers.get("Content-Length")
                if length is not None:
                    return int(length)
        except (requests.exceptions.RequestException, ValueError):  # type: ignore[union-attr]
            pass

        try:
            payload = self._request(
                f"{API_ROOT}/repos/{repo}/contents/{path}", params={"ref": ref}
            ).json()
            return int(payload.get("size") or 0)
        except GitHubError:
            return 0

    def check_token(self) -> str:
        """校验 Token，返回账号名。"""
        if not self.token:
            raise AuthError("未填写 Token")
        payload = self._request(f"{API_ROOT}/user").json()
        return payload.get("login", "")


def probe_token(token: str | None) -> tuple[bool, str]:
    """给界面用的轻量探测：返回 (是否可用, 描述)。"""
    if not token:
        return False, "未使用 Token（限额 60 次/小时）"
    try:
        login = GitHubClient(token).check_token()
        return True, f"已认证：{login}"
    except GitHubError as exc:
        return False, str(exc)


def find_sensitive(tree: Iterable[dict[str, Any]], detector) -> list[dict[str, Any]]:
    """从文件树里挑出命中规则的文件条目。"""
    hits = []
    for entry in tree:
        if entry.get("type") != "blob":
            continue
        kind = detector(entry.get("path", ""))
        if kind:
            hits.append({"path": entry["path"], "kind": kind, "sha": entry.get("sha", "")})
    return hits
