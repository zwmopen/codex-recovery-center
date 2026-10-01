# Microsoft WebView2 runtime bridge

本目录只保留发布版 WinForms 内嵌网页所需的 Microsoft WebView2 SDK 文件：

- `Microsoft.Web.WebView2.Core.dll`
- `Microsoft.Web.WebView2.WinForms.dll`
- `WebView2Loader.dll`（Windows x64）

来源：NuGet 包 `Microsoft.Web.WebView2` `1.0.3650.58`。

程序仍需要目标 Windows 已安装 Microsoft Edge WebView2 Runtime；SDK DLL 只负责托管控件桥接，不包含 Chromium Runtime 本身。

---

## 📝 变更记录

| 日期 (时间) | 执行者 | 记录 |
|---|---|---|
| 2026-09-17 23:11 | 💻 本地 PC / 反重力 | 初始化创建文档并补齐变更记录历史 |
