# 故障排查

## 黑框闪一下，没有修好

- 现象：旧 PowerShell/VBS 入口一闪而过或没有反馈。
- 根因：启动器退出、残留进程阻止安全参数生效；旧恢复脚本还过早判断 `NeedsRemediation`。
- 修复：使用独立 `Codex 恢复中心.exe`，所有阶段在界面和日志中显示。
- 防回归：桌面不再保留旧的两个脚本快捷方式。

## 注册成功但状态仍不是 Ok

- 现象：`Add-AppxPackage` 没报错，但包显示 `Modified, NeedsRemediation`。
- 根因：机器级包仍需商店重新暂存；状态恢复不是即时完成。
- 修复：1.2.0 对 `NeedsRemediation` 等待 12 秒，其他异常最多等待 30 秒；仍异常时调用微软商店官方产品 `9PLM9XGG6VKS` 并持续检查状态。
- 验证：`Get-AppxPackage OpenAI.Codex` 状态为 `Ok`，随后安全启动。

## 工具提示商店入口不可用

确认 Windows App Installer 存在：

```text
%LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe
```

如果缺失，点击工具中的“打开官方商店页”。不从第三方下载未知安装包。
