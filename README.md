# Codex 恢复中心

当前版本：1.1.0

服务对象：AI 基础设施与 Windows 桌面稳定性。

这是一个独立于 Codex 运行的 Windows 图形化恢复工具。它解决“Codex 崩溃后提示应用有问题、脚本黑框一闪而过、AppX 状态需要修复、只能反复去商店更新”的问题。

## 使用

双击桌面 `Codex 恢复中心`：

- **一键恢复并启动**：打不开或出现“应用有问题”时使用。工具会关闭残留进程、修复注册；仍异常时调用微软商店官方源重新暂存，恢复后安全启动。
- **安全模式启动**：安装正常但担心多窗口再次崩溃时使用，会关闭 GPU 加速后重新启动。
- **重新检查**：只读取安装与运行状态，不执行修改。
- **微软商店修复**：自动恢复仍未完成时，打开官方商店产品页。
- **处理记录**：默认折叠，排查时再展开。

工具不会主动执行 `Reset-AppxPackage`，不会主动清空应用数据。日志保存在：

```text
%LOCALAPPDATA%\CodexRecoveryCenter\logs
```

## 安装与更新

发布文件位于 `releases\Codex-Recovery-Center.exe`。运行构建脚本可重建并更新桌面入口：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Build.ps1 -InstallDesktopShortcut
```

## 已知限制

- 显卡驱动或 Codex 自身 GPU 缺陷仍可能导致运行时崩溃；本工具负责降低风险和恢复可启动状态，不修改厂商程序代码。
- 微软商店服务、网络或 App Installer 不可用时，官方源重新暂存可能失败，工具会保留日志并打开商店页。
- 修复按钮会关闭所有 Codex 窗口，使用前先保存正在进行的任务。
