# 架构

## 2.0 统一修复外壳

```text
WindowsRepairCenterForm / WindowsToolShell
  ├─ 单实例、主题、DPI、模块导航与健康总览
  ├─ IRepairModule：Detect → Evidence → Repair → Verify（模块内复查）→ Rollback（如有备份）
  ├─ CodexRepairModule：AppX、GPU、崩溃与 ResourceGuard
  ├─ BluetoothRepairModule：蓝牙端点与 AudioEndpointBuilder/Audiosrv
  ├─ MouseVoiceRepairModule：watchdog、PID、心跳与隐藏任务
  ├─ PopupAuditRepairModule：Task Scheduler、Startup、Run/RunOnce、profile、Event Log
  ├─ NetworkRepairModule：网卡、DNS、代理、Cloudflare/tunnel 只读诊断与 DNS 刷新
  ├─ StorageAnalysisModule：磁盘容量、缓存候选与用户资料只读分级
  ├─ PerformanceRepairModule：内存、临时文件、开发进程白名单与资源守卫
  └─ StartupRepairModule：启动项、桌面入口、备份与迁移记录
```

所有模块都返回 `Normal`、`NeedsAttention`、`NeedsConfirmation`、`Unsupported` 或 `Error`，UI 不直接执行未声明的系统操作。

```text
WinForms GUI
  ├─ Single-instance mutex：重复启动只唤醒已有窗口
  ├─ WindowsRepairCenterForm：健康总览、模块导航、证据、设置、记录与安全修复入口
  ├─ MainForm：旧版 Codex 专用界面，保留作回退参考
  ├─ SettingsForm：双主题与更新偏好
  ├─ AboutForm / LogForm：产品说明与诊断记录
  ├─ RecoveryEngine
  │   ├─ 包状态探针：Get-AppxPackage OpenAI.Codex
  │   ├─ 进程管理：ChatGPT.exe / codex.exe
  │   ├─ 崩溃分诊：Application Error + Codex 桌面日志
  │   │   ├─ 内存耗尽：异常码 / memory allocation failed
  │   │   └─ GPU / 会话渲染：GPU crashed / launch-failed + conversationId
  │   ├─ 安全启动：ChatGPT.exe --disable-gpu
  │   ├─ 注册修复：RegisterByFamilyName + 12/30 秒轮询
  │   └─ 商店兜底：winget → msstore → 9PLM9XGG6VKS + 240 秒轮询
  ├─ GlobalMemoryStatusEx：物理/虚拟内存余量与提交压力
  ├─ ResourceGuard（计划任务每 2 分钟调用 `--resource-guard-once`）
  │   ├─ 6 GB 单进程硬保护：Node / PowerShell / pwsh
  │   ├─ 4 小时宽限后的高句柄孤儿开发服务回收
  │   ├─ 高线程孤儿 conhost 回收
  │   └─ 整机高压时降低优先级并回收工作集
  ├─ ThemeManager / SettingsStore：视觉与偏好持久化
  └─ UpdateService：Latest Release 清单 → EXE 下载 → SHA-256 → 用户确认替换
```

## 关键文件

- 程序入口与恢复引擎：`src\CodexRecoveryCenter.cs`
- 统一修复外壳与模块：`src\WindowsRepairCenter.cs`（概览、Codex、蓝牙、鼠标语音、弹窗审计、网络、存储、资源和启动项）
- 资源守卫与计划任务：`src\ResourceGuard.cs`
- 主界面与日志窗口：`src\MainForm.cs`
- 设置、主题、关于与更新：`src\ProductExperience.cs`
- 构建入口：`scripts\Build.ps1`
- 源码优先启动：`scripts\run-windows-repair-center.vbs` → `scripts\Start-WindowsRepairCenter.ps1`
- 发布文件：`releases\Windows-Repair-Center.exe`
- 在线版本清单：`releases\manifest.json`

## 本地路径

```text
%LOCALAPPDATA%\WindowsRepairCenter\
  ├─ settings.ini
  ├─ logs\
  ├─ reports\
  ├─ backups\
  ├─ resource-guard\latest.txt + guard-YYYYMMDD.log
  └─ updates\
```

迁移期仍兼容读取 `%LOCALAPPDATA%\CodexRecoveryCenter` 的旧设置和资源守卫数据，不静默删除旧目录。

程序使用系统自带 .NET Framework WinForms，发布文件不需要额外运行库。微软商店兜底依赖 Windows App Installer 提供的 `winget.exe`。

更新服务不需要 GitHub Token。它读取最新 GitHub Release 随附的 `manifest.json`，下载同版本 EXE 并核对 SHA-256；校验通过后仍由用户决定是否替换当前 EXE。
