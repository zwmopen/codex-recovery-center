using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace CodexRecoveryCenter
{
    internal static class BackupMonitorPaths
    {
        public const string StatePath = @"D:\AICode\运行数据\business-skill-agent\sync-state.json";
        public const string ConfigPath = @"D:\AICode\AI\config\backup_config.json";
        public const string DashboardUrl = "http://127.0.0.1:8766/";
    }

    /// <summary>
    /// Read-only backup status module. It reads the local checkpoint written by
    /// backup_workflow.py; it never starts, pauses, retries, deletes, or uploads.
    /// </summary>
    internal sealed class BackupMonitorModule : RepairModuleBase
    {
        public override string Id { get { return "backup"; } }
        public override string Title { get { return "备份中心"; } }
        public override string Caption { get { return "技能、专属配置与增量同步状态"; } }

        public override RepairFinding Detect()
        {
            if (!File.Exists(BackupMonitorPaths.StatePath))
            {
                RepairFinding missing = Finding(
                    RepairFindingStatus.NeedsAttention,
                    "还没有读取到备份运行状态。",
                    false,
                    "",
                    "状态文件不存在，当前模块只读，不会自行启动上传。",
                    "只读诊断");
                Add(missing, "StatePath=" + BackupMonitorPaths.StatePath);
                Add(missing, "Dashboard=" + BackupMonitorPaths.DashboardUrl);
                return missing;
            }

            try
            {
                Dictionary<string, object> root = ReadRoot(BackupMonitorPaths.StatePath);
                // The local checkpoint is the raw workflow schema. The web
                // dashboard normalizes it into state/config/privacy sections;
                // the embedded module must also understand the raw form.
                IDictionary<string, object> state = Map(root, "state");
                if (state == null) state = root;
                IDictionary<string, object> progress = Map(state, "progress");
                IDictionary<string, object> config = null;
                try
                {
                    if (File.Exists(BackupMonitorPaths.ConfigPath))
                        config = Map(ReadRoot(BackupMonitorPaths.ConfigPath), "webdav");
                }
                catch { config = null; }

                string status = Text(state, "status", "");
                if (String.IsNullOrWhiteSpace(status)) status = Text(state, "lastStatus", "unknown");
                long completedFiles = Number(progress, "completedFiles");
                long totalFiles = Number(progress, "totalFiles");
                long completedBytes = Number(progress, "completedBytes");
                long totalBytes = Number(progress, "totalBytes");
                double filePercent = Percent(completedFiles, totalFiles);
                double bytePercent = Percent(completedBytes, totalBytes);
                bool attention = status.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
                    status.Equals("partial", StringComparison.OrdinalIgnoreCase) ||
                    status.Equals("not_started", StringComparison.OrdinalIgnoreCase);
                string summary = StatusSummary(status, filePercent, bytePercent);
                string failure = Text(state, "failure", "");
                RepairFinding finding = Finding(
                    attention ? RepairFindingStatus.NeedsAttention : RepairFindingStatus.Normal,
                    summary,
                    false,
                    "",
                    "工具箱只读取本地检查点；详细监控台会自动刷新，但不控制上传任务。",
                    "只读，不上传、不暂停、不重试、不删除");

                Add(finding, "状态=" + status + "; 当前范围=" + Text(state, "activeScope", "—") + "; 当前批次=" + Text(state, "activeBatch", "—"));
                Add(finding, "文件=" + completedFiles.ToString("N0") + "/" + totalFiles.ToString("N0") + "（" + filePercent.ToString("F1") + "%）");
                Add(finding, "容量=" + FormatBytes(completedBytes) + "/" + FormatBytes(totalBytes) + "（" + bytePercent.ToString("F1") + "%）");
                Add(finding, "远端=" + Text(state, "remote", Text(config, "remote", "—")) + "; 目录=" + Text(state, "remoteRoot", Text(config, "folder", "—")));
                Add(finding, "限速=" + Text(config, "tpslimit", "—") + " TPS; 并发=" + Text(config, "transfers", "—") + "/" + Text(config, "checkers", "—"));
                Add(finding, "最后尝试=" + Text(state, "lastAttemptAt", "—") + "; 自动化=由 Codex 自动化任务管理");
                Add(finding, "计划校验=" + Text(state, "planValidation", "—") + "; 密钥层=" + Text(state, "secretLayerStatus", "—"));
                Add(finding, "安全边界=修复中心只读，不启动、暂停、重试、删除或上传");
                Add(finding, "状态源=" + BackupMonitorPaths.StatePath);
                Add(finding, "配置源=" + BackupMonitorPaths.ConfigPath);
                Add(finding, "详细监控台=" + BackupMonitorPaths.DashboardUrl);
                if (!String.IsNullOrWhiteSpace(failure)) Add(finding, "失败原因=" + OneLine(failure));
                return finding;
            }
            catch (Exception ex)
            {
                RepairFinding error = Finding(
                    RepairFindingStatus.Error,
                    "备份状态读取失败。",
                    false,
                    "",
                    OneLine(ex.Message),
                    "只读诊断");
                Add(error, "StatePath=" + BackupMonitorPaths.StatePath);
                return error;
            }
        }

        private static Dictionary<string, object> ReadRoot(string path)
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            object value = new JavaScriptSerializer().DeserializeObject(json);
            Dictionary<string, object> root = value as Dictionary<string, object>;
            if (root == null) throw new InvalidDataException("备份状态不是有效的 JSON 对象。");
            return root;
        }

        private static IDictionary<string, object> Map(IDictionary<string, object> source, string key)
        {
            if (source == null || !source.ContainsKey(key)) return null;
            return source[key] as IDictionary<string, object>;
        }

        private static string Text(IDictionary<string, object> source, string key, string fallback)
        {
            if (source == null || !source.ContainsKey(key) || source[key] == null) return fallback;
            string value = Convert.ToString(source[key], CultureInfo.InvariantCulture);
            return String.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static long Number(IDictionary<string, object> source, string key)
        {
            long value;
            return Int64.TryParse(Text(source, key, "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 0L;
        }

        private static double Percent(long completed, long total)
        {
            if (total <= 0) return 0D;
            return Math.Max(0D, Math.Min(100D, completed * 100D / total));
        }

        private static string StatusSummary(string status, double filePercent, double bytePercent)
        {
            if (status.Equals("running", StringComparison.OrdinalIgnoreCase))
                return "备份正在运行，文件完成 " + filePercent.ToString("F1") + "%、容量完成 " + bytePercent.ToString("F1") + "%。";
            if (status.Equals("completed", StringComparison.OrdinalIgnoreCase)) return "备份已完成并保留了运行检查点。";
            if (status.Equals("partial", StringComparison.OrdinalIgnoreCase)) return "备份部分完成，将从检查点继续。";
            if (status.Equals("failed", StringComparison.OrdinalIgnoreCase)) return "备份失败，需要查看失败原因。";
            return "备份状态为 " + status + "。";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1000L) return bytes.ToString("N0") + " B";
            if (bytes < 1000000L) return (bytes / 1000D).ToString("F1") + " KB";
            if (bytes < 1000000000L) return (bytes / 1000000D).ToString("F1") + " MB";
            return (bytes / 1000000000D).ToString("F2") + " GB";
        }
    }
}
