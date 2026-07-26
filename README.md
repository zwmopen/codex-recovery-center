# Codex 恢复中心

服务对象：AI 基础设施与 Windows 桌面稳定性。

这是一个独立于 Codex 运行的 Windows 图形化恢复工具。它解决“Codex 崩溃后提示应用有问题、脚本黑框一闪而过、AppX 状态需要修复、只能反复去商店更新”的问题。

## 使用

双击桌面 `Codex 恢复中心`：

- **检查状态**：查看官方包是否安装、状态是否为 `Ok`。
- **安全启动**：完全关闭 Codex 后，以 `--disable-gpu` 启动。
- **一键修复并启动**：关闭残留进程；状态异常时修复当前用户注册并等待；仍异常时调用微软商店官方源 `9PLM9XGG6VKS` 重新暂存；恢复为 `Ok` 后安全启动。
- **打开官方商店页**：仅在自动商店恢复失败时使用。

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

