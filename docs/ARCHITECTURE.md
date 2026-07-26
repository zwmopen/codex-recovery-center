# 架构

```text
WinForms GUI
  ├─ MainForm：状态、恢复路径、操作组、更新入口
  ├─ SettingsForm：双主题与更新偏好
  ├─ AboutForm / LogForm：产品说明与诊断记录
  ├─ RecoveryEngine
  │   ├─ 包状态探针：Get-AppxPackage OpenAI.Codex
  │   ├─ 进程管理：ChatGPT.exe / codex.exe
  │   ├─ 安全启动：ChatGPT.exe --disable-gpu
  │   ├─ 注册修复：RegisterByFamilyName + 12/30 秒轮询
  │   └─ 商店兜底：winget → msstore → 9PLM9XGG6VKS + 240 秒轮询
  ├─ ThemeManager / SettingsStore：视觉与偏好持久化
  └─ UpdateService：Raw 清单 → Release 下载 → SHA-256 → 用户确认替换
```

## 关键文件

- 程序入口与恢复引擎：`src\CodexRecoveryCenter.cs`
- 主界面与日志窗口：`src\MainForm.cs`
- 设置、主题、关于与更新：`src\ProductExperience.cs`
- 构建入口：`scripts\Build.ps1`
- 发布文件：`releases\Codex-Recovery-Center.exe`
- 在线版本清单：`releases\manifest.json`

## 本地路径

```text
%LOCALAPPDATA%\CodexRecoveryCenter\
  ├─ settings.ini
  ├─ logs\
  └─ updates\
```

程序使用系统自带 .NET Framework WinForms，发布文件不需要额外运行库。微软商店兜底依赖 Windows App Installer 提供的 `winget.exe`。

更新服务不需要 GitHub Token。它读取仓库 Raw 清单，下载对应 GitHub Release 文件并核对 SHA-256；校验通过后仍由用户决定是否替换当前 EXE。
