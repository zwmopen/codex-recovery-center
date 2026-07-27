# 开发交接

最后核对：2026-07-27  
当前版本：1.4.1

## 当前状态

- 独立 WinForms EXE 已构建，桌面入口为 `Codex 恢复中心.lnk`。
- 2026-07-26 18:47 的真实 `0x3CFC` 故障已完成“注册修复 → 微软商店重新暂存 → 状态恢复 → GPU 安全模式启动”闭环。
- 1.2.0 已将 `Modified, NeedsRemediation` 的注册后等待由 45 秒缩短为 12 秒，并显示商店阶段实时用时。
- 1.3.0 已按用户真实项目视觉重做：默认冷灰蓝拟态悬浮，可切换克制玻璃；设置、关于和日志均使用同一视觉系统。
- 1.3.1 嵌入正式多尺寸图标并启用 Per-Monitor DPI 感知；小按钮移除硬阴影和矩形底块，文字与圆角在高缩放屏幕下已实际截图验证。
- 1.3.2 增加全局单实例锁和虚拟内存压力探针；第二次启动会退出并唤醒已有窗口，主界面会在提交压力高时显示橙色提醒。
- 1.4.0 增加崩溃分诊与“释放内存”。分诊用原生 EventLog 读取 Application 日志（源 `Application Error`、事件 1000、`ReplacementStrings[10]` 即故障应用路径含 `OpenAI.Codex`）。释放内存按进程名分组取提交内存前 12 名，硬编码保护系统关键进程与自身，关闭前二次核对进程名。已在 2026-07-27 08:24 自检中用真实数据验证（`ChatGPT.exe / e0000008` 判定 OutOfMemory；claude×17≈4.1GB 列为大户第一名）。
- 1.4.1 补齐 Windows EXE 文件版本元数据；构建脚本自动生成通用桌面成品、版本化发布包与 SHA-256 清单，降低手工发布错配风险。

## 崩溃分诊判定顺序（不要改回只认单一异常码）

1. 异常码命中 `e0000008`（Chromium OOM）、`c0000017`、`c00000fd` → 内存耗尽。
2. 否则读桌面日志 `%LOCALAPPDATA%\Packages\OpenAI.Codex_2p2nqsd0c76g0\LocalCache\Local\Codex\Logs\yyyy\MM\dd\*.log`，出现 `memory allocation of` → 内存耗尽。
3. 否则异常码含 `c0000409` → 程序中止。
4. 否则其他异常。

本机同时存在两条真实崩溃路径：`codex.exe` 的 `c0000409`（Rust 分配失败）与 `ChatGPT.exe` 的 `e0000008`（Electron 宿主 OOM）。1.4.0 的初版只认 `c0000409`，把当天最新的两次 `e0000008` 误判为“其他异常”，已在发布前修正。
- 设置已持久化，更新区只显示“检查版本”；自动发现和安全下载使用产品默认行为，替换前必须确认。
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

1. 状态 `Ok`：直接安全启动，不注册；若最近崩溃判定为内存耗尽，在日志中提示改用“释放内存”。
2. 状态异常：按 Package Family 修复当前用户注册；`NeedsRemediation` 等待 8 秒（7-26 两次真实事件注册均无效，最终都靠商店暂存），其他异常最多等待 30 秒。
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
- 发布后须再次验证“本机版本 = 在线版本”，并下载校验正式文件。
- 真正的自替换安装需在后续有新版时做一次端到端回归；当前下载、校验和调度路径已具备。
- 显卡驱动和 Codex 自身 GPU 崩溃仍需厂商更新；恢复中心不能修补第三方二进制。

## 2026-07-26 21:22 事件

- 21:22:47，AppModel Runtime 先出现 `0x3CFC`，机器级包状态检查失败；21:22:53 恢复中心首次读取时，包已是 `Modified, NeedsRemediation`。因此恢复中心不是这次初始损坏来源。
- 21:22:48 的 Schannel `10013` 属于随后启动的 `CrashSender1500` 崩溃上报进程，不是 Codex 的首发 TLS 故障。
- 第一份恢复流程从 21:23 开始；第二个恢复中心实例在 21:24 又启动，抢占同一个 `AppxManifest.xml`，触发 `0x80070020`。并发实例不是初始故障原因，但会让恢复更慢、更混乱；1.3.2 已用全局单实例锁修复并验证。
- 第一份流程在 21:28:17 把状态从 `0x2` 恢复为 `0x0`；21:33:44 再次成功启动，之后未出现新的 AppModel 错误。
- 机器有 15.73 GB 物理内存，事件后虚拟内存总量约 30.05 GB、仅余约 3.89 GB，D 盘页面文件当时已使用约 9.27 GB。历史上多次“虚拟内存不足”与 `codex.exe` 崩溃对齐，资源压力是强风险因素；但 21:22 当刻没有新的 Resource Exhaustion 事件，因此不能把本次首因确定为内存耗尽。
- 23:42 再次实测时提交压力仍在约 87%，可用虚拟内存约 4.0 GB；页面文件设置为 D 盘初始 16 GB、最大 32 GB，但当前实际分配约 16 GB。1.3.2 将可用虚拟内存低于 6 GB、物理内存低于 1.5 GB或提交压力达到 85% 视为高压力。
