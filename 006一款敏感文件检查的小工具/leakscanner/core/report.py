#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""报告生成：TXT / JSON / CSV。

三种格式共用同一份 ScanResult 与同一套风险判定，保证「界面看到的」
和「报告里写的」完全一致。
"""

from __future__ import annotations

import csv
import json
from datetime import datetime
from pathlib import Path
from typing import Iterable

from .. import APP_TITLE, VERSION
from .models import ScanItem, ScanResult
from .rules import KIND_NAMES, RISK_NAMES, RISK_ORDER

LINE = "=" * 78
SUB = "-" * 78


def _now() -> str:
    return datetime.now().strftime("%Y-%m-%d %H:%M:%S")


def _group_by_source(items: Iterable[ScanItem]) -> dict[str, list[ScanItem]]:
    grouped: dict[str, list[ScanItem]] = {}
    for item in items:
        grouped.setdefault(item.source, []).append(item)
    return {key: sorted(value, key=lambda i: (-i.risk_rank, i.path)) for key, value in sorted(grouped.items())}


def build_text_report(result: ScanResult, generated_at: str | None = None) -> str:
    """生成纯文本报告。"""
    stamp = generated_at or _now()
    counts = result.count_by_risk()
    lines: list[str] = [
        LINE,
        f"{APP_TITLE} · 扫描报告",
        LINE,
        "",
        f"生成时间: {stamp}",
        f"工具版本: v{VERSION}",
        f"扫描目标: {result.target or '—'}",
        f"耗时: {result.elapsed:.1f} 秒",
        f"扫描文件: {result.files_scanned} 个",
        f"敏感文件: {len(result.items)} 个",
        f"涉及来源: {len(result.sources)} 个",
        f"已提交/推送: {result.committed_count} 个",
    ]
    if result.cancelled:
        lines.append("注意: 本次扫描被手动中止，结果可能不完整")

    lines += ["", LINE, "风险概览", LINE, ""]
    for level in RISK_ORDER:
        if counts.get(level):
            lines.append(f"  [{RISK_NAMES[level]}] {counts[level]} 个")
    kind_counts = result.count_by_kind()
    if kind_counts:
        lines.append("")
        for kind, number in kind_counts.items():
            lines.append(f"  {KIND_NAMES.get(kind, kind)}: {number} 个")

    if not result.items:
        lines += ["", LINE, "结论", LINE, "", "未发现敏感文件，保持现状即可。", ""]
    else:
        for source, items in _group_by_source(result.items).items():
            lines += ["", LINE, f"来源: {source}", LINE, ""]
            for index, item in enumerate(items, 1):
                lines.append(f"  [{index}] {item.path}")
                lines.append(f"      类型: {KIND_NAMES.get(item.kind, item.kind)}")
                lines.append(f"      大小: {item.size_human}")
                lines.append(f"      状态: {item.status}")
                if item.url:
                    lines.append(f"      链接: {item.url}")
                lines.append(f"      风险: {RISK_NAMES[item.risk]}")
                if item.reasons:
                    lines.append(f"      原因: {item.reason_text}")
                lines.append("")

    lines += [
        LINE,
        "修复建议",
        LINE,
        "",
        "1. 阻断新增：在 .gitignore 中加入",
        "     *.db",
        "     *.sqlite*",
        "     *.sql",
        "     .env",
        "     *.bak",
        "",
        "2. 停止跟踪（保留本地文件）",
        "     git rm --cached <文件路径>",
        "     git commit -m \"chore: 移除敏感文件跟踪\"",
        "",
        "3. 重写历史并强推（文件已经推送到远程时必做）",
        "     java -jar bfg.jar --delete-files '*.db'",
        "     git reflog expire --expire=now --all",
        "     git gc --prune=now --aggressive",
        "     git push --force --all",
        "",
        "4. 善后",
        "     - 立即轮换已泄露的密码 / Token / 密钥",
        "     - 通知协作者重新克隆，Fork 与缓存副本不会自动清理",
        "     - 仓库设置中开启 Secret Scanning 与 push protection",
        "",
        LINE,
        f"报告结束 · {stamp}",
        LINE,
    ]
    return "\n".join(lines)


def build_payload(result: ScanResult, generated_at: str | None = None) -> dict:
    """生成 JSON 结构。"""
    counts = result.count_by_risk()
    return {
        "tool": APP_TITLE,
        "version": VERSION,
        "generated_at": generated_at or datetime.now().isoformat(timespec="seconds"),
        "target": result.target,
        "elapsed_seconds": round(result.elapsed, 2),
        "cancelled": result.cancelled,
        "summary": {
            "total": len(result.items),
            "files_scanned": result.files_scanned,
            "sources": len(result.sources),
            "committed": result.committed_count,
            "by_risk": {RISK_NAMES[level]: counts.get(level, 0) for level in RISK_ORDER},
            "by_kind": {KIND_NAMES.get(k, k): v for k, v in result.count_by_kind().items()},
        },
        "items": [
            {
                "source": item.source,
                "path": item.path,
                "kind": item.kind,
                "kind_name": KIND_NAMES.get(item.kind, item.kind),
                "size": item.size,
                "size_human": item.size_human,
                "status": item.status,
                "tracked": item.tracked,
                "risk": item.risk,
                "risk_name": RISK_NAMES[item.risk],
                "reasons": list(item.reasons),
                "url": item.url,
                "absolute_path": item.abs_path,
            }
            for item in result.sorted_items()
        ],
    }


def save_text(path: str | Path, result: ScanResult) -> None:
    Path(path).write_text(build_text_report(result), encoding="utf-8")


def save_json(path: str | Path, result: ScanResult) -> None:
    payload = build_payload(result)
    Path(path).write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")


def save_csv(path: str | Path, result: ScanResult) -> None:
    with open(path, "w", encoding="utf-8-sig", newline="") as handle:
        writer = csv.writer(handle)
        writer.writerow(["风险等级", "来源", "类型", "文件路径", "大小", "Git 状态", "链接", "风险原因"])
        for item in result.sorted_items():
            writer.writerow(
                [
                    RISK_NAMES[item.risk],
                    item.source,
                    KIND_NAMES.get(item.kind, item.kind),
                    item.path,
                    item.size_human,
                    item.status,
                    item.url,
                    item.reason_text,
                ]
            )


def save(path: str | Path, result: ScanResult) -> None:
    """按扩展名选择格式。"""
    suffix = Path(path).suffix.lower()
    if suffix == ".json":
        save_json(path, result)
    elif suffix == ".csv":
        save_csv(path, result)
    else:
        save_text(path, result)


def build_fix_guide(result: ScanResult) -> str:
    """针对本次结果生成「可复制执行」的修复指引。"""
    items = result.sorted_items()
    local_items = [i for i in items if not i.is_remote]
    remote_items = [i for i in items if i.is_remote]

    lines: list[str] = [
        LINE,
        "修复指引",
        LINE,
        "",
        f"待处理敏感文件：{len(items)} 个（本地 {len(local_items)} / 远程 {len(remote_items)}）",
        "",
    ]

    if not items:
        lines += ["本次未发现敏感文件，无需处理。", ""]
        return "\n".join(lines)

    lines += [
        "第 1 步 · 阻断新增",
        SUB,
        "把下面这些模式追加到项目根目录的 .gitignore：",
        "",
        "    *.db",
        "    *.db3",
        "    *.sqlite",
        "    *.sqlite3",
        "    .env",
        "    .env.*",
        "    *.sql",
        "    *.bak",
        "    credentials.json",
        "",
    ]

    if local_items:
        lines += [
            "第 2 步 · 停止跟踪（保留本地文件，仅从 Git 移除）",
            SUB,
            "    git rm --cached 后面跟下面这些路径（仓库根目录下执行）：",
            "",
        ]
        for item in local_items[:20]:
            lines.append(f"    git rm --cached \"{item.path}\"")
        if len(local_items) > 20:
            lines.append(f"    # …… 其余 {len(local_items) - 20} 个同上")
        lines += ["", "    然后提交：", "    git commit -m \"chore: 移除敏感文件跟踪\"", ""]

    lines += [
        "第 3 步 · 重写历史（文件已推送时必须做，否则远程依旧可下载）",
        SUB,
        "    方式一 · git filter-repo（推荐，Git 官方推荐工具）",
        "        pip install git-filter-repo",
    ]
    for item in items[:10]:
        lines.append(f"        git filter-repo --path \"{item.path}\" --invert-paths")
    if len(items) > 10:
        lines.append(f"        # …… 其余 {len(items) - 10} 个同上")
    lines += [
        "",
        "    方式二 · BFG Repo-Cleaner（本仓库附带 bfg.jar，速度快）",
        "        java -jar bfg.jar --delete-files '*.db'",
        "        git reflog expire --expire=now --all",
        "        git gc --prune=now --aggressive",
        "",
        "    推送重写后的历史：",
        "        git push --force --all",
        "        git push --force --tags",
        "",
    ]

    if remote_items:
        lines += [
            "远程仓库补充说明",
            SUB,
            "    · 强制推送后，旧提交仍在 GitHub 的缓存与 Fork 中，需要联系 GitHub 支持清理缓存",
            "    · 若仓库被 Fork，请逐个 Fork 方同步或删除，否则泄露内容仍可访问",
            "    · 文件公开期间可能已被爬取，按「已泄露」处理",
            "",
        ]

    lines += [
        "第 4 步 · 善后",
        SUB,
        "    [ ] 轮换所有可能暴露的密码、Token、密钥、数据库账号",
        "    [ ] 检查访问日志，确认是否有异常下载",
        "    [ ] 仓库 Settings → Code security 开启 Secret Scanning 与 Push protection",
        "    [ ] 安装 pre-commit 钩子，提交前自动拦截敏感文件",
        "",
        "可用的 pre-commit 配置（.pre-commit-config.yaml）：",
        "",
        "    repos:",
        "      - repo: https://github.com/gitleaks/gitleaks",
        "        rev: v8.18.0",
        "        hooks:",
        "          - id: gitleaks",
        "",
        LINE,
    ]
    return "\n".join(lines)
