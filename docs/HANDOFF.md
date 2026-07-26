# 开发交接

最后核对：2026-07-26  
当前版本：1.2.0

## 当前状态

- 独立 WinForms EXE 已构建并安装桌面快捷方式 `Codex 恢复中心.lnk`。
- 自检确认当前包 `OpenAI.Codex_26.721.4979.0_x64__2p2nqsd0c76g0`、状态 `Ok`、主程序存在、微软商店入口存在。
- GUI 已真实启动，窗口标题正确，自动检查日志记录“安装状态：Ok；正在运行：是”。
- 1.1.0 已按用户视觉偏好完成界面重构：暖灰纸感、墨绿单一主操作、圆角卡片、用户语言状态和默认折叠日志。
- 2026-07-26 18:47 的首次真实故障已完整恢复成功。恢复中心 18:47:51 开始，18:51:48 启动完成，总耗时约 237 秒。
- 本次注册修复约 1 秒完成，但状态持续为 `Modified, NeedsRemediation`；旧版随后等待约 55 秒。微软商店阶段约 179 秒，Windows AppX 记录的 Stage 总耗时 171625 ms，其中 Stage required cost 150844 ms。
- 1.2.0 将该状态的注册后等待缩短为 12 秒，并增加商店阶段实时计时；同类故障预计减少约 35–40 秒。
- 未在当前活跃 Codex 会话中执行破坏性修复按钮，避免主动断开本次任务。

## 事故事实

2026-07-26 18:03 附近再次出现 AppModel `0x3CFC`。18:05 旧脚本注册修复本身成功，但脚本仅等待 3 秒，看到 `Modified, NeedsRemediation` 后错误停止。Windows 后续通过微软商店/Windows Update 对同版本包重新暂存约 169 秒，18:08 把状态从 `0x2` 恢复为 `0x0` 并重新启动。

## 权威路径

- 源码：`src\CodexRecoveryCenter.cs`
- 构建：`scripts\Build.ps1`
- 发布：`releases\Codex-Recovery-Center.exe`
- 桌面：`C:\Users\z\Desktop\Codex 恢复中心.lnk`
- 日志：`C:\Users\z\AppData\Local\CodexRecoveryCenter\logs`
- 远程仓库：`https://github.com/zwmopen/codex-recovery-center`
- 公开下载：`https://github.com/zwmopen/codex-recovery-center/releases`

## 恢复算法

1. 状态 `Ok`：直接安全启动，不注册。
2. 状态异常：按 Package Family 修复当前用户注册；`NeedsRemediation` 等待 12 秒，其他状态最多等待 30 秒。
3. 仍异常：调用微软商店产品 ID `9PLM9XGG6VKS` 强制重新暂存，最多等待 240 秒。
4. 恢复 `Ok`：以 `--disable-gpu` 启动。
5. 仍失败：保留日志并打开官方商店页。

## 禁止事项

- 不把 3 秒内的临时 `NeedsRemediation` 当成最终失败。
- 不对状态为 `Ok` 的包重复执行注册修复。
- 不使用 `--ignore-certificate-errors`。
- 不自动调用 `Reset-AppxPackage`。
- 不在公开仓库提交本机日志、事件或用户数据。

## 未完成与验证边界

- 已在 2026-07-26 18:47 的真实 `0x3CFC` 故障中完成“注册 → 商店重新暂存 → 安全启动”闭环。
- 1.2.0 的缩短等待算法尚待下一次真实故障计时回归；正常状态、自检和构建可立即验证。
- 显卡驱动和 Codex 自身 GPU 崩溃仍需厂商更新；恢复中心不能修补第三方二进制。
- 下一次故障只使用恢复中心，记录完整日志，再决定是否需要调整恢复算法。
