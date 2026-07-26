# 架构

```text
WinForms GUI
  ├─ 包状态探针：Get-AppxPackage OpenAI.Codex
  ├─ 进程管理：ChatGPT.exe / codex.exe
  ├─ 安全启动：ChatGPT.exe --disable-gpu
  ├─ 注册修复：RegisterByFamilyName + 45 秒轮询
  ├─ 商店兜底：winget → msstore → 9PLM9XGG6VKS + 240 秒轮询
  └─ 本地日志：%LOCALAPPDATA%\CodexRecoveryCenter\logs
```

源码真源：`src\CodexRecoveryCenter.cs`。  
构建入口：`scripts\Build.ps1`。  
交付文件：`releases\Codex-Recovery-Center.exe`。

程序使用系统自带 .NET Framework WinForms，发布文件不需要额外运行库。微软商店兜底依赖 Windows App Installer 提供的 `winget.exe`。

