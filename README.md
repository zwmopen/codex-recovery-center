# Codex 恢复中心

当前版本：1.3.0

这是一个独立于 Codex 运行的 Windows 图形化恢复工具。它处理“应用有问题、请重新安装”、AppX 注册异常、旧脚本黑框闪退，以及只能反复进入微软商店更新才能恢复的问题。

## 怎么用

双击桌面 `Codex 恢复中心`：

- **一键恢复并启动**：Codex 打不开或出现“应用有问题”时使用。工具会关闭残留进程、修复注册；仍异常时调用微软商店官方源重新暂存，恢复后以更稳妥的方式启动。
- **安全模式启动**：安装状态正常，但担心多窗口或 GPU 渲染再次崩溃时使用。
- **重新检查**：只读取安装与运行状态，不做修改。
- **微软商店修复**：自动恢复没有完成时，打开官方产品页。
- **处理记录**：查看本次判断、修复步骤、耗时和结果。
- **设置**：切换视觉主题、检查更新并设置启动时自动检查或自动下载。
- **关于**：查看产品初衷、安全边界、数据与隐私说明。

遇到 `Modified, NeedsRemediation` 时，工具先快速确认注册结果；仍未恢复便直接进入微软商店官方重新暂存。商店阶段通常需要 2–4 分钟，界面会持续显示已用时间。

工具不会主动执行 `Reset-AppxPackage`，也不会主动清空 Codex 用户数据。修复会关闭所有 Codex 窗口，使用前先保存正在进行的任务。

## 两套视觉主题

- **拟态悬浮（默认）**：冷灰蓝背景、同色实体卡片、明暗双阴影，接近窗口管理器的工具感。
- **克制玻璃**：浅蓝玻璃面板、细白边和有限高光，不使用大面积透明或炫光。

主题会保存在：

```text
%LOCALAPPDATA%\CodexRecoveryCenter\settings.ini
```

## 安装与更新

普通用户从 [GitHub Releases](https://github.com/zwmopen/codex-recovery-center/releases/latest) 下载最新 `Codex-Recovery-Center-v*.exe`，双击即可运行。

设置页支持手动和启动时检查更新。新版从公开 Release 下载，必须通过版本清单中的 SHA-256 校验；即使开启“自动下载”，替换程序前仍需用户确认。

开发者可重新构建并更新桌面入口：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Build.ps1 -InstallDesktopShortcut
```

## 本地数据

```text
日志：%LOCALAPPDATA%\CodexRecoveryCenter\logs
设置：%LOCALAPPDATA%\CodexRecoveryCenter\settings.ini
更新：%LOCALAPPDATA%\CodexRecoveryCenter\updates
```

项目不会上传聊天内容、凭据或本地诊断日志。

## 版本

| 版本 | 主要变化 |
|---|---|
| 1.0.0 | 独立图形界面、状态检查、安全启动和分级恢复 |
| 1.1.0 | 第一版界面重构，后确认视觉方向不准确 |
| 1.2.0 | 真实故障闭环验证，缩短无效等待并显示商店处理用时 |
| 1.3.0 | 按真实产品视觉系统重做；双主题、设置、关于和安全自更新 |

完整变更见 [CHANGELOG.md](CHANGELOG.md)。

## 已知边界

- 显卡驱动或 Codex 自身 GPU 缺陷仍可能导致运行时崩溃；本工具负责降低风险和恢复可启动状态，不能修改厂商程序代码。
- 微软商店、网络或 App Installer 不可用时，官方源重新暂存可能失败；工具会保留日志并提供官方商店入口。
- 恢复中心无法保证“软件永远不崩”，但能减少重复下载安装器，把常见注册异常变成可观察、可重试、可恢复的流程。

源码采用 [MIT License](LICENSE)。
