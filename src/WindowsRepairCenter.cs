using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;

namespace CodexRecoveryCenter
{
    internal enum RepairFindingStatus
    {
        Normal,
        NeedsAttention,
        NeedsConfirmation,
        Unsupported,
        Error
    }

    internal sealed class RepairFinding
    {
        public string ModuleId = "";
        public string Title = "";
        public string Summary = "";
        public string Detail = "";
        public string Risk = "只读诊断";
        public string RepairLabel = "";
        public bool CanRepair;
        public RepairFindingStatus Status;
        public DateTime CheckedAt = DateTime.Now;
        public List<string> Evidence = new List<string>();

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case RepairFindingStatus.NeedsAttention: return "建议处理";
                    case RepairFindingStatus.NeedsConfirmation: return "需要确认";
                    case RepairFindingStatus.Unsupported: return "暂不支持";
                    case RepairFindingStatus.Error: return "检查失败";
                    default: return "正常";
                }
            }
        }

        public string ToReport()
        {
            var builder = new StringBuilder();
            builder.AppendLine("模块：" + Title);
            builder.AppendLine("状态：" + StatusText);
            builder.AppendLine("检查时间：" + CheckedAt.ToString("O"));
            builder.AppendLine("摘要：" + Summary);
            builder.AppendLine("风险：" + Risk);
            builder.AppendLine("可修复：" + CanRepair);
            if (!String.IsNullOrWhiteSpace(Detail))
                builder.AppendLine("详情：" + Detail);
            if (Evidence.Count > 0)
            {
                builder.AppendLine("证据：");
                foreach (string line in Evidence)
                    builder.AppendLine("- " + line);
            }
            return builder.ToString();
        }
    }

    internal interface IRepairModule
    {
        string Id { get; }
        string Title { get; }
        string Caption { get; }
        RepairFinding Detect();
        RepairFinding Repair();
    }

    internal static class RepairCenterPaths
    {
        public static string Root
        {
            get
            {
                string root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WindowsRepairCenter");
                Directory.CreateDirectory(root);
                return root;
            }
        }

        public static string Logs { get { return Ensure("logs"); } }
        public static string Reports { get { return Ensure("reports"); } }
        public static string Backups { get { return Ensure("backups"); } }

        private static string Ensure(string name)
        {
            string path = Path.Combine(Root, name);
            Directory.CreateDirectory(path);
            return path;
        }
    }

    internal abstract class RepairModuleBase : IRepairModule
    {
        public abstract string Id { get; }
        public abstract string Title { get; }
        public abstract string Caption { get; }
        public abstract RepairFinding Detect();
        public virtual RepairFinding Repair()
        {
            return Finding(RepairFindingStatus.Unsupported, "此模块当前没有安全自动修复动作。", false, "",
                "请先保存诊断报告，再按详情中的手动路径处理。", "需要确认");
        }

        protected RepairFinding Finding(RepairFindingStatus status, string summary, bool canRepair,
            string repairLabel, string detail, string risk)
        {
            return new RepairFinding
            {
                ModuleId = Id,
                Title = Title,
                Summary = summary,
                CanRepair = canRepair,
                RepairLabel = repairLabel,
                Detail = detail ?? "",
                Risk = risk,
                Status = status
            };
        }

        protected static void Add(RepairFinding finding, string line)
        {
            if (!String.IsNullOrWhiteSpace(line) && finding.Evidence.Count < 24)
                finding.Evidence.Add(line.Trim());
        }

        protected static string OneLine(string text, int max = 280)
        {
            string value = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            return value.Length <= max ? value : value.Substring(0, max) + "…";
        }

        protected static string FirstUsefulLine(string output)
        {
            return (output ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim()).FirstOrDefault(value => value.Length > 0) ?? "";
        }

        protected static string SafeFileInfo(string path)
        {
            try
            {
                if (!File.Exists(path)) return "不存在：" + path;
                FileInfo info = new FileInfo(path);
                return path + "（" + info.Length + " bytes，修改于 " + info.LastWriteTime.ToString("yyyy-MM-dd HH:mm") + "）";
            }
            catch { return path; }
        }
    }

    internal sealed class CodexRepairModule : RepairModuleBase
    {
        public override string Id { get { return "codex"; } }
        public override string Title { get { return "Codex 恢复"; } }
        public override string Caption { get { return "AppX、GPU、崩溃与资源状态"; } }

        public override RepairFinding Detect()
        {
            try
            {
                PackageState package = RecoveryEngine.GetPackageState();
                MemoryState memory = RecoveryEngine.GetMemoryState();
                CrashInfo crash = RecoveryEngine.GetLastCodexCrash();
                bool running = RecoveryEngine.IsCodexRunning();
                bool attention = !package.IsOk || memory.IsHighPressure || crash.IsRecent;
                RepairFinding finding = Finding(
                    !package.IsOk && running ? RepairFindingStatus.NeedsConfirmation :
                        (attention ? RepairFindingStatus.NeedsAttention : RepairFindingStatus.Normal),
                    package.IsOk ? "Codex 安装注册正常。" : "Windows 检测到 Codex 安装注册异常。",
                    !package.IsOk, "修复 Codex 并安全启动",
                    (running ? "会关闭 Codex 后继续。" : "仅处理已确认异常。") + "修复会先尝试当前用户注册；仍异常时才使用 Microsoft Store 官方回退。",
                    "注册修复可回滚；关闭运行中应用需确认");
                Add(finding, "Package=" + package.Status + "; Installed=" + package.Installed);
                Add(finding, "Running=" + running);
                Add(finding, "Memory available physical=" + memory.AvailablePhysicalGb.ToString("F2") + " GB; virtual=" + memory.AvailableVirtualGb.ToString("F2") + " GB; pressure=" + memory.CommitPressurePercent.ToString("F1") + "%");
                if (crash.Found)
                    Add(finding, "Last crash=" + crash.Time.ToString("yyyy-MM-dd HH:mm:ss") + "; kind=" + crash.Kind + "; code=" + crash.ExceptionCode);
                if (!String.IsNullOrWhiteSpace(package.InstallLocation))
                    Add(finding, "InstallLocation=" + package.InstallLocation);
                return finding;
            }
            catch (Exception ex)
            {
                return Finding(RepairFindingStatus.Error, "Codex 状态读取失败。", false, "", OneLine(ex.Message), "只读");
            }
        }

        public override RepairFinding Repair()
        {
            try
            {
                if (RecoveryEngine.IsCodexRunning())
                    RecoveryEngine.StopCodex();
                PackageState state = RecoveryEngine.GetPackageState();
                CommandResult registration = state.IsOk ? new CommandResult { ExitCode = 0 } : RecoveryEngine.RepairRegistration();
                if (registration.ExitCode == 0 && !registration.TimedOut)
                    state = RecoveryEngine.WaitForOk(state.Status.IndexOf("NeedsRemediation", StringComparison.OrdinalIgnoreCase) >= 0 ? 12 : 30, null);
                if (!state.IsOk)
                {
                    CommandResult store = RecoveryEngine.RestageFromMicrosoftStore(null);
                    if (store.TimedOut || store.ExitCode != 0)
                        return Finding(RepairFindingStatus.Error, "Codex 官方回退未完成。", false, "", OneLine(store.Output), "需要人工处理");
                    state = RecoveryEngine.WaitForOk(240, null);
                }
                bool launched = state.IsOk && RecoveryEngine.LaunchSafe(state);
                RepairFinding result = Detect();
                result.Summary = launched ? "Codex 已修复并以安全模式启动。" : "Codex 注册已处理，但安全启动未完成。";
                result.Status = launched ? RepairFindingStatus.Normal : RepairFindingStatus.NeedsAttention;
                result.CanRepair = !launched;
                result.Detail = launched ? "已完成注册、状态复核和 GPU 安全启动。" : result.Detail;
                return result;
            }
            catch (Exception ex)
            {
                return Finding(RepairFindingStatus.Error, "Codex 修复失败。", false, "", OneLine(ex.ToString()), "需要人工处理");
            }
        }
    }

    internal sealed class BluetoothRepairModule : RepairModuleBase
    {
        public override string Id { get { return "bluetooth"; } }
        public override string Title { get { return "蓝牙音频"; } }
        public override string Caption { get { return "耳机端点与 Windows 音频服务"; } }

        public override RepairFinding Detect()
        {
            try
            {
                string script =
                    "$bt=Get-PnpDevice -Class Bluetooth -ErrorAction SilentlyContinue | Where-Object {$_.Status -ne 'OK'} | Select-Object -First 5;" +
                    "$aud=Get-Service -Name AudioEndpointBuilder,Audiosrv -ErrorAction SilentlyContinue;" +
                    "'bluetoothProblems='+@($bt).Count;" +
                    "'audio='+($aud | ForEach-Object {$_.Name+'='+$_.Status} -join ',');" +
                    "'endpoints='+((Get-CimInstance Win32_SoundDevice -ErrorAction SilentlyContinue | Where-Object {$_.Name -match 'Bluetooth|Headset|耳机'} | Select-Object -First 5 -ExpandProperty Name) -join ' | ')";
                CommandResult command = RecoveryEngine.RunPowerShell(script, 20000);
                string output = command.Output ?? "";
                int problems = ParseInt(output, "bluetoothProblems=");
                bool servicesOk = output.IndexOf("AudioEndpointBuilder=Running", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    output.IndexOf("Audiosrv=Running", StringComparison.OrdinalIgnoreCase) >= 0;
                bool attention = problems > 0 || !servicesOk;
                RepairFinding finding = Finding(attention ? RepairFindingStatus.NeedsAttention : RepairFindingStatus.Normal,
                    attention ? "蓝牙或音频服务需要复核。" : "蓝牙音频服务与端点状态正常。",
                    attention, "刷新蓝牙音频服务", "会重启 AudioEndpointBuilder 和 Audiosrv，播放会短暂中断；不会卸载驱动。", "低风险，可重试");
                foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    Add(finding, line);
                return finding;
            }
            catch (Exception ex)
            {
                return Finding(RepairFindingStatus.Error, "蓝牙诊断失败。", false, "", OneLine(ex.Message), "只读");
            }
        }

        public override RepairFinding Repair()
        {
            CommandResult result = RecoveryEngine.RunPowerShell(
                "Restart-Service -Name AudioEndpointBuilder -Force -ErrorAction Stop; Restart-Service -Name Audiosrv -Force -ErrorAction Stop; 'audio-services-restarted'",
                30000);
            RepairFinding finding = Detect();
            Add(finding, "repairExitCode=" + result.ExitCode + "; timedOut=" + result.TimedOut);
            if (result.ExitCode != 0 || result.TimedOut)
            {
                finding.Status = RepairFindingStatus.Error;
                finding.Summary = "音频服务刷新失败。";
                finding.Detail = OneLine(result.Output);
            }
            return finding;
        }

        private static int ParseInt(string output, string key)
        {
            string line = (output ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(text => text.TrimStart().StartsWith(key, StringComparison.OrdinalIgnoreCase));
            int parsed;
            return line != null && Int32.TryParse(line.Substring(line.IndexOf('=') + 1).Trim(), out parsed) ? parsed : 0;
        }
    }

    internal sealed class MouseVoiceRepairModule : RepairModuleBase
    {
        private const string TaskName = "WeChat Voice X2 Bridge Watchdog";
        private const string VoiceRoot = @"D:\AICode\AI\skills\技能包\技能\AI协作\鼠标快速语音打字";

        public override string Id { get { return "mouse-voice"; } }
        public override string Title { get { return "鼠标语音输入"; } }
        public override string Caption { get { return "桥接进程、心跳与隐藏 watchdog"; } }

        public override RepairFinding Detect()
        {
            try
            {
                CommandResult task = RecoveryEngine.RunProcess("schtasks.exe",
                    "/query /tn \"" + TaskName + "\" /fo LIST /v", 15000);
                string processEvidence = FindVoiceProcesses();
                bool taskExists = task.ExitCode == 0 && !task.TimedOut;
                bool heartbeat = File.Exists(Path.Combine(VoiceRoot, "runtime", "bridge-health.json")) ||
                    File.Exists(Path.Combine(VoiceRoot, "runtime", "heartbeat.json"));
                bool attention = !taskExists || processEvidence.IndexOf("voice-process", StringComparison.OrdinalIgnoreCase) < 0;
                RepairFinding finding = Finding(
                    attention ? RepairFindingStatus.NeedsAttention : RepairFindingStatus.Normal,
                    taskExists ? "鼠标语音 watchdog 已注册，正在检查单实例与心跳。" : "未找到鼠标语音 watchdog 任务。",
                    attention && taskExists, "后台启动语音 watchdog", "只运行已注册的隐藏任务，不创建新任务；修复后复核任务状态和桥接进程。", "低风险，不隐藏未知窗口");
                Add(finding, "taskExists=" + taskExists);
                Add(finding, "heartbeatFile=" + heartbeat);
                Add(finding, "task=" + OneLine(task.Output, 600));
                Add(finding, processEvidence);
                Add(finding, SafeFileInfo(Path.Combine(VoiceRoot, "scripts", "run-hidden.vbs")));
                Add(finding, SafeFileInfo(Path.Combine(VoiceRoot, "scripts", "watchdog-health.ps1")));
                return finding;
            }
            catch (Exception ex)
            {
                return Finding(RepairFindingStatus.Error, "鼠标语音诊断失败。", false, "", OneLine(ex.Message), "只读");
            }
        }

        public override RepairFinding Repair()
        {
            CommandResult result = RecoveryEngine.RunProcess("schtasks.exe",
                "/run /tn \"" + TaskName + "\"", 15000);
            Thread.Sleep(1200);
            RepairFinding finding = Detect();
            Add(finding, "repairExitCode=" + result.ExitCode + "; timedOut=" + result.TimedOut);
            if (result.ExitCode != 0 || result.TimedOut)
            {
                finding.Status = RepairFindingStatus.Error;
                finding.Summary = "无法启动已注册的语音 watchdog。";
                finding.Detail = OneLine(result.Output);
            }
            return finding;
        }

        private static string FindVoiceProcesses()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT ProcessId,Name,CommandLine,ParentProcessId FROM Win32_Process"))
                {
                    var lines = new List<string>();
                    foreach (ManagementObject item in searcher.Get())
                    {
                        string command = Convert.ToString(item["CommandLine"]);
                        string name = Convert.ToString(item["Name"]);
                        if ((command ?? "").IndexOf("鼠标快速语音打字", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            (command ?? "").IndexOf("Voice X2", StringComparison.OrdinalIgnoreCase) >= 0)
                            lines.Add("voice-process=" + name + "; pid=" + item["ProcessId"] + "; parent=" + item["ParentProcessId"] + "; command=" + OneLine(command, 420));
                    }
                    return lines.Count == 0 ? "voice-process=none" : String.Join(" | ", lines.Take(4));
                }
            }
            catch (Exception ex) { return "voice-process-query-error=" + OneLine(ex.Message); }
        }
    }

    internal sealed class PopupAuditRepairModule : RepairModuleBase
    {
        public override string Id { get { return "popup-audit"; } }
        public override string Title { get { return "PowerShell / CMD 弹窗审计"; } }
        public override string Caption { get { return "计划任务、启动项、脚本与事件证据链"; } }

        public override RepairFinding Detect()
        {
            try
            {
                RepairFinding finding = Finding(RepairFindingStatus.Normal,
                    "已完成启动源只读审计。", false, "", "覆盖 Run/RunOnce、Startup、计划任务动作与注册时间、PowerShell Operational、进程创建、profile、服务和实时 shell 父进程；未知 PowerShell 不会被静默隐藏。", "只读，不修改系统");
                int hits = 0;
                int knownHits = 0;
                int runHits = 0;
                int startupHits = 0;
                int taskHits = 0;
                int profileHits = 0;
                int serviceHits = 0;
                int processHits = 0;
                var evidenceGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in ReadRunKeys())
                {
                    bool candidate = IsSourceCandidate(line);
                    Add(finding, "RunSource=" + DescribeSource(line) + "; " + line);
                    if (candidate) { if (IsKnownSource(line)) knownHits++; else if (RegisterEvidence(evidenceGroups, "run", line)) { hits++; runHits++; } }
                }
                string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (Directory.Exists(startup))
                    foreach (string file in Directory.GetFiles(startup))
                    {
                        string line = "Startup=" + file;
                        bool candidate = IsSourceCandidate(line);
                        Add(finding, "StartupSource=" + DescribeSource(line) + "; " + line);
                        if (candidate) { if (IsKnownSource(line)) knownHits++; else if (RegisterEvidence(evidenceGroups, "startup", line)) { hits++; startupHits++; } }
                    }
                foreach (string line in ReadScheduledTaskEvidence())
                {
                    bool candidate = IsSourceCandidate(line);
                    Add(finding, "TaskSource=" + DescribeSource(line) + "; " + OneLine(line, 520));
                    if (candidate) { if (IsKnownSource(line)) knownHits++; else if (RegisterEvidence(evidenceGroups, "task", line)) { hits++; taskHits++; } }
                }
                foreach (string line in ReadPowerShellOperationalLog())
                {
                    Add(finding, "PowerShellOperational=" + OneLine(line, 520));
                    if (IsActionEvidence(line)) { if (IsKnownSource(line)) knownHits++; else if (RegisterEvidence(evidenceGroups, "powershell-operational", line)) hits++; }
                }
                foreach (string line in ReadProcessCreationLog())
                {
                    Add(finding, "ProcessCreation=" + OneLine(line, 520));
                    if (IsActionEvidence(line)) { if (IsKnownSource(line)) knownHits++; else if (RegisterEvidence(evidenceGroups, "process-creation", line)) { hits++; processHits++; } }
                }
                foreach (string line in ReadServiceEvidence())
                {
                    Add(finding, "ServiceSource=" + DescribeSource(line) + "; " + OneLine(line, 520));
                    if (IsSourceCandidate(line)) { if (IsKnownSource(line)) knownHits++; else if (RegisterEvidence(evidenceGroups, "service", line)) { hits++; serviceHits++; } }
                }
                foreach (string line in ReadLiveShellEvidence())
                {
                    Add(finding, "LiveShell=" + DescribeSource(line) + "; " + OneLine(line, 520));
                    if (IsLiveShellCandidate(line)) { if (IsKnownSource(line)) knownHits++; else if (RegisterEvidence(evidenceGroups, "live-shell", line)) { hits++; processHits++; } }
                }
                foreach (string profile in PowerShellProfiles())
                {
                    foreach (string line in ReadProfileEvidence(profile))
                    {
                        Add(finding, "ProfileSource=" + DescribeSource(line) + "; " + line);
                        if (IsSourceCandidate(line)) { if (IsKnownSource(line)) knownHits++; else if (RegisterEvidence(evidenceGroups, "profile", line)) { hits++; profileHits++; } }
                    }
                }
                int processCount = Process.GetProcessesByName("powershell").Length + Process.GetProcessesByName("pwsh").Length + Process.GetProcessesByName("cmd").Length;
                Add(finding, "interactive-shell-processes=" + processCount);
                Add(finding, "coverage=unclassified Run/RunOnce:" + runHits + "; Startup:" + startupHits + "; Task:" + taskHits + "; Profile:" + profileHits + "; Service:" + serviceHits + "; Process:" + processHits + "; known=" + knownHits + "; window=last 24h for event evidence");
                if (hits > 0)
                {
                    finding.Status = RepairFindingStatus.NeedsConfirmation;
                    finding.Summary = "发现 " + hits + " 条需要人工确认的启动源或进程证据。";
                    finding.Detail = "先按来源、父进程、命令行和注册时间确认；只有确认无用的遗留项才允许停用，当前版本不自动删除。";
                }
                else if (knownHits > 0)
                {
                    finding.Summary = "发现 " + knownHits + " 条已归因的管理来源，未发现未归因启动证据。";
                    finding.Detail = "已知来源仍保留在证据链中；若弹窗再次出现，应优先对照时间、父进程和命令行，而不是直接停用这些任务。";
                }
                else
                {
                    finding.Detail = "未发现未归因的启动型证据；当前日志只保留最近 24 小时，下一次弹窗出现后请立即重新检查并保存报告。";
                }
                return finding;
            }
            catch (Exception ex)
            {
                return Finding(RepairFindingStatus.Error, "启动源审计失败。", false, "", OneLine(ex.Message), "只读");
            }
        }

        private static IEnumerable<string> ReadRunKeys()
        {
            string[] paths = { @"Software\Microsoft\Windows\CurrentVersion\Run", @"Software\Microsoft\Windows\CurrentVersion\RunOnce" };
            foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                    foreach (string path in paths)
                    using (RegistryKey key = baseKey.OpenSubKey(path))
                    {
                        if (key == null) continue;
                        foreach (string name in key.GetValueNames())
                            yield return hive + "\\" + path + "\\" + name + "=" + OneLine(Convert.ToString(key.GetValue(name)), 420);
                    }
            }
        }

        private static IEnumerable<string> ReadScheduledTaskEvidence()
        {
            var lines = new List<string>();
            string script =
                "$pattern='powershell|pwsh|cmd\\.exe|wscript|cscript|\\.ps1|\\.vbs|\\.bat|\\.cmd|cloudflared|tunnel|updater|sync|dida|滴答|工作台|团建|D:\\\\AICode';" +
                "Get-ScheduledTask -ErrorAction SilentlyContinue | ForEach-Object { $t=$_; $actions=(@($t.Actions) | ForEach-Object { ($_.Execute+' '+$_.Arguments).Trim() }) -join ' || '; $hay=($t.TaskPath+' '+$t.TaskName+' '+$actions+' '+$t.Principal.UserId); if($hay -match $pattern) { $last=''; $next=''; $result=''; try{$i=Get-ScheduledTaskInfo -TaskName $t.TaskName -TaskPath $t.TaskPath; $last=$i.LastRunTime.ToString('o'); $next=$i.NextRunTime.ToString('o'); $result=[string]$i.LastTaskResult}catch{}; $date=''; try{$x=[xml](Export-ScheduledTask -TaskName $t.TaskName -TaskPath $t.TaskPath); $date=[string]$x.Task.RegistrationInfo.Date}catch{}; 'task|'+$t.TaskPath+$t.TaskName+'|state='+$t.State+'|registered='+$date+'|last='+$last+'|next='+$next+'|result='+$result+'|action='+$actions } }";
            CommandResult result = RecoveryEngine.RunPowerShell(script, 30000);
            if (!result.TimedOut && result.ExitCode == 0)
            {
                foreach (string line in (result.Output ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string value = RedactSensitive(line.Trim());
                    if (value.StartsWith("task|", StringComparison.OrdinalIgnoreCase)) lines.Add(value);
                }
            }
            if (lines.Count == 0)
            {
                CommandResult fallback = RecoveryEngine.RunProcess("schtasks.exe", "/query /fo CSV /nh /v", 30000);
                foreach (string line in (fallback.Output ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string value = RedactSensitive(line.Trim());
                    if (IsSourceCandidate(value)) lines.Add("legacy-task|" + value);
                }
            }
            return lines;
        }

        private static IEnumerable<string> ReadPowerShellOperationalLog()
        {
            CommandResult result = RecoveryEngine.RunProcess(
                "wevtutil.exe",
                "qe Microsoft-Windows-PowerShell/Operational /q:\"*[System[(EventID=400 or EventID=4103 or EventID=4104) and TimeCreated[timediff(@SystemTime) <= 86400000]]]\" /c:8 /rd:true /f:text",
                15000);
            if (result.TimedOut || result.ExitCode != 0) yield break;
            foreach (string line in (result.Output ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string value = line.Trim();
                if (value.Length > 0 && (IsOperationalEvidence(value) || value.StartsWith("Date", StringComparison.OrdinalIgnoreCase) || value.StartsWith("Event ID", StringComparison.OrdinalIgnoreCase))) yield return RedactSensitive(value);
            }
        }

        private static IEnumerable<string> ReadProcessCreationLog()
        {
            var lines = new List<string>();
            foreach (string logName in new[] { "Security", "Microsoft-Windows-Sysmon/Operational" })
            {
                try
                {
                    using (var log = new EventLog(logName))
                    {
                        int scanned = 0;
                        DateTime cutoff = DateTime.Now.AddHours(-24);
                        for (int index = log.Entries.Count - 1; index >= 0 && scanned < 250; index--, scanned++)
                        {
                            EventLogEntry entry;
                            try { entry = log.Entries[index]; } catch { continue; }
                            if (entry.TimeGenerated < cutoff) break;
                            long eventId = entry.InstanceId & 0xFFFF;
                            bool processCreation = logName.Equals("Security", StringComparison.OrdinalIgnoreCase)
                                ? eventId == 4688
                                : eventId == 1;
                            if (!processCreation) continue;
                            string data = String.Join(" | ", entry.ReplacementStrings ?? new string[0]);
                            lines.Add(RedactSensitive(logName + " event=" + eventId + "; time=" + entry.TimeGenerated.ToString("O") +
                                "; data=" + OneLine(data, 460)));
                        }
                    }
                }
                catch { }
            }
            return lines;
        }

        private static IEnumerable<string> ReadServiceEvidence()
        {
            string script =
                "$pattern='powershell|pwsh|cmd\\.exe|wscript|cscript|\\.ps1|\\.vbs|\\.bat|\\.cmd|cloudflared|tunnel|updater|sync|dida|滴答|工作台|团建|D:\\\\AICode';" +
                "Get-CimInstance Win32_Service -ErrorAction SilentlyContinue | ForEach-Object { $hay=($_.Name+' '+$_.DisplayName+' '+$_.PathName); if($hay -match $pattern) { 'service|name='+$_.Name+'|display='+$_.DisplayName+'|state='+$_.State+'|start='+$_.StartMode+'|path='+$_.PathName } }";
            CommandResult result = RecoveryEngine.RunPowerShell(script, 20000);
            if (result.TimedOut || result.ExitCode != 0) return new string[0];
            return (result.Output ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => RedactSensitive(value.Trim())).Where(value => value.StartsWith("service|", StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        private static IEnumerable<string> ReadLiveShellEvidence()
        {
            var lines = new List<string>();
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT ProcessId,Name,CommandLine,ParentProcessId,CreationDate FROM Win32_Process WHERE Name='powershell.exe' OR Name='pwsh.exe' OR Name='cmd.exe'"))
                using (ManagementObjectCollection items = searcher.Get())
                foreach (ManagementObject item in items)
                {
                    string name = Convert.ToString(item["Name"]);
                    string command = RedactSensitive(OneLine(Convert.ToString(item["CommandLine"]), 360));
                    string parentPid = Convert.ToString(item["ParentProcessId"]);
                    string parent = "unknown";
                    try { using (Process process = Process.GetProcessById(Convert.ToInt32(parentPid))) parent = process.ProcessName; } catch { }
                    lines.Add("live|name=" + name + "|pid=" + Convert.ToString(item["ProcessId"]) + "|parentPid=" + parentPid + "|parent=" + parent + "|created=" + Convert.ToString(item["CreationDate"]) + "|command=" + command);
                }
            }
            catch { }
            return lines;
        }

        private static IEnumerable<string> ReadProfileEvidence(string path)
        {
            if (!File.Exists(path)) return new string[0];
            var lines = new List<string>();
            try
            {
                string[] content = File.ReadAllLines(path, Encoding.Default).Take(240).ToArray();
                int matched = 0;
                for (int index = 0; index < content.Length; index++)
                {
                    if (!IsSourceCandidate(content[index])) continue;
                    lines.Add("profile=" + path + "|line=" + (index + 1) + "|text=" + RedactSensitive(OneLine(content[index], 360)));
                    matched++;
                    if (matched >= 8) break;
                }
                if (matched == 0) lines.Add("profile=" + path + "|exists=true|scriptEvidence=none");
            }
            catch (Exception ex) { lines.Add("profile=" + path + "|readError=" + RedactSensitive(OneLine(ex.Message))); }
            return lines;
        }

        private static IEnumerable<string> PowerShellProfiles()
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WindowsPowerShell", "Microsoft.PowerShell_profile.ps1");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PowerShell", "Microsoft.PowerShell_profile.ps1");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "Profile.ps1");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "Microsoft.PowerShell_profile.ps1");
        }

        private static bool IsInteresting(string line)
        {
            string value = line ?? "";
            string[] needles = { "powershell", "pwsh", "cmd.exe", ".ps1", ".vbs", ".bat", ".cmd", "D:\\AICode", "cloudflare", "tunnel", "updater", "同步", "滴答", "工作台", "团建" };
            return needles.Any(value.IndexOfIgnoreCase);
        }

        private static bool IsOperationalEvidence(string line)
        {
            string value = line ?? "";
            string[] needles = { "powershell.exe", "pwsh.exe", "cmd.exe", ".ps1", ".vbs", ".bat", ".cmd", "D:\\AICode", "Start-Process", "schtasks", "-File", "-Command" };
            return needles.Any(value.IndexOfIgnoreCase);
        }

        private static bool IsSourceCandidate(string line)
        {
            string value = line ?? "";
            if (value.IndexOf("scriptEvidence=none", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            string[] needles = { "powershell", "pwsh", "cmd.exe", "wscript", "cscript", ".ps1", ".vbs", ".bat", ".cmd", "cloudflared", "tunnel", "updater", "start-process", "schtasks", "D:\\AICode", "同步", "滴答", "工作台", "团建" };
            return needles.Any(value.IndexOfIgnoreCase);
        }

        private static bool IsKnownSource(string line)
        {
            string value = line ?? "";
            string[] markers =
            {
                "D:\\AICode\\AI\\skills\\技能包\\技能\\aicode-system-quality-check\\",
                "D:\\AICode\\AI\\infrastructure\\launchers\\start-openclaw-control-gateway.ps1",
                "D:\\AICode\\AI\\infrastructure\\launchers\\start-shared-memory-gateway.ps1",
                "D:\\AICode\\AI\\skills\\技能包\\技能\\AI协作\\鼠标快速语音打字\\",
                "D:\\AICode\\项目推进\\projects\\江湖有旅人\\主项目\\daily_report.ps1",
                "microsoft\\windows\\", "onedrive", "syncthing", "openclaw",
                "aicode system quality check", "jianghudailyreport", "wechat voice x2",
                "鼠标快速语音打字", "windows repair center", "codexrecoverycenter"
            };
            return markers.Any(value.IndexOfIgnoreCase);
        }

        private static bool RegisterEvidence(HashSet<string> groups, string kind, string line)
        {
            string value = line ?? "";
            string key = kind;
            if (kind == "run" || kind == "startup" || kind == "task" || kind == "service" || kind == "profile")
            {
                int separator = value.IndexOf("|action=", StringComparison.OrdinalIgnoreCase);
                if (separator < 0) separator = value.IndexOf("|path=", StringComparison.OrdinalIgnoreCase);
                if (separator < 0) separator = value.IndexOf("|line=", StringComparison.OrdinalIgnoreCase);
                key += ":" + (separator >= 0 ? value.Substring(0, separator) : value);
            }
            return groups.Add(key);
        }

        private static bool IsLiveShellCandidate(string line)
        {
            string value = line ?? "";
            int commandIndex = value.IndexOf("|command=", StringComparison.OrdinalIgnoreCase);
            string command = commandIndex >= 0 ? value.Substring(commandIndex + 9) : value;
            return IsActionEvidence(command);
        }

        private static bool IsActionEvidence(string line)
        {
            string value = line ?? "";
            string[] needles = { "-file", "-command", ".ps1", ".vbs", ".bat", ".cmd", "start-process", "schtasks", "D:\\AICode", "D:\\AI编程", "cloudflared", "tunnel", "updater", "sync", "滴答", "工作台", "团建" };
            return needles.Any(value.IndexOfIgnoreCase);
        }

        private static string DescribeSource(string line)
        {
            string value = line ?? "";
            if (value.IndexOf("powershell", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("pwsh", StringComparison.OrdinalIgnoreCase) >= 0) return "PowerShell";
            if (value.IndexOf("cmd.exe", StringComparison.OrdinalIgnoreCase) >= 0) return "CMD";
            if (value.IndexOf("wscript", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("cscript", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf(".vbs", StringComparison.OrdinalIgnoreCase) >= 0) return "脚本宿主";
            if (value.IndexOf("cloudflared", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("tunnel", StringComparison.OrdinalIgnoreCase) >= 0) return "隧道/网络工具";
            if (value.IndexOf("D:\\AICode", StringComparison.OrdinalIgnoreCase) >= 0) return "本地工作台";
            if (value.IndexOf("updater", StringComparison.OrdinalIgnoreCase) >= 0) return "更新器";
            return "启动来源";
        }

        private static string RedactSensitive(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return value ?? "";
            string redacted = Regex.Replace(value, "(?i)(password|passwd|token|api[_-]?key|secret|authorization|cookie)\\s*[=:]\\s*(\\\"[^\\\"]*\\\"|'[^']*'|[^\\s|;]+)", "$1=<redacted>");
            redacted = Regex.Replace(redacted, "(?i)([?&](?:token|key|secret|password)=)[^&\\s]+", "$1<redacted>");
            redacted = Regex.Replace(redacted, "(?i)(--?(?:token|api[-_]?key|secret|password|cookie|authorization)\\s+)[^\\s|;]+", "$1<redacted>");
            return Regex.Replace(redacted, "(?i)(Bearer\\s+)[^\\s|;]+", "$1<redacted>");
        }
    }

    internal sealed class NetworkRepairModule : RepairModuleBase
    {
        public override string Id { get { return "network"; } }
        public override string Title { get { return "网络诊断"; } }
        public override string Caption { get { return "DNS、代理、网卡与 Cloudflare/tunnel"; } }

        public override RepairFinding Detect()
        {
            try
            {
                NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
                NetworkInterface[] active = interfaces.Where(value => value.OperationalStatus == OperationalStatus.Up && value.NetworkInterfaceType != NetworkInterfaceType.Loopback).ToArray();
                var finding = Finding(active.Length == 0 ? RepairFindingStatus.NeedsAttention : RepairFindingStatus.Normal,
                    active.Length == 0 ? "当前没有可用的活动网卡。" : "网络接口与本机代理状态可用。", active.Length == 0,
                    "刷新 DNS 并验证", "只执行 ipconfig /flushdns，再重新检查网络；不会修改 DNS、代理、Cloudflare 或隧道配置。", "低风险");
                Add(finding, "active-adapters=" + active.Length + "; total=" + interfaces.Length);
                foreach (NetworkInterface adapter in active.Take(6))
                {
                    IPInterfaceProperties props = adapter.GetIPProperties();
                    Add(finding, "adapter=" + adapter.Name + "; type=" + adapter.NetworkInterfaceType + "; dns=" + String.Join(",", props.DnsAddresses.Take(4)));
                }
                CommandResult proxy = RecoveryEngine.RunProcess("netsh.exe", "winhttp show proxy", 10000);
                Add(finding, "winhttp-proxy=" + OneLine(proxy.Output));
                CommandResult ipconfig = RecoveryEngine.RunProcess("ipconfig.exe", "/all", 15000);
                Add(finding, "ipconfig=" + OneLine(ipconfig.Output, 520));
                return finding;
            }
            catch (Exception ex)
            {
                return Finding(RepairFindingStatus.Error, "网络诊断失败。", false, "", OneLine(ex.Message), "只读");
            }
        }

        public override RepairFinding Repair()
        {
            CommandResult flush = RecoveryEngine.RunProcess("ipconfig.exe", "/flushdns", 15000);
            RepairFinding finding = Detect();
            Add(finding, "flushDnsExitCode=" + flush.ExitCode + "; timedOut=" + flush.TimedOut);
            if (flush.ExitCode != 0 || flush.TimedOut)
            {
                finding.Status = RepairFindingStatus.Error;
                finding.Summary = "DNS 缓存刷新失败。";
            }
            return finding;
        }
    }

    internal sealed class StorageAnalysisModule : RepairModuleBase
    {
        public override string Id { get { return "storage"; } }
        public override string Title { get { return "存储分析"; } }
        public override string Caption { get { return "磁盘总览、缓存候选与用户数据边界"; } }

        public override RepairFinding Detect()
        {
            try
            {
                RepairFinding finding = Finding(RepairFindingStatus.Normal,
                    "已完成只读存储分析。", false, "",
                    "绿色项目是可重建缓存，黄色项目是用户资料；本模块不会自动删除文件。", "只读，不修改文件");
                int measured = 0;
                long greenBytes = 0;
                long reviewBytes = 0;

                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!drive.IsReady) continue;
                        long used = drive.TotalSize - drive.AvailableFreeSpace;
                        Add(finding, "磁盘 " + drive.Name + "：已用 " + FormatBytes(used) + " / " + FormatBytes(drive.TotalSize) + "；可用 " + FormatBytes(drive.AvailableFreeSpace));
                    }
                    catch (Exception ex) { Add(finding, "磁盘读取失败：" + OneLine(ex.Message)); }
                }

                foreach (StorageCandidate candidate in Candidates())
                {
                    if (!Directory.Exists(candidate.Path)) continue;
                    long size = MeasureDirectory(candidate.Path, 2, 12000, 4000);
                    if (size <= 0) continue;
                    measured++;
                    if (candidate.Green) greenBytes += size; else reviewBytes += size;
                    Add(finding, (candidate.Green ? "绿色·可重建缓存" : "黄色·用户资料") + "：" + candidate.Title + "；" + candidate.Path + "；约 " + FormatBytes(size));
                }

                AddTopLevelBreakdown(finding, @"D:\AICode", "AICode 顶层占用", 8, 5500);
                AddTopLevelBreakdown(finding, @"D:\", "D 盘根目录占用", 8, 3500);

                finding.Summary = measured == 0
                    ? "已读取磁盘容量，未发现可测量的候选目录。"
                    : "已分析 " + measured + " 个候选目录，并完成 D 盘/AICode 顶层占用排行；可重建缓存约 " + FormatBytes(greenBytes) + "，用户资料约 " + FormatBytes(reviewBytes) + "。";
                if (greenBytes >= 512L * 1024L * 1024L)
                {
                    finding.Status = RepairFindingStatus.NeedsAttention;
                    finding.Detail = "建议先处理绿色缓存；下载、聊天、项目和 D:\\AICode 只提供路径与大小，不提供自动删除按钮。";
                }
                return finding;
            }
            catch (Exception ex)
            {
                return Finding(RepairFindingStatus.Error, "存储分析失败。", false, "", OneLine(ex.Message), "只读");
            }
        }

        private static void AddTopLevelBreakdown(RepairFinding finding, string root, string label, int take, int budgetMilliseconds)
        {
            if (!Directory.Exists(root)) return;
            Stopwatch stopwatch = Stopwatch.StartNew();
            var summaries = new List<DirectorySummary>();
            try
            {
                foreach (DirectoryInfo directory in new DirectoryInfo(root).GetDirectories())
                {
                    if (stopwatch.ElapsedMilliseconds >= budgetMilliseconds) break;
                    try
                    {
                        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        long bytes = MeasureDirectory(directory.FullName, 3, 3500, 650);
                        if (bytes > 0) summaries.Add(new DirectorySummary(directory.Name, directory.FullName, bytes));
                    }
                    catch { }
                }
            }
            catch { return; }

            foreach (DirectorySummary summary in summaries.OrderByDescending(value => value.Bytes).Take(take))
                Add(finding, label + "：" + summary.Name + "；约 " + FormatBytes(summary.Bytes) + "；" + summary.Path);
            if (summaries.Count > take)
                Add(finding, label + "：其余 " + (summaries.Count - take) + " 个目录未展开，仅保留排行前 " + take + " 项。");
            Add(finding, label + "：扫描耗时约 " + stopwatch.ElapsedMilliseconds + " ms；只读排行，不代表可直接删除。");
        }

        private static IEnumerable<StorageCandidate> Candidates()
        {
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return new StorageCandidate("临时文件", Path.GetTempPath(), true);
            yield return new StorageCandidate("用户缓存目录", Path.Combine(user, ".cache"), true);
            yield return new StorageCandidate("npm 缓存", Path.Combine(user, ".npm"), true);
            yield return new StorageCandidate("pip 缓存", Path.Combine(local, "pip", "Cache"), true);
            yield return new StorageCandidate("Chrome 默认配置缓存", Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Cache"), true);
            yield return new StorageCandidate("下载目录", Path.Combine(user, "Downloads"), false);
            yield return new StorageCandidate("开发资料", @"D:\AICode", false);
        }

        private static long MeasureDirectory(string path, int maxDepth, int maxEntries, int maxMilliseconds)
        {
            long total = 0;
            int entries = 0;
            Stopwatch stopwatch = Stopwatch.StartNew();
            var pending = new Stack<DirectoryNode>();
            string root = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            pending.Push(new DirectoryNode(root, 0));
            while (pending.Count > 0 && entries < maxEntries && stopwatch.ElapsedMilliseconds < maxMilliseconds)
            {
                DirectoryNode node = pending.Pop();
                try
                {
                    foreach (FileInfo file in new DirectoryInfo(node.Path).GetFiles())
                    {
                        if (++entries > maxEntries || stopwatch.ElapsedMilliseconds >= maxMilliseconds) break;
                        if ((file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        try { total += file.Length; } catch { }
                    }
                    if (node.Depth >= maxDepth) continue;
                    foreach (DirectoryInfo directory in new DirectoryInfo(node.Path).GetDirectories())
                    {
                        if (entries >= maxEntries || stopwatch.ElapsedMilliseconds >= maxMilliseconds) break;
                        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        string full = directory.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        if (full.Equals(root, StringComparison.OrdinalIgnoreCase)) continue;
                        pending.Push(new DirectoryNode(directory.FullName, node.Depth + 1));
                    }
                }
                catch { }
            }
            return total;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024L * 1024L) return (bytes / 1024L) + " KB";
            if (bytes < 1024L * 1024L * 1024L) return (bytes / 1024D / 1024D).ToString("F1") + " MB";
            return (bytes / 1024D / 1024D / 1024D).ToString("F1") + " GB";
        }

        private sealed class StorageCandidate
        {
            public readonly string Title;
            public readonly string Path;
            public readonly bool Green;
            public StorageCandidate(string title, string path, bool green) { Title = title; Path = path; Green = green; }
        }

        private sealed class DirectoryNode
        {
            public readonly string Path;
            public readonly int Depth;
            public DirectoryNode(string path, int depth) { Path = path; Depth = depth; }
        }

        private sealed class DirectorySummary
        {
            public readonly string Name;
            public readonly string Path;
            public readonly long Bytes;
            public DirectorySummary(string name, string path, long bytes) { Name = name; Path = path; Bytes = bytes; }
        }
    }

    internal sealed class PowerSpaceRepairModule : RepairModuleBase
    {
        private const long AttentionBytes = 512L * 1024L * 1024L;
        private const int MaxFiles = 40000;
        private const int MaxMilliseconds = 9000;

        public override string Id { get { return "power-space"; } }
        public override string Title { get { return "强力空间"; } }
        public override string Caption { get { return "释放可重建缓存，不碰个人资料和开发项目"; } }

        public override RepairFinding Detect()
        {
            try
            {
                SpacePlan plan = BuildPlan();
                bool hasCandidates = plan.Files.Count > 0;
                RepairFinding finding = Finding(
                    plan.TotalBytes >= AttentionBytes ? RepairFindingStatus.NeedsAttention : RepairFindingStatus.Normal,
                    hasCandidates ? "发现可安全释放的缓存空间。" : "暂未发现可安全释放的缓存空间。",
                    hasCandidates,
                    "清理强力空间",
                    "仅处理当前用户临时文件、开发工具缓存和浏览器可重建缓存；不会触碰桌面、文档、下载、D:\\AICode 或项目源文件。运行中的文件会自动跳过。",
                    "会删除可重建缓存，不可撤销；应用下次使用时可能重新下载或生成");
                finding.Summary = hasCandidates
                    ? "可释放约 " + FormatBytes(plan.TotalBytes) + "，共 " + plan.Files.Count + " 个文件。"
                    : "已检查临时文件和常见开发缓存，未发现可处理项目。";
                foreach (SpaceGroup group in plan.Groups)
                {
                    if (group.FileCount > 0)
                        Add(finding, group.Title + "：" + FormatBytes(group.Bytes) + "，" + group.FileCount + " 个文件；" + group.Root);
                }
                Add(finding, "扫描文件=" + plan.ScannedFiles + "；候选=" + plan.Files.Count + "；扫描上限=" + MaxMilliseconds + " ms");
                if (plan.Truncated) Add(finding, "本次扫描触及保护上限，点击清理后会再次复核剩余空间。");
                return finding;
            }
            catch (Exception ex)
            {
                return Finding(RepairFindingStatus.Error, "强力空间分析失败。", false, "", OneLine(ex.Message), "只读");
            }
        }

        public override RepairFinding Repair()
        {
            SpacePlan plan = BuildPlan();
            int deleted = 0;
            int failed = 0;
            long released = 0;
            foreach (SpaceFile candidate in plan.Files)
            {
                try
                {
                    if (!IsWithin(candidate.Path, candidate.Root) || !File.Exists(candidate.Path)) continue;
                    FileInfo info = new FileInfo(candidate.Path);
                    if (info.IsReadOnly) { failed++; continue; }
                    long length = info.Length;
                    File.Delete(candidate.Path);
                    deleted++;
                    released += length;
                }
                catch { failed++; }
            }
            RepairFinding finding = Detect();
            Add(finding, "本次清理：" + deleted + " 个文件；释放约 " + FormatBytes(released) + "；跳过/失败 " + failed + " 个。");
            return finding;
        }

        private static SpacePlan BuildPlan()
        {
            SpacePlan plan = new SpacePlan();
            Stopwatch stopwatch = Stopwatch.StartNew();
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var seenRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddCandidate(plan, seenRoots, "当前用户临时文件", Path.GetTempPath(), 3, stopwatch);
            AddCandidate(plan, seenRoots, "npm 缓存", Path.Combine(local, "npm-cache"), 14, stopwatch);
            AddCandidate(plan, seenRoots, "用户 npm 缓存", Path.Combine(user, ".npm"), 14, stopwatch);
            AddCandidate(plan, seenRoots, "pnpm store", Path.Combine(local, "pnpm", "store"), 14, stopwatch);
            AddCandidate(plan, seenRoots, "pip 缓存", Path.Combine(local, "pip", "Cache"), 14, stopwatch);
            AddCandidate(plan, seenRoots, "NuGet 缓存", Path.Combine(local, "NuGet", "v3-cache"), 14, stopwatch);
            AddCandidate(plan, seenRoots, "Chrome 可重建缓存", Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Cache"), 7, stopwatch);
            AddCandidate(plan, seenRoots, "Edge 可重建缓存", Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "Cache"), 7, stopwatch);
            AddCandidate(plan, seenRoots, "VS Code 可重建缓存", Path.Combine(local, "Microsoft", "VS Code", "Cache"), 14, stopwatch);
            return plan;
        }

        private static void AddCandidate(SpacePlan plan, HashSet<string> seenRoots, string title, string root, int ageDays, Stopwatch stopwatch)
        {
            if (String.IsNullOrWhiteSpace(root)) return;
            string normalized;
            try { normalized = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch { return; }
            if (!seenRoots.Add(normalized) || !Directory.Exists(normalized)) return;
            SpaceGroup group = new SpaceGroup(title, normalized);
            plan.Groups.Add(group);
            DateTime cutoff = DateTime.UtcNow.AddDays(-ageDays);
            var pending = new Stack<string>();
            pending.Push(normalized);
            while (pending.Count > 0 && !plan.Truncated && stopwatch.ElapsedMilliseconds < MaxMilliseconds)
            {
                string current = pending.Pop();
                try
                {
                    foreach (string file in Directory.GetFiles(current))
                    {
                        plan.ScannedFiles++;
                        if (plan.Files.Count >= MaxFiles || stopwatch.ElapsedMilliseconds >= MaxMilliseconds)
                        {
                            plan.Truncated = true;
                            break;
                        }
                        try
                        {
                            FileInfo info = new FileInfo(file);
                            if (info.IsReadOnly || info.LastWriteTimeUtc >= cutoff || !IsWithin(file, normalized)) continue;
                            SpaceFile candidate = new SpaceFile(file, normalized, info.Length);
                            plan.Files.Add(candidate);
                            group.FileCount++;
                            group.Bytes += candidate.Bytes;
                            plan.TotalBytes += candidate.Bytes;
                        }
                        catch { }
                    }
                    if (plan.Truncated) break;
                    foreach (string directory in Directory.GetDirectories(current))
                    {
                        if (stopwatch.ElapsedMilliseconds >= MaxMilliseconds) { plan.Truncated = true; break; }
                        try
                        {
                            DirectoryInfo info = new DirectoryInfo(directory);
                            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                            pending.Push(info.FullName);
                        }
                        catch { }
                    }
                }
                catch { }
            }
            if (stopwatch.ElapsedMilliseconds >= MaxMilliseconds) plan.Truncated = true;
        }

        private static bool IsWithin(string path, string root)
        {
            try
            {
                string file = Path.GetFullPath(path);
                string directory = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return file.StartsWith(directory, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024L * 1024L) return Math.Max(0, bytes / 1024L) + " KB";
            if (bytes < 1024L * 1024L * 1024L) return (bytes / 1024D / 1024D).ToString("F1") + " MB";
            return (bytes / 1024D / 1024D / 1024D).ToString("F1") + " GB";
        }

        private sealed class SpacePlan
        {
            public long TotalBytes;
            public int ScannedFiles;
            public bool Truncated;
            public readonly List<SpaceFile> Files = new List<SpaceFile>();
            public readonly List<SpaceGroup> Groups = new List<SpaceGroup>();
        }

        private sealed class SpaceFile
        {
            public readonly string Path;
            public readonly string Root;
            public readonly long Bytes;
            public SpaceFile(string path, string root, long bytes) { Path = path; Root = root; Bytes = bytes; }
        }

        private sealed class SpaceGroup
        {
            public readonly string Title;
            public readonly string Root;
            public int FileCount;
            public long Bytes;
            public SpaceGroup(string title, string root) { Title = title; Root = root; }
        }
    }

    internal sealed class OverviewRepairModule : RepairModuleBase
    {
        public override string Id { get { return "overview"; } }
        public override string Title { get { return "电脑概览"; } }
        public override string Caption { get { return "先看结论，再决定是否处理"; } }
        public override RepairFinding Detect()
        {
            return Finding(RepairFindingStatus.Normal, "等待其他模块完成检查。", false, "", "从左侧选择具体问题查看证据和下一步。", "只读");
        }
    }

    internal sealed class PerformanceRepairModule : RepairModuleBase
    {
        public override string Id { get { return "performance"; } }
        public override string Title { get { return "电脑加速与空间"; } }
        public override string Caption { get { return "内存、临时文件、开发进程与资源守卫"; } }

        public override RepairFinding Detect()
        {
            try
            {
                MemoryState memory = RecoveryEngine.GetMemoryState();
                TempStats temp = InspectTemp();
                List<ProcessGroup> groups = RecoveryEngine.GetTopCommitGroups(6);
                bool cleanupSuggested = memory.IsHighPressure || temp.CandidateBytes >= 512L * 1024L * 1024L;
                string cleanupDetail = "只清理当前用户临时目录中超过 3 天且可删除的文件，共 " + temp.CandidateCount + " 个，约 " +
                    (temp.CandidateBytes / 1024 / 1024) + " MB；不会关闭开发进程或清空用户数据。";
                RepairFinding finding = Finding(cleanupSuggested ? RepairFindingStatus.NeedsAttention : RepairFindingStatus.Normal,
                    memory.IsHighPressure ? "当前内存/提交压力偏高。" : "资源压力在可接受范围内。", temp.CandidateBytes > 0,
                    "清理可重建临时文件", cleanupDetail, "低风险，可恢复的缓存除外");
                Add(finding, "memory=" + memory.AvailablePhysicalGb.ToString("F2") + " GB physical free; " + memory.AvailableVirtualGb.ToString("F2") + " GB virtual free; pressure=" + memory.CommitPressurePercent.ToString("F1") + "%");
                Add(finding, "tempCandidates=" + temp.CandidateCount + "; bytes=" + (temp.CandidateBytes / 1024 / 1024) + " MB; path=" + temp.Root);
                foreach (ProcessGroup group in groups)
                    Add(finding, "process-group=" + group.Name + " x" + group.Count + " / " + group.CommitMb.ToString("F0") + " MB" + (group.IsCodex ? " [Codex]" : ""));
                return finding;
            }
            catch (Exception ex)
            {
                return Finding(RepairFindingStatus.Error, "资源诊断失败。", false, "", OneLine(ex.Message), "只读");
            }
        }

        public override RepairFinding Repair()
        {
            TempStats stats = InspectTemp();
            int deleted = 0;
            long bytes = 0;
            foreach (string file in stats.Candidates)
            {
                try
                {
                    long length = new FileInfo(file).Length;
                    File.Delete(file);
                    deleted++;
                    bytes += length;
                }
                catch { }
            }
            RepairFinding finding = Detect();
            Add(finding, "deletedTempFiles=" + deleted + "; releasedBytes=" + bytes);
            return finding;
        }

        private sealed class TempStats
        {
            public string Root = "";
            public int CandidateCount;
            public long CandidateBytes;
            public List<string> Candidates = new List<string>();
        }

        private static TempStats InspectTemp()
        {
            var stats = new TempStats { Root = Path.GetTempPath() };
            DateTime cutoff = DateTime.Now.AddDays(-3);
            try
            {
                foreach (string file in Directory.GetFiles(stats.Root, "*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        FileInfo info = new FileInfo(file);
                        if (info.LastWriteTime < cutoff && !info.IsReadOnly && stats.Candidates.Count < 20000)
                        {
                            stats.Candidates.Add(file);
                            stats.CandidateCount++;
                            stats.CandidateBytes += info.Length;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return stats;
        }
    }

    internal sealed class StartupRepairModule : RepairModuleBase
    {
        public override string Id { get { return "startup"; } }
        public override string Title { get { return "启动项与桌面入口"; } }
        public override string Caption { get { return "Startup、Run/RunOnce、任务与快捷方式"; } }

        public override RepairFinding Detect()
        {
            var finding = Finding(RepairFindingStatus.Normal, "启动项已完成审计。", false, "", "可疑项只会进入备份后停用流程，不会被自动删除。", "只读");
            int count = 0;
            int suspicious = 0;
            string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (Directory.Exists(startup))
                foreach (string file in Directory.GetFiles(startup)) { Add(finding, "Startup=" + file); count++; if (IsInteresting(file)) suspicious++; }
            foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
            {
                foreach (string path in new[] { @"Software\Microsoft\Windows\CurrentVersion\Run", @"Software\Microsoft\Windows\CurrentVersion\RunOnce" })
                using (RegistryKey key = baseKey.OpenSubKey(path))
                {
                    if (key == null) continue;
                    foreach (string name in key.GetValueNames())
                    {
                        string line = hive + "\\" + path + "\\" + name + "=" + OneLine(Convert.ToString(key.GetValue(name)));
                        Add(finding, line); count++; if (IsInteresting(line)) suspicious++;
                    }
                }
            }
            if (suspicious > 0) { finding.Status = RepairFindingStatus.NeedsAttention; finding.Summary = "发现 " + suspicious + " 个需要确认来源的开机入口（共 " + count + " 个）。"; }
            else finding.Summary = "已审计 " + count + " 个开机入口，未发现脚本型高风险项。";
            return finding;
        }

        private static bool IsInteresting(string line)
        {
            string value = line ?? "";
            string[] needles = { "powershell", "pwsh", "cmd.exe", ".ps1", ".vbs", ".bat", ".cmd", "D:\\AICode", "cloudflare", "tunnel", "updater", "同步", "滴答", "工作台", "团建" };
            return needles.Any(value.IndexOfIgnoreCase);
        }
    }

    internal sealed class OpenClawRepairModule : RepairModuleBase
    {
        public override string Id { get { return "openclaw"; } }
        public override string Title { get { return "小龙虾"; } }
        public override string Caption { get { return "启动、重启、模型切换与内置网页"; } }

        public override RepairFinding Detect()
        {
            try
            {
                OpenClawStatus status = OpenClawController.GetStatus();
                if (!status.Available)
                    return Finding(RepairFindingStatus.Error, "没有找到小龙虾控制脚本。", false, "", "请确认 " + OpenClawController.ManagerPath + " 仍在本机。", "需要人工处理");
                RepairFinding finding = Finding(
                    status.Running ? RepairFindingStatus.Normal : RepairFindingStatus.NeedsAttention,
                    status.Running ? "小龙虾正在运行。" : "小龙虾当前未启动。",
                    !status.Running,
                    "启动小龙虾",
                    "启动动作由本机控制脚本执行，不会写入密钥或上传诊断内容。",
                    "本机控制；启动/重启会改变后台进程");
                Add(finding, "运行状态=" + (status.Running ? "运行中" : "未启动"));
                Add(finding, "当前模型=" + status.Model);
                Add(finding, "控制脚本=" + OpenClawController.ManagerPath);
                Add(finding, "控制面板操作：启动、重启、模型切换、打开控制网页");
                if (status.Result != null && status.Result.ExitCode != 0)
                    Add(finding, "状态读取返回：" + OpenClawController.Describe(status.Result));
                return finding;
            }
            catch (Exception ex)
            {
                return Finding(RepairFindingStatus.Error, "小龙虾状态读取失败。", false, "", OneLine(ex.Message), "只读");
            }
        }

        public override RepairFinding Repair()
        {
            CommandResult result = OpenClawController.Run("Start", null, 240000);
            RepairFinding finding = Detect();
            Add(finding, "启动结果=" + OpenClawController.Describe(result));
            if (result.TimedOut || result.ExitCode != 0)
            {
                finding.Status = RepairFindingStatus.Error;
                finding.Summary = "小龙虾启动没有完成。";
                finding.CanRepair = true;
            }
            else
            {
                finding.Status = RepairFindingStatus.Normal;
                finding.Summary = "小龙虾已启动。";
                finding.CanRepair = false;
            }
            return finding;
        }
    }

    internal sealed class WindowsRepairCenterForm : ThemedForm
    {
        private readonly List<IRepairModule> modules;
        private readonly IRepairModule overviewModule;
        private readonly Dictionary<string, RepairFinding> findings = new Dictionary<string, RepairFinding>();
        private readonly FlowLayoutPanel moduleList;
        private readonly Label healthLabel;
        private readonly Label healthDetail;
        private readonly Label moduleTitle;
        private readonly Label moduleCaption;
        private readonly Label moduleStatus;
        private readonly TextBox evidenceBox;
        private readonly SoftButton scanButton;
        private readonly SoftButton settingsButton;
        private readonly SoftButton aboutButton;
        private readonly SoftButton repairButton;
        private readonly SoftButton reportButton;
        private readonly SoftButton recordButton;
        private readonly Panel openClawActions;
        private readonly SoftButton openClawStartButton;
        private readonly SoftButton openClawRestartButton;
        private readonly SoftButton openClawDashboardButton;
        private readonly SoftButton openClawSwitchButton;
        private readonly SoftButton openClawRefreshButton;
        private readonly ComboBox openClawModelsBox;
        private readonly Label openClawModelHint;
        private readonly ProgressBar progress;
        private readonly string sessionLog;
        private readonly Dictionary<string, SoftButton> moduleButtons = new Dictionary<string, SoftButton>();
        private IRepairModule selectedModule;
        private AppSettings appSettings;
        private bool busy;

        public WindowsRepairCenterForm()
        {
            overviewModule = new OverviewRepairModule();
            modules = new List<IRepairModule>(BuiltInToolbox.CreateModules());
            selectedModule = overviewModule;
            sessionLog = Path.Combine(RepairCenterPaths.Logs, "session-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            appSettings = SettingsStore.Load();

            Text = "Windows 修复中心";
            // Keep the first view readable on a 100% 1366px display and leave
            // enough room for the evidence pane without forcing tiny controls.
            ClientSize = new Size(1280, 760);
            MinimumSize = new Size(1100, 640);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
            TrySetIcon();

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(30, 18, 30, 14), ColumnCount = 1, RowCount = 3 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            Controls.Add(root);

            var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            Label title = MakeLabel("Windows 修复中心", 0, 0, 17F, FontStyle.Bold, "title");
            Label subtitle = MakeLabel("本机诊断 · 安全修复 · 可验证回滚", 2, 31, 8.5F, FontStyle.Regular, "muted");
            settingsButton = MakeButton("设置", 0, 4, 72, 40, ButtonVisualRole.Ghost);
            aboutButton = MakeButton("关于", 0, 4, 72, 40, ButtonVisualRole.Ghost);
            scanButton = MakeButton("开始检查", 0, 4, 122, 40, ButtonVisualRole.Primary);
            scanButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            settingsButton.Anchor = aboutButton.Anchor = scanButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            header.Controls.AddRange(new Control[] { title, subtitle, settingsButton, aboutButton, scanButton });
            header.Layout += (s, e) =>
            {
                scanButton.Left = header.ClientSize.Width - scanButton.Width;
                aboutButton.Left = scanButton.Left - aboutButton.Width - 8;
                settingsButton.Left = aboutButton.Left - settingsButton.Width - 6;
            };
            root.Controls.Add(header, 0, 0);

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 8, 0, 8) };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 310F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.Controls.Add(body, 0, 1);

            var sidebar = new SoftPanel { Dock = DockStyle.Fill, CornerRadius = 22, Tag = "card", Padding = new Padding(18, 18, 18, 14), Margin = new Padding(0, 0, 22, 0) };
            healthLabel = MakeLabel("准备检查", 0, 0, 13F, FontStyle.Bold, "title");
            healthDetail = MakeLabel("将读取本机状态，不会上传诊断内容", 0, 30, 8.5F, FontStyle.Regular, "muted");
            healthDetail.AutoSize = false; healthDetail.Width = 262; healthDetail.Height = 44;
            moduleList = new FlowLayoutPanel { Location = new Point(0, 94), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.Transparent, Padding = new Padding(0) };
            sidebar.Controls.AddRange(new Control[] { healthLabel, healthDetail, moduleList });
            body.Controls.Add(sidebar, 0, 0);

            var content = new SoftPanel { Dock = DockStyle.Fill, CornerRadius = 22, Tag = "card", Padding = new Padding(26, 22, 26, 20) };
            moduleTitle = MakeLabel("", 0, 0, 15F, FontStyle.Bold, "title");
            moduleCaption = MakeLabel("", 0, 34, 9F, FontStyle.Regular, "muted");
            moduleStatus = MakeLabel("等待检查", 0, 70, 10F, FontStyle.Bold, "warning");
            progress = new ProgressBar { Location = new Point(0, 102), Height = 5, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Visible = false, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 22 };
            evidenceBox = new TextBox { Location = new Point(0, 126), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(226, 235, 241), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, Font = new Font("Consolas", 9F), TabStop = false };
            repairButton = MakeButton("执行建议修复", 0, 0, 150, 40, ButtonVisualRole.Primary); repairButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            reportButton = MakeButton("保存诊断报告", 162, 0, 150, 40, ButtonVisualRole.Secondary); reportButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            recordButton = MakeButton("查看处理记录", 324, 0, 150, 40, ButtonVisualRole.Ghost); recordButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            openClawActions = new Panel { Location = new Point(0, 0), Height = 82, BackColor = Color.Transparent, Visible = false, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            openClawStartButton = MakeButton("启动小龙虾", 0, 0, 132, 36, ButtonVisualRole.Secondary);
            openClawRestartButton = MakeButton("重启网关", 140, 0, 132, 36, ButtonVisualRole.Secondary);
            openClawDashboardButton = MakeButton("打开控制网页", 280, 0, 150, 36, ButtonVisualRole.Ghost);
            openClawModelHint = MakeLabel("模型", 0, 47, 8.5F, FontStyle.Regular, "muted");
            openClawModelsBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(42, 43), Width = 300, Height = 34, Font = new Font("Microsoft YaHei UI", 9F), Anchor = AnchorStyles.Left | AnchorStyles.Right };
            openClawSwitchButton = MakeButton("切换并重启", 350, 42, 132, 36, ButtonVisualRole.Primary);
            openClawRefreshButton = MakeButton("刷新模型", 490, 42, 100, 36, ButtonVisualRole.Ghost);
            openClawActions.Controls.AddRange(new Control[] { openClawStartButton, openClawRestartButton, openClawDashboardButton, openClawModelHint, openClawModelsBox, openClawSwitchButton, openClawRefreshButton });
            content.Controls.AddRange(new Control[] { moduleTitle, moduleCaption, moduleStatus, progress, evidenceBox, openClawActions, repairButton, reportButton, recordButton });
            content.Layout += (s, e) =>
            {
                progress.Width = Math.Max(120, content.ClientSize.Width);
                int actionsHeight = openClawActions.Visible ? 100 : 0;
                evidenceBox.Width = Math.Max(200, content.ClientSize.Width);
                evidenceBox.Height = Math.Max(100, content.ClientSize.Height - 180 - actionsHeight);
                openClawActions.Width = Math.Max(420, content.ClientSize.Width);
                openClawActions.Top = content.ClientSize.Height - 132;
                openClawModelsBox.Width = Math.Max(180, openClawActions.ClientSize.Width - 390);
                openClawSwitchButton.Left = openClawModelsBox.Right + 8;
                openClawRefreshButton.Left = openClawSwitchButton.Right + 8;
                repairButton.Top = content.ClientSize.Height - repairButton.Height;
                reportButton.Top = repairButton.Top;
                recordButton.Top = repairButton.Top;
            };
            body.Controls.Add(content, 1, 0);

            Label footer = MakeLabel("本地运行 · 不上传诊断日志 · 修复前显示影响与备份 · 桌面入口将统一为 Windows 修复中心", 0, 0, 8.5F, FontStyle.Regular, "muted");
            footer.Dock = DockStyle.Fill; footer.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(footer, 0, 2);

            BuildModuleButtons();
            scanButton.Click += async (s, e) => await ScanAllAsync();
            settingsButton.Click += (s, e) => OpenSettings();
            aboutButton.Click += (s, e) =>
            {
                using (var about = new AboutForm(appSettings.Theme)) about.ShowDialog(this);
            };
            repairButton.Click += async (s, e) => await RepairSelectedAsync();
            reportButton.Click += (s, e) => SaveReport();
            recordButton.Click += (s, e) => ShowSessionLog();
            openClawStartButton.Click += async (s, e) => await RunOpenClawActionAsync("Start", null, "正在启动小龙虾…");
            openClawRestartButton.Click += async (s, e) => await RunOpenClawActionAsync("Restart", null, "正在重启小龙虾网关…");
            openClawDashboardButton.Click += async (s, e) => await RunOpenClawActionAsync("Dashboard", null, "正在打开小龙虾内置网页…");
            openClawSwitchButton.Click += async (s, e) =>
            {
                string model = Convert.ToString(openClawModelsBox.SelectedItem);
                if (String.IsNullOrWhiteSpace(model))
                {
                    MessageBox.Show(this, "请先选择模型。", "小龙虾", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                await RunOpenClawActionAsync("SetModel", model, "正在验证模型、备份配置并重启小龙虾…");
            };
            openClawRefreshButton.Click += async (s, e) => await RefreshOpenClawModelsAsync();
            Load += async (s, e) => await ScanAllAsync();
            Shown += (s, e) => BeginInvoke((Action)(() => CenterToScreen()));
            ThemeManager.Apply(this, appSettings.Theme);
            SelectModule(selectedModule);
        }

        private void OpenSettings()
        {
            using (var settings = new SettingsForm(appSettings, ApplySettings, CheckUpdatesAsync))
                settings.ShowDialog(this);
        }

        private async Task CheckUpdatesAsync()
        {
            try
            {
                UpdateInfo latest = await Task.Run(() => UpdateService.CheckLatest());
                if (!latest.IsNewerThan(ProductInfo.Version))
                {
                    MessageBox.Show(this, "当前已经是最新版 v" + ProductInfo.Version + "。", "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                DialogResult open = MessageBox.Show(this,
                    "发现新版 v" + latest.Version + "。\n\n是否打开官方发布页？", "发现新版本", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (open == DialogResult.Yes) Process.Start(latest.ReleaseUrl);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "暂时无法完成更新检查。\n\n" + ex.Message, "检查更新失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ApplySettings(AppSettings updated)
        {
            appSettings = updated;
            ThemeManager.Apply(this, appSettings.Theme);
        }

        private void BuildModuleButtons()
        {
            moduleList.Controls.Clear();
            moduleButtons.Clear();
            SoftButton overviewButton = MakeButton(overviewModule.Title, 0, 0, 262, 42, ButtonVisualRole.Ghost);
            overviewButton.TextAlign = ContentAlignment.MiddleLeft;
            overviewButton.Margin = new Padding(0, 0, 0, 7);
            overviewButton.Click += (s, e) => SelectModule(overviewModule);
            moduleList.Controls.Add(overviewButton);
            moduleButtons[overviewModule.Id] = overviewButton;
            foreach (IRepairModule module in modules)
            {
                SoftButton button = MakeButton(module.Title, 0, 0, 262, 42, ButtonVisualRole.Ghost);
                button.TextAlign = ContentAlignment.MiddleLeft;
                button.Margin = new Padding(0, 0, 0, 7);
                button.Tag = module.Id;
                button.Click += (s, e) => SelectModule(module);
                moduleList.Controls.Add(button);
                moduleButtons[module.Id] = button;
            }
        }

        private void SelectModule(IRepairModule module)
        {
            selectedModule = module;
            bool isOpenClaw = module.Id == "openclaw";
            openClawActions.Visible = isOpenClaw;
            repairButton.Visible = !isOpenClaw;
            foreach (KeyValuePair<string, SoftButton> entry in moduleButtons)
                entry.Value.VisualRole = entry.Key == module.Id ? ButtonVisualRole.Secondary : ButtonVisualRole.Ghost;
            if (module.Id == overviewModule.Id)
            {
                RenderOverview();
                return;
            }
            moduleTitle.Text = module.Title;
            moduleCaption.Text = module.Caption;
            RepairFinding finding;
            if (!findings.TryGetValue(module.Id, out finding))
            {
                RenderPending(module);
                if (isOpenClaw && IsHandleCreated) BeginInvoke((Action)(async () => { await RefreshOpenClawStatusAsync(); await RefreshOpenClawModelsAsync(); }));
                return;
            }
            RenderFinding(finding);
            if (isOpenClaw && IsHandleCreated) BeginInvoke((Action)(async () => { await RefreshOpenClawStatusAsync(); await RefreshOpenClawModelsAsync(); }));
        }

        private async Task RefreshOpenClawStatusAsync()
        {
            if (busy || selectedModule == null || selectedModule.Id != "openclaw") return;
            try
            {
                RepairFinding finding = await Task.Run(() => selectedModule.Detect());
                findings[finding.ModuleId] = finding;
                if (selectedModule.Id == "openclaw") RenderFinding(finding);
            }
            catch (Exception ex)
            {
                WriteLog("小龙虾状态刷新失败：" + ex.Message);
            }
        }

        private async Task RefreshOpenClawModelsAsync()
        {
            if (busy || selectedModule == null || selectedModule.Id != "openclaw") return;
            openClawRefreshButton.Enabled = false;
            try
            {
                CommandResult command = null;
                List<string> models = await Task.Run(() => OpenClawController.GetModels(out command));
                openClawModelsBox.Items.Clear();
                foreach (string model in models) openClawModelsBox.Items.Add(model);
                string current = "";
                RepairFinding finding;
                if (findings.TryGetValue("openclaw", out finding))
                    current = finding.Evidence.FirstOrDefault(line => line.StartsWith("当前模型=", StringComparison.Ordinal)) ?? "";
                current = current.StartsWith("当前模型=", StringComparison.Ordinal) ? current.Substring("当前模型=".Length) : "";
                int providerSeparator = current.IndexOf('/');
                if (providerSeparator >= 0 && providerSeparator < current.Length - 1)
                    current = current.Substring(providerSeparator + 1);
                if (openClawModelsBox.Items.Contains(current)) openClawModelsBox.SelectedItem = current;
                else if (openClawModelsBox.Items.Count > 0) openClawModelsBox.SelectedIndex = 0;
                if (models.Count == 0 && command != null && command.ExitCode != 0)
                    WriteLog("小龙虾模型读取失败：" + OpenClawController.Describe(command));
                else
                    WriteLog("小龙虾模型列表已刷新：" + models.Count + " 个");
            }
            catch (Exception ex)
            {
                WriteLog("小龙虾模型读取失败：" + ex.Message);
            }
            finally
            {
                openClawRefreshButton.Enabled = true;
            }
        }

        private async Task RunOpenClawActionAsync(string action, string model, string message)
        {
            if (busy) return;
            busy = true;
            scanButton.Enabled = false;
            repairButton.Enabled = false;
            openClawStartButton.Enabled = false;
            openClawRestartButton.Enabled = false;
            openClawDashboardButton.Enabled = false;
            openClawSwitchButton.Enabled = false;
            openClawRefreshButton.Enabled = false;
            progress.Visible = true;
            moduleStatus.Text = message;
            WriteLog("小龙虾操作：" + action + (String.IsNullOrWhiteSpace(model) ? "" : " / " + model));
            try
            {
                int timeout = action == "SetModel" || action == "Start" || action == "Restart" ? 240000 : 30000;
                CommandResult command = await Task.Run(() => OpenClawController.Run(action, model, timeout));
                if (command.TimedOut || command.ExitCode != 0)
                    WriteLog("小龙虾操作失败：" + OpenClawController.Describe(command));
                else
                    WriteLog("小龙虾操作完成：" + OpenClawController.Describe(command));
                RepairFinding finding = await Task.Run(() => selectedModule.Detect());
                findings[finding.ModuleId] = finding;
                RenderFinding(finding);
                if (command.TimedOut || command.ExitCode != 0)
                    moduleStatus.Text = "操作未完成 · " + OpenClawController.Describe(command);
                await RefreshOpenClawModelsAsync();
            }
            catch (Exception ex)
            {
                WriteLog("小龙虾操作异常：" + ex);
                moduleStatus.Text = "操作失败 · " + ex.Message;
            }
            finally
            {
                busy = false;
                scanButton.Enabled = true;
                progress.Visible = false;
                openClawStartButton.Enabled = true;
                openClawRestartButton.Enabled = true;
                openClawDashboardButton.Enabled = true;
                openClawSwitchButton.Enabled = true;
                openClawRefreshButton.Enabled = true;
            }
        }

        private async Task ScanAllAsync()
        {
            if (busy) return;
            busy = true; scanButton.Enabled = false; repairButton.Enabled = false; progress.Visible = true;
            healthLabel.Text = "正在检查…";
            healthDetail.Text = "正在并行读取 " + modules.Count + " 个模块；请稍候。";
            findings.Clear();
            RenderPending(selectedModule, "正在读取本机状态…");
            WriteLog("开始全量只读检查");
            try
            {
                RepairFinding[] results = await Task.WhenAll(modules.Select(DetectWithTimeoutAsync));
                foreach (RepairFinding finding in results) findings[finding.ModuleId] = finding;
                foreach (RepairFinding finding in results) WriteLog(finding.Title + "：" + finding.StatusText + "；" + finding.Summary);
                UpdateHealth(results.ToList());
                SelectModule(selectedModule);
            }
            catch (Exception ex)
            {
                healthLabel.Text = "检查未完成";
                healthDetail.Text = "本次检查遇到异常，但不会影响已完成的模块。";
                RenderPending(selectedModule, "检查未完成：" + ex.Message);
                WriteLog("全量检查失败：" + ex);
            }
            finally
            {
                busy = false; scanButton.Enabled = true; progress.Visible = false;
                RepairFinding selected;
                repairButton.Enabled = findings.TryGetValue(selectedModule.Id, out selected) && selected.CanRepair;
            }
        }

        private async Task RepairSelectedAsync()
        {
            RepairFinding current;
            if (busy || selectedModule.Id == overviewModule.Id || !findings.TryGetValue(selectedModule.Id, out current) || !current.CanRepair) return;
            DialogResult answer = MessageBox.Show(
                "将执行：" + current.RepairLabel + "\n\n影响：" + current.Detail + "\n\n是否继续？",
                "确认修复", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
            busy = true; scanButton.Enabled = false; repairButton.Enabled = false; progress.Visible = true; moduleStatus.Text = "正在修复…";
            WriteLog("开始修复：" + selectedModule.Title + " / " + current.RepairLabel);
            try
            {
                RepairFinding result = await Task.Run(() => selectedModule.Repair());
                findings[result.ModuleId] = result;
                WriteLog("修复完成：" + result.StatusText + "；" + result.Summary);
                UpdateHealth(findings.Values.ToList()); RenderFinding(result);
            }
            catch (Exception ex)
            {
                WriteLog("修复失败：" + ex); moduleStatus.Text = "修复失败"; evidenceBox.Text = ex.ToString();
            }
            finally
            {
                busy = false; scanButton.Enabled = true; progress.Visible = false;
                if (findings.TryGetValue(selectedModule.Id, out current)) repairButton.Enabled = current.CanRepair;
            }
        }

        private void UpdateHealth(List<RepairFinding> results)
        {
            int normal = results.Count(value => value.Status == RepairFindingStatus.Normal);
            int confirmation = results.Count(value => value.Status == RepairFindingStatus.NeedsConfirmation);
            int issue = results.Count(value => value.Status == RepairFindingStatus.NeedsAttention || value.Status == RepairFindingStatus.Error);
            int review = issue + confirmation;
            healthLabel.Text = review == 0 ? "电脑状态正常" : "发现 " + review + " 项需要查看";
            healthDetail.Text = normal + " 个模块正常；" + issue + " 个建议处理；" + confirmation + " 个需要人工确认。";
            healthLabel.Tag = issue == 0 && confirmation == 0 ? "accent" : "warning";
            ThemeManager.Apply(this, SettingsStore.Load().Theme);
        }

        private void RenderFinding(RepairFinding finding)
        {
            moduleStatus.Text = finding.StatusText + " · " + finding.Summary;
            moduleStatus.Tag = finding.Status == RepairFindingStatus.Normal ? "accent" : (finding.Status == RepairFindingStatus.Error ? "danger" : "warning");
            var builder = new StringBuilder();
            builder.AppendLine("状态：" + finding.StatusText);
            builder.AppendLine("检查时间：" + finding.CheckedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine("风险：" + finding.Risk);
            builder.AppendLine("摘要：" + finding.Summary);
            if (!String.IsNullOrWhiteSpace(finding.Detail)) builder.AppendLine("详情：" + finding.Detail);
            builder.AppendLine(); builder.AppendLine("证据链：");
            foreach (string line in finding.Evidence) builder.AppendLine("  " + line);
            evidenceBox.Text = builder.ToString();
            repairButton.Text = finding.CanRepair ? (String.IsNullOrWhiteSpace(finding.RepairLabel) ? "执行修复" : finding.RepairLabel) : "当前无需修复";
            repairButton.Enabled = !busy && finding.CanRepair;
            ThemeManager.Apply(this, SettingsStore.Load().Theme);
        }

        private void RenderPending(IRepairModule module, string message = null)
        {
            moduleTitle.Text = module.Title;
            moduleCaption.Text = module.Caption;
            moduleStatus.Text = "等待检查 · " + (String.IsNullOrWhiteSpace(message) ? "点击“开始检查”读取本机状态。" : message);
            moduleStatus.Tag = "warning";
            evidenceBox.Text = "当前模块还没有诊断结果。\r\n" +
                (String.IsNullOrWhiteSpace(message) ? "点击“开始检查”后，这里会显示状态、风险和证据链。" : "检查完成后，这里会显示状态、风险和证据链。") +
                "\r\n\r\n扫描过程只读，不会自动删除文件或隐藏窗口。";
            repairButton.Text = "等待检查";
            repairButton.Enabled = false;
            ThemeManager.Apply(this, SettingsStore.Load().Theme);
        }

        private void RenderOverview()
        {
            moduleTitle.Text = overviewModule.Title;
            moduleCaption.Text = overviewModule.Caption;
            if (findings.Count == 0)
            {
                moduleStatus.Text = "等待检查 · 点击右上角“开始检查”。";
                moduleStatus.Tag = "warning";
                evidenceBox.Text = "这里会先给你一个简单结论：电脑是否正常、哪些地方值得处理、下一步该点哪里。\r\n\r\n" +
                    "扫描只读，不会自动删除文件、修改网络或隐藏 PowerShell 窗口。\r\n\r\n" +
                    "常用入口：\r\n  · 存储分析：查看磁盘、缓存和用户资料\r\n  · PowerShell / CMD 弹窗审计：追查真正启动来源\r\n  · 蓝牙音频：复核耳机和音频服务";
            }
            else
            {
                List<RepairFinding> attention = findings.Values.Where(value => value.Status != RepairFindingStatus.Normal).ToList();
                moduleStatus.Text = attention.Count == 0 ? "检查完成 · 电脑状态正常。" : "检查完成 · 有 " + attention.Count + " 项值得关注。";
                moduleStatus.Tag = attention.Count == 0 ? "accent" : "warning";
                var builder = new StringBuilder();
                builder.AppendLine("先看结论");
                builder.AppendLine("  正常模块：" + (findings.Count - attention.Count) + " 个");
                builder.AppendLine("  建议查看：" + attention.Count + " 个");
                builder.AppendLine();
                if (attention.Count == 0)
                    builder.AppendLine("暂时没有需要处理的项目。你可以定期重新检查。" );
                else
                {
                    builder.AppendLine("建议从这里开始：");
                    foreach (RepairFinding finding in attention.Take(4))
                        builder.AppendLine("  · " + finding.Title + "：" + finding.Summary);
                    builder.AppendLine();
                    builder.AppendLine("点击左侧具体模块，查看证据、影响和可执行动作。" );
                }
                evidenceBox.Text = builder.ToString();
            }
            repairButton.Text = "先选择具体模块";
            repairButton.Enabled = false;
            ThemeManager.Apply(this, SettingsStore.Load().Theme);
        }

        private static RepairFinding DetectSafely(IRepairModule module)
        {
            try
            {
                return module.Detect();
            }
            catch (Exception ex)
            {
                return new RepairFinding
                {
                    ModuleId = module.Id,
                    Title = module.Title,
                    Status = RepairFindingStatus.Error,
                    Summary = "该模块检查失败，其他模块仍已完成。",
                    Detail = ex.Message,
                    Risk = "只读",
                    CheckedAt = DateTime.Now
                };
            }
        }

        private static async Task<RepairFinding> DetectWithTimeoutAsync(IRepairModule module)
        {
            Task<RepairFinding> detection = Task.Run(() => DetectSafely(module));
            Task completed = await Task.WhenAny(detection, Task.Delay(45000));
            if (completed == detection)
                return await detection;

            return new RepairFinding
            {
                ModuleId = module.Id,
                Title = module.Title,
                Status = RepairFindingStatus.Error,
                Summary = "检查超时，已跳过该模块；其他模块仍可用。",
                Detail = "外部 Windows 接口超过 45 秒没有返回。可稍后单独重新检查。",
                Risk = "只读",
                CheckedAt = DateTime.Now
            };
        }

        private void SaveReport()
        {
            string path = Path.Combine(RepairCenterPaths.Reports, "repair-report-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
            var builder = new StringBuilder();
            builder.AppendLine("Windows 修复中心诊断报告");
            builder.AppendLine("时间：" + DateTime.Now.ToString("O"));
            builder.AppendLine("版本：" + ProductInfo.Version);
            builder.AppendLine();
            foreach (IRepairModule module in modules)
            {
                RepairFinding finding;
                if (findings.TryGetValue(module.Id, out finding)) { builder.AppendLine(finding.ToReport()); builder.AppendLine(new String('-', 60)); }
            }
            File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
            WriteLog("已保存诊断报告：" + path);
            MessageBox.Show("报告已保存：\n" + path, "诊断报告", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowSessionLog()
        {
            string content = "";
            try { content = File.Exists(sessionLog) ? File.ReadAllText(sessionLog, Encoding.UTF8) : "本次还没有处理记录。"; }
            catch (Exception ex) { content = "读取记录失败：" + ex.Message; }
            using (var log = new LogForm(appSettings.Theme, content, sessionLog))
                log.ShowDialog(this);
        }

        private void WriteLog(string text)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + text;
            try { File.AppendAllText(sessionLog, line + Environment.NewLine, Encoding.UTF8); } catch { }
        }

        private static Label MakeLabel(string text, int left, int top, float size, FontStyle style, string role)
        {
            return new Label { Text = text, Location = new Point(left, top), AutoSize = true, Font = new Font("Microsoft YaHei UI", size, style), Tag = role, BackColor = Color.Transparent };
        }

        private static SoftButton MakeButton(string text, int left, int top, int width, int height, ButtonVisualRole role)
        {
            return new SoftButton { Text = text, Location = new Point(left, top), Width = width, Height = height, VisualRole = role, Font = new Font("Microsoft YaHei UI", 9F) };
        }

        private void TrySetIcon()
        {
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        }
    }

    internal static class RepairModuleStringExtensions
    {
        public static bool IndexOfIgnoreCase(this string value, string search)
        {
            return (value ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
