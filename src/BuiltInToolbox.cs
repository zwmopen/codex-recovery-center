using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CodexRecoveryCenter
{
    internal sealed class BuiltInToolDefinition
    {
        public readonly string Id;
        public readonly string Title;
        public readonly string Summary;
        public readonly string EntryPoint;
        public readonly string Risk;

        public BuiltInToolDefinition(string id, string title, string summary, string entryPoint, string risk)
        {
            Id = id;
            Title = title;
            Summary = summary;
            EntryPoint = entryPoint;
            Risk = risk;
        }
    }

    /// <summary>
    /// Runtime tool registry. New repair modules are registered here once and
    /// are then used by the UI, command-line self-test and the built-in manifest.
    /// Build/launcher scripts remain development helpers, not runtime dependencies.
    /// </summary>
    internal static class BuiltInToolbox
    {
        private static readonly BuiltInToolDefinition[] Definitions =
        {
            new BuiltInToolDefinition("codex", "Codex 恢复", "AppX 注册、崩溃分诊、GPU 安全启动和资源状态。", "CodexRepairModule", "修复前确认"),
            new BuiltInToolDefinition("bluetooth", "蓝牙音频", "蓝牙端点、AudioEndpointBuilder 和 Audiosrv 状态。", "BluetoothRepairModule", "低风险服务刷新"),
            new BuiltInToolDefinition("mouse-voice", "鼠标语音输入", "watchdog、PID、心跳和隐藏任务状态。", "MouseVoiceRepairModule", "低风险重启已注册任务"),
            new BuiltInToolDefinition("popup-audit", "弹窗审计", "PowerShell/CMD 的任务、父进程、命令行和事件证据链。", "PopupAuditRepairModule", "只读"),
            new BuiltInToolDefinition("network", "网络诊断", "网卡、DNS、代理和 Cloudflare/tunnel 证据。", "NetworkRepairModule", "只刷新 DNS"),
            new BuiltInToolDefinition("storage", "存储分析", "磁盘容量、缓存候选和用户资料边界。", "StorageAnalysisModule", "只读"),
            new BuiltInToolDefinition("power-space", "强力空间", "开发缓存、浏览器缓存和临时文件的预览与确认清理。", "PowerSpaceRepairModule", "逐项确认后清理"),
            new BuiltInToolDefinition("performance", "电脑加速与空间", "内存压力、临时文件、开发进程和资源守卫。", "PerformanceRepairModule", "低风险清理"),
            new BuiltInToolDefinition("startup", "启动项与桌面入口", "Startup、Run/RunOnce、任务和桌面快捷方式审计。", "StartupRepairModule", "只读"),
            new BuiltInToolDefinition("backup", "备份中心", "技能、专属配置和 WebDAV 增量同步状态。", "BackupMonitorModule", "只读；不控制上传"),
            new BuiltInToolDefinition("openclaw", "小龙虾", "启动、重启、模型切换和控制网页。", "OpenClawRepairModule", "本机控制；操作前明确反馈"),
            new BuiltInToolDefinition("quick-tools", "快捷工具", "把常用检查、空间、网络、蓝牙、内存和小龙虾动作集中到一个入口。", "QuickToolsForm", "逐项确认；不静默修改"),
            new BuiltInToolDefinition("resource-guard", "资源守卫", "内置后台保护，按安全边界处理失控开发进程。", "ResourceGuard.RunOnce", "计划任务受控执行")
        };

        public static BuiltInToolDefinition[] DefinitionsSnapshot()
        {
            return (BuiltInToolDefinition[])Definitions.Clone();
        }

        public static IRepairModule[] CreateModules()
        {
            return new IRepairModule[]
            {
                new CodexRepairModule(),
                new BluetoothRepairModule(),
                new MouseVoiceRepairModule(),
                new PopupAuditRepairModule(),
                new NetworkRepairModule(),
                new StorageAnalysisModule(),
                new PowerSpaceRepairModule(),
                new PerformanceRepairModule(),
                new StartupRepairModule(),
                new BackupMonitorModule(),
                new OpenClawRepairModule()
            };
        }

        public static string BuildManifest()
        {
            var builder = new StringBuilder();
            builder.AppendLine(ProductInfo.ProductName + "内置工具包");
            builder.AppendLine("version=" + ProductInfo.Version);
            builder.AppendLine("runtime=embedded-in-exe");
            builder.AppendLine("external-repair-scripts=none");
            builder.AppendLine("external-control-scripts=openclaw=" + OpenClawController.ManagerPath);
            builder.AppendLine("注意：Build.ps1、Start-WindowsRepairCenter.ps1 和 VBS 仅用于源码开发/热更新；小龙虾控制复用本机控制脚本，不复制密钥或配置逻辑。");
            builder.AppendLine();
            foreach (BuiltInToolDefinition definition in Definitions)
            {
                builder.AppendLine("[" + definition.Id + "] " + definition.Title);
                builder.AppendLine("用途=" + definition.Summary);
                builder.AppendLine("入口=" + definition.EntryPoint);
                builder.AppendLine("风险=" + definition.Risk);
            }
            return builder.ToString();
        }

        public static void WriteManifest(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("输出路径不能为空。", "path");
            string fullPath = Path.GetFullPath(path);
            string parent = Path.GetDirectoryName(fullPath);
            if (!String.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
            File.WriteAllText(fullPath, BuildManifest(), new UTF8Encoding(false));
        }
    }
}
