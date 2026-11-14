#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""核心层自测（不依赖 GUI）：python -m pytest tests  或  python tests/test_core.py"""

from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))

from leakscanner.core import engine as engine_mod  # noqa: E402
from leakscanner.core.engine import ScanEngine, ScanError  # noqa: E402
from leakscanner.core.gitutil import GitRepo  # noqa: E402
from leakscanner.core.models import JOB_LOCAL, ScanRequest  # noqa: E402
from leakscanner.core.report import build_payload, build_text_report, save_csv, save_json  # noqa: E402
from leakscanner.core.rules import (  # noqa: E402
    assess_risk,
    detect_kind,
    format_size,
    normalize_username,
    parse_github_url,
)


class RulesTest(unittest.TestCase):
    def test_detect_kind(self):
        self.assertEqual(detect_kind("backend/data/simnotice.db"), "sqlite")
        self.assertEqual(detect_kind("dump.SQL"), "mysql")
        self.assertEqual(detect_kind(".env"), "secret")
        self.assertEqual(detect_kind("config/.env.production"), "secret")
        self.assertEqual(detect_kind("keys/id_rsa"), "secret")
        self.assertEqual(detect_kind("app.accdb"), "access")
        self.assertEqual(detect_kind("backup/database.bak"), "other")
        self.assertIsNone(detect_kind("src/main.py"))
        self.assertIsNone(detect_kind("data.db.backup.zip"))

    def test_risk_levels(self):
        self.assertEqual(assess_risk(".env", "secret")[0], "critical")
        self.assertEqual(assess_risk("credentials.json", "secret")[0], "critical")
        self.assertEqual(assess_risk("data/app.db", "sqlite")[0], "high")
        self.assertEqual(assess_risk("db/schema.sql", "mysql")[0], "low")
        self.assertEqual(assess_risk("db/dump.sql", "mysql")[0], "high")
        self.assertEqual(assess_risk("user_data.bak", "other")[0], "high")
        self.assertTrue(assess_risk("data/app.db", "sqlite")[1])

    def test_parse_github_url(self):
        cases = {
            "owner/repo": "owner/repo",
            "https://github.com/owner/repo": "owner/repo",
            "https://github.com/owner/repo.git": "owner/repo",
            "https://github.com/owner/repo/tree/main/sub": "owner/repo",
            "git@github.com:owner/repo.git": "owner/repo",
            "https://gitee.com/owner/repo": None,
            "": None,
        }
        for raw, expected in cases.items():
            self.assertEqual(parse_github_url(raw), expected, raw)

    def test_normalize_username(self):
        self.assertEqual(normalize_username("https://github.com/ZeroOneCN"), "ZeroOneCN")
        self.assertEqual(normalize_username("ZeroOneCN"), "ZeroOneCN")
        self.assertIsNone(normalize_username("bad name!"))

    def test_format_size(self):
        self.assertEqual(format_size(0), "0 B")
        self.assertEqual(format_size(1536), "1.5 KB")
        self.assertEqual(format_size(5 * 1024 * 1024), "5.0 MB")


class LocalScanTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        base = Path(self.tmp.name)
        (base / "backend" / "data").mkdir(parents=True)
        (base / "backend" / "data" / "app.db").write_bytes(b"x" * 2048)
        (base / "backend" / "data" / "app.db-wal").write_bytes(b"y")
        (base / ".env").write_text("SECRET=1", encoding="utf-8")
        (base / "schema.sql").write_text("create table t(id int);", encoding="utf-8")
        (base / "src").mkdir()
        (base / "src" / "main.py").write_text("print(1)", encoding="utf-8")
        (base / "node_modules").mkdir()
        (base / "node_modules" / "pkg.db").write_bytes(b"z")
        self.base = base

    def tearDown(self):
        self.tmp.cleanup()

    def test_scan_finds_expected_files(self):
        engine = ScanEngine()
        result = engine.run(ScanRequest(kind=JOB_LOCAL, target=str(self.base)))
        paths = {item.path for item in result.items}
        self.assertIn("backend/data/app.db", paths)
        self.assertIn(".env", paths)
        self.assertIn("schema.sql", paths)
        self.assertNotIn("node_modules/pkg.db", paths)
        self.assertEqual(result.files_scanned, 5)

    def test_scan_missing_dir(self):
        with self.assertRaises(ScanError):
            ScanEngine().run(ScanRequest(kind=JOB_LOCAL, target=str(self.base / "not-exists")))

    def test_cancel_stops_scan(self):
        engine = ScanEngine()
        engine.token.cancel()
        with self.assertRaises(engine_mod.ScanCancelled):
            engine.run(ScanRequest(kind=JOB_LOCAL, target=str(self.base)))

    def test_git_status_of_this_repo(self):
        repo = GitRepo(ROOT)
        if not repo.available:
            self.skipTest("当前目录不在 Git 仓库中")
        mapping = repo.status_map(["README.md"])
        self.assertTrue(mapping["README.md"][0])

    def test_reports(self):
        result = ScanEngine().run(ScanRequest(kind=JOB_LOCAL, target=str(self.base)))
        text = build_text_report(result)
        self.assertIn("扫描报告", text)
        self.assertIn("app.db", text)

        payload = build_payload(result)
        self.assertEqual(payload["summary"]["total"], 3)
        self.assertTrue(payload["items"])

        target = Path(self.tmp.name) / "report"
        save_json(target.with_suffix(".json"), result)
        save_csv(target.with_suffix(".csv"), result)
        self.assertEqual(json.loads(target.with_suffix(".json").read_text(encoding="utf-8"))["summary"]["total"], 3)
        self.assertIn("app.db", target.with_suffix(".csv").read_text(encoding="utf-8-sig"))


if __name__ == "__main__":
    unittest.main(verbosity=2)
