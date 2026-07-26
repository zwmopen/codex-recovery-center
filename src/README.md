# 源码

这里是功能源码唯一真源。

- `CodexRecoveryCenter.cs`：程序入口、AppX 状态检查、注册修复、微软商店重新暂存与安全启动。
- `MainForm.cs`：主界面、状态反馈、恢复流程编排和独立日志窗口。
- `ProductExperience.cs`：两套主题、设置持久化、设置页、关于页、安全更新与更新自检。
- `assets\app.manifest`：Windows DPI 感知声明。
- `scripts\GenerateIcon.ps1`：生成可复现的多尺寸应用图标。

构建脚本会按文件名顺序编译 `src\*.cs`。界面变更必须同时验证拟态悬浮和克制玻璃两套主题。
