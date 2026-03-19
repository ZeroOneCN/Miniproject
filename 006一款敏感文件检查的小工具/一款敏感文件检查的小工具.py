#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
GitHub 仓库数据库泄露检查工具 - GUI 版本

功能描述：
    提供图形界面扫描本地 Git 仓库或 GitHub 远程仓库，
    发现可能泄露的数据库文件（如 SQLite、MySQL、PostgreSQL 等）。
    支持仓库链接解析、批量扫描等功能。

参数说明：
    无命令行参数，直接运行启动 GUI 界面

返回值：
    无
"""

import os
import re
import json
import threading
import subprocess
import webbrowser
from pathlib import Path
from typing import List, Dict, Optional, Tuple
from datetime import datetime
from urllib.parse import urlparse

try:
    import requests
    REQUESTS_AVAILABLE = True
except ImportError:
    REQUESTS_AVAILABLE = False

import tkinter as tk
from tkinter import ttk, filedialog, messagebox, scrolledtext
from tkinter import font as tkfont


SENSITIVE_PATTERNS = {
    'sqlite': [
        r'\.db$',
        r'\.db3$',
        r'\.sqlite$',
        r'\.sqlite3$',
        r'\.sqlite2$',
    ],
    'mysql': [
        r'\.sql$',
        r'\.mysqldump$',
    ],
    'postgresql': [
        r'\.pgpass$',
        r'\.psql_history$',
    ],
    'access': [
        r'\.accdb$',
        r'\.mdb$',
    ],
    'other': [
        r'\.bak$',
        r'\.backup$',
        r'database\.json$',
        r'data\.json$',
        r'\.env$',
        r'credentials\.json$',
        r'db_config\.json$',
    ]
}

EXCLUDE_DIRS = {
    '.git',
    '__pycache__',
    'node_modules',
    '.venv',
    'venv',
    'env',
    '.idea',
    '.vscode',
    'dist',
    'build',
    '.tox',
    '.eggs',
}

DB_TYPE_COLORS = {
    'sqlite': '#D32F2F',
    'mysql': '#1976D2',
    'postgresql': '#388E3C',
    'access': '#7B1FA2',
    'other': '#F57C00',
}

DB_TYPE_NAMES = {
    'sqlite': 'SQLite 数据库',
    'mysql': 'MySQL 数据库',
    'postgresql': 'PostgreSQL 相关',
    'access': 'Access 数据库',
    'other': '其他敏感文件',
}


def is_sensitive_file(filename: str) -> Tuple[bool, Optional[str]]:
    """
    检查文件名是否匹配敏感数据库文件模式
    
    参数:
        filename: 文件名
        
    返回值:
        Tuple[bool, Optional[str]]: (是否敏感, 匹配的类型)
    """
    filename_lower = filename.lower()
    for db_type, patterns in SENSITIVE_PATTERNS.items():
        for pattern in patterns:
            if re.search(pattern, filename_lower):
                return True, db_type
    return False, None


def should_exclude_dir(dirname: str) -> bool:
    """
    判断目录是否应该被排除扫描
    
    参数:
        dirname: 目录名
        
    返回值:
        bool: 是否排除
    """
    dirname_lower = dirname.lower()
    return dirname_lower in {d.lower() for d in EXCLUDE_DIRS}


def format_size(size_bytes: int) -> str:
    """
    格式化文件大小为人类可读格式
    
    参数:
        size_bytes: 字节数
        
    返回值:
        str: 格式化后的大小字符串
    """
    for unit in ['B', 'KB', 'MB', 'GB']:
        if size_bytes < 1024:
            return f"{size_bytes:.2f} {unit}"
        size_bytes /= 1024
    return f"{size_bytes:.2f} TB"


def parse_github_url(url: str) -> Optional[str]:
    """
    解析 GitHub URL，提取 owner/repo 格式
    
    参数:
        url: GitHub URL 或 owner/repo 格式
        
    返回值:
        Optional[str]: owner/repo 格式，解析失败返回 None
    """
    url = url.strip()
    
    if not url:
        return None
    
    if re.match(r'^[\w-]+/[\w.-]+$', url):
        return url
    
    patterns = [
        r'github\.com[:/]([\w-]+/[\w.-]+?)(?:\.git)?/?$',
        r'github\.com/([\w-]+/[\w.-]+)',
    ]
    
    for pattern in patterns:
        match = re.search(pattern, url, re.IGNORECASE)
        if match:
            repo = match.group(1)
            if repo.endswith('.git'):
                repo = repo[:-4]
            return repo
    
    return None


def check_git_tracked(path: str, filename: str) -> Tuple[bool, str]:
    """
    检查文件是否被 Git 跟踪
    
    参数:
        path: 仓库根目录
        filename: 相对文件路径
        
    返回值:
        Tuple[bool, str]: (是否被跟踪, 状态描述)
    """
    try:
        result = subprocess.run(
            ['git', 'ls-files', '--error-unmatch', filename],
            cwd=path,
            capture_output=True,
            text=True
        )
        if result.returncode == 0:
            return True, "已跟踪"
        
        result = subprocess.run(
            ['git', 'check-ignore', '-v', filename],
            cwd=path,
            capture_output=True,
            text=True
        )
        if result.returncode == 0:
            return False, "已忽略"
        
        return False, "未跟踪"
    except Exception:
        return False, "未知"


class ScannerEngine:
    """
    扫描引擎类
    
    负责执行本地和远程仓库的扫描操作
    """
    
    def __init__(self, callback=None):
        """
        初始化扫描引擎
        
        参数:
            callback: 进度回调函数
        """
        self.callback = callback
        self._stop_flag = False
    
    def stop(self):
        """停止扫描"""
        self._stop_flag = True
    
    def report_progress(self, message: str, progress: int = None):
        """
        报告扫描进度
        
        参数:
            message: 进度消息
            progress: 进度百分比 (0-100)
        """
        if self.callback:
            self.callback(message, progress)
    
    def scan_local_directory(self, path: str, source_name: str = None) -> List[Dict]:
        """
        扫描本地目录
        
        参数:
            path: 本地目录路径
            source_name: 来源名称（用于批量扫描时区分）
            
        返回值:
            List[Dict]: 发现的敏感文件列表
        """
        results = []
        root_path = Path(path).resolve()
        
        if not root_path.exists():
            return results
        
        source = source_name or root_path.name
        
        total_files = sum(1 for _ in root_path.rglob('*') if _.is_file())
        scanned = 0
        
        for current_dir, dirs, files in os.walk(root_path):
            if self._stop_flag:
                break
                
            dirs[:] = [d for d in dirs if not should_exclude_dir(d)]
            
            for filename in files:
                if self._stop_flag:
                    break
                    
                scanned += 1
                if total_files > 0 and scanned % 50 == 0:
                    progress = int(scanned / total_files * 100)
                    self.report_progress(f"[{source}] 扫描: {filename}", progress)
                
                is_sensitive, db_type = is_sensitive_file(filename)
                if is_sensitive:
                    file_path = os.path.join(current_dir, filename)
                    relative_path = os.path.relpath(file_path, root_path)
                    file_size = os.path.getsize(file_path)
                    
                    tracked, status = check_git_tracked(path, relative_path)
                    
                    results.append({
                        'source': source,
                        'path': relative_path,
                        'absolute_path': file_path,
                        'type': db_type,
                        'size': file_size,
                        'size_human': format_size(file_size),
                        'tracked': tracked,
                        'status': status,
                    })
        
        return results
    
    def scan_github_repo(self, repo_name: str, token: Optional[str] = None) -> List[Dict]:
        """
        扫描 GitHub 远程仓库
        
        参数:
            repo_name: 仓库名称 (owner/repo)
            token: GitHub Personal Access Token
            
        返回值:
            List[Dict]: 发现的敏感文件列表
        """
        if not REQUESTS_AVAILABLE:
            self.report_progress("错误: 需要安装 requests 库", None)
            return []
        
        results = []
        headers = {'Accept': 'application/vnd.github.v3+json'}
        
        if token:
            headers['Authorization'] = f'token {token}'
        
        self.report_progress(f"连接 GitHub 仓库: {repo_name}...", 5)
        
        default_branch = None
        try:
            repo_api_url = f'https://api.github.com/repos/{repo_name}'
            repo_response = requests.get(repo_api_url, headers=headers, timeout=30)
            
            if repo_response.status_code == 200:
                repo_data = repo_response.json()
                default_branch = repo_data.get('default_branch', 'main')
            elif repo_response.status_code == 403:
                self.report_progress("API 限制，请稍后再试或使用 Token", None)
                return []
            elif repo_response.status_code == 404:
                self.report_progress(f"仓库 {repo_name} 不存在或无权访问", None)
                return []
        except requests.exceptions.RequestException as e:
            self.report_progress(f"网络错误: {e}", None)
            return []
        
        branches_to_try = [default_branch] if default_branch else ['main', 'master']
        tree_data = None
        used_branch = None
        
        for branch in branches_to_try:
            if self._stop_flag:
                break
            
            if not branch:
                continue
                
            api_url = f'https://api.github.com/repos/{repo_name}/git/trees/{branch}?recursive=1'
            
            try:
                response = requests.get(api_url, headers=headers, timeout=30)
                
                if response.status_code == 200:
                    tree_data = response.json()
                    used_branch = branch
                    self.report_progress(f"获取仓库文件列表 ({branch} 分支)...", 20)
                    break
                elif response.status_code == 403:
                    self.report_progress("API 限制，请稍后再试或使用 Token", None)
                    return []
                elif response.status_code == 404:
                    continue
                else:
                    self.report_progress(f"HTTP 错误: {response.status_code}", None)
                    continue
                    
            except requests.exceptions.RequestException as e:
                self.report_progress(f"网络错误: {e}", None)
                return []
        
        if not tree_data:
            self.report_progress(f"无法访问仓库 {repo_name} 的文件树", None)
            return []
        
        tree = tree_data.get('tree', [])
        total = len(tree)
        sensitive_files = []
        
        for i, item in enumerate(tree):
            if self._stop_flag:
                break
            
            if i % 200 == 0:
                progress = 20 + int(i / total * 60)
                self.report_progress(f"[{repo_name}] 分析文件 {i}/{total}...", progress)
            
            if item['type'] == 'blob':
                filename = item['path']
                is_sensitive, db_type = is_sensitive_file(filename)
                if is_sensitive:
                    sensitive_files.append({
                        'path': filename,
                        'type': db_type,
                        'sha': item.get('sha'),
                    })
        
        if sensitive_files:
            self.report_progress(f"[{repo_name}] 获取 {len(sensitive_files)} 个敏感文件详情...", 80)
            
            for i, sf in enumerate(sensitive_files):
                if self._stop_flag:
                    break
                
                file_path = sf['path']
                file_size = 0
                
                try:
                    content_url = f'https://api.github.com/repos/{repo_name}/contents/{file_path}?ref={used_branch}'
                    content_response = requests.get(content_url, headers=headers, timeout=10)
                    
                    if content_response.status_code == 200:
                        content_data = content_response.json()
                        file_size = content_data.get('size', 0)
                except:
                    pass
                
                results.append({
                    'source': repo_name,
                    'path': file_path,
                    'type': sf['type'],
                    'url': f'https://github.com/{repo_name}/blob/{used_branch}/{file_path}',
                    'size': file_size,
                    'size_human': format_size(file_size),
                    'tracked': True,
                    'status': '已提交到远程仓库',
                })
        
        return results
    
    def get_user_repos(self, username: str, token: Optional[str] = None) -> List[str]:
        """
        获取 GitHub 用户名下的所有仓库名称
        
        参数:
            username: GitHub 用户名
            token: GitHub Personal Access Token
            
        返回值:
            List[str]: 仓库名称列表 (owner/repo 格式)
        """
        if not REQUESTS_AVAILABLE:
            self.report_progress("错误: 需要安装 requests 库", None)
            return []
        
        repos = []
        headers = {'Accept': 'application/vnd.github.v3+json'}
        
        if token:
            headers['Authorization'] = f'token {token}'
        
        self.report_progress(f"获取用户 {username} 的仓库列表...", 5)
        
        page = 1
        per_page = 100
        
        while True:
            if self._stop_flag:
                break
            
            api_url = f'https://api.github.com/users/{username}/repos?page={page}&per_page={per_page}'
            
            try:
                response = requests.get(api_url, headers=headers, timeout=30)
                
                if response.status_code == 200:
                    data = response.json()
                    if not data:
                        break
                    
                    for repo in data:
                        full_name = repo.get('full_name')
                        if full_name:
                            repos.append(full_name)
                    
                    if len(data) < per_page:
                        break
                    
                    page += 1
                    
                elif response.status_code == 403:
                    self.report_progress("API 限制，请稍后再试或使用 Token", None)
                    break
                elif response.status_code == 404:
                    self.report_progress(f"用户 {username} 不存在", None)
                    break
                else:
                    self.report_progress(f"HTTP 错误: {response.status_code}", None)
                    break
                    
            except requests.exceptions.RequestException as e:
                self.report_progress(f"网络错误: {e}", None)
                break
        
        self.report_progress(f"找到 {len(repos)} 个仓库", 10)
        return repos
    
    def scan_user_all_repos(self, username: str, token: Optional[str] = None) -> List[Dict]:
        """
        扫描 GitHub 用户名下的所有仓库
        
        参数:
            username: GitHub 用户名
            token: GitHub Personal Access Token
            
        返回值:
            List[Dict]: 发现的敏感文件列表
        """
        repos = self.get_user_repos(username, token)
        
        if not repos:
            return []
        
        all_results = []
        total_repos = len(repos)
        
        for i, repo_name in enumerate(repos):
            if self._stop_flag:
                break
            
            progress = 10 + int(i / total_repos * 90)
            self.report_progress(f"扫描仓库 ({i+1}/{total_repos}): {repo_name}", progress)
            
            repo_results = self.scan_github_repo(repo_name, token)
            all_results.extend(repo_results)
        
        return all_results


class BatchScanManager:
    """
    批量扫描管理器
    
    管理多个扫描任务的批量执行
    """
    
    def __init__(self, scanner: ScannerEngine):
        """
        初始化批量扫描管理器
        
        参数:
            scanner: 扫描引擎实例
        """
        self.scanner = scanner
        self.results = []
        self.current_index = 0
        self.total_tasks = 0
    
    def scan_batch(self, tasks: List[Dict], token: Optional[str] = None) -> List[Dict]:
        """
        批量扫描多个目标
        
        参数:
            tasks: 扫描任务列表，每个任务包含 'type' 和 'target'
            token: GitHub Token
            
        返回值:
            List[Dict]: 所有扫描结果
        """
        self.results = []
        self.total_tasks = len(tasks)
        
        for i, task in enumerate(tasks):
            if self.scanner._stop_flag:
                break
            
            self.current_index = i
            
            task_type = task.get('type')
            target = task.get('target')
            
            self.scanner.report_progress(
                f"批量扫描 ({i+1}/{self.total_tasks}): {target}",
                int(i / self.total_tasks * 100)
            )
            
            if task_type == 'local':
                task_results = self.scanner.scan_local_directory(target)
            elif task_type == 'remote':
                task_results = self.scanner.scan_github_repo(target, token)
            else:
                continue
            
            self.results.extend(task_results)
        
        return self.results


class DBLeakCheckerGUI:
    """
    数据库泄露检查工具 GUI 主类
    
    提供图形界面进行本地和远程仓库扫描
    """
    
    def __init__(self, root):
        """
        初始化 GUI
        
        参数:
            root: Tkinter 根窗口
        """
        self.root = root
        self.root.title("GitHub 仓库数据库泄露检查工具 v1.0")
        self.root.geometry("1100x800")
        self.root.minsize(900, 650)
        
        self.scanner = None
        self.scan_thread = None
        self.scan_results = []
        self.batch_tasks = []
        
        self.setup_styles()
        self.create_widgets()
        self.center_window()
    
    def setup_styles(self):
        """设置界面样式"""
        style = ttk.Style()
        style.theme_use('clam')
        
        style.configure('Title.TLabel', font=('Microsoft YaHei UI', 16, 'bold'))
        style.configure('Subtitle.TLabel', font=('Microsoft YaHei UI', 10))
        style.configure('Status.TLabel', font=('Microsoft YaHei UI', 9))
        
        style.configure('Action.TButton', font=('Microsoft YaHei UI', 10), padding=8)
        style.configure('Danger.TButton', font=('Microsoft YaHei UI', 10))
        style.configure('Small.TButton', font=('Microsoft YaHei UI', 9), padding=3)
        
        style.configure('Treeview', font=('Microsoft YaHei UI', 9), rowheight=26)
        style.configure('Treeview.Heading', font=('Microsoft YaHei UI', 9, 'bold'))
        
        style.configure('Batch.TFrame', background='#f0f0f0')
    
    def create_widgets(self):
        """创建界面组件"""
        main_frame = ttk.Frame(self.root, padding="10")
        main_frame.pack(fill=tk.BOTH, expand=True)
        
        self.create_header(main_frame)
        
        notebook = ttk.Notebook(main_frame)
        notebook.pack(fill=tk.BOTH, expand=True, pady=(0, 10))
        
        single_frame = ttk.Frame(notebook, padding="10")
        notebook.add(single_frame, text="  单个扫描  ")
        
        batch_frame = ttk.Frame(notebook, padding="10")
        notebook.add(batch_frame, text="  批量扫描  ")
        
        self.create_single_scan_tab(single_frame)
        self.create_batch_scan_tab(batch_frame)
        
        self.create_progress_section(main_frame)
        self.create_results_section(main_frame)
        self.create_summary_section(main_frame)
    
    def create_header(self, parent):
        """创建标题区域"""
        header_frame = ttk.Frame(parent)
        header_frame.pack(fill=tk.X, pady=(0, 10))
        
        title_label = ttk.Label(
            header_frame,
            text="🔍 GitHub 仓库数据库泄露检查工具",
            style='Title.TLabel'
        )
        title_label.pack(side=tk.LEFT)
        
        subtitle_label = ttk.Label(
            header_frame,
            text="检查 SQLite、MySQL、PostgreSQL 等数据库文件泄露风险 | 支持批量扫描",
            style='Subtitle.TLabel'
        )
        subtitle_label.pack(side=tk.LEFT, padx=(20, 0), pady=(8, 0))
    
    def create_single_scan_tab(self, parent):
        """创建单个扫描标签页"""
        control_frame = ttk.LabelFrame(parent, text="扫描设置", padding="10")
        control_frame.pack(fill=tk.X, pady=(0, 10))
        
        scan_mode_frame = ttk.Frame(control_frame)
        scan_mode_frame.pack(fill=tk.X, pady=(0, 10))
        
        ttk.Label(scan_mode_frame, text="扫描模式:").pack(side=tk.LEFT)
        
        self.scan_mode = tk.StringVar(value="local")
        
        self.local_radio = ttk.Radiobutton(
            scan_mode_frame,
            text="本地目录",
            variable=self.scan_mode,
            value="local",
            command=lambda: self.on_mode_change('single')
        )
        self.local_radio.pack(side=tk.LEFT, padx=(10, 20))
        
        self.remote_radio = ttk.Radiobutton(
            scan_mode_frame,
            text="GitHub 远程仓库",
            variable=self.scan_mode,
            value="remote",
            command=lambda: self.on_mode_change('single')
        )
        self.remote_radio.pack(side=tk.LEFT, padx=(0, 20))
        
        self.user_radio = ttk.Radiobutton(
            scan_mode_frame,
            text="扫描用户所有仓库",
            variable=self.scan_mode,
            value="user",
            command=lambda: self.on_mode_change('single')
        )
        self.user_radio.pack(side=tk.LEFT)
        
        local_frame = ttk.Frame(control_frame)
        local_frame.pack(fill=tk.X, pady=(0, 5))
        
        ttk.Label(local_frame, text="本地路径:").pack(side=tk.LEFT)
        
        self.path_var = tk.StringVar()
        self.path_entry = ttk.Entry(local_frame, textvariable=self.path_var, width=60)
        self.path_entry.pack(side=tk.LEFT, padx=(10, 10), fill=tk.X, expand=True)
        
        self.browse_btn = ttk.Button(
            local_frame,
            text="浏览...",
            command=self.browse_directory,
            style='Small.TButton'
        )
        self.browse_btn.pack(side=tk.LEFT)
        
        remote_frame = ttk.Frame(control_frame)
        remote_frame.pack(fill=tk.X, pady=(0, 5))
        
        ttk.Label(remote_frame, text="仓库地址:").pack(side=tk.LEFT)
        
        self.repo_var = tk.StringVar()
        self.repo_entry = ttk.Entry(remote_frame, textvariable=self.repo_var, width=50)
        self.repo_entry.pack(side=tk.LEFT, padx=(10, 10))
        
        ttk.Label(remote_frame, text="支持 owner/repo 或完整 URL", foreground='gray').pack(side=tk.LEFT)
        
        user_frame = ttk.Frame(control_frame)
        user_frame.pack(fill=tk.X, pady=(0, 5))
        
        ttk.Label(user_frame, text="GitHub 用户名:").pack(side=tk.LEFT)
        
        self.username_var = tk.StringVar()
        self.username_entry = ttk.Entry(user_frame, textvariable=self.username_var, width=30)
        self.username_entry.pack(side=tk.LEFT, padx=(10, 10))
        
        ttk.Label(user_frame, text="扫描该用户名下的所有公开仓库", foreground='gray').pack(side=tk.LEFT)
        
        token_frame = ttk.Frame(control_frame)
        token_frame.pack(fill=tk.X)
        
        ttk.Label(token_frame, text="GitHub Token:").pack(side=tk.LEFT)
        
        self.token_var = tk.StringVar()
        self.token_entry = ttk.Entry(token_frame, textvariable=self.token_var, width=40, show="*")
        self.token_entry.pack(side=tk.LEFT, padx=(10, 20))
        
        ttk.Label(token_frame, text="(可选，用于私有仓库或提高 API 限制)", foreground='gray').pack(side=tk.LEFT)
        
        self.single_remote_widgets = [remote_frame, token_frame]
        self.single_user_widgets = [user_frame]
        for widget in self.single_remote_widgets + self.single_user_widgets:
            for child in widget.winfo_children():
                if isinstance(child, ttk.Entry):
                    child.configure(state='disabled')
        
        button_frame = ttk.Frame(control_frame)
        button_frame.pack(fill=tk.X, pady=(10, 0))
        
        self.scan_btn = ttk.Button(
            button_frame,
            text="🔍 开始扫描",
            style='Action.TButton',
            command=self.start_single_scan
        )
        self.scan_btn.pack(side=tk.LEFT, padx=(0, 10))
        
        self.stop_btn = ttk.Button(
            button_frame,
            text="⏹ 停止",
            style='Danger.TButton',
            command=self.stop_scan,
            state='disabled'
        )
        self.stop_btn.pack(side=tk.LEFT, padx=(0, 10))
    
    def create_batch_scan_tab(self, parent):
        """创建批量扫描标签页"""
        paned = ttk.PanedWindow(parent, orient=tk.HORIZONTAL)
        paned.pack(fill=tk.BOTH, expand=True)
        
        left_frame = ttk.LabelFrame(paned, text="批量任务列表", padding="5")
        paned.add(left_frame, weight=1)
        
        list_frame = ttk.Frame(left_frame)
        list_frame.pack(fill=tk.BOTH, expand=True)
        
        columns = ('type', 'target')
        self.batch_tree = ttk.Treeview(
            list_frame,
            columns=columns,
            show='headings',
            selectmode='extended',
            height=10
        )
        
        self.batch_tree.heading('type', text='类型')
        self.batch_tree.heading('target', text='目标')
        
        self.batch_tree.column('type', width=80, minwidth=80)
        self.batch_tree.column('target', width=300, minwidth=200)
        
        scrollbar = ttk.Scrollbar(list_frame, orient=tk.VERTICAL, command=self.batch_tree.yview)
        self.batch_tree.configure(yscrollcommand=scrollbar.set)
        
        self.batch_tree.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        scrollbar.pack(side=tk.RIGHT, fill=tk.Y)
        
        add_frame = ttk.LabelFrame(paned, text="添加任务", padding="10")
        paned.add(add_frame, weight=1)
        
        type_frame = ttk.Frame(add_frame)
        type_frame.pack(fill=tk.X, pady=(0, 10))
        
        ttk.Label(type_frame, text="任务类型:").pack(side=tk.LEFT)
        
        self.batch_type = tk.StringVar(value="remote")
        
        ttk.Radiobutton(
            type_frame,
            text="远程仓库",
            variable=self.batch_type,
            value="remote"
        ).pack(side=tk.LEFT, padx=(10, 20))
        
        ttk.Radiobutton(
            type_frame,
            text="本地目录",
            variable=self.batch_type,
            value="local"
        ).pack(side=tk.LEFT)
        
        target_frame = ttk.Frame(add_frame)
        target_frame.pack(fill=tk.X, pady=(0, 10))
        
        ttk.Label(target_frame, text="目标地址:").pack(anchor=tk.W)
        
        self.batch_target_var = tk.StringVar()
        self.batch_target_entry = ttk.Entry(target_frame, textvariable=self.batch_target_var, width=50)
        self.batch_target_entry.pack(fill=tk.X, pady=(5, 0))
        
        ttk.Label(target_frame, text="支持 owner/repo、GitHub URL 或本地路径", foreground='gray').pack(anchor=tk.W, pady=(2, 0))
        
        add_btn_frame = ttk.Frame(add_frame)
        add_btn_frame.pack(fill=tk.X, pady=(10, 0))
        
        ttk.Button(
            add_btn_frame,
            text="➕ 添加任务",
            command=self.add_batch_task,
            style='Small.TButton'
        ).pack(side=tk.LEFT, padx=(0, 5))
        
        ttk.Button(
            add_btn_frame,
            text="📁 添加本地目录",
            command=self.add_local_to_batch,
            style='Small.TButton'
        ).pack(side=tk.LEFT, padx=(0, 5))
        
        ttk.Button(
            add_btn_frame,
            text="📋 从剪贴板导入",
            command=self.import_from_clipboard,
            style='Small.TButton'
        ).pack(side=tk.LEFT, padx=(0, 5))
        
        manage_frame = ttk.Frame(add_frame)
        manage_frame.pack(fill=tk.X, pady=(10, 0))
        
        ttk.Button(
            manage_frame,
            text="🗑 删除选中",
            command=self.remove_batch_task,
            style='Small.TButton'
        ).pack(side=tk.LEFT, padx=(0, 5))
        
        ttk.Button(
            manage_frame,
            text="🗑 清空列表",
            command=self.clear_batch_list,
            style='Small.TButton'
        ).pack(side=tk.LEFT, padx=(0, 5))
        
        batch_token_frame = ttk.Frame(add_frame)
        batch_token_frame.pack(fill=tk.X, pady=(20, 0))
        
        ttk.Label(batch_token_frame, text="GitHub Token (可选):").pack(anchor=tk.W)
        
        self.batch_token_var = tk.StringVar()
        ttk.Entry(
            batch_token_frame,
            textvariable=self.batch_token_var,
            width=40,
            show="*"
        ).pack(fill=tk.X, pady=(5, 0))
        
        batch_action_frame = ttk.Frame(add_frame)
        batch_action_frame.pack(fill=tk.X, pady=(20, 0))
        
        self.batch_scan_btn = ttk.Button(
            batch_action_frame,
            text="🔍 开始批量扫描",
            style='Action.TButton',
            command=self.start_batch_scan
        )
        self.batch_scan_btn.pack(side=tk.LEFT, padx=(0, 10))
        
        self.batch_stop_btn = ttk.Button(
            batch_action_frame,
            text="⏹ 停止",
            style='Danger.TButton',
            command=self.stop_scan,
            state='disabled'
        )
        self.batch_stop_btn.pack(side=tk.LEFT)
    
    def create_progress_section(self, parent):
        """创建进度区域"""
        progress_frame = ttk.Frame(parent)
        progress_frame.pack(fill=tk.X, pady=(0, 10))
        
        self.progress_var = tk.DoubleVar()
        self.progress_bar = ttk.Progressbar(
            progress_frame,
            variable=self.progress_var,
            maximum=100,
            mode='determinate'
        )
        self.progress_bar.pack(fill=tk.X, side=tk.LEFT, expand=True, padx=(0, 10))
        
        self.status_var = tk.StringVar(value="就绪")
        self.status_label = ttk.Label(
            progress_frame,
            textvariable=self.status_var,
            style='Status.TLabel',
            width=50
        )
        self.status_label.pack(side=tk.LEFT)
    
    def create_results_section(self, parent):
        """创建结果区域"""
        results_frame = ttk.LabelFrame(parent, text="扫描结果", padding="5")
        results_frame.pack(fill=tk.BOTH, expand=True)
        
        columns = ('source', 'type', 'path', 'size', 'status')
        self.results_tree = ttk.Treeview(
            results_frame,
            columns=columns,
            show='tree headings',
            selectmode='extended'
        )
        
        self.results_tree.heading('#0', text='')
        self.results_tree.heading('source', text='来源')
        self.results_tree.heading('type', text='数据库类型')
        self.results_tree.heading('path', text='文件路径')
        self.results_tree.heading('size', text='文件大小')
        self.results_tree.heading('status', text='Git 状态')
        
        self.results_tree.column('#0', width=30, minwidth=30, stretch=False)
        self.results_tree.column('source', width=150, minwidth=100)
        self.results_tree.column('type', width=100, minwidth=80)
        self.results_tree.column('path', width=400, minwidth=200)
        self.results_tree.column('size', width=80, minwidth=60)
        self.results_tree.column('status', width=80, minwidth=60)
        
        scrollbar_y = ttk.Scrollbar(results_frame, orient=tk.VERTICAL, command=self.results_tree.yview)
        scrollbar_x = ttk.Scrollbar(results_frame, orient=tk.HORIZONTAL, command=self.results_tree.xview)
        self.results_tree.configure(yscrollcommand=scrollbar_y.set, xscrollcommand=scrollbar_x.set)
        
        self.results_tree.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        scrollbar_y.pack(side=tk.RIGHT, fill=tk.Y)
        
        self.results_tree.bind('<Double-1>', self.on_item_double_click)
        self.results_tree.bind('<Button-3>', self.show_context_menu)
    
    def create_summary_section(self, parent):
        """创建摘要区域"""
        summary_frame = ttk.Frame(parent)
        summary_frame.pack(fill=tk.X, pady=(10, 0))
        
        left_summary = ttk.Frame(summary_frame)
        left_summary.pack(side=tk.LEFT)
        
        self.summary_var = tk.StringVar(value="发现 0 个敏感文件")
        self.summary_label = ttk.Label(
            left_summary,
            textvariable=self.summary_var,
            style='Subtitle.TLabel'
        )
        self.summary_label.pack(side=tk.LEFT)
        
        right_frame = ttk.Frame(summary_frame)
        right_frame.pack(side=tk.RIGHT)
        
        self.export_btn = ttk.Button(
            right_frame,
            text="📄 导出报告",
            command=self.export_report,
            state='disabled',
            style='Small.TButton'
        )
        self.export_btn.pack(side=tk.LEFT, padx=(0, 5))
        
        self.clear_btn = ttk.Button(
            right_frame,
            text="🗑 清空结果",
            command=self.clear_results,
            style='Small.TButton'
        )
        self.clear_btn.pack(side=tk.LEFT)
        
        self.help_btn = ttk.Button(
            right_frame,
            text="❓ 解决办法",
            command=self.show_solutions,
            state='disabled',
            style='Small.TButton'
        )
        self.help_btn.pack(side=tk.LEFT, padx=(5, 0))
        
        legend_frame = ttk.Frame(right_frame)
        legend_frame.pack(side=tk.LEFT, padx=(20, 0))
        
        for db_type, color in DB_TYPE_COLORS.items():
            legend_item = ttk.Frame(legend_frame)
            legend_item.pack(side=tk.LEFT, padx=(8, 0))
            
            color_label = tk.Label(legend_item, bg=color, width=2, height=1)
            color_label.pack(side=tk.LEFT, padx=(0, 2))
            
            ttk.Label(legend_item, text=DB_TYPE_NAMES[db_type], font=('Microsoft YaHei UI', 8)).pack(side=tk.LEFT)
    
    def center_window(self):
        """窗口居中显示"""
        self.root.update_idletasks()
        width = self.root.winfo_width()
        height = self.root.winfo_height()
        x = (self.root.winfo_screenwidth() // 2) - (width // 2)
        y = (self.root.winfo_screenheight() // 2) - (height // 2)
        self.root.geometry(f'{width}x{height}+{x}+{y}')
    
    def on_mode_change(self, tab='single'):
        """扫描模式切换处理"""
        if tab == 'single':
            mode = self.scan_mode.get()
            
            if mode == "local":
                self.path_entry.configure(state='normal')
                self.browse_btn.configure(state='normal')
                for widget in self.single_remote_widgets + self.single_user_widgets:
                    for child in widget.winfo_children():
                        if isinstance(child, ttk.Entry):
                            child.configure(state='disabled')
            elif mode == "remote":
                self.path_entry.configure(state='disabled')
                self.browse_btn.configure(state='disabled')
                for widget in self.single_remote_widgets:
                    for child in widget.winfo_children():
                        if isinstance(child, ttk.Entry):
                            child.configure(state='normal')
                for widget in self.single_user_widgets:
                    for child in widget.winfo_children():
                        if isinstance(child, ttk.Entry):
                            child.configure(state='disabled')
            else:
                self.path_entry.configure(state='disabled')
                self.browse_btn.configure(state='disabled')
                for widget in self.single_remote_widgets:
                    for child in widget.winfo_children():
                        if isinstance(child, ttk.Entry):
                            child.configure(state='normal')
                for widget in self.single_user_widgets:
                    for child in widget.winfo_children():
                        if isinstance(child, ttk.Entry):
                            child.configure(state='normal')
    
    def browse_directory(self):
        """浏览选择目录"""
        directory = filedialog.askdirectory(
            title="选择要扫描的目录",
            initialdir=self.path_var.get() or os.getcwd()
        )
        if directory:
            self.path_var.set(directory)
    
    def start_single_scan(self):
        """开始单个扫描"""
        mode = self.scan_mode.get()
        
        if mode == "local":
            path = self.path_var.get().strip()
            if not path:
                messagebox.showwarning("警告", "请选择要扫描的目录")
                return
            if not os.path.exists(path):
                messagebox.showerror("错误", "指定的目录不存在")
                return
            target = path
        elif mode == "remote":
            repo_input = self.repo_var.get().strip()
            if not repo_input:
                messagebox.showwarning("警告", "请输入 GitHub 仓库地址")
                return
            
            repo = parse_github_url(repo_input)
            if not repo:
                messagebox.showwarning("警告", "无法解析仓库地址，请使用 owner/repo 格式或完整 GitHub URL")
                return
            
            target = repo
        else:
            username = self.username_var.get().strip()
            if not username:
                messagebox.showwarning("警告", "请输入 GitHub 用户名")
                return
            target = username
        
        self.set_scanning_state(True)
        self.clear_results()
        
        self.scanner = ScannerEngine(callback=self.update_progress)
        
        if mode == "local":
            self.scan_thread = threading.Thread(
                target=self.run_local_scan,
                args=(target,),
                daemon=True
            )
        elif mode == "remote":
            token = self.token_var.get().strip() or None
            self.scan_thread = threading.Thread(
                target=self.run_remote_scan,
                args=(target, token),
                daemon=True
            )
        else:
            token = self.token_var.get().strip() or None
            self.scan_thread = threading.Thread(
                target=self.run_user_scan,
                args=(target, token),
                daemon=True
            )
        
        self.scan_thread.start()
    
    def add_batch_task(self):
        """添加批量任务"""
        task_type = self.batch_type.get()
        target = self.batch_target_var.get().strip()
        
        if not target:
            messagebox.showwarning("警告", "请输入目标地址")
            return
        
        if task_type == 'remote':
            repo = parse_github_url(target)
            if not repo:
                messagebox.showwarning("警告", "无法解析仓库地址")
                return
            target = repo
            display_type = "远程仓库"
        else:
            if not os.path.exists(target):
                messagebox.showwarning("警告", "指定的本地路径不存在")
                return
            display_type = "本地目录"
        
        self.batch_tree.insert('', 'end', values=(display_type, target))
        self.batch_target_var.set("")
    
    def add_local_to_batch(self):
        """添加本地目录到批量任务"""
        directory = filedialog.askdirectory(
            title="选择要扫描的目录",
            initialdir=os.getcwd()
        )
        if directory:
            self.batch_tree.insert('', 'end', values=("本地目录", directory))
    
    def import_from_clipboard(self):
        """从剪贴板导入仓库列表"""
        try:
            clipboard_text = self.root.clipboard_get()
            lines = clipboard_text.strip().split('\n')
            
            added = 0
            for line in lines:
                line = line.strip()
                if not line:
                    continue
                
                repo = parse_github_url(line)
                if repo:
                    self.batch_tree.insert('', 'end', values=("远程仓库", repo))
                    added += 1
                elif os.path.exists(line):
                    self.batch_tree.insert('', 'end', values=("本地目录", line))
                    added += 1
            
            if added > 0:
                messagebox.showinfo("导入成功", f"成功导入 {added} 个任务")
            else:
                messagebox.showwarning("导入失败", "剪贴板中未找到有效的仓库地址或本地路径")
                
        except tk.TclError:
            messagebox.showwarning("警告", "剪贴板为空或无法读取")
    
    def remove_batch_task(self):
        """删除选中的批量任务"""
        selected = self.batch_tree.selection()
        for item in selected:
            self.batch_tree.delete(item)
    
    def clear_batch_list(self):
        """清空批量任务列表"""
        for item in self.batch_tree.get_children():
            self.batch_tree.delete(item)
    
    def start_batch_scan(self):
        """开始批量扫描"""
        tasks = []
        
        for item in self.batch_tree.get_children():
            values = self.batch_tree.item(item, 'values')
            if len(values) >= 2:
                task_type_str = values[0]
                target = values[1]
                
                if task_type_str == "远程仓库":
                    task_type = 'remote'
                else:
                    task_type = 'local'
                
                tasks.append({'type': task_type, 'target': target})
        
        if not tasks:
            messagebox.showwarning("警告", "请先添加扫描任务")
            return
        
        self.set_scanning_state(True, batch=True)
        self.clear_results()
        
        self.scanner = ScannerEngine(callback=self.update_progress)
        token = self.batch_token_var.get().strip() or None
        
        self.scan_thread = threading.Thread(
            target=self.run_batch_scan,
            args=(tasks, token),
            daemon=True
        )
        self.scan_thread.start()
    
    def set_scanning_state(self, scanning: bool, batch: bool = False):
        """设置扫描状态"""
        if scanning:
            self.scan_btn.configure(state='disabled')
            self.batch_scan_btn.configure(state='disabled')
            self.stop_btn.configure(state='normal')
            self.batch_stop_btn.configure(state='normal')
            self.export_btn.configure(state='disabled')
            self.help_btn.configure(state='disabled')
            self.progress_var.set(0)
            self.status_var.set("正在扫描...")
        else:
            self.scan_btn.configure(state='normal')
            self.batch_scan_btn.configure(state='normal')
            self.stop_btn.configure(state='disabled')
            self.batch_stop_btn.configure(state='normal')
            self.export_btn.configure(state='normal' if self.scan_results else 'disabled')
            self.help_btn.configure(state='normal' if self.scan_results else 'disabled')
    
    def run_local_scan(self, path: str):
        """执行本地扫描（线程）"""
        try:
            results = self.scanner.scan_local_directory(path)
            self.root.after(0, lambda: self.scan_complete(results, path))
        except Exception as e:
            self.root.after(0, lambda: self.scan_error(str(e)))
    
    def run_remote_scan(self, repo: str, token: Optional[str]):
        """执行远程扫描（线程）"""
        try:
            results = self.scanner.scan_github_repo(repo, token)
            self.root.after(0, lambda: self.scan_complete(results, repo))
        except Exception as e:
            self.root.after(0, lambda: self.scan_error(str(e)))
    
    def run_batch_scan(self, tasks: List[Dict], token: Optional[str]):
        """执行批量扫描（线程）"""
        try:
            manager = BatchScanManager(self.scanner)
            results = manager.scan_batch(tasks, token)
            self.root.after(0, lambda: self.scan_complete(results, f"批量扫描 ({len(tasks)} 个任务)"))
        except Exception as e:
            self.root.after(0, lambda: self.scan_error(str(e)))
    
    def run_user_scan(self, username: str, token: Optional[str]):
        """执行用户所有仓库扫描（线程）"""
        try:
            results = self.scanner.scan_user_all_repos(username, token)
            self.root.after(0, lambda: self.scan_complete(results, f"用户 {username} 的所有仓库"))
        except Exception as e:
            self.root.after(0, lambda: self.scan_error(str(e)))
    
    def update_progress(self, message: str, progress: Optional[int]):
        """更新进度显示"""
        def _update():
            self.status_var.set(message)
            if progress is not None:
                self.progress_var.set(progress)
        self.root.after(0, _update)
    
    def scan_complete(self, results: List[Dict], source: str):
        """扫描完成处理"""
        self.scan_results = results
        self.set_scanning_state(False)
        
        self.progress_var.set(100)
        self.status_var.set(f"扫描完成 - 发现 {len(results)} 个敏感文件")
        self.summary_var.set(f"发现 {len(results)} 个敏感文件 | 扫描目标: {source}")
        
        by_source = {}
        for item in results:
            src = item.get('source', '未知')
            if src not in by_source:
                by_source[src] = []
            by_source[src].append(item)
        
        for src, items in sorted(by_source.items()):
            source_parent = self.results_tree.insert(
                '',
                'end',
                text="📁",
                values=(src, f"({len(items)} 个文件)", "", "", ""),
            )
            
            by_type = {}
            for item in items:
                db_type = item['type']
                if db_type not in by_type:
                    by_type[db_type] = []
                by_type[db_type].append(item)
            
            for db_type, type_items in sorted(by_type.items()):
                type_name = DB_TYPE_NAMES.get(db_type, db_type)
                
                type_parent = self.results_tree.insert(
                    source_parent,
                    'end',
                    text="📂",
                    values=("", type_name, f"({len(type_items)} 个)", "", ""),
                    tags=(db_type,)
                )
                
                for item in type_items:
                    size_info = item.get('size_human', 'N/A')
                    status = item.get('status', '未知')
                    
                    self.results_tree.insert(
                        type_parent,
                        'end',
                        text="📄",
                        values=("", "", item['path'], size_info, status),
                        tags=(db_type,),
                        iid=item.get('url') or item.get('absolute_path') or f"{src}_{item['path']}"
                    )
        
        self.results_tree.tag_configure('sqlite', foreground=DB_TYPE_COLORS['sqlite'])
        self.results_tree.tag_configure('mysql', foreground=DB_TYPE_COLORS['mysql'])
        self.results_tree.tag_configure('postgresql', foreground=DB_TYPE_COLORS['postgresql'])
        self.results_tree.tag_configure('access', foreground=DB_TYPE_COLORS['access'])
        self.results_tree.tag_configure('other', foreground=DB_TYPE_COLORS['other'])
        
        if results:
            sources = set(item.get('source', '') for item in results)
            messagebox.showwarning(
                "扫描完成",
                f"发现 {len(results)} 个可能泄露的敏感文件！\n"
                f"涉及 {len(sources)} 个来源\n\n请检查结果并采取相应措施。"
            )
        else:
            messagebox.showinfo(
                "扫描完成",
                "✅ 未发现敏感数据库文件泄露风险"
            )
    
    def scan_error(self, error: str):
        """扫描错误处理"""
        self.set_scanning_state(False)
        self.status_var.set(f"扫描出错: {error}")
        messagebox.showerror("扫描错误", f"扫描过程中发生错误:\n{error}")
    
    def stop_scan(self):
        """停止扫描"""
        if self.scanner:
            self.scanner.stop()
        self.set_scanning_state(False)
        self.status_var.set("扫描已停止")
    
    def clear_results(self):
        """清空结果"""
        for item in self.results_tree.get_children():
            self.results_tree.delete(item)
        self.scan_results = []
        self.summary_var.set("发现 0 个敏感文件")
        self.export_btn.configure(state='disabled')
        self.help_btn.configure(state='disabled')
    
    def show_solutions(self):
        """显示解决办法"""
        if not self.scan_results:
            return
        
        solutions = []
        solutions.append("=" * 70)
        solutions.append("🔧 敏感文件泄露解决办法")
        solutions.append("=" * 70)
        solutions.append("")
        
        local_files = [r for r in self.scan_results if 'absolute_path' in r]
        remote_files = [r for r in self.scan_results if 'url' in r]
        
        if local_files:
            solutions.append("📁 本地文件处理方案:")
            solutions.append("-" * 70)
            solutions.append("")
            solutions.append("1️⃣ 立即添加到 .gitignore:")
            solutions.append("   在项目根目录的 .gitignore 文件中添加以下行:")
            for item in local_files[:5]:
                solutions.append(f"   {item['path']}")
            if len(local_files) > 5:
                solutions.append(f"   ... 等共 {len(local_files)} 个文件")
            solutions.append("")
            
            solutions.append("2️⃣ 从 Git 跟踪中移除（如果已提交）:")
            solutions.append("   执行以下命令:")
            solutions.append("   git rm --cached backend/data/simnotice.db")
            solutions.append("   git rm --cached backend/data/test.db")
            solutions.append("   git commit -m '移除敏感数据库文件'")
            solutions.append("")
            
            solutions.append("3️⃣ 如果已推送到远程仓库，需要清理历史记录:")
            solutions.append("   方法一：使用 git filter-branch")
            solutions.append("   git filter-branch --force --index-filter \\")
            solutions.append("     'git rm --cached --ignore-unmatch backend/data/*.db' \\")
            solutions.append("     --prune-empty --tag-name-filter cat -- --all")
            solutions.append("")
            solutions.append("   方法二：使用 BFG Repo-Cleaner（推荐，更快）")
            solutions.append("   下载 BFG: https://rtyley.github.io/bfg-repo-cleaner/")
            solutions.append("   运行：java -jar bfg.jar --delete-files '*.db'")
            solutions.append("")
        
        if remote_files:
            solutions.append("🌐 远程仓库处理方案:")
            solutions.append("-" * 70)
            solutions.append("")
            solutions.append("⚠️  文件已提交到远程仓库，需要立即处理:")
            solutions.append("")
            solutions.append("1️⃣ 清理本地历史并强制推送:")
            solutions.append("   git filter-branch --force --index-filter \\")
            solutions.append("     'git rm --cached --ignore-unmatch backend/data/*.db' \\")
            solutions.append("     --prune-empty --tag-name-filter cat -- --all")
            solutions.append("   git push origin --force --all")
            solutions.append("")
            solutions.append("2️⃣ 使用 BFG Repo-Cleaner（推荐）:")
            solutions.append("   java -jar bfg.jar --delete-files '*.db'")
            solutions.append("   git reflog expire --expire=now --all")
            solutions.append("   git gc --prune=now --aggressive")
            solutions.append("   git push origin --force --all")
            solutions.append("")
            
            solutions.append("3️⃣ 如果包含敏感信息，考虑:")
            solutions.append("   - 立即更改相关密码和密钥")
            solutions.append("   - 通知相关人员检查是否有未授权访问")
            solutions.append("")
        
        solutions.append("=" * 70)
        solutions.append("📌 预防措施:")
        solutions.append("=" * 70)
        solutions.append("")
        solutions.append("✅ 在项目开始时配置好 .gitignore 文件")
        solutions.append("✅ 使用 pre-commit 钩子自动检查敏感文件")
        solutions.append("✅ 使用 GitHub Secret Scanning 功能")
        solutions.append("✅ 定期使用本工具扫描仓库")
        solutions.append("")
        
        solution_text = "\n".join(solutions)
        
        solution_window = tk.Toplevel(self.root)
        solution_window.title("🔧 解决办法")
        solution_window.geometry("800x600")
        solution_window.transient(self.root)
        
        text_widget = scrolledtext.ScrolledText(
            solution_window,
            wrap=tk.WORD,
            font=('Consolas', 10),
            padx=20,
            pady=20
        )
        text_widget.pack(fill=tk.BOTH, expand=True, padx=10, pady=10)
        
        text_widget.insert(tk.END, solution_text)
        text_widget.configure(state='disabled')
        
        button_frame = ttk.Frame(solution_window)
        button_frame.pack(fill=tk.X, padx=10, pady=(0, 10))
        
        ttk.Button(
            button_frame,
            text="📋 复制解决方案",
            command=lambda: self.copy_to_clipboard(solution_text)
        ).pack(side=tk.LEFT, padx=(0, 10))
        
        ttk.Button(
            button_frame,
            text="💾 保存为文件",
            command=lambda: self.save_solutions(solution_text)
        ).pack(side=tk.LEFT, padx=(0, 10))
        
        ttk.Button(
            button_frame,
            text="关闭",
            command=solution_window.destroy
        ).pack(side=tk.RIGHT)
    
    def save_solutions(self, text: str):
        """保存解决方案到文件"""
        file_path = filedialog.asksaveasfilename(
            title="保存解决方案",
            defaultextension=".txt",
            initialfile=f"solution_{datetime.now().strftime('%Y%m%d_%H%M%S')}.txt"
        )
        
        if file_path:
            with open(file_path, 'w', encoding='utf-8') as f:
                f.write(text)
            messagebox.showinfo("保存成功", f"解决方案已保存到:\n{file_path}")
    
    def on_item_double_click(self, event):
        """双击项目处理"""
        selection = self.results_tree.selection()
        if selection:
            item_id = selection[0]
            if item_id.startswith('http'):
                webbrowser.open(item_id)
            elif os.path.exists(item_id):
                path = Path(item_id)
                if path.is_file():
                    folder = path.parent
                else:
                    folder = path
                os.startfile(str(folder))
    
    def show_context_menu(self, event):
        """显示右键菜单"""
        item = self.results_tree.identify_row(event.y)
        if item:
            self.results_tree.selection_set(item)
            
            menu = tk.Menu(self.root, tearoff=0)
            
            if item.startswith('http'):
                menu.add_command(label="🌐 在浏览器中打开", command=lambda: webbrowser.open(item))
                menu.add_command(label="📋 复制 URL", command=lambda: self.copy_to_clipboard(item))
            elif os.path.exists(item):
                menu.add_command(label="📂 打开所在文件夹", command=lambda: self.open_folder(item))
                menu.add_command(label="📋 复制路径", command=lambda: self.copy_to_clipboard(item))
            
            menu.add_separator()
            menu.add_command(label="📋 复制文件路径", command=self.copy_selected_paths)
            
            menu.tk_popup(event.x_root, event.y_root)
    
    def copy_selected_paths(self):
        """复制选中项的路径"""
        paths = []
        for item_id in self.results_tree.selection():
            if item_id.startswith('http'):
                paths.append(item_id)
            elif os.path.exists(item_id):
                paths.append(item_id)
        
        if paths:
            self.copy_to_clipboard('\n'.join(paths))
    
    def copy_to_clipboard(self, text: str):
        """复制文本到剪贴板"""
        self.root.clipboard_clear()
        self.root.clipboard_append(text)
    
    def open_folder(self, path: str):
        """打开文件所在文件夹"""
        path = Path(path)
        if path.is_file():
            folder = path.parent
        else:
            folder = path
        os.startfile(str(folder))
    
    def export_report(self):
        """导出扫描报告"""
        if not self.scan_results:
            messagebox.showwarning("警告", "没有扫描结果可导出")
            return
        
        file_path = filedialog.asksaveasfilename(
            title="保存报告",
            defaultextension=".txt",
            filetypes=[
                ("文本文件", "*.txt"),
                ("JSON 文件", "*.json"),
                ("CSV 文件", "*.csv"),
                ("所有文件", "*.*")
            ],
            initialfile=f"db_leak_report_{datetime.now().strftime('%Y%m%d_%H%M%S')}.txt"
        )
        
        if file_path:
            if file_path.endswith('.json'):
                self.export_json_report(file_path)
            elif file_path.endswith('.csv'):
                self.export_csv_report(file_path)
            else:
                self.export_text_report(file_path)
    
    def export_text_report(self, file_path: str):
        """导出文本格式报告"""
        lines = []
        lines.append("=" * 80)
        lines.append("GitHub 仓库数据库泄露检查报告")
        lines.append("=" * 80)
        lines.append("")
        lines.append(f"📅 扫描时间: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
        lines.append(f"📊 发现问题: {len(self.scan_results)} 个敏感文件")
        
        by_source = {}
        for item in self.scan_results:
            src = item.get('source', '未知')
            if src not in by_source:
                by_source[src] = []
            by_source[src].append(item)
        
        lines.append(f"📁 涉及仓库: {len(by_source)} 个")
        lines.append("")
        
        high_risk_count = 0
        critical_risk_count = 0
        
        db_file_extensions = {'.db', '.db3', '.sqlite', '.sqlite3', '.sqlite2', '.accdb', '.mdb'}
        sql_with_data_patterns = ['insert', 'values', 'password', 'credential', 'secret', 'token', 'api_key']
        
        for src, items in sorted(by_source.items()):
            lines.append("=" * 80)
            lines.append(f"📦 仓库: {src}")
            lines.append("=" * 80)
            lines.append("")
            
            for idx, item in enumerate(items, 1):
                file_path_item = item.get('path', '')
                file_ext = Path(file_path_item).suffix.lower()
                file_size = item.get('size_human', 'N/A')
                db_type = item.get('type', 'other')
                type_name = DB_TYPE_NAMES.get(db_type, db_type)
                url = item.get('url', '')
                
                lines.append(f"  [{idx}] 文件: {file_path_item}")
                lines.append(f"      类型: {type_name}")
                lines.append(f"      大小: {file_size}")
                lines.append(f"      状态: ✅ 确认存在")
                
                if url:
                    lines.append(f"      链接: {url}")
                
                risk_level = "中危"
                risk_reasons = []
                
                if file_ext in db_file_extensions:
                    risk_level = "高危"
                    risk_reasons.append("二进制数据库文件，可能包含真实数据")
                    high_risk_count += 1
                
                if db_type == 'sqlite':
                    risk_level = "高危"
                    risk_reasons.append("SQLite 数据库文件可能包含敏感数据")
                
                if file_ext == '.sql':
                    risk_reasons.append("SQL 脚本可能暴露数据库结构")
                
                if db_type == 'other':
                    if file_ext in ['.env', '.bak', '.backup']:
                        risk_level = "高危"
                        risk_reasons.append("可能包含配置信息或备份数据")
                
                if 'credential' in file_path_item.lower() or 'secret' in file_path_item.lower():
                    risk_level = "严重"
                    risk_reasons.append("文件名暗示包含凭证信息")
                    critical_risk_count += 1
                
                if 'password' in file_path_item.lower() or 'token' in file_path_item.lower():
                    risk_level = "严重"
                    risk_reasons.append("文件名暗示包含密码或令牌")
                    critical_risk_count += 1
                
                if 'user' in file_path_item.lower() or 'account' in file_path_item.lower():
                    if risk_level != "严重":
                        risk_level = "高危"
                    risk_reasons.append("可能包含用户数据")
                
                risk_icon = {"低危": "🟢", "中危": "🟡", "高危": "🟠", "严重": "🔴"}
                lines.append(f"      风险等级: {risk_icon.get(risk_level, '⚪')} {risk_level}")
                
                if risk_reasons:
                    lines.append(f"      风险原因: {', '.join(risk_reasons)}")
                
                lines.append("")
            
            lines.append("")
        
        lines.append("=" * 80)
        lines.append("📈 风险统计")
        lines.append("=" * 80)
        lines.append(f"  • 总计发现: {len(self.scan_results)} 个敏感文件")
        lines.append(f"  • 涉及仓库: {len(by_source)} 个")
        lines.append(f"  • 高危文件: {high_risk_count} 个")
        lines.append(f"  • 严重风险: {critical_risk_count} 个")
        lines.append("")
        
        lines.append("=" * 80)
        lines.append("🚨 紧急建议")
        lines.append("=" * 80)
        lines.append("")
        lines.append("【立即行动】")
        lines.append("")
        lines.append("1️⃣ 对于所有仓库:")
        lines.append("   立即将以下模式添加到 .gitignore 文件:")
        lines.append("   *.db")
        lines.append("   *.db3")
        lines.append("   *.sqlite")
        lines.append("   *.sqlite3")
        lines.append("   *.sql")
        lines.append("   *.env")
        lines.append("   *.bak")
        lines.append("   *.backup")
        lines.append("")
        lines.append("2️⃣ 对于已提交的二进制数据库文件 (.db, .sqlite 等):")
        lines.append("   这些文件可能包含真实数据，需要立即处理:")
        lines.append("")
        lines.append("   方法一：使用 git filter-branch 清理历史")
        lines.append("   git filter-branch --force --index-filter \\")
        lines.append('     "git rm --cached --ignore-unmatch PATH/TO/FILE.db" \\')
        lines.append("     --prune-empty --tag-name-filter cat -- --all")
        lines.append("   git push origin --force --all")
        lines.append("")
        lines.append("   方法二：使用 BFG Repo-Cleaner（推荐，更快）")
        lines.append("   下载: https://rtyley.github.io/bfg-repo-cleaner/")
        lines.append("   java -jar bfg.jar --delete-files '*.db'")
        lines.append("   git reflog expire --expire=now --all")
        lines.append("   git gc --prune=now --aggressive")
        lines.append("   git push origin --force --all")
        lines.append("")
        lines.append("3️⃣ 对于 SQL 脚本文件:")
        lines.append("   检查是否包含敏感数据（密码、密钥等）")
        lines.append("   如包含敏感数据，按上述方法清理历史")
        lines.append("   如仅包含结构定义，考虑移除或添加到 .gitignore")
        lines.append("")
        lines.append("【后续加固】")
        lines.append("")
        lines.append("✅ 凭证轮换:")
        lines.append("   如果暴露了密码哈希或密钥，立即更改所有相关凭证")
        lines.append("   检查这些密钥是否在其他地方使用")
        lines.append("")
        lines.append("✅ 使用环境变量/机密存储:")
        lines.append("   确保数据库连接字符串、API密钥等敏感配置绝不写入代码")
        lines.append("   使用 .env 文件（并添加到 .gitignore）或机密管理服务")
        lines.append("")
        lines.append("✅ 启用 pre-commit 钩子:")
        lines.append("   自动检查即将提交的文件是否包含敏感信息")
        lines.append("")
        lines.append("✅ 启用 GitHub Secret Scanning:")
        lines.append("   在仓库设置中启用安全功能")
        lines.append("")
        lines.append("=" * 80)
        lines.append("⚠️  重要提示")
        lines.append("=" * 80)
        lines.append("")
        lines.append("• 重写 Git 历史会强制所有协作者进行复杂操作，操作前务必备份")
        lines.append("• 如果仓库有 Fork，清理历史后仍可能存在于 Fork 中")
        lines.append("• 对于严重敏感数据泄露，建议联系 GitHub 支持寻求帮助")
        lines.append("• 定期使用本工具扫描仓库，预防数据泄露")
        lines.append("")
        lines.append("=" * 80)
        lines.append("报告生成完成")
        lines.append("=" * 80)
        
        with open(file_path, 'w', encoding='utf-8') as f:
            f.write('\n'.join(lines))
        
        messagebox.showinfo("导出成功", f"报告已保存到:\n{file_path}")
    
    def export_json_report(self, file_path: str):
        """导出 JSON 格式报告"""
        report = {
            'scan_time': datetime.now().isoformat(),
            'total_issues': len(self.scan_results),
            'results': self.scan_results
        }
        
        with open(file_path, 'w', encoding='utf-8') as f:
            json.dump(report, f, ensure_ascii=False, indent=2)
        
        messagebox.showinfo("导出成功", f"报告已保存到:\n{file_path}")
    
    def export_csv_report(self, file_path: str):
        """导出 CSV 格式报告"""
        import csv
        
        with open(file_path, 'w', encoding='utf-8-sig', newline='') as f:
            writer = csv.writer(f)
            writer.writerow(['来源', '数据库类型', '文件路径', '文件大小', 'Git状态', 'URL'])
            
            for item in self.scan_results:
                writer.writerow([
                    item.get('source', ''),
                    DB_TYPE_NAMES.get(item.get('type', ''), item.get('type', '')),
                    item.get('path', ''),
                    item.get('size_human', ''),
                    item.get('status', ''),
                    item.get('url', '')
                ])
        
        messagebox.showinfo("导出成功", f"报告已保存到:\n{file_path}")


def main():
    """主函数"""
    root = tk.Tk()
    app = DBLeakCheckerGUI(root)
    root.mainloop()


if __name__ == '__main__':
    main()
