# 开发交接

最后核对：2026-07-26  
当前版本：1.3.0

## 当前状态

- 独立 WinForms EXE 已构建，桌面入口为 `Codex 恢复中心.lnk`。
- 2026-07-26 18:47 的真实 `0x3CFC` 故障已完成“注册修复 → 微软商店重新暂存 → 状态恢复 → GPU 安全模式启动”闭环。
- 1.2.0 已将 `Modified, NeedsRemediation` 的注册后等待由 45 秒缩短为 12 秒，并显示商店阶段实时用时。
- 1.3.0 已按用户真实项目视觉重做：默认冷灰蓝拟态悬浮，可切换克制玻璃；设置、关于和日志均使用同一视觉系统。
- 设置已持久化，支持启动时检查更新、自动下载和手动检查。
- 更新链路使用最新 Release 随附的公开清单和 SHA-256；下载后仍需用户确认才替换并重启，不嵌入 GitHub Token。
- 不在当前活跃 Codex 会话中执行破坏性恢复按钮，避免主动断开任务。

## 视觉决策

真实参考源：

- `D:\AICode\工具开发\projects\window-layout-launcher`
- `D:\AICode\工具开发\projects\teambuilding-workflow-dashboard\docs\DESIGN.md`
- `D:\AICode\工具开发\projects\jianghu-conversion-assistant\src\说明\视觉语言规范.md`

默认拟态悬浮使用冷灰蓝画布、同色实体卡片和明暗双阴影；亮蓝只用于一个主操作。克制玻璃使用浅蓝面板、细白边和有限高光。

1.1.0 的暖灰、纸面、墨绿方案已被用户明确否定，不能作为后续版本的视觉参考。

## 权威路径

- 恢复引擎：`src\CodexRecoveryCenter.cs`
- 主界面：`src\MainForm.cs`
- 主题、设置与更新：`src\ProductExperience.cs`
- 构建：`scripts\Build.ps1`
- 发布：`releases\Codex-Recovery-Center.exe`
- 更新清单：`releases\manifest.json`
- 桌面：`C:\Users\z\Desktop\Codex 恢复中心.lnk`
- 本地数据：`C:\Users\z\AppData\Local\CodexRecoveryCenter`
- 远程仓库：`https://github.com/zwmopen/codex-recovery-center`

## 恢复算法

1. 状态 `Ok`：直接安全启动，不注册。
2. 状态异常：按 Package Family 修复当前用户注册；`NeedsRemediation` 等待 12 秒，其他异常最多等待 30 秒。
3. 仍异常：调用微软商店产品 ID `9PLM9XGG6VKS` 强制重新暂存，最多等待 240 秒。
4. 恢复 `Ok`：以 `--disable-gpu` 启动。
5. 仍失败：保留日志并打开官方商店页。

## 更新算法

1. 读取 `releases/latest/download/manifest.json`，它随最新 Release 一同发布。
2. 比较语义版本。
3. 从 GitHub Release 下载版本化 EXE。
4. 核对清单中的 SHA-256，不一致则拒绝安装。
5. 用户确认后，由临时 PowerShell 等待当前程序退出、替换原文件并重新启动。

GitHub API 匿名访问曾在真实测试中返回 403；Raw 分支地址在推送后仍固定返回旧清单。最终方案是把清单作为最新 Release 的正式附件，并保留防缓存参数。不要恢复成 API 或 Raw 分支作为唯一来源。

## 禁止事项

- 不把 3 秒内的临时 `NeedsRemediation` 当成最终失败。
- 不对状态为 `Ok` 的包重复执行注册修复。
- 不使用 `--ignore-certificate-errors`。
- 不自动调用 `Reset-AppxPackage`。
- 不在公开仓库提交本机日志、事件、Token 或用户数据。
- 不重新采用 1.1.0 的暖灰墨绿临时视觉。

## 当前验收与剩余边界

- x64 WinForms 构建、正常状态自检、双主题切换、设置保存、设置页、关于页和日志页已验证。
- 更新自检须验证：读取 Latest Release 清单、下载同版本 EXE、SHA-256 一致。
- v1.3.0 正式发布后须再次验证“本机 1.3.0 = 在线 1.3.0”，并下载校验正式 v1.3.0 文件。
- 真正的自替换安装需在后续有新版时做一次端到端回归；当前下载、校验和调度路径已具备。
- 显卡驱动和 Codex 自身 GPU 崩溃仍需厂商更新；恢复中心不能修补第三方二进制。
