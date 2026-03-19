# GitHub 仓库敏感文件清理教程

> 本教程用于清理已提交到 GitHub 仓库的敏感文件（如数据库文件、密钥等），并彻底从 Git 历史中移除。

---

## 目录

- [前置要求](#前置要求)
- [第一步：安装 BFG Repo-Cleaner](#第一步安装-bfg-repo-cleaner)
- [第二步：添加 .gitignore 防止再次提交](#第二步添加-gitignore-防止再次提交)
- [第三步：从仓库中删除敏感文件](#第三步从仓库中删除敏感文件)
- [第四步：使用 BFG 清理 Git 历史](#第四步使用-bfg-清理-git-历史)
- [第五步：验证清理结果](#第五步验证清理结果)
- [常见问题](#常见问题)

---

## 前置要求

- 已安装 Git
- 已安装 Java（运行 `java -version` 检查）
- 有仓库的写入权限

---

## 第一步：安装 BFG Repo-Cleaner

BFG 是清理 Git 历史最快最安全的工具。

1. 下载 BFG：https://rtyley.github.io/bfg-repo-cleaner/
2. 将下载的 `bfg.jar` 放到方便访问的目录，例如：
   - `C:\Tools\bfg.jar`

---

## 第二步：添加 .gitignore 防止再次提交

在开始清理之前，先确保敏感文件不会再次被提交。

```bash
# 1. 克隆仓库
git clone https://github.com/用户名/仓库名.git
cd 仓库名

# 2. 创建或编辑 .gitignore
```

**.gitignore 内容：**

```gitignore
# 数据库文件
*.db
*.db3
*.sqlite
*.sqlite3

# 配置文件
*.env
.env.local
.env.*.local

# 备份文件
*.bak
*.backup

# 其他敏感文件
# *.sql  # 根据需要取消注释
```

```bash
# 3. 提交 .gitignore
git add .gitignore
git commit -m "添加 .gitignore 防止敏感文件提交"
git push origin master
```

---

## 第三步：从仓库中删除敏感文件

**重要：BFG 会保护最新提交（HEAD）中的文件，所以必须先手动删除文件并提交。**

**⚠️ 安全提示：不要直接在本地开发目录操作！先克隆一个干净的副本，避免误删本地文件。**

```bash
# 1. 克隆一个干净的副本（不影响本地开发环境）
git clone https://github.com/用户名/仓库名.git 仓库名-clean
cd 仓库名-clean

# 2. 删除敏感文件（根据实际情况修改路径）
git rm path/to/sensitive-file.db
git rm path/to/another-file.db

# 3. 提交删除
git commit -m "移除敏感数据库文件"
git push origin master

# 4. 操作完成后可以删除这个副本
cd ..
rm -rf 仓库名-clean
```

**完整示例：**

```bash
# 清理 Simnotice 仓库
cd C:\Code\Sim_Notice
git clone https://github.com/ZeroOneCN/Simnotice.git Simnotice-clean
cd Simnotice-clean
git rm backend/data/simnotice.db
git rm backend/data/test.db
git commit -m "移除敏感数据库文件"
git push origin master
cd ..
rmdir /s /q Simnotice-clean
```

**💡 为什么这样做？**
- 克隆副本操作，不会影响本地正在开发的项目
- 即使操作失误，也只是删除了副本，原项目不受影响
- 操作完成后删除副本，保持目录整洁

---

## 第四步：使用 BFG 清理 Git 历史

文件从最新版本删除后，使用 BFG 清理历史记录。

```bash
# 1. 创建镜像仓库（用于清理历史）
cd 父目录
git clone --mirror https://github.com/用户名/仓库名.git 仓库名-mirror
cd 仓库名-mirror

# 2. 运行 BFG 删除指定类型的文件
java -jar C:\Tools\bfg.jar --delete-files "*.db"

# 或者删除特定文件
java -jar C:\Tools\bfg.jar --delete-files "filename.db"

# 或者删除整个文件夹
java -jar C:\Tools\bfg.jar --delete-folders foldername

# 3. 清理 Git 对象
git reflog expire --expire=now --all
git gc --prune=now --aggressive

# 4. 强制推送到远程仓库
git push --force
```

**完整示例：**

```bash
# 清理 Simnotice 仓库
cd C:\Code\Sim_Notice
git clone --mirror https://github.com/ZeroOneCN/Simnotice.git Simnotice-mirror
cd Simnotice-mirror
java -jar C:\Code\PythonProject\bfg.jar --delete-files "*.db"
git reflog expire --expire=now --all
git gc --prune=now --aggressive
git push --force
```

---

## 第五步：验证清理结果

```bash
# 方法1：检查文件是否还在历史中
git log --all --full-history -- "*.db"

# 方法2：搜索文件内容
git log -p --all -- "敏感内容关键词"

# 如果没有输出，说明清理成功
```

**在 GitHub 上验证：**

1. 访问仓库页面
2. 检查文件是否已删除
3. 查看提交历史，确认敏感文件不再出现

---

## 常见问题

### Q1: BFG 提示 "Protected commits" 怎么办？

**原因：** BFG 默认保护最新提交（HEAD）中的文件。

**解决：** 先执行 [第三步](#第三步从仓库中删除敏感文件) 手动删除文件并提交，然后再运行 BFG。

### Q2: 强制推送后其他人怎么办？

**解决：** 通知所有协作者重新克隆仓库：

```bash
# 删除本地仓库
cd ..
rm -rf 仓库名

# 重新克隆
git clone https://github.com/用户名/仓库名.git
```

### Q3: 仓库有 Fork 怎么办？

**注意：** Fork 中可能仍包含敏感文件。

**解决：**
- 联系 Fork 所有者删除或更新
- 如果是严重泄露，联系 GitHub 支持

### Q4: 如何清理多个仓库？

可以编写批处理脚本：

```batch
@echo off
set REPOS=Simnotice Travelrecord Vaginaldiary
set BFG_PATH=C:\Code\PythonProject\bfg.jar

for %%r in (%REPOS%) do (
    echo 正在处理 %%r...
    git clone --mirror https://github.com/ZeroOneCN/%%r.git %%r-mirror
    cd %%r-mirror
    java -jar %BFG_PATH% --delete-files "*.db"
    git reflog expire --expire=now --all
    git gc --prune=now --aggressive
    git push --force
    cd ..
)
```

### Q5: 清理后还能恢复吗？

**不能。** 强制推送后，敏感文件会从 Git 历史中永久删除。建议：

1. 操作前备份仓库
2. 确认不再需要这些文件

---

## 需要清理的仓库清单

根据扫描报告，以下仓库需要处理：

### 高危（SQLite 数据库文件）

| 仓库 | 文件路径 | 状态 |
|------|----------|------|
| Simnotice | `backend/data/simnotice.db` | ⬜ 待处理 |
| Simnotice | `backend/data/test.db` | ⬜ 待处理 |
| Travelrecord | `backend/database/travel_expense.db` | ⬜ 待处理 |
| Vaginaldiary | `diary.db` | ⬜ 待处理 |

### 中危（SQL 脚本文件）

| 仓库 | 文件路径 | 状态 |
|------|----------|------|
| Bodyhealth | `backend/database/init.sql` | ⬜ 待处理 |
| Hnalines | `hainan_airlines_data.json` | ⬜ 待处理 |
| Itemstorage | `docs/database-schema.sql` | ⬜ 待处理 |
| Medication | `backend/config/init.sql` | ⬜ 待处理 |
| Socialmatch | `Social-Server/src/main/resources/db/init.sql` | ⬜ 待处理 |
| Travelrecord | `backend/database/schema.sql` | ⬜ 待处理 |

---

## 后续加固建议

1. **凭证轮换**：如果暴露了密码或密钥，立即更改
2. **启用 pre-commit 钩子**：自动检查敏感文件
3. **启用 GitHub Secret Scanning**：在仓库设置中开启
4. **定期扫描**：使用工具定期检查仓库

---

## 参考链接

- [BFG Repo-Cleaner 官网](https://rtyley.github.io/bfg-repo-cleaner/)
- [GitHub - 从历史中删除敏感数据](https://docs.github.com/zh/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository)
- [git-filter-repo（替代方案）](https://github.com/newren/git-filter-repo)

---

*最后更新：2026-03-19*